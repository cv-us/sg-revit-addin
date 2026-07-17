using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.SprinklerLayout
{
    /// <summary>
    /// "Layout" — lays out branch lines with sprinklers at VARIABLE spacings that TILE
    /// to fill a picked area. Line spacings live in numbered slots, head spacings in
    /// lettered slots; the sequence strings cycle to span the area ("112112" tiles the
    /// line pattern across the width, "AABA" tiles heads along each line's length).
    ///
    /// Two pick modes:
    ///  • Fill area — pick two opposite corners; lines fill the rectangle.
    ///  • Area + central main — pick two corners and a main line; branches slope DOWN
    ///    toward the main from both sides (draining dry/pre-action systems) and tie into
    ///    the main with a riser nipple + tee, while the main slopes toward its riser end.
    ///
    /// Branch ends can be capped (cap fitting from the pipe type's routing preferences)
    /// a settable distance past the last head. Heads connect directly at outlets or on
    /// vertical sprigs; sprigs can terminate at a common elevation (lengths adapt to the
    /// slope) or a fixed length.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LayoutCommand : IExternalCommand
    {
        private const double SprigMinFt = 0.05;   // a sprig shorter than this becomes a line outlet
        private const double MinSegFt = 0.25;     // 3" — shortest pipe piece we'll build between two nodes
        private const double MainStubFt = 0.5;    // 6" the main continues past the last high-end riser
        private const double CrossFlatFt = 1.0;   // short horizontal run at the crossing so the tee is collinear

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            Document doc = uidoc.Document;

            // ── Dropdown data ──
            var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
                .OrderBy(p => p.Name).Select(p => (id: p.Id.IntegerValue, name: p.Name)).ToList();
            if (pipeTypes.Count == 0)
            { TaskDialog.Show("Layout", "No pipe types in this project."); return Result.Cancelled; }

            var systemTypes = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>()
                .OrderBy(s => s.Name).Select(s => (id: s.Id.IntegerValue, name: s.Name)).ToList();
            if (systemTypes.Count == 0)
            { TaskDialog.Show("Layout", "No piping system types in this project."); return Result.Cancelled; }

            var headSyms = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers)
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .Select(fs => (id: fs.Id.IntegerValue, name: $"{fs.FamilyName} : {fs.Name}")).ToList();

            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0)
            { TaskDialog.Show("Layout", "No levels in this project."); return Result.Cancelled; }
            string defaultLevel = doc.ActiveView?.GenLevel?.Name ?? levels[0].Name;

            var fittingSyms = FittingSymbols(doc);
            var fittings = fittingSyms
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .Select(fs => (id: fs.Id.IntegerValue, name: $"{fs.FamilyName} : {fs.Name}")).ToList();

            LayoutDialog dlg;
            using (dlg = new LayoutDialog(pipeTypes, systemTypes, headSyms,
                       levels.Select(l => l.Name).ToList(), defaultLevel,
                       fittings, DefaultOutletName(fittingSyms), DefaultRiserTeeName(fittingSyms)))
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;

            Level level = levels.FirstOrDefault(l => l.Name == dlg.LevelName);
            if (level == null)
            { TaskDialog.Show("Layout", $"Level \"{dlg.LevelName}\" not found."); return Result.Cancelled; }

            bool wantHeads = dlg.HeadSequence.Length > 0;
            FamilySymbol headSym = wantHeads ? doc.GetElement(new ElementId(dlg.HeadSymbolId)) as FamilySymbol : null;
            if (wantHeads && headSym == null)
            { TaskDialog.Show("Layout", "The selected sprinkler type was not found."); return Result.Cancelled; }

            var pipeType = doc.GetElement(new ElementId(dlg.PipeTypeId)) as PipeType;
            var mainPipeTypeId = dlg.MainPipeTypeId > 0 ? new ElementId(dlg.MainPipeTypeId) : new ElementId(dlg.PipeTypeId);
            var sprigPipeTypeId = dlg.SprigPipeTypeId > 0 ? new ElementId(dlg.SprigPipeTypeId) : new ElementId(dlg.PipeTypeId);
            var mainPipeType = doc.GetElement(mainPipeTypeId) as PipeType;

            // ── Pick points ──
            bool mainMode = dlg.PickMode == LayoutDialog.PickModeKind.AreaMain;
            bool twoMains = dlg.PickMode == LayoutDialog.PickModeKind.TwoMains;
            XYZ c1, c2, cm = null, cm2 = null;
            try
            {
                c1 = uidoc.Selection.PickPoint("Layout: pick the FIRST corner of the area");
                c2 = uidoc.Selection.PickPoint("Layout: pick the OPPOSITE corner");
                if (mainMode)
                    cm = uidoc.Selection.PickPoint("Layout: pick where the MAIN runs (branches slope toward it)");
                else if (twoMains)
                {
                    cm = uidoc.Selection.PickPoint("Layout: pick where the PRIMARY main runs");
                    cm2 = uidoc.Selection.PickPoint("Layout: pick where the SECONDARY (floater) main runs");
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                TaskDialog.Show("Layout",
                    "Point picking needs a plan view (or a view with an active work plane). " +
                    "Open a floor plan and run Layout again.");
                return Result.Cancelled;
            }

            var ctx = new Ctx
            {
                Doc = doc,
                Level = level,
                HeadSym = headSym,
                CapSym = dlg.CapEnds ? ResolveCapSymbol(doc, pipeType) : null,
                SysId = new ElementId(dlg.SystemTypeId),
                PipeTypeId = new ElementId(dlg.PipeTypeId),
                MainPipeTypeId = mainPipeTypeId,
                SprigPipeTypeId = sprigPipeTypeId,
                PipeType = pipeType,
                MainPipeType = mainPipeType,
                OutletSymId = dlg.OutletFittingId > 0 ? new ElementId(dlg.OutletFittingId) : ElementId.InvalidElementId,
                RiserTeeSymId = dlg.RiserTeeFittingId > 0 ? new ElementId(dlg.RiserTeeFittingId) : ElementId.InvalidElementId,
                LinesAlongX = dlg.LinesAlongX,
                LineSizeFt = dlg.LineSizeIn / 12.0,
                SprigSizeFt = dlg.SprigSizeIn / 12.0,
                MainSizeFt = dlg.MainSizeIn / 12.0,
                RiserSizeFt = dlg.RiserSizeIn / 12.0,
                UseSprigs = dlg.UseSprigs,
                CommonElev = dlg.SprigsToCommonElev,
                TermZ = level.Elevation + dlg.TermElevFt,
                SprigLenFt = dlg.SprigLenFt,
                CapEnds = dlg.CapEnds,
                ExtendToCapFt = dlg.ExtendToCapFt,
                HeadClearFt = dlg.MainHeadClearFt,
                Tailback = dlg.Tailback
            };

            double[] lineGaps = dlg.LineSequence.Select(c => dlg.LineSlotFt[c - '1']).ToArray();
            double[] headGaps = dlg.HeadSequence.Select(c => dlg.HeadSlotFt[c - 'A']).ToArray();

            var rpt = new Report();
            Result result = twoMains
                ? BuildTwoMainsMode(ctx, dlg, c1, c2, cm, cm2, lineGaps, headGaps, rpt)
                : mainMode
                    ? BuildMainMode(ctx, dlg, c1, c2, cm, lineGaps, headGaps, rpt: rpt)
                    : BuildFillArea(ctx, dlg, c1, c2, lineGaps, headGaps, rpt);

            if (result != Result.Succeeded) return result;

            TaskDialog.Show("Layout", rpt.ToString(mainMode || twoMains, ctx.CapEnds && ctx.CapSym == null));
            return Result.Succeeded;
        }

        // ── Shared per-run context + report ──

        private class Ctx
        {
            public Document Doc;
            public Level Level;
            public FamilySymbol HeadSym;
            public FamilySymbol CapSym;
            public ElementId SysId, PipeTypeId, MainPipeTypeId, SprigPipeTypeId;
            public PipeType PipeType, MainPipeType;
            public ElementId OutletSymId, RiserTeeSymId;
            public bool LinesAlongX;
            public double LineSizeFt, SprigSizeFt, MainSizeFt, RiserSizeFt;
            public bool UseSprigs, CommonElev;
            public double TermZ, SprigLenFt;
            public bool CapEnds;
            public double ExtendToCapFt;
            public double HeadClearFt;   // min centerline gap main -> nearest head (the main shifts, heads never move)
            public bool Tailback;   // two-mains: tee + stub (vs elbow) at each main tie-in

            /// <summary>Map (along-u, along-m, z) into world XY per the branch direction.</summary>
            public XYZ Pt(double u, double m, double z)
                => LinesAlongX ? new XYZ(u, m, z) : new XYZ(m, u, z);
        }

        private class Report
        {
            public int Lines, Segs, Heads, Sprigs, Nipples, Tees, Elbows, Caps,
                       HeadConn, PlainConn, Outlet, ShortNipple, Fail, ChosenFit, DefaultFit, Joints;
            public double MainShiftFt;      // how far the main had to slide off a head
            public double ClearFloored;     // effective head clearance when it had to exceed the dialog's
            public bool ClearFlooredSlope;  // ...because of the sloped crossing's flat run (vs the min pipe piece)
            public double ClearShortFt;     // clearance actually achieved when it couldn't be honored

            public string ToString(bool mainMode, bool capMissing)
            {
                string r = "Layout\n\n" +
                           $"{(mainMode ? "Branch lines" : "Lines")}: {Lines}   (pipe segments: {Segs})\n" +
                           $"Heads placed: {Heads}\n";
                if (Sprigs > 0) r += $"Sprigs: {Sprigs}\n";
                if (mainMode) r += $"Riser nipples: {Nipples}\n";
                r += $"Tees: {Tees}   Elbows: {Elbows}";
                if (Caps > 0) r += $"   Caps: {Caps}";
                r += $"\nHeads connected: {HeadConn}\n";
                if (mainMode && (ChosenFit > 0 || DefaultFit > 0))
                    r += $"Junction fittings: {ChosenFit} chosen (GOL / Firelock)" +
                         (DefaultFit > 0 ? $", {DefaultFit} fell back to the routing-preference default" : "") + "\n";
                if (Joints > 0) r += $"Slope-transition couplings: {Joints}\n";
                if (MainShiftFt > 0.005)
                    r += $"Main shifted {MainShiftFt * 12.0:0.#}\" off the picked point to clear the nearest " +
                         "head (head spacing kept).\n";
                if (ClearFloored > 0)
                    r += $"Head clear raised to {ClearFloored * 12.0:0.#}\" — " + (ClearFlooredSlope
                        ? $"a sloped branch runs flat for {CrossFlatFt * 12.0:0.#}\" through the crossing, so a head " +
                          "can't sit closer than that plus a fitting piece (unslope the branch for a smaller clearance)."
                        : "that's the shortest pipe piece that can be built between the main and a head.") + "\n";
                if (ClearShortFt > 0)
                    r += $"Head spacing is tighter than twice the head clearance — the main is only " +
                         $"{ClearShortFt * 12.0:0.#}\" from the nearest head (the roomiest spot available).\n";
                if (PlainConn > 0) r += $"Joined without a fitting (tee/elbow failed — check routing preferences): {PlainConn}\n";
                if (Outlet > 0) r += $"Heads placed as line outlets (sprig omitted): {Outlet}\n";
                if (ShortNipple > 0) r += $"Crossings with the main at/above the branch (nipple skipped — check the main elevation): {ShortNipple}\n";
                if (capMissing) r += "Caps requested but the pipe type has no cap in its routing preferences (and none loaded) — ends left open.\n";
                if (Fail > 0) r += $"Failures: {Fail}\n";
                return r;
            }
        }

        // ── Fill-area mode (two corners) ──

        private Result BuildFillArea(Ctx ctx, LayoutDialog dlg, XYZ c1, XYZ c2,
                                     double[] lineGaps, double[] headGaps, Report rpt)
        {
            Document doc = ctx.Doc;
            double minX = Math.Min(c1.X, c2.X), maxX = Math.Max(c1.X, c2.X);
            double minY = Math.Min(c1.Y, c2.Y), maxY = Math.Max(c1.Y, c2.Y);

            double uMin, uMax, mMin, mMax;
            if (ctx.LinesAlongX) { uMin = minX; uMax = maxX; mMin = minY; mMax = maxY; }
            else { uMin = minY; uMax = maxY; mMin = minX; mMax = maxX; }
            double lengthSpan = uMax - uMin, widthSpan = mMax - mMin;
            if (lengthSpan < 0.5 || widthSpan < 0.5)
            { TaskDialog.Show("Layout", "The two corners are too close together to lay anything out."); return Result.Cancelled; }

            List<double> lineOffsets = LineOffsets(dlg.StartOffsetFt, lineGaps, widthSpan, FromMin(ctx, c1, mMin, mMax));
            if (lineOffsets.Count == 0)
            { TaskDialog.Show("Layout", "The first line spacing is wider than the picked area — nothing to place."); return Result.Cancelled; }

            // Node list along each line (d = distance from uMin): head stations + the open/cap
            // ends. With an End offset, the FIRST-picked corner's along-line edge is the dead
            // (capped) end — the last sprinkler sits that far from it, the cap runs one
            // cap-length further toward the corner, and heads tile away toward the far (open)
            // edge. End offset 0 keeps the original behavior (heads from uMin, cap far).
            var nodes = new List<(double d, bool head)>();
            int capNodeIdx = -1;
            double endOff = dlg.EndOffsetFt, capExt = ctx.ExtendToCapFt;
            int stationCount;

            if (endOff <= 1e-6 || headGaps == null || headGaps.Length == 0)
            {
                List<double> st = Tile(headGaps, lengthSpan);
                stationCount = st.Count;
                nodes.Add((0.0, false));
                foreach (var s in st) nodes.Add((s, true));
                double lastStation = st.Count > 0 ? st[st.Count - 1] : 0.0;
                double farEnd = st.Count == 0 ? lengthSpan : lastStation + capExt;
                if (farEnd - lastStation > 0.02)
                {
                    nodes.Add((farEnd, false));
                    if (ctx.CapEnds && ctx.CapSym != null && st.Count > 0) capNodeIdx = nodes.Count - 1;
                }
            }
            else
            {
                double c1u = ctx.LinesAlongX ? c1.X : c1.Y;
                bool uFromMin = Math.Abs(c1u - uMin) <= Math.Abs(c1u - uMax);   // corner's along-line edge = uMin?
                double dir = uFromMin ? 1.0 : -1.0;
                double capEdge = uFromMin ? 0.0 : lengthSpan;
                double d0 = capEdge + dir * endOff;                            // last sprinkler d

                var st = new List<double>();
                if (headGaps != null && headGaps.Length > 0)
                {
                    double cum = 0;
                    for (int i = 0, guard = 0; guard < 100000; i++, guard++)
                    {
                        double d = d0 + dir * cum;
                        if (d < -0.02 || d > lengthSpan + 0.02) break;
                        st.Add(d);
                        double gap = headGaps[i % headGaps.Length]; if (gap <= 1e-6) break;
                        cum += gap;
                    }
                }
                stationCount = st.Count;

                bool wantCap = ctx.CapEnds && ctx.CapSym != null && st.Count > 0 && capExt > 0.02;
                double capD = capEdge + dir * (endOff - capExt);              // cap = last head back toward the corner
                double farEdge = uFromMin ? lengthSpan : 0.0;                 // open far end

                // When capping is wanted, add the cap-stub node toward the corner; when not
                // (e.g. Extend-to-cap ~0), skip it so the last head elbows cleanly with no
                // dangling open stub (matching the End-offset-0 behavior).
                if (wantCap) nodes.Add((capD, false));
                foreach (var s in st) nodes.Add((s, true));
                nodes.Add((farEdge, false));
                nodes.Sort((a, b) => a.d.CompareTo(b.d));
                for (int i = nodes.Count - 1; i > 0; i--)
                    if (nodes[i].d - nodes[i - 1].d < 0.02)
                    { nodes[i - 1] = (nodes[i - 1].d, nodes[i].head || nodes[i - 1].head); nodes.RemoveAt(i); }

                if (wantCap)
                {
                    double target = capD;
                    capNodeIdx = nodes.FindIndex(n => Math.Abs(n.d - target) < 1e-6);
                    if (capNodeIdx < 0) capNodeIdx = uFromMin ? 0 : nodes.Count - 1;
                }
            }

            double slope = dlg.SlopeFtPerFt;
            double zBase = ctx.Level.Elevation + dlg.StartElevFt;

            if (!Confirmed(lineOffsets.Count, stationCount)) return Result.Cancelled;

            using (var tx = new Transaction(doc, "Layout"))
            {
                ApplySwallow(tx);
                tx.Start();
                Activate(ctx);

                var lines = new List<LineWork>();
                foreach (double lineOff in lineOffsets)
                {
                    var work = new LineWork();
                    Func<double, XYZ> at = d => ctx.Pt(uMin + d, mMin + lineOff, zBase + slope * d);

                    for (int k = 0; k + 1 < nodes.Count; k++)
                        work.Segs.Add(CreatePipe(ctx, at(nodes[k].d), at(nodes[k + 1].d), ctx.LineSizeFt, ctx.PipeTypeId, rpt));

                    for (int i = 0; i < nodes.Count; i++)
                    {
                        if (!nodes[i].head) continue;
                        XYZ T = at(nodes[i].d);
                        (bool useSprig, XYZ target) = SprigDecision(ctx, T, rpt);
                        work.Stations.Add(new Station { T = T, Target = target, UseSprig = useSprig, Kind = StationKind.Head, NodeIndex = i });
                    }
                    lines.Add(work);
                    rpt.Lines++;
                }

                PlaceHeads(ctx, lines, rpt);
                doc.Regenerate();
                RepinHeads(ctx, lines);
                doc.Regenerate();

                foreach (var work in lines)
                {
                    ConnectLine(ctx, work, rpt);
                    // Cap the dead end (the still-open outer connector of that end's segment).
                    if (capNodeIdx >= 0 && work.Segs.Count > 0)
                    {
                        Pipe capSeg = capNodeIdx == 0 ? work.Segs[0] : work.Segs[work.Segs.Count - 1];
                        CapEnd(ctx, capSeg, null, rpt);
                    }
                }

                tx.Commit();
            }
            return Result.Succeeded;
        }

        // ── Area + central main mode (three points) ──

        private Result BuildMainMode(Ctx ctx, LayoutDialog dlg, XYZ c1, XYZ c2, XYZ cm,
                                     double[] lineGaps, double[] headGaps, Report rpt)
        {
            Document doc = ctx.Doc;
            double minX = Math.Min(c1.X, c2.X), maxX = Math.Max(c1.X, c2.X);
            double minY = Math.Min(c1.Y, c2.Y), maxY = Math.Max(c1.Y, c2.Y);

            double uMin, uMax, mMin, mMax, uMain;
            if (ctx.LinesAlongX)
            { uMin = minX; uMax = maxX; mMin = minY; mMax = maxY; uMain = Clamp(cm.X, uMin, uMax); }
            else
            { uMin = minY; uMax = maxY; mMin = minX; mMax = maxX; uMain = Clamp(cm.Y, uMin, uMax); }
            double widthSpan = mMax - mMin;
            if (uMax - uMin < 1.0 || widthSpan < 0.5)
            { TaskDialog.Show("Layout", "The picked area is too small to lay out a main + branches."); return Result.Cancelled; }

            List<double> mOffsets = LineOffsets(dlg.StartOffsetFt, lineGaps, widthSpan, FromMin(ctx, c1, mMin, mMax));      // branch positions along the main
            if (mOffsets.Count == 0)
            { TaskDialog.Show("Layout", "The first line spacing is wider than the picked area — nothing to place."); return Result.Cancelled; }
            var branchM = mOffsets.Select(o => mMin + o).ToList();

            double branchSlope = Math.Abs(dlg.SlopeFtPerFt);        // magnitude; both sides fall to the main
            double branchLowZ = ctx.Level.Elevation + dlg.StartElevFt;

            // Heads tile ONCE across the whole line (same rhythm on both sides of the main),
            // then the main slides off any head it lands on. Every line shares these
            // stations, so the shift is one decision for the whole run.
            var headU = HeadStations(uMin, uMax, headGaps);
            double clearFloor = MainClearFloor(branchSlope);
            double clearFt = Math.Max(ctx.HeadClearFt, clearFloor);
            double mainShift;
            uMain = ShiftMainClear(uMain, headU, clearFt, uMin, uMax, out mainShift);
            rpt.MainShiftFt = mainShift;
            rpt.ClearFloored = clearFt > ctx.HeadClearFt + 1e-9 ? clearFt : 0.0;
            rpt.ClearFlooredSlope = branchSlope > 1e-9;

            // Below the floor a head would sit inside the crossing's flat zone: the slope
            // break would land on that head's tee (which must be collinear) and its node
            // would fall on the wrong side of the flat-zone joint. Refuse rather than build it.
            double got = headU.Count > 0 ? HeadClearance(uMain, headU) : double.MaxValue;
            if (got < clearFloor - 1e-6)
            {
                TaskDialog.Show("Layout",
                    "The head spacing is too tight to fit the cross-main between two heads.\n\n" +
                    $"The main needs at least {clearFloor * 12.0:0.#}\" of centerline clearance to the nearest " +
                    $"head, but the roomiest spot in the picked area is {got * 12.0:0.#}\".\n\n" +
                    (branchSlope > 1e-9
                        ? $"A sloped branch runs flat for {CrossFlatFt * 12.0:0.#}\" through the crossing so the " +
                          "riser tee stays straight — widen the head spacing, or set the branch slope to 0."
                        : "Widen the head spacing."));
                return Result.Cancelled;
            }
            rpt.ClearShortFt = got < clearFt - 1e-6 ? got : 0.0;
            double mainElev = ctx.Level.Elevation + dlg.MainElevFt;
            double mainSlope = Math.Abs(dlg.MainSlopeFtPerFt);
            // main falls toward the riser end; "reversed" puts the riser at the far (mMax) end
            Func<double, double> mainZ = m =>
            {
                double distFromHigh = dlg.MainSlopeReversed ? (m - mMin) : (mMax - m);
                return mainElev - mainSlope * distFromHigh;
            };

            if (!Confirmed(mOffsets.Count, headGaps.Length == 0 ? 0 : (int)((uMax - uMin) / Math.Max(0.5, headGaps.Min())))) return Result.Cancelled;

            using (var tx = new Transaction(doc, "Layout"))
            {
                ApplySwallow(tx);
                tx.Start();
                Activate(ctx);

                // Branch V nodes per line.
                var branches = new List<LineWork>();
                foreach (double m in branchM)
                {
                    var work = BuildBranchV(ctx, headU, uMain, m, branchLowZ, branchSlope, rpt);
                    branches.Add(work);
                    rpt.Lines++;
                }

                // Segmented cross-main: it BREAKS at each riser crossing, with the outlet
                // (GOL) fitting between the two pieces. Past the last riser at the high
                // (top-of-slope) end it continues a 6" stub that gets capped; the low/riser
                // end is left open to tie into the system riser.
                var mainSegs = new List<Pipe>();
                for (int i = 0; i + 1 < branchM.Count; i++)
                    mainSegs.Add(CreatePipe(ctx,
                        ctx.Pt(uMain, branchM[i], mainZ(branchM[i])),
                        ctx.Pt(uMain, branchM[i + 1], mainZ(branchM[i + 1])), ctx.MainSizeFt, ctx.MainPipeTypeId, rpt));

                int highDir = dlg.MainSlopeReversed ? -1 : 1;
                int hiIndex = dlg.MainSlopeReversed ? 0 : branchM.Count - 1;
                int loIndex = dlg.MainSlopeReversed ? branchM.Count - 1 : 0;

                // High (top-of-slope) end: continue 6" and CAP it.
                double stubHiM = branchM[hiIndex] + highDir * MainStubFt;
                XYZ mainStubHiPt = ctx.Pt(uMain, stubHiM, mainZ(stubHiM));
                Pipe mainStub = CreatePipe(ctx,
                    ctx.Pt(uMain, branchM[hiIndex], mainZ(branchM[hiIndex])), mainStubHiPt, ctx.MainSizeFt, ctx.MainPipeTypeId, rpt);

                // Low (riser) end: continue 6" and leave it OPEN (no cap) so it can be
                // extended into the system riser.
                double stubLoM = branchM[loIndex] - highDir * MainStubFt;
                Pipe mainStubLo = CreatePipe(ctx,
                    ctx.Pt(uMain, branchM[loIndex], mainZ(branchM[loIndex])),
                    ctx.Pt(uMain, stubLoM, mainZ(stubLoM)), ctx.MainSizeFt, ctx.MainPipeTypeId, rpt);

                PlaceHeads(ctx, branches, rpt);
                doc.Regenerate();
                RepinHeads(ctx, branches);
                doc.Regenerate();

                // Connect each branch's heads/sprigs + build the crossing nipple.
                for (int i = 0; i < branches.Count; i++)
                {
                    LineWork work = branches[i];
                    ConnectLine(ctx, work, rpt);
                    Station cross = work.Stations.FirstOrDefault(s => s.Kind == StationKind.Crossing);

                    // Riser nipple down to the main + tee on the main.
                    double mZ = mainZ(branchM[i]);
                    if (cross != null && branchLowZ - mZ > SprigMinFt)
                    {
                        XYZ topPt = ctx.Pt(uMain, branchM[i], branchLowZ);
                        XYZ botPt = ctx.Pt(uMain, branchM[i], mZ);
                        Pipe nipple = CreatePipe(ctx, topPt, botPt, ctx.RiserSizeFt, ctx.MainPipeTypeId, rpt);
                        if (nipple != null)
                        {
                            rpt.Nipples++;
                            // Break the main at the crossing: the two collinear main pieces
                            // + the nipple bottom become the outlet (GOL) tee.
                            Connector nBot = FreeEndNear(nipple, botPt);
                            Pipe mBefore = i - 1 >= 0 && i - 1 < mainSegs.Count ? mainSegs[i - 1] : null;
                            Pipe mAfter = i < mainSegs.Count ? mainSegs[i] : null;
                            // Splice the high-end (capped) and low-end (open) stubs in so
                            // those end crossings tee (not elbow).
                            if (i == hiIndex && mainStub != null)
                            { if (dlg.MainSlopeReversed) mBefore = mainStub; else mAfter = mainStub; }
                            if (i == loIndex && mainStubLo != null)
                            { if (dlg.MainSlopeReversed) mAfter = mainStubLo; else mBefore = mainStubLo; }
                            Connector mA = mBefore != null ? FreeEndNear(mBefore, botPt) : null;
                            Connector mB = mAfter != null ? FreeEndNear(mAfter, botPt) : null;
                            JoinRun(ctx, mA, mB, nBot, rpt);

                            // Riser-top Firelock tee: the two branch halves + the nipple top.
                            Connector nTop = FreeEndNear(nipple, topPt);
                            if (nTop != null && (cross.RunA != null || cross.RunB != null))
                                JoinBranch(ctx, cross.RunA, cross.RunB, nTop, rpt);
                            SetDiameter(nipple, ctx.RiserSizeFt);
                        }
                    }
                    else if (cross != null)
                    {
                        // Main is at/above the branch — no nipple; keep the branch's two
                        // halves connected to each other so it isn't hydraulically split.
                        rpt.ShortNipple++;
                        if (cross.RunA != null && cross.RunB != null)
                        { try { cross.RunA.ConnectTo(cross.RunB); rpt.PlainConn++; } catch { rpt.Fail++; } }
                    }

                    // Cap the true outer branch ends (never the main crossing).
                    if (ctx.CapEnds && ctx.CapSym != null)
                    {
                        if (work.HasLeftCap && work.Segs.Count > 0) CapEnd(ctx, work.Segs[0], work.LeftCapPt, rpt);
                        if (work.HasRightCap && work.Segs.Count > 0) CapEnd(ctx, work.Segs[work.Segs.Count - 1], work.RightCapPt, rpt);
                    }
                    continue;
                }

                // Cap the far (high) end of the main's 6" stub; the riser end stays open.
                if (mainStub != null && ctx.CapEnds && ctx.CapSym != null)
                {
                    doc.Regenerate();   // settle the tees before reading the stub's open end
                    CapEnd(ctx, mainStub, mainStubHiPt, rpt);
                }

                tx.Commit();
            }
            return Result.Succeeded;
        }

        // ── Two-mains mode (four points: 2 corners + primary + secondary main) ──

        private class MainRun
        {
            public double U;                               // the main's position on the branch (u) axis
            public List<Pipe> Segs = new List<Pipe>();     // pieces between consecutive branch crossings
            public Pipe LoStub, HiStub;                    // open 6" stubs past the outer branches

            public Pipe Before(int bi) => bi - 1 >= 0 && bi - 1 < Segs.Count ? Segs[bi - 1] : LoStub;
            public Pipe After(int bi) => bi >= 0 && bi < Segs.Count ? Segs[bi] : HiStub;
        }

        /// <summary>Two parallel mains (primary + secondary/floater). Each FLAT branch runs
        /// between them, tying into both with a riser nipple + GOL on the main. With a
        /// tailback the branch continues a short capped stub past each main (Firelock tee);
        /// without, it terminates into the riser with an elbow. Unsloped.</summary>
        private Result BuildTwoMainsMode(Ctx ctx, LayoutDialog dlg, XYZ c1, XYZ c2, XYZ cmA, XYZ cmB,
                                         double[] lineGaps, double[] headGaps, Report rpt)
        {
            Document doc = ctx.Doc;
            if (cmA == null || cmB == null)
            { TaskDialog.Show("Layout", "Two-mains mode needs both main points."); return Result.Cancelled; }

            double minX = Math.Min(c1.X, c2.X), maxX = Math.Max(c1.X, c2.X);
            double minY = Math.Min(c1.Y, c2.Y), maxY = Math.Max(c1.Y, c2.Y);

            double uMin, uMax, mMin, mMax, uA, uB;
            if (ctx.LinesAlongX)
            { uMin = minX; uMax = maxX; mMin = minY; mMax = maxY; uA = Clamp(cmA.X, uMin, uMax); uB = Clamp(cmB.X, uMin, uMax); }
            else
            { uMin = minY; uMax = maxY; mMin = minX; mMax = maxX; uA = Clamp(cmA.Y, uMin, uMax); uB = Clamp(cmB.Y, uMin, uMax); }

            double uLo = Math.Min(uA, uB), uHi = Math.Max(uA, uB);
            double widthSpan = mMax - mMin;
            if (uHi - uLo < 1.0 || widthSpan < 0.5)
            { TaskDialog.Show("Layout", "The two mains are too close together, or the area is too small."); return Result.Cancelled; }

            List<double> mOffsets = LineOffsets(dlg.StartOffsetFt, lineGaps, widthSpan, FromMin(ctx, c1, mMin, mMax));          // branch positions along the mains
            if (mOffsets.Count == 0)
            { TaskDialog.Show("Layout", "The first line spacing is wider than the picked area — nothing to place."); return Result.Cancelled; }
            var branchM = mOffsets.Select(o => mMin + o).ToList();

            // Heads tile continuously from the primary main. If the last one crowds the
            // secondary main, THAT main slides outward — the head rhythm never changes.
            // (The stations are anchored at uLo, so only uHi can move without moving them.)
            double clearFt = Math.Max(ctx.HeadClearFt, MainClearFloor(0.0));   // two-mains branches are flat
            var headU = HeadStations(uLo, uHi, headGaps).Where(u => u - uLo >= clearFt - 1e-9).ToList();
            if (headU.Count > 0)
            {
                double lastHead = headU[headU.Count - 1];
                if (uHi - lastHead < clearFt - 1e-9)
                { rpt.MainShiftFt = clearFt - (uHi - lastHead); uHi = lastHead + clearFt; }
            }

            double branchZ = ctx.Level.Elevation + dlg.StartElevFt;      // flat
            double mainZ = ctx.Level.Elevation + dlg.MainElevFt;         // flat
            double tailFt = ctx.ExtendToCapFt > 0.02 ? ctx.ExtendToCapFt : MainStubFt;

            if (!Confirmed(mOffsets.Count, headGaps.Length == 0 ? 0 : (int)((uHi - uLo) / Math.Max(0.5, headGaps.Min())))) return Result.Cancelled;

            using (var tx = new Transaction(doc, "Layout"))
            {
                ApplySwallow(tx);
                tx.Start();
                Activate(ctx);

                // Two flat mains along m, each broken at every branch crossing (open 6" ends).
                var mains = new[] { new MainRun { U = uLo }, new MainRun { U = uHi } };
                foreach (var main in mains)
                {
                    for (int i = 0; i + 1 < branchM.Count; i++)
                        main.Segs.Add(CreatePipe(ctx,
                            ctx.Pt(main.U, branchM[i], mainZ),
                            ctx.Pt(main.U, branchM[i + 1], mainZ), ctx.MainSizeFt, ctx.MainPipeTypeId, rpt));
                    main.LoStub = CreatePipe(ctx,
                        ctx.Pt(main.U, branchM[0], mainZ),
                        ctx.Pt(main.U, branchM[0] - MainStubFt, mainZ), ctx.MainSizeFt, ctx.MainPipeTypeId, rpt);
                    main.HiStub = CreatePipe(ctx,
                        ctx.Pt(main.U, branchM[branchM.Count - 1], mainZ),
                        ctx.Pt(main.U, branchM[branchM.Count - 1] + MainStubFt, mainZ), ctx.MainSizeFt, ctx.MainPipeTypeId, rpt);
                }

                // Flat branches: [stub] main(uLo) heads... main(uHi) [stub].
                var branches = new List<LineWork>();
                foreach (double m in branchM)
                {
                    branches.Add(BuildBranchFlat(ctx, uLo, uHi, m, headU, branchZ, ctx.Tailback, tailFt, rpt));
                    rpt.Lines++;
                }

                PlaceHeads(ctx, branches, rpt);
                doc.Regenerate();
                RepinHeads(ctx, branches);
                doc.Regenerate();

                for (int bi = 0; bi < branches.Count; bi++)
                {
                    LineWork work = branches[bi];
                    ConnectLine(ctx, work, rpt);
                    double m = branchM[bi];

                    foreach (var cross in work.Stations.Where(s => s.Kind == StationKind.Crossing))
                    {
                        MainRun main = mains[Math.Max(0, cross.MainIdx)];
                        if (branchZ - mainZ > SprigMinFt)
                        {
                            XYZ topPt = ctx.Pt(main.U, m, branchZ);
                            XYZ botPt = ctx.Pt(main.U, m, mainZ);
                            Pipe nipple = CreatePipe(ctx, topPt, botPt, ctx.RiserSizeFt, ctx.MainPipeTypeId, rpt);
                            if (nipple != null)
                            {
                                rpt.Nipples++;
                                Connector nBot = FreeEndNear(nipple, botPt);
                                Connector mA = FreeEndNear(main.Before(bi), botPt);
                                Connector mB = FreeEndNear(main.After(bi), botPt);
                                JoinRun(ctx, mA, mB, nBot, rpt);                    // GOL, break the main

                                Connector nTop = FreeEndNear(nipple, topPt);
                                if (nTop != null && (cross.RunA != null || cross.RunB != null))
                                    JoinBranch(ctx, cross.RunA, cross.RunB, nTop, rpt);   // tee (tailback) or elbow
                                SetDiameter(nipple, ctx.RiserSizeFt);
                            }
                        }
                        else
                        {
                            rpt.ShortNipple++;
                            if (cross.RunA != null && cross.RunB != null)
                            { try { cross.RunA.ConnectTo(cross.RunB); rpt.PlainConn++; } catch { rpt.Fail++; } }
                        }
                    }

                    // Cap the tailback stubs at the branch ends (never a main crossing).
                    if (ctx.Tailback && ctx.CapEnds && ctx.CapSym != null)
                    {
                        if (work.HasLeftCap && work.Segs.Count > 0) CapEnd(ctx, work.Segs[0], work.LeftCapPt, rpt);
                        if (work.HasRightCap && work.Segs.Count > 0) CapEnd(ctx, work.Segs[work.Segs.Count - 1], work.RightCapPt, rpt);
                    }
                }

                tx.Commit();
            }
            return Result.Succeeded;
        }

        /// <summary>One flat branch spanning the two mains, with a crossing node at each
        /// main. <paramref name="heads"/> is the shared station list (tiled once from the
        /// primary main and already clear of both). With a tailback a short stub extends
        /// past each main (so the crossing tees); without, the crossing is the branch end
        /// (elbow).</summary>
        private LineWork BuildBranchFlat(Ctx ctx, double uLo, double uHi, double m, List<double> heads,
                                         double branchZ, bool tailback, double tailFt, Report rpt)
        {
            Func<double, XYZ> at = u => ctx.Pt(u, m, branchZ);

            var work = new LineWork();
            var nodeU = new List<double>();
            var nodeKind = new List<StationKind?>();   // null = cap stub
            var nodeMain = new List<int>();            // crossing → main index (0=uLo, 1=uHi)

            if (tailback) { nodeU.Add(uLo - tailFt); nodeKind.Add(null); nodeMain.Add(-1); work.HasLeftCap = true; work.LeftCapPt = at(uLo - tailFt); }
            nodeU.Add(uLo); nodeKind.Add(StationKind.Crossing); nodeMain.Add(0);
            foreach (double u in heads) { nodeU.Add(u); nodeKind.Add(StationKind.Head); nodeMain.Add(-1); }
            nodeU.Add(uHi); nodeKind.Add(StationKind.Crossing); nodeMain.Add(1);
            if (tailback) { nodeU.Add(uHi + tailFt); nodeKind.Add(null); nodeMain.Add(-1); work.HasRightCap = true; work.RightCapPt = at(uHi + tailFt); }

            for (int k = 0; k + 1 < nodeU.Count; k++)
                work.Segs.Add(CreatePipe(ctx, at(nodeU[k]), at(nodeU[k + 1]), ctx.LineSizeFt, ctx.PipeTypeId, rpt));

            for (int k = 0; k < nodeU.Count; k++)
            {
                if (nodeKind[k] == null) continue;
                XYZ T = at(nodeU[k]);
                var st = new Station { T = T, NodeIndex = k, Kind = nodeKind[k].Value, MainIdx = nodeMain[k] };
                if (nodeKind[k] == StationKind.Head)
                {
                    (bool useSprig, XYZ target) = SprigDecision(ctx, T, rpt);
                    st.UseSprig = useSprig; st.Target = target;
                }
                else { st.Target = T; }
                work.Stations.Add(st);
            }
            return work;
        }

        /// <summary>Build one branch as a shallow V (low at the main crossing, rising to
        /// both outer edges). <paramref name="headU"/> is the line's head stations — one
        /// continuous tiling of the sequence across the whole span — so the head-to-head
        /// gap straddling the main matches every other gap. The main has already been
        /// shifted clear of those stations; heads never move for it.</summary>
        private LineWork BuildBranchV(Ctx ctx, List<double> headU, double uMain, double m,
                                      double branchLowZ, double branchSlope, Report rpt)
        {
            var leftHeads = headU.Where(u => u < uMain).ToList();
            var rightHeads = headU.Where(u => u > uMain).ToList();

            // A short horizontal run centered on the crossing keeps the riser-top tee's
            // two run connectors collinear (a sloped V would kink and defeat NewTeeFitting).
            // The pipe is flat within flatHalf of the crossing, then slopes up to the edges,
            // so the slope break lands on the Joint nodes — couplings, which tolerate a kink.
            // It must never land on a HEAD, whose tee needs collinear runs; MainClearFloor
            // keeps every head outside the flat zone by a buildable margin.
            double flatHalf = branchSlope > 1e-9 ? CrossFlatFt / 2.0 : 0.0;
            Func<double, double> bz = u => branchLowZ + branchSlope * Math.Max(0.0, Math.Abs(u - uMain) - flatHalf);
            Func<double, XYZ> at = u => ctx.Pt(u, m, bz(u));

            // Node u-list (ascending): [leftCap] leftHeads uMain rightHeads [rightCap].
            // A cap stub is only added where that side has heads AND there is a real
            // extend-to-cap distance (else the outermost head IS the branch end — no
            // degenerate zero-length segment, and nothing to cap there).
            bool addLeftCap = leftHeads.Count > 0 && ctx.CapEnds && ctx.ExtendToCapFt > 0.02;
            bool addRightCap = rightHeads.Count > 0 && ctx.CapEnds && ctx.ExtendToCapFt > 0.02;

            var work = new LineWork();
            var nodeU = new List<double>();
            var nodeKind = new List<StationKind?>();   // null = cap
            if (addLeftCap) { nodeU.Add(leftHeads[0] - ctx.ExtendToCapFt); nodeKind.Add(null); work.HasLeftCap = true; work.LeftCapPt = at(nodeU[0]); }
            foreach (var u in leftHeads) { nodeU.Add(u); nodeKind.Add(StationKind.Head); }
            if (flatHalf > 0) { nodeU.Add(uMain - flatHalf); nodeKind.Add(StationKind.Joint); }
            nodeU.Add(uMain); nodeKind.Add(StationKind.Crossing);
            if (flatHalf > 0) { nodeU.Add(uMain + flatHalf); nodeKind.Add(StationKind.Joint); }
            foreach (var u in rightHeads) { nodeU.Add(u); nodeKind.Add(StationKind.Head); }
            if (addRightCap) { double rc = rightHeads[rightHeads.Count - 1] + ctx.ExtendToCapFt; nodeU.Add(rc); nodeKind.Add(null); work.HasRightCap = true; work.RightCapPt = at(rc); }

            for (int k = 0; k + 1 < nodeU.Count; k++)
                work.Segs.Add(CreatePipe(ctx, at(nodeU[k]), at(nodeU[k + 1]), ctx.LineSizeFt, ctx.PipeTypeId, rpt));

            for (int k = 0; k < nodeU.Count; k++)
            {
                if (nodeKind[k] == null) continue;
                XYZ T = at(nodeU[k]);
                var st = new Station { T = T, NodeIndex = k, Kind = nodeKind[k].Value };
                if (nodeKind[k] == StationKind.Head)
                {
                    (bool useSprig, XYZ target) = SprigDecision(ctx, T, rpt);
                    st.UseSprig = useSprig; st.Target = target;
                }
                else { st.Target = T; }   // crossing: nipple handled after connect
                work.Stations.Add(st);
            }
            return work;
        }

        // ── Shared placement / connection ──

        private void PlaceHeads(Ctx ctx, List<LineWork> lines, Report rpt)
        {
            if (ctx.HeadSym == null) return;
            foreach (var work in lines)
                foreach (var st in work.Stations)
                {
                    if (st.Kind != StationKind.Head) continue;
                    try { st.Head = ctx.Doc.Create.NewFamilyInstance(st.Target, ctx.HeadSym, ctx.Level, StructuralType.NonStructural); }
                    catch { }
                    if (st.Head != null) rpt.Heads++; else rpt.Fail++;
                }
        }

        private void RepinHeads(Ctx ctx, List<LineWork> lines)
        {
            foreach (var work in lines)
                foreach (var st in work.Stations)
                {
                    if (st.Head == null) continue;
                    Connector inlet = InletConnector(st.Head);
                    if (inlet == null) continue;
                    XYZ d = st.Target - inlet.Origin;
                    if (d.GetLength() > 1e-6 && st.Head.Location is LocationPoint lp)
                    { try { lp.Point = lp.Point + d; } catch { } }
                }
        }

        /// <summary>Connect a line's head/sprig stations to its collinear segments (tee
        /// interior, elbow at the last). Records the crossing station's run/branch
        /// connectors so the caller can add the riser nipple.</summary>
        private void ConnectLine(Ctx ctx, LineWork work, Report rpt)
        {
            Document doc = ctx.Doc;
            foreach (var st in work.Stations)
            {
                // Uniform node indexing: node k sits between Segs[k-1] and Segs[k].
                int k = st.NodeIndex;
                Pipe segBefore = k - 1 >= 0 && k - 1 < work.Segs.Count ? work.Segs[k - 1] : null;
                Pipe segAfter = k >= 0 && k < work.Segs.Count ? work.Segs[k] : null;

                Connector cA = segBefore != null ? FreeEndNear(segBefore, st.T) : null;
                Connector cB = segAfter != null ? FreeEndNear(segAfter, st.T) : null;

                if (st.Kind == StationKind.Joint)
                {
                    // Slope transition (flat run ↔ sloped half): a plain grooved coupling.
                    if (cA != null && cB != null) { try { cA.ConnectTo(cB); rpt.Joints++; } catch { rpt.Fail++; } }
                    continue;
                }

                if (st.Kind == StationKind.Crossing)
                {
                    // The branch runs continuously through; the nipple (added later) is
                    // the tee branch. Remember the two run ends for the caller.
                    st.RunA = cA; st.RunB = cB;
                    continue;
                }

                Connector branchConn = null;
                if (st.UseSprig)
                {
                    Pipe sprig = CreatePipe(ctx, st.T, new XYZ(st.T.X, st.T.Y, st.Target.Z), ctx.SprigSizeFt, ctx.SprigPipeTypeId, rpt);
                    if (sprig != null) { st.Sprig = sprig; rpt.Sprigs++; branchConn = FreeEndNear(sprig, st.T); }
                }
                else if (st.Head != null)
                {
                    branchConn = InletConnector(st.Head);
                }

                if (cA != null && cB != null && branchConn != null)
                {
                    try { doc.Create.NewTeeFitting(cA, cB, branchConn); rpt.Tees++; }
                    catch { try { cA.ConnectTo(cB); branchConn.ConnectTo(cA); rpt.PlainConn++; } catch { rpt.Fail++; } }
                }
                else if ((cA != null || cB != null) && branchConn != null)
                {
                    // end station — elbow with whichever single segment is present
                    Connector one = cA ?? cB;
                    try { doc.Create.NewElbowFitting(one, branchConn); rpt.Elbows++; }
                    catch { try { one.ConnectTo(branchConn); rpt.PlainConn++; } catch { rpt.Fail++; } }
                }
                else if (cA != null && cB != null)
                {
                    try { cA.ConnectTo(cB); rpt.PlainConn++; } catch { rpt.Fail++; }
                }

                if (st.Sprig != null && st.Head != null)
                {
                    Connector top = FreeEndNear(st.Sprig, new XYZ(st.T.X, st.T.Y, st.Target.Z));
                    Connector inlet = InletConnector(st.Head);
                    if (top != null && inlet != null && top.Origin.DistanceTo(inlet.Origin) < 0.1)
                    { try { top.ConnectTo(inlet); rpt.HeadConn++; } catch { rpt.Fail++; } }
                }
                else if (!st.UseSprig && st.Head != null && branchConn != null && branchConn.IsConnected)
                { rpt.HeadConn++; }
            }

            foreach (var st in work.Stations)
                if (st.Sprig != null) SetDiameter(st.Sprig, ctx.SprigSizeFt);
        }

        /// <summary>Break the main into its two collinear pieces + the nipple bottom as a
        /// tee (the outlet / GOL), forcing the chosen outlet family; elbow at a main end.</summary>
        private void JoinRun(Ctx ctx, Connector mainA, Connector mainB, Connector branch, Report rpt)
        {
            Document doc = ctx.Doc;
            if (branch == null) { rpt.Fail++; return; }
            if (mainA != null && mainB != null)
            {
                FamilySymbol chosen = Resolve(doc, ctx.OutletSymId);
                try
                {
                    if (chosen != null)
                    {
                        if (!chosen.IsActive) { chosen.Activate(); doc.Regenerate(); }
                        WithForcedJunctionFitting(ctx.MainPipeType ?? ctx.PipeType, chosen.Id, () => doc.Create.NewTeeFitting(mainA, mainB, branch));
                        rpt.Tees++; rpt.ChosenFit++;
                    }
                    else { doc.Create.NewTeeFitting(mainA, mainB, branch); rpt.Tees++; rpt.DefaultFit++; }
                }
                catch { try { mainA.ConnectTo(mainB); branch.ConnectTo(mainA); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
            else if (mainA != null || mainB != null)
            {
                Connector m = mainA ?? mainB;
                try { doc.Create.NewElbowFitting(m, branch); rpt.Elbows++; }
                catch { try { m.ConnectTo(branch); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
        }

        /// <summary>Tee the branch's two through halves + the nipple top at the crossing
        /// (a Firelock tee — force the chosen tee family; elbow if one half is missing).</summary>
        private void JoinBranch(Ctx ctx, Connector runA, Connector runB, Connector nipTop, Report rpt)
        {
            Document doc = ctx.Doc;
            if (nipTop == null) { rpt.Fail++; return; }
            if (runA != null && runB != null)
            {
                FamilySymbol chosen = Resolve(doc, ctx.RiserTeeSymId);
                try
                {
                    if (chosen != null)
                    {
                        if (!chosen.IsActive) { chosen.Activate(); doc.Regenerate(); }
                        WithForcedJunctionFitting(ctx.PipeType, chosen.Id, () => doc.Create.NewTeeFitting(runA, runB, nipTop));
                        rpt.Tees++; rpt.ChosenFit++;
                    }
                    else { doc.Create.NewTeeFitting(runA, runB, nipTop); rpt.Tees++; rpt.DefaultFit++; }
                }
                catch { try { runA.ConnectTo(runB); nipTop.ConnectTo(runA); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
            else if (runA != null || runB != null)
            {
                Connector r = runA ?? runB;
                try { doc.Create.NewElbowFitting(r, nipTop); rpt.Elbows++; }
                catch { try { r.ConnectTo(nipTop); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
        }

        // ── Fitting selection (force a chosen family via a temporary Junctions rule) ──

        private static readonly StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        private static List<FamilySymbol> FittingSymbols(Document doc) =>
            new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>().ToList();

        private static PartType GetPartType(FamilySymbol fs)
        {
            Parameter p = fs?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            return p != null ? (PartType)p.AsInteger() : PartType.Undefined;
        }

        private static string DefaultOutletName(List<FamilySymbol> fittings)
        {
            var fs = fittings.FirstOrDefault(f =>
            {
                string fam = f.FamilyName ?? "";
                PartType pt = GetPartType(f);
                bool tap = pt == PartType.TapAdjustable || pt == PartType.TapPerpendicular || pt == PartType.Tee;
                bool named = fam.IndexOf("GOL", OIC) >= 0 || fam.IndexOf("grooved outlet", OIC) >= 0;
                return tap && named;
            });
            return fs != null ? $"{fs.FamilyName} : {fs.Name}" : null;
        }

        private static string DefaultRiserTeeName(List<FamilySymbol> fittings)
        {
            var fs = fittings.FirstOrDefault(f =>
            {
                string fam = f.FamilyName ?? "";
                return GetPartType(f) == PartType.Tee
                    && fam.IndexOf("firelock", OIC) >= 0 && fam.IndexOf("tee", OIC) >= 0;
            });
            return fs != null ? $"{fs.FamilyName} : {fs.Name}" : null;
        }

        private static FamilySymbol Resolve(Document doc, ElementId id)
            => id != null && id != ElementId.InvalidElementId && doc.GetElement(id) is FamilySymbol fs ? fs : null;

        /// <summary>Run <paramref name="create"/> with a top-priority Junctions rule that
        /// forces <paramref name="fittingSymId"/>, then remove it (leaving the shared
        /// pipe type byte-identical). No-op wrapper if the type has no routing manager.</summary>
        private static void WithForcedJunctionFitting(PipeType pipeType, ElementId fittingSymId, Action create)
        {
            var rpm = pipeType?.RoutingPreferenceManager;
            if (rpm == null) { create(); return; }
            var rule = new RoutingPreferenceRule(fittingSymId, "SG temp force");
            rule.AddCriterion(new PrimarySizeCriterion(0.0, 100.0));   // any size (feet)
            rpm.AddRule(RoutingPreferenceRuleGroupType.Junctions, rule, 0);
            try { create(); }
            finally { try { rpm.RemoveRule(RoutingPreferenceRuleGroupType.Junctions, 0); } catch { } }
        }

        private (bool, XYZ) SprigDecision(Ctx ctx, XYZ T, Report rpt)
        {
            if (!ctx.UseSprigs) return (false, T);
            double topZ = ctx.CommonElev ? ctx.TermZ : T.Z + ctx.SprigLenFt;
            if (topZ - T.Z > SprigMinFt) return (true, new XYZ(T.X, T.Y, topZ));
            rpt.Outlet++;
            return (false, T);
        }

        private Pipe CreatePipe(Ctx ctx, XYZ a, XYZ b, double sizeFt, ElementId pipeTypeId, Report rpt)
        {
            if (a.DistanceTo(b) <= 0.01) return null;
            Pipe p = null;
            try { p = Pipe.Create(ctx.Doc, ctx.SysId, pipeTypeId, ctx.Level.Id, a, b); } catch { }
            if (p != null) { SetDiameter(p, sizeFt); rpt.Segs++; }
            else rpt.Fail++;
            return p;
        }

        private void Activate(Ctx ctx)
        {
            if (ctx.HeadSym != null && !ctx.HeadSym.IsActive) ctx.HeadSym.Activate();
            if (ctx.CapSym != null && !ctx.CapSym.IsActive) { ctx.CapSym.Activate(); ctx.Doc.Regenerate(); }
        }

        private bool Confirmed(int lines, int perLine)
        {
            long estimate = (long)lines * Math.Max(1, perLine);
            if (estimate <= 4000) return true;
            var confirm = new TaskDialog("Layout")
            {
                MainInstruction = $"This will place roughly {lines} lines and {estimate} heads.",
                MainContent = "That's a lot — double-check the spacings and the picked area. Continue?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            return confirm.Show() == TaskDialogResult.Yes;
        }

        // ── Cap fittings ──

        /// <summary>The cap FamilySymbol from a pipe type's routing preferences (Caps
        /// group); falls back to any loaded pipe-fitting family named "cap".</summary>
        private static FamilySymbol ResolveCapSymbol(Document doc, PipeType pipeType)
        {
            var rpm = pipeType?.RoutingPreferenceManager;
            if (rpm != null)
            {
                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Caps);
                for (int i = 0; i < n; i++)
                {
                    RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Caps, i);
                    if (doc.GetElement(rule.MEPPartId) is FamilySymbol fs) return fs;
                }
            }
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => (fs.FamilyName ?? "").IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Cap the open outer end of a segment nearest <paramref name="nearPt"/>
        /// with the size-matched cap type, then connect it. Matching the cap size to the
        /// pipe means Revit needs no reducing coupling to bridge the joint.</summary>
        private void CapEnd(Ctx ctx, Pipe seg, XYZ nearPt, Report rpt)
        {
            if (seg == null || ctx.CapSym == null) return;
            Document doc = ctx.Doc;
            Connector end = OpenEndNear(seg, nearPt);
            if (end == null) return;

            // Pick the cap TYPE whose nominal size matches this branch, so the cap comes
            // in the same size as the pipe and no reducing coupling is needed.
            double dia = PipeDiameter(seg);
            FamilySymbol capSym = CapSymbolForSize(ctx, dia);
            if (capSym == null) return;
            if (!capSym.IsActive) { capSym.Activate(); doc.Regenerate(); }

            FamilyInstance cap;
            try { cap = doc.Create.NewFamilyInstance(end.Origin, capSym, StructuralType.NonStructural); }
            catch { return; }
            if (cap == null) return;
            doc.Regenerate();

            Connector capConn = cap.MEPModel?.ConnectorManager?.Connectors?.Cast<Connector>().FirstOrDefault();
            if (capConn == null) { try { doc.Delete(cap.Id); } catch { } return; }

            XYZ desired = end.CoordinateSystem.BasisZ.Negate();
            XYZ current = capConn.CoordinateSystem.BasisZ;
            double angle = current.AngleTo(desired);
            if (angle > 1e-6)
            {
                XYZ axis = current.CrossProduct(desired);
                if (axis.GetLength() < 1e-9) axis = Math.Abs(current.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
                axis = axis.Normalize();
                try { ElementTransformUtils.RotateElement(doc, cap.Id, Line.CreateBound(capConn.Origin, capConn.Origin + axis), angle); }
                catch { }
                doc.Regenerate();
                capConn = cap.MEPModel.ConnectorManager.Connectors.Cast<Connector>().First();
            }

            XYZ delta = end.Origin - capConn.Origin;
            if (delta.GetLength() > 1e-9)
            {
                try { ElementTransformUtils.MoveElement(doc, cap.Id, delta); } catch { }
                doc.Regenerate();
                capConn = cap.MEPModel.ConnectorManager.Connectors.Cast<Connector>().First();
            }

            // A last resort in case the family is parametric (single type, size-driven).
            TrySetSize(cap, dia);
            // Connect the cap to the pipe. Because the cap type now matches the pipe size,
            // no reducing coupling is inserted.
            try { end.ConnectTo(capConn); } catch { }
            rpt.Caps++;
        }

        private static double PipeDiameter(Pipe p)
        {
            var d = p?.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            return d != null ? d.AsDouble() : 0.0;
        }

        /// <summary>The cap type in the resolved cap's family whose nominal size best
        /// matches <paramref name="diaFt"/> (within ~1/4"); the resolved default otherwise.</summary>
        private FamilySymbol CapSymbolForSize(Ctx ctx, double diaFt)
        {
            if (ctx.CapSym == null || diaFt <= 0) return ctx.CapSym;
            Family fam = ctx.CapSym.Family;
            if (fam == null) return ctx.CapSym;
            FamilySymbol best = null; double bestErr = double.MaxValue;
            foreach (ElementId sid in fam.GetFamilySymbolIds())
            {
                if (!(ctx.Doc.GetElement(sid) is FamilySymbol s)) continue;
                double sz = SymbolSize(s);
                if (sz <= 0) continue;
                double err = Math.Abs(sz - diaFt);
                if (err < bestErr) { bestErr = err; best = s; }
            }
            return best != null && bestErr < 0.02 ? best : ctx.CapSym;   // within ~1/4"
        }

        /// <summary>The nominal size (feet) of a fitting type — from a size parameter, or
        /// parsed from the type/family name (fab caps are named by size, e.g. "2", "1-1/2").</summary>
        private static double SymbolSize(FamilySymbol s)
        {
            foreach (var name in new[] { "Nominal Diameter", "Nominal Radius", "Nominal Size",
                                         "Size", "Diameter", "ND", "Pipe Size" })
            {
                Parameter p = s.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double v = p.AsDouble();
                    if (v > 0) return name.IndexOf("Radius", OIC) >= 0 ? v * 2.0 : v;
                }
            }
            double inch = ParseSizeInches(s.Name);
            if (inch <= 0) inch = ParseSizeInches(s.Family?.Name);
            return inch > 0 ? inch / 12.0 : 0;
        }

        /// <summary>Parse a leading pipe size in inches from a name — "2", "2\"", "2 in",
        /// "2.5", "1-1/2", "1 1/2", "3/4". Returns 0 if none in a plausible range.</summary>
        private static double ParseSizeInches(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*[- ]\s*(\d+)\s*/\s*(\d+)");  // 1-1/2 or 1 1/2
            if (m.Success) { double dd = Num(m.Groups[3]); return dd > 0 ? Num(m.Groups[1]) + Num(m.Groups[2]) / dd : 0; }
            m = System.Text.RegularExpressions.Regex.Match(s, @"(?<!\d)(\d+)\s*/\s*(\d+)");              // 3/4
            if (m.Success) { double dd = Num(m.Groups[2]); return dd > 0 ? Num(m.Groups[1]) / dd : 0; }
            m = System.Text.RegularExpressions.Regex.Match(s, @"\d+(?:\.\d+)?");                         // 2 or 2.5
            if (m.Success) { double v = Num(m.Groups[0]); if (v >= 0.25 && v <= 48) return v; }
            return 0;
        }

        private static double Num(System.Text.RegularExpressions.Group g)
            => double.TryParse(g.Value, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

        /// <summary>Drive a fitting's size to the pipe diameter via whatever writable size
        /// parameter it exposes. For the HCAD/Vic indexed cap, "Connector Diameter" is the
        /// writable driver (Nominal Diameter/Radius are read-only lookup outputs).</summary>
        private static void TrySetSize(FamilyInstance fi, double diaFt)
        {
            if (fi == null || diaFt <= 0) return;
            foreach (var name in new[] { "Connector Diameter", "Connector Radius",
                                         "Nominal Diameter", "Nominal Radius", "Size", "Diameter" })
            {
                Parameter p = fi.LookupParameter(name);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                { try { p.Set(name.IndexOf("Radius", OIC) >= 0 ? diaFt / 2.0 : diaFt); } catch { } return; }
            }
        }

        // ── Working state ──

        private enum StationKind { Head, Crossing, Joint }

        private class LineWork
        {
            public List<Pipe> Segs { get; } = new List<Pipe>();
            public List<Station> Stations { get; } = new List<Station>();
            public bool HasLeftCap, HasRightCap;   // main mode: is Segs[0]/Segs[last] a real outer cap stub?
            public XYZ LeftCapPt, RightCapPt;
        }

        private class Station
        {
            public XYZ T;
            public XYZ Target;
            public FamilyInstance Head;
            public Pipe Sprig;
            public bool UseSprig;
            public StationKind Kind = StationKind.Head;
            public int NodeIndex = -1;      // >=0 in main mode (index into Segs/nodes); -1 in fill mode
            public int MainIdx = -1;        // two-mains: which main (0/1) this crossing ties into
            public Connector RunA, RunB, BranchConn;   // crossing: the two through ends
        }

        // ── Geometry + Revit helpers ──

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>Cumulative offsets from cycling the gap pattern until the running
        /// total would exceed <paramref name="span"/>. Empty/zero pattern → empty list.</summary>
        private static List<double> Tile(double[] gaps, double span)
        {
            var offs = new List<double>();
            if (gaps == null || gaps.Length == 0) return offs;
            double cum = 0;
            for (int i = 0, guard = 0; guard < 100000; i++, guard++)
            {
                double gap = gaps[i % gaps.Length];
                if (gap <= 1e-6) break;
                cum += gap;
                if (cum > span + 0.02) break;
                offs.Add(cum);
            }
            return offs;
        }

        /// <summary>Head u-stations for a line: the head sequence tiled ONCE, continuously,
        /// from uMin across the whole span. A main crossing never breaks that rhythm — the
        /// head-to-head gap straddling the main is the same sequence gap as everywhere else.
        /// The main moves instead (see <see cref="ShiftMainClear"/>).</summary>
        private static List<double> HeadStations(double uMin, double uMax, double[] headGaps)
            => Tile(headGaps, uMax - uMin).Select(o => uMin + o).ToList();

        /// <summary>Smallest main-to-head clearance the branch geometry can actually take.
        /// A sloped branch is flat for CrossFlatFt/2 either side of the crossing so the riser
        /// tee's run connectors stay collinear; the slope break then sits on a Joint, which is
        /// only a coupling and tolerates the kink. A HEAD there would get a NewTeeFitting on
        /// non-collinear runs and fail, so no head may sit inside the flat zone — and it needs
        /// a buildable piece of pipe clear of it. Unsloped branches have no flat zone, so the
        /// user's value stands (down to one buildable piece).</summary>
        private static double MainClearFloor(double branchSlope)
            => branchSlope > 1e-9 ? CrossFlatFt / 2.0 + 2.0 * MinSegFt : MinSegFt;

        /// <summary>Centerline distance from <paramref name="u"/> to the nearest head.</summary>
        private static double HeadClearance(double u, List<double> heads)
        {
            double d = double.MaxValue;
            foreach (double h in heads) d = Math.Min(d, Math.Abs(h - u));
            return d;
        }

        /// <summary>Nudge a picked main off the heads it would land on. Head spacing is
        /// sacred, so the MAIN moves: if the nearest head centerline is closer than
        /// <paramref name="clearFt"/>, the main slides to that head's nearer side at exactly
        /// clearFt (staying inside [uLo, uHi]). <paramref name="shiftedFt"/> reports how far.</summary>
        private static double ShiftMainClear(double uMain, List<double> heads, double clearFt,
                                             double uLo, double uHi, out double shiftedFt)
        {
            shiftedFt = 0.0;
            if (clearFt <= 1e-6 || heads == null || heads.Count == 0) return uMain;
            if (HeadClearance(uMain, heads) >= clearFt - 1e-9) return uMain;

            double nearest = heads[0];
            foreach (double h in heads)
                if (Math.Abs(h - uMain) < Math.Abs(nearest - uMain)) nearest = h;

            // Shorter move first: the side of that head the pick already sits on.
            double near = nearest - clearFt, far = nearest + clearFt;
            foreach (double cand in uMain <= nearest ? new[] { near, far } : new[] { far, near })
            {
                if (cand < uLo - 1e-9 || cand > uHi + 1e-9) continue;
                if (HeadClearance(cand, heads) >= clearFt - 1e-9)
                { shiftedFt = Math.Abs(cand - uMain); return cand; }
            }

            // Heads packed tighter than 2x the clearance (or the area is too small): the
            // clearance can't be honored anywhere. Take the roomiest spot going — including
            // the middle of the gap the pick landed in, which beats both ±clearFt candidates
            // when the heads are that tight — and let the caller report the shortfall.
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            foreach (double h in heads)
            {
                if (h <= uMain && h > lo) lo = h;
                if (h >= uMain && h < hi) hi = h;
            }
            double mid = double.IsNegativeInfinity(lo) || double.IsPositiveInfinity(hi)
                ? uMain : 0.5 * (lo + hi);

            double best = Clamp(uMain, uLo, uHi), bestClear = HeadClearance(best, heads);
            foreach (double cand in new[] { near, far, mid })
            {
                double c2 = Clamp(cand, uLo, uHi);
                double c = HeadClearance(c2, heads);
                bool better = c > bestClear + 1e-9
                    || (c > bestClear - 1e-9 && Math.Abs(c2 - uMain) < Math.Abs(best - uMain) - 1e-9);
                if (better) { bestClear = c; best = c2; }
            }
            shiftedFt = Math.Abs(best - uMain);
            return best;
        }

        /// <summary>Line offsets from the mMin edge. With a start offset the first line sits
        /// that far from the FIRST-picked corner (<paramref name="fromMin"/> says which m-edge
        /// that corner is), then the gap sequence tiles inward; offset 0 keeps the original
        /// behavior (first line = first gap in from mMin).</summary>
        private static List<double> LineOffsets(double startOffset, double[] gaps, double span, bool fromMin)
        {
            if (startOffset <= 1e-6) return Tile(gaps, span);
            var offs = new List<double>();
            if (startOffset > span + 0.02) return offs;
            double cum = startOffset;
            offs.Add(cum);
            if (gaps != null && gaps.Length > 0)
                for (int i = 0, guard = 0; guard < 100000; i++, guard++)
                {
                    double gap = gaps[i % gaps.Length];
                    if (gap <= 1e-6) break;
                    cum += gap;
                    if (cum > span + 0.02) break;
                    offs.Add(cum);
                }
            if (!fromMin) for (int i = 0; i < offs.Count; i++) offs[i] = span - offs[i];
            offs.Sort();
            return offs;
        }

        /// <summary>True when the first-picked corner is on the mMin edge (so the start offset
        /// is measured from there); false when it's the mMax edge.</summary>
        private static bool FromMin(Ctx ctx, XYZ c1, double mMin, double mMax)
        {
            double c1m = ctx.LinesAlongX ? c1.Y : c1.X;
            return Math.Abs(c1m - mMin) <= Math.Abs(c1m - mMax);
        }

        private static void SetDiameter(MEPCurve curve, double sizeFt)
        {
            if (curve == null || sizeFt <= 0) return;
            try
            {
                var d = curve.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (d != null && !d.IsReadOnly) d.Set(sizeFt);
            }
            catch { }
        }

        private static Connector FreeEndNear(MEPCurve curve, XYZ pt)
        {
            if (curve?.ConnectorManager == null) return null;
            Connector best = null; double bestD = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End || c.IsConnected) continue;
                double d = c.Origin.DistanceTo(pt);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best != null && bestD < 0.1 ? best : null;
        }

        private static Connector OpenEndNear(MEPCurve curve, XYZ pt)
        {
            if (curve?.ConnectorManager == null) return null;
            Connector best = null; double bestD = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End || c.IsConnected) continue;
                double d = pt == null ? 0 : c.Origin.DistanceTo(pt);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private static Connector InletConnector(FamilyInstance head)
        {
            var cm = head?.MEPModel?.ConnectorManager;
            if (cm == null) return null;
            Connector best = null;
            foreach (Connector c in cm.Connectors)
            {
                if (c.Domain != Domain.DomainPiping) continue;
                if (c.IsConnected) continue;
                if (c.ConnectorType == ConnectorType.End) return c;
                best = best ?? c;
            }
            return best;
        }

        private static void ApplySwallow(Transaction tx)
        {
            try
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new WarningSwallower());
                tx.SetFailureHandlingOptions(opts);
            }
            catch { }
        }

        private class WarningSwallower : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (var f in a.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning)
                        a.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}
