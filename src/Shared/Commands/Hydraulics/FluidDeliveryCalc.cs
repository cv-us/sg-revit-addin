using System;
using System.Collections.Generic;

namespace SgRevitAddin.Commands.Hydraulics
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Water-delivery-time engine for dry / (double-interlock) preaction systems.
    //
    //  Two-phase model (Heskestad & Kung split):
    //     t_delivery = t_detection + t_trip + t_valve + t_transit
    //
    //   • t_trip    — trapped gas blows down from the supervisory pressure to the
    //                 valve trip pressure through the open sprinkler orifices,
    //                 over the WHOLE system gas volume.
    //   • t_transit — water front marches valve -> most-remote flowing head; at
    //                 each segment the fill rate is the balance of (a) how fast
    //                 air can vent through the open K-orifices and (b) how fast
    //                 the supply can push water against friction + lift + the
    //                 residual air back-pressure.
    //
    //  The K-factor gives the effective vent area directly: A_eff = 0.02633*K*N,
    //  so more open heads (a bigger flagged region) => faster air displacement.
    //
    //  This is a DOCUMENTED ENGINEERING ESTIMATE (~±25-40%), tree-layout,
    //  quasi-steady isothermal.  NOT a substitute for a listed transient program
    //  (e.g. Tyco SprinkFDT) or a physical trip test for code compliance.
    //
    //  Pure C#, no Revit references — identical on net48 and net8.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One pipe run on the flow path (valve -> remote head), in flow order.</summary>
    public sealed class FlowSegment
    {
        public string Label = "";
        public double LengthFt;
        public double InnerDiaIn;   // true bore (from RBS_PIPE_INNER_DIAM_PARAM)
        public double RiseFt;       // elevation gain along flow (end Z minus start Z), +up
        public double Gallons;      // internal volume of this run
    }

    public enum HazardClass
    {
        Dwelling, Light, OrdinaryI, OrdinaryII, ExtraI, ExtraII, HighPiledStorage, Custom
    }

    public sealed class FluidDeliveryInputs
    {
        /// <summary>Segments from the source valve to the governing remote head, in flow order.</summary>
        public List<FlowSegment> Path = new List<FlowSegment>();

        /// <summary>Whole wetted system internal volume (gal) — code gate + trip-phase blowdown.</summary>
        public double SystemVolumeGal;

        /// <summary>Path internal volume (gal). If left 0 it is summed from <see cref="Path"/>.</summary>
        public double PathVolumeGal;

        // Sprinkler / venting
        public double KFactor = 5.6;
        public int OpenHeads = 1;                 // N flowing/venting heads on the remote area

        // Water supply (two-point curve)
        public double SupplyStaticPsi = 60;
        public double SupplyResidualPsi = 50;
        public double SupplyResidualFlowGpm = 500;

        // Pipe / gas
        public double CFactor = 100;              // 100 dry/preaction black steel, 120 nitrogen
        public double SupervisoryPsi = 7;         // gauge, normal system pressure
        public double TripPsi = 3;                // gauge, valve opens at/below this
        public double GasTempF = 70;
        public bool Nitrogen = false;             // scales gas constant (N2 vs air)

        // Latencies (added verbatim to the total)
        public double DetectionSec = 0;
        public double ValveLatencySec = 0;

        public bool ModelTripPhase = true;        // full two-phase vs transit-only

        // Pass/fail
        public double TargetSec = 60;
        public HazardClass Hazard = HazardClass.Light;
    }

    public sealed class SegmentResult
    {
        public string Label = "";
        public double Gallons;
        public double InnerDiaIn;
        public double LengthFt;
        public double FillGpm;      // Q* at this segment
        public string Regime = "";  // "air-vent" | "supply"
        public double CumTimeSec;   // cumulative transit time when this segment is filled
    }

    public sealed class FluidDeliveryResult
    {
        public double DetectionSec;
        public double TripSec;
        public double ValveSec;
        public double TransitSec;
        public double TotalSec;

        public double SystemVolumeGal;
        public double PathVolumeGal;

        public double TargetSec;
        public bool Pass;

        public List<SegmentResult> Segments = new List<SegmentResult>();
        public List<string> Warnings = new List<string>();
        public List<string> Notes = new List<string>();
    }

    public static class FluidDeliverySolver
    {
        private const double Patm = 14.7;        // psia
        private const double Rc = 0.528;         // critical pressure ratio, gamma = 1.40
        private const double GpmPerCfs = 448.831;
        private const double GalPerCf = 7.4805195;

        /// <summary>NFPA-13 Table 8.2.3.6.1 defaults: # most-remote open heads and max time (s).</summary>
        public static void HazardDefaults(HazardClass h, out int heads, out double targetSec)
        {
            switch (h)
            {
                case HazardClass.Dwelling:         heads = 1; targetSec = 15; break;
                case HazardClass.Light:            heads = 1; targetSec = 60; break;
                case HazardClass.OrdinaryI:
                case HazardClass.OrdinaryII:       heads = 2; targetSec = 50; break;
                case HazardClass.ExtraI:
                case HazardClass.ExtraII:          heads = 4; targetSec = 45; break;
                case HazardClass.HighPiledStorage: heads = 4; targetSec = 40; break;
                default:                           heads = 1; targetSec = 60; break; // Custom
            }
        }

        /// <summary>Internal volume of a segment (gal): use the supplied value, else compute from bore × length.</summary>
        private static double SegGallons(FlowSegment s)
        {
            if (s.Gallons > 0) return s.Gallons;
            double d = Math.Max(0.1, s.InnerDiaIn);
            return Math.PI * Math.Pow(d / 2.0, 2) * s.LengthFt * 12.0 / 231.0;
        }

        public static FluidDeliveryResult Solve(FluidDeliveryInputs f)
        {
            var res = new FluidDeliveryResult { TargetSec = f.TargetSec };

            double T = Math.Max(1.0, f.GasTempF + 459.67);   // deg R, floored above absolute zero
            double R = f.Nitrogen ? 55.15 : 53.35;       // ft·lbf/(lbm·R)
            int n = Math.Max(1, f.OpenHeads);
            double k = f.KFactor > 0 ? f.KFactor : 5.6;
            double cFactor = f.CFactor > 1.0 ? f.CFactor : 100.0;   // guard Hazen-Williams C
            double aEff = 0.02633 * k * n;               // in^2 effective vent area

            // scale the air@530R/53.35 constants to the actual gas & temperature
            double gasScale = Math.Sqrt(530.0 * 53.35 / (T * R));   // ∝ 1/sqrt(R·T)

            double pathVol = f.PathVolumeGal;
            if (pathVol <= 0) { pathVol = 0; foreach (var s in f.Path) pathVol += SegGallons(s); }
            res.PathVolumeGal = pathVol;
            res.SystemVolumeGal = f.SystemVolumeGal > 0 ? f.SystemVolumeGal : pathVol;

            // ── gas mass flow (lbm/s) out the open orifices at absolute pressure Pabs ──
            Func<double, double> mdot = Pabs =>
            {
                if (Pabs <= Patm) return 0;
                if (Patm / Pabs <= Rc)                       // choked
                    return 0.02310 * gasScale * aEff * Pabs;
                double r = Patm / Pabs;                      // subsonic
                double rad = Math.Pow(r, 1.4286) - Math.Pow(r, 1.7143);
                if (rad < 0) rad = 0;
                return 0.08925 * gasScale * aEff * Pabs * Math.Sqrt(rad);
            };
            Func<double, double> rho = Pabs => 144.0 * Pabs / (R * T);          // lbm/ft^3
            Func<double, double> qAir = Pabs =>                                 // gpm the front can advance
                Pabs <= Patm ? 0 : mdot(Pabs) / rho(Pabs) * GpmPerCfs;

            // ── Phase 1: air-trip blowdown over the whole system volume ──
            res.DetectionSec = Math.Max(0, f.DetectionSec);
            res.ValveSec = Math.Max(0, f.ValveLatencySec);
            if (f.ModelTripPhase && res.SystemVolumeGal > 0)
            {
                double vFt3 = res.SystemVolumeGal / GalPerCf;
                double pSup = f.SupervisoryPsi + Patm;
                double pTrip = Math.Max(Patm + 0.05, f.TripPsi + Patm);
                if (pSup <= pTrip)
                {
                    res.Warnings.Add("Supervisory pressure is not above the trip pressure — trip time modeled as 0.");
                }
                else
                {
                    double p = pSup, t = 0, dt = 0.02; int guard = 0;
                    while (p > pTrip && guard++ < 3_000_000)
                    {
                        double dP = -(mdot(p) * R * T / (144.0 * vFt3)) * dt;
                        if (-dP < 1e-10) { res.Warnings.Add("Trip blowdown stalled — check supervisory/trip pressures."); break; }
                        p += dP; t += dt;
                    }
                    res.TripSec = t;
                }
            }

            // ── Phase 2: march the water front to the remote head ──
            double cumDz = 0, cumKfric = 0, cumTime = 0;
            foreach (var s in f.Path)
            {
                double d = Math.Max(0.1, s.InnerDiaIn);
                cumKfric += 4.52 * s.LengthFt / (Math.Pow(cFactor, 1.85) * Math.Pow(d, 4.87));
                cumDz += s.RiseFt;

                string regime;
                double qStar = SolveFront(f, qAir, cumKfric, cumDz, out regime);

                double segGal = SegGallons(s);

                if (qStar < 0.01)
                {
                    res.Warnings.Add($"Segment '{s.Label}' cannot fill — supply pressure insufficient against lift/friction/back-pressure.");
                    qStar = 0.01;
                }
                cumTime += segGal / qStar * 60.0;

                res.Segments.Add(new SegmentResult
                {
                    Label = s.Label,
                    Gallons = segGal,
                    InnerDiaIn = d,
                    LengthFt = s.LengthFt,
                    FillGpm = qStar,
                    Regime = regime,
                    CumTimeSec = cumTime
                });
            }
            res.TransitSec = cumTime;

            res.TotalSec = res.DetectionSec + res.TripSec + res.ValveSec + res.TransitSec;
            res.Pass = res.TargetSec <= 0 || res.TotalSec <= res.TargetSec;

            // ── code-gate notes ──
            if (res.SystemVolumeGal <= 500)
                res.Notes.Add("System volume ≤ 500 gal — typically exempt from the water-delivery-time requirement.");
            else if (res.SystemVolumeGal <= 750)
                res.Notes.Add("System volume 500–750 gal — a listed quick-opening device (QOD) may satisfy the requirement for dry systems.");
            else
                res.Notes.Add("System volume > 750 gal — must meet the delivery-time table (listed program / trip test).");

            return res;
        }

        /// <summary>
        /// Fill rate at the current front (gpm): find the gas back-pressure P_air where
        /// air-venting rate equals water-supply rate.  Labels the binding regime.
        /// </summary>
        private static double SolveFront(FluidDeliveryInputs f, Func<double, double> qAir,
                                         double sumKfric, double sumDz, out string regime)
        {
            // water-supply-limited flow for a given front back-pressure (psia)
            Func<double, double> qWater = pAir =>
            {
                if (sumKfric <= 1e-12) return 1e6;            // no friction yet -> supply not limiting
                double q = Math.Max(1, f.SupplyResidualFlowGpm);
                for (int i = 0; i < 12; i++)
                {
                    double pSupply = f.SupplyStaticPsi -
                        (f.SupplyStaticPsi - f.SupplyResidualPsi) *
                        Math.Pow(q / Math.Max(1, f.SupplyResidualFlowGpm), 1.85);
                    double pAvail = pSupply - 0.433 * sumDz - (pAir - Patm);
                    if (pAvail <= 0) return 0;
                    double qn = Math.Pow(pAvail / sumKfric, 1.0 / 1.85);
                    if (Math.Abs(qn - q) < 0.01 * Math.Max(1, q)) { q = qn; break; }
                    q = 0.5 * (q + qn);                        // damp the supply-droop coupling
                }
                return q;
            };

            double lo = Patm + 0.001;
            double hi = f.SupplyStaticPsi + Patm;              // driving-pressure ceiling
            if (hi <= lo) hi = lo + 1;

            Func<double, double> g = p => qAir(p) - qWater(p); // rises with p (qAir up, qWater down)

            double qStar;
            if (g(lo) >= 0)            // supply always the bottleneck
            {
                qStar = qWater(lo); regime = "supply";
            }
            else if (g(hi) <= 0)       // air-venting always the bottleneck
            {
                qStar = qAir(hi); regime = "air-vent";
            }
            else                       // interior balance
            {
                double a = lo, b = hi;
                for (int i = 0; i < 48; i++)
                {
                    double m = 0.5 * (a + b);
                    if (g(m) > 0) b = m; else a = m;
                }
                double p = 0.5 * (a + b);
                qStar = Math.Min(qAir(p), qWater(p));
                // label by the fundamental cap: which max rate is smaller
                double qAirMax = qAir(hi);
                double qWaterMax = qWater(lo);
                regime = qWaterMax < qAirMax ? "supply" : "air-vent";
            }
            return qStar;
        }
    }
}
