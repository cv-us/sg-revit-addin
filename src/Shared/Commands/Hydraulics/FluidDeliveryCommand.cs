using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using SgRevitAddin.Utils.Pdf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SgRevitAddin.Commands.Hydraulics
{
    /// <summary>
    /// Fluid Delivery — estimates the water-delivery time for a dry / (double-interlock)
    /// preaction system: pick the source valve, flag the flowing (remote-area) heads by
    /// drawing a region, traverse the pipe network to the most-remote flowing head, and
    /// run the two-phase air-displacement model (see FluidDeliverySolver). Reports on
    /// screen and to a clean PDF.
    ///
    /// This is a documented engineering ESTIMATE (~±25-40%), not a listed calculation —
    /// verify final designs with a listed program (e.g. Tyco SprinkFDT) or a trip test.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FluidDeliveryCommand : IExternalCommand
    {
        private const string FlowParam = "Flowing (Hydratec)";
        private const string VolParam = "Volume Hydratec";
        private const double GalPerCf = 7.4805195;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── 1. Inputs ──
                var dlg = new FluidDeliveryDialog();
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                // ── 2. Source ──
                Element source;
                try
                {
                    var refX = uidoc.Selection.PickObject(ObjectType.Element,
                        new SourceFilter(),
                        "Pick the preaction / dry valve or riser (the water source)");
                    source = doc.GetElement(refX.ElementId);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                // ── 3. Region → design (flowing) heads ──
                List<FamilyInstance> allHeads = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Sprinklers)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                List<FamilyInstance> designHeads;
                if (dlg.RegionMode == RegionPickMode.ExistingFlowing)
                {
                    designHeads = allHeads.Where(IsFlowing).ToList();
                    if (designHeads.Count == 0)
                    {
                        TaskDialog.Show("Fluid Delivery",
                            "No heads in the active view have Flowing (Hydratec) set. " +
                            "Flag some, or choose a draw-a-region option.");
                        return Result.Cancelled;
                    }
                }
                else
                {
                    Func<XYZ, bool> inRegion;
                    try { inRegion = PickRegion(uidoc, dlg.RegionMode, doc.ActiveView); }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                    if (inRegion == null) return Result.Cancelled;

                    designHeads = allHeads
                        .Where(h => (h.Location as LocationPoint)?.Point is XYZ pt && inRegion(pt))
                        .ToList();
                    if (designHeads.Count == 0)
                    {
                        TaskDialog.Show("Fluid Delivery", "No sprinkler heads fell inside the region.");
                        return Result.Cancelled;
                    }

                    // Flag them Flowing (Hydratec) = 1.
                    using (var tw = new TransactionWrapper(doc, "Flag flowing heads"))
                    {
                        foreach (var h in designHeads)
                        {
                            Parameter p = h.LookupParameter(FlowParam);
                            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
                                p.Set(1);
                        }
                        tw.Commit();
                    }
                }

                // ── 4. Traversal scope (dry system[s] the flowing heads live in) ──
                var allowedSystems = new HashSet<string>(
                    designHeads.Select(SystemName).Where(s => !string.IsNullOrEmpty(s)),
                    StringComparer.OrdinalIgnoreCase);

                // ── 5. Dijkstra from the source over the connector graph ──
                var reach = Traverse(doc, source, allowedSystems, designHeads);

                var reached = designHeads.Where(h => reach.Prev.ContainsKey(h.Id.IntegerValue) || h.Id.IntegerValue == reach.SourceId).ToList();
                if (reached.Count == 0)
                {
                    TaskDialog.Show("Fluid Delivery",
                        "Couldn't trace the pipe network from the source to any flagged head.\n\n" +
                        "Check that the source connects (through pipe) to the flowing heads and that the " +
                        "connectors are actually joined in Revit (an unjoined butt joint dead-ends the trace).");
                    return Result.Failed;
                }

                // ── 6. Whole-system volume (code gate + trip-phase blowdown) ──
                double vsysGal = SystemVolumeGal(doc, allowedSystems);

                // K-factor (uniform assumption)
                double kFactor = dlg.KFactor;
                if (dlg.ReadKFromHeads)
                {
                    double? k = ReadK(reached[0]);
                    if (k.HasValue && k.Value > 0) kFactor = k.Value;
                }

                // ── 7. Solve per reachable head; the governing head is the slowest ──
                FluidDeliveryResult best = null;
                FamilyInstance bestHead = null;
                List<FlowSegment> bestSegs = null;

                foreach (var head in reached)
                {
                    var segs = BuildPath(reach, head);
                    if (segs.Count == 0) continue;

                    var inp = new FluidDeliveryInputs
                    {
                        Path = segs,
                        SystemVolumeGal = vsysGal,
                        KFactor = kFactor,
                        OpenHeads = designHeads.Count,
                        SupplyStaticPsi = dlg.SupplyStaticPsi,
                        SupplyResidualPsi = dlg.SupplyResidualPsi,
                        SupplyResidualFlowGpm = dlg.SupplyResidualFlowGpm,
                        CFactor = dlg.CFactor,
                        Nitrogen = dlg.Nitrogen,
                        GasTempF = dlg.GasTempF,
                        SupervisoryPsi = dlg.SupervisoryPsi,
                        TripPsi = dlg.TripPsi,
                        ModelTripPhase = dlg.ModelBlowdown,
                        DetectionSec = dlg.ModelBlowdown ? 0 : dlg.LatencySec,
                        ValveLatencySec = 0,
                        TargetSec = dlg.TargetSec,
                        Hazard = dlg.Hazard
                    };
                    var r = FluidDeliverySolver.Solve(inp);
                    if (best == null || r.TotalSec > best.TotalSec)
                    {
                        best = r; bestHead = head; bestSegs = segs;
                    }
                }

                if (best == null)
                {
                    TaskDialog.Show("Fluid Delivery", "The flagged heads have no pipe path from the source.");
                    return Result.Failed;
                }

                // ── 8. Report ──
                var meta = new ReportMeta
                {
                    ProjectTitle = doc.Title,
                    SourceName = DescribeElement(source),
                    SystemName = string.Join(", ", allowedSystems),
                    HeadCount = designHeads.Count,
                    ReachedCount = reached.Count,
                    GoverningHeadId = bestHead.Id.IntegerValue,
                    KFactor = kFactor,
                    ValveMode = dlg.ModelBlowdown ? "Dry-pipe differential (air blowdown modeled)" : "Preaction — electric double-interlock (latency)",
                    Supply = $"{dlg.SupplyStaticPsi:0.#} psi static / {dlg.SupplyResidualPsi:0.#} psi @ {dlg.SupplyResidualFlowGpm:0} gpm",
                    CFactor = dlg.CFactor,
                    Nitrogen = dlg.Nitrogen,
                    GasTempF = dlg.GasTempF,
                    LatencySec = dlg.LatencySec,
                    SupervisoryPsi = dlg.SupervisoryPsi,
                    TripPsi = dlg.TripPsi,
                    HazardName = dlg.Hazard.ToString()
                };

                string body = BuildTextReport(best, meta);
                string headline = $"Total delivery: {best.TotalSec:0.0} s   (target {best.TargetSec:0} s)   —   {(best.Pass ? "PASS" : "FAIL")}";

                Action savePdf = () => SavePdf(best, meta, doc);
                using (var rd = new FluidDeliveryResultsDialog(headline, best.Pass, body, savePdf))
                    rd.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Region picking
        // ════════════════════════════════════════════════════════════════
        private Func<XYZ, bool> PickRegion(UIDocument uidoc, RegionPickMode mode, View view)
        {
            if (mode == RegionPickMode.Rectangle)
            {
                PickedBox box = uidoc.Selection.PickBox(PickBoxStyle.Crossing,
                    "Drag a box around the flowing heads (2 corners)");
                if (box == null) return null;
                // PickBox corners are model-space points of a SCREEN-aligned rubber band, so
                // test in the view's right/up basis — correct even when the plan view is rotated
                // (a raw model-X/Y box would silently mis-select heads in a rotated view).
                XYZ o = view.Origin, rt = view.RightDirection, up = view.UpDirection;
                double u1 = (box.Min - o).DotProduct(rt), u2 = (box.Max - o).DotProduct(rt);
                double v1 = (box.Min - o).DotProduct(up), v2 = (box.Max - o).DotProduct(up);
                double ulo = Math.Min(u1, u2), uhi = Math.Max(u1, u2);
                double vlo = Math.Min(v1, v2), vhi = Math.Max(v1, v2);
                return pt =>
                {
                    double pu = (pt - o).DotProduct(rt), pv = (pt - o).DotProduct(up);
                    return pu >= ulo && pu <= uhi && pv >= vlo && pv <= vhi;
                };
            }

            // Polygon: click corners; Esc finishes.
            var poly = new List<XYZ>();
            try
            {
                while (true)
                {
                    XYZ p = uidoc.Selection.PickPoint(
                        $"Click region corners ({poly.Count} placed) — Esc/Enter to finish");
                    poly.Add(p);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* done */ }

            if (poly.Count < 3) return null;
            return pt => PointInPolygonXY(pt.X, pt.Y, poly);
        }

        private static bool PointInPolygonXY(double px, double py, List<XYZ> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y, xj = poly[j].X, yj = poly[j].Y;
                bool cross = ((yi > py) != (yj > py)) &&
                             (px < (xj - xi) * (py - yi) / ((yj - yi) == 0 ? 1e-12 : (yj - yi)) + xi);
                if (cross) inside = !inside;
            }
            return inside;
        }

        // ════════════════════════════════════════════════════════════════
        //  Network traversal (Dijkstra over the connector graph)
        // ════════════════════════════════════════════════════════════════
        private sealed class Reach
        {
            public int SourceId;
            public Dictionary<int, int> Prev = new Dictionary<int, int>();
            public Dictionary<int, Element> Elem = new Dictionary<int, Element>();
        }

        private Reach Traverse(Document doc, Element source, HashSet<string> allowedSystems,
                               List<FamilyInstance> designHeads)
        {
            var r = new Reach { SourceId = source.Id.IntegerValue };
            var dist = new Dictionary<int, double>();
            var pq = new SortedSet<(double d, int id)>();

            int s0 = r.SourceId;
            dist[s0] = 0; r.Elem[s0] = source; pq.Add((0, s0));

            var targets = new HashSet<int>(designHeads.Select(h => h.Id.IntegerValue));
            int settledTargets = 0;

            while (pq.Count > 0)
            {
                var top = pq.Min; pq.Remove(top);
                double d = top.d; int cid = top.id;
                if (dist.TryGetValue(cid, out double dc) && d > dc) continue;
                Element cur = r.Elem[cid];
                if (targets.Contains(cid) && ++settledTargets >= targets.Count) break;

                ConnectorSet cs = GetConnectors(cur);
                if (cs == null) continue;
                foreach (Connector c in cs)
                {
                    if (c.ConnectorType != ConnectorType.End) continue;
                    ConnectorSet refs;
                    try { refs = c.AllRefs; } catch { continue; }
                    if (refs == null) continue;
                    foreach (Connector o in refs)
                    {
                        Element nb = o.Owner;
                        if (nb == null || nb.Id == cur.Id) continue;
                        int nid = nb.Id.IntegerValue;
                        // scope: source, a direct neighbour of the source, or in an allowed system
                        if (!(nid == s0 || cid == s0 || allowedSystems.Contains(SystemName(nb))))
                            continue;
                        double w = (nb is Pipe || nb is FlexPipe) ? LengthFt(nb) : 0.0;
                        double nd = d + w;
                        if (!dist.TryGetValue(nid, out double old) || nd < old)
                        {
                            dist[nid] = nd;
                            r.Prev[nid] = cid;
                            r.Elem[nid] = nb;
                            pq.Add((nd, nid));
                        }
                    }
                }
            }
            return r;
        }

        /// <summary>Pipe/flex segments from the source to the given head, in flow order.</summary>
        private List<FlowSegment> BuildPath(Reach r, Element head)
        {
            var nodes = new List<Element>();
            int cur = head.Id.IntegerValue;
            var guard = new HashSet<int>();
            while (true)
            {
                if (!r.Elem.TryGetValue(cur, out Element e)) break;
                if (!guard.Add(cur)) break;              // cycle guard
                nodes.Add(e);
                if (cur == r.SourceId) break;
                if (!r.Prev.TryGetValue(cur, out int p)) break;
                cur = p;
            }
            nodes.Reverse();   // source → head

            var segs = new List<FlowSegment>();
            for (int i = 0; i < nodes.Count; i++)
            {
                Element e = nodes[i];
                if (!(e is Pipe || e is FlexPipe)) continue;
                Element prevNode = i > 0 ? nodes[i - 1] : null;
                Element nextNode = i < nodes.Count - 1 ? nodes[i + 1] : null;
                segs.Add(new FlowSegment
                {
                    Label = SizeLabel(e),
                    LengthFt = LengthFt(e),
                    InnerDiaIn = InnerDiaIn(e),
                    RiseFt = PipeRise(e, prevNode?.Id, nextNode?.Id),
                    Gallons = Gallons(e)
                });
            }
            return segs;
        }

        // ════════════════════════════════════════════════════════════════
        //  Element data helpers
        // ════════════════════════════════════════════════════════════════
        private static ConnectorSet GetConnectors(Element e)
        {
            if (e is Pipe p) return p.ConnectorManager?.Connectors;
            if (e is FlexPipe fp) return fp.ConnectorManager?.Connectors;
            if (e is FamilyInstance fi) return fi.MEPModel?.ConnectorManager?.Connectors;
            return null;
        }

        private static string SystemName(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
            string s = p?.AsString();
            if (!string.IsNullOrEmpty(s)) return s;
            return e.LookupParameter("System Name")?.AsString() ?? string.Empty;
        }

        private static double LengthFt(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            if (e is MEPCurve mc && mc.Location is LocationCurve lc && lc.Curve != null) return lc.Curve.Length;
            return 0;
        }

        private static double InnerDiaIn(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.RBS_PIPE_INNER_DIAM_PARAM);
            if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0) return p.AsDouble() * 12.0;
            p = e.LookupParameter("Inside Diameter");
            if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0) return p.AsDouble() * 12.0;
            p = e.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (p != null && p.StorageType == StorageType.Double && p.AsDouble() > 0) return p.AsDouble() * 12.0;
            return 1.0;
        }

        private static double Gallons(Element e)
        {
            Parameter v = e.LookupParameter(VolParam);   // Hydratec per-pipe volume, raw = cubic feet
            if (v != null && v.StorageType == StorageType.Double && v.AsDouble() > 0)
                return v.AsDouble() * GalPerCf;
            double idIn = InnerDiaIn(e), lenFt = LengthFt(e);
            return Math.PI * Math.Pow(idIn / 2.0, 2) * lenFt * 12.0 / 231.0;
        }

        private static string SizeLabel(Element e)
        {
            string s = e.LookupParameter("Size")?.AsString();
            if (!string.IsNullOrEmpty(s)) return s.Replace("\"", "\"");
            Parameter d = e.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (d != null) return $"{d.AsDouble() * 12.0:0.##}\"";
            return e is FlexPipe ? "flex" : "pipe";
        }

        private double PipeRise(Element pipe, ElementId prevId, ElementId nextId)
        {
            ConnectorSet cs = GetConnectors(pipe);
            if (cs == null) return 0;
            var ends = new List<Connector>();
            foreach (Connector c in cs) if (c.ConnectorType == ConnectorType.End) ends.Add(c);
            if (ends.Count < 2) return 0;

            // Orient by the actual joined nodes, not by (unordered) connector index.
            Connector up = EndTouching(ends, prevId);     // end joined to the upstream node
            Connector down = EndTouching(ends, nextId);   // end joined to the downstream node
            if (up == null) up = ends.FirstOrDefault(c => !ReferenceEquals(c, down)) ?? ends[0];
            if (down == null) down = ends.FirstOrDefault(c => !ReferenceEquals(c, up)) ?? ends[1];
            try { return down.Origin.Z - up.Origin.Z; } catch { return 0; }
        }

        private static Connector EndTouching(List<Connector> ends, ElementId id)
        {
            if (id == null) return null;
            foreach (var c in ends)
            {
                ConnectorSet refs;
                try { refs = c.AllRefs; } catch { continue; }
                if (refs == null) continue;
                foreach (Connector o in refs)
                    if (o.Owner != null && o.Owner.Id == id) return c;
            }
            return null;
        }

        private static double SystemVolumeGal(Document doc, HashSet<string> allowedSystems)
        {
            double vsys = 0;
            foreach (Pipe p in new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>())
                if (allowedSystems.Contains(SystemName(p))) vsys += Gallons(p);
            foreach (FlexPipe f in new FilteredElementCollector(doc).OfClass(typeof(FlexPipe)).Cast<FlexPipe>())
                if (allowedSystems.Contains(SystemName(f))) vsys += Gallons(f);
            return vsys;
        }

        private static bool IsFlowing(FamilyInstance h)
        {
            Parameter p = h.LookupParameter(FlowParam);
            return p != null && p.StorageType == StorageType.Integer && p.AsInteger() == 1;
        }

        private static double? ReadK(Element head)
        {
            if (head is FamilyInstance fi && fi.Symbol != null)
            {
                Parameter p = fi.Symbol.LookupParameter("K-Factor");
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            return null;
        }

        private static string DescribeElement(Element e)
        {
            string fam = (e as FamilyInstance)?.Symbol?.Family?.Name;
            return $"id {e.Id.IntegerValue} — {(string.IsNullOrEmpty(fam) ? e.Name : fam)} ({e.Category?.Name})";
        }

        // ════════════════════════════════════════════════════════════════
        //  Reporting
        // ════════════════════════════════════════════════════════════════
        private sealed class ReportMeta
        {
            public string ProjectTitle, SourceName, SystemName, ValveMode, Supply, HazardName;
            public int HeadCount, ReachedCount, GoverningHeadId;
            public double KFactor, CFactor, GasTempF, LatencySec, SupervisoryPsi, TripPsi;
            public bool Nitrogen;
        }

        private const string Caveat =
            "ENGINEERING ESTIMATE (approx. +/-25-40%). Tree-layout, quasi-steady air displacement. " +
            "NOT a listed calculation and NOT for NFPA code compliance — verify with a listed program " +
            "(e.g. Tyco SprinkFDT) or a physical trip test.";

        private static string BuildTextReport(FluidDeliveryResult r, ReportMeta m)
        {
            var sb = new StringBuilder();
            string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

            sb.AppendLine($"TOTAL WATER DELIVERY : {F(r.TotalSec)} s      (target {r.TargetSec:0} s)   {(r.Pass ? "PASS" : "FAIL")}");
            sb.AppendLine();
            sb.AppendLine($"   detection / valve : {F(r.DetectionSec + r.ValveSec)} s");
            sb.AppendLine($"   trip (air blowdown): {F(r.TripSec)} s");
            sb.AppendLine($"   water transit      : {F(r.TransitSec)} s");
            sb.AppendLine();
            sb.AppendLine($"Governing head    : id {m.GoverningHeadId}   (slowest of {m.ReachedCount} reached / {m.HeadCount} flagged)");
            sb.AppendLine($"System            : {m.SystemName}");
            sb.AppendLine($"Source            : {m.SourceName}");
            sb.AppendLine($"System volume     : {r.SystemVolumeGal:0.0} gal    Path volume: {r.PathVolumeGal:0.0} gal");
            sb.AppendLine($"Open heads (N)    : {m.HeadCount}      K-factor: {m.KFactor:0.0}");
            sb.AppendLine($"Valve model       : {m.ValveMode}");
            sb.AppendLine($"Supply            : {m.Supply}");
            sb.AppendLine($"C-factor          : {m.CFactor:0.#}{(m.Nitrogen ? "  (nitrogen)" : "")}     Gas: {m.GasTempF:0} F");
            sb.AppendLine($"Hazard            : {m.HazardName}");
            sb.AppendLine();
            sb.AppendLine("PATH  (source -> governing head)");
            sb.AppendLine("  #  size      len(ft)   gal    fill(gpm)  regime     cum(s)");
            sb.AppendLine("  -- --------- -------- ------ ---------- ---------- --------");
            int i = 1;
            foreach (var s in r.Segments)
                sb.AppendLine($"  {i++,2} {Trunc(s.Label, 9),-9} {s.LengthFt,8:0.0} {s.Gallons,6:0.00} {s.FillGpm,10:0.0} {s.Regime,-10} {s.CumTimeSec,8:0.0}");
            sb.AppendLine();
            if (r.Warnings.Count > 0)
            {
                sb.AppendLine("WARNINGS");
                foreach (var w in r.Warnings) sb.AppendLine("  - " + w);
                sb.AppendLine();
            }
            if (r.Notes.Count > 0)
            {
                sb.AppendLine("NOTES");
                foreach (var n in r.Notes) sb.AppendLine("  - " + n);
                sb.AppendLine();
            }
            sb.AppendLine(Caveat);
            return sb.ToString();
        }

        private static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

        private void SavePdf(FluidDeliveryResult r, ReportMeta m, Document doc)
        {
            using (var save = new System.Windows.Forms.SaveFileDialog
            {
                Title = "Save Fluid Delivery Report",
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                DefaultExt = "pdf",
                FileName = $"FluidDelivery_{SafeName(m.ProjectTitle)}_{DateTime.Now:yyyyMMdd_HHmm}",
                OverwritePrompt = true
            })
            {
                if (save.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                var pdf = new PdfDocument();
                pdf.Line("Fluid Delivery — Water Delivery Time Estimate", 18, bold: true);
                pdf.Line($"Project: {m.ProjectTitle}", 10);
                pdf.HLine();
                pdf.Gap(6);

                pdf.Line($"TOTAL DELIVERY:  {r.TotalSec:0.0} s     (target {r.TargetSec:0} s)     {(r.Pass ? "PASS" : "FAIL")}", 13, bold: true);
                pdf.Gap(4);
                pdf.KeyValue("Detection/valve:", $"{r.DetectionSec + r.ValveSec:0.0} s");
                pdf.KeyValue("Trip (air blowdown):", $"{r.TripSec:0.0} s");
                pdf.KeyValue("Water transit:", $"{r.TransitSec:0.0} s");
                pdf.Gap(8);

                pdf.KeyValue("Generated:", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                pdf.KeyValue("System:", m.SystemName);
                pdf.KeyValue("Source:", m.SourceName);
                pdf.KeyValue("Governing head:", $"id {m.GoverningHeadId}  ({m.ReachedCount} reached / {m.HeadCount} flagged)");
                pdf.KeyValue("System volume:", $"{r.SystemVolumeGal:0.0} gal");
                pdf.KeyValue("Path volume:", $"{r.PathVolumeGal:0.0} gal");
                pdf.KeyValue("Open heads (N):", m.HeadCount.ToString());
                pdf.KeyValue("K-factor:", $"{m.KFactor:0.0}");
                pdf.KeyValue("Valve model:", m.ValveMode);
                pdf.KeyValue("Supply:", m.Supply);
                pdf.KeyValue("C-factor:", $"{m.CFactor:0.#}{(m.Nitrogen ? "  (nitrogen)" : "")}");
                pdf.KeyValue("Gas temp:", $"{m.GasTempF:0} F");
                pdf.KeyValue("Hazard:", m.HazardName);
                pdf.Gap(10);

                pdf.Line("Path  (source → governing head)", 12, bold: true);
                pdf.Gap(4);
                var rows = new List<string[]>();
                int i = 1;
                foreach (var s in r.Segments)
                    rows.Add(new[] { (i++).ToString(), Trunc(s.Label, 10), s.LengthFt.ToString("0.0"),
                                     s.Gallons.ToString("0.00"), s.FillGpm.ToString("0.0"), s.Regime, s.CumTimeSec.ToString("0.0") });
                pdf.Table(
                    new[] { "#", "Size", "Len ft", "Gal", "Fill gpm", "Regime", "Cum s" },
                    new double[] { 26, 74, 54, 54, 60, 72, 54 },
                    rows);
                pdf.Gap(10);

                if (r.Warnings.Count > 0)
                {
                    pdf.Line("Warnings", 12, bold: true);
                    foreach (var w in r.Warnings) pdf.Line("• " + w, 9, indent: 6);
                    pdf.Gap(6);
                }
                if (r.Notes.Count > 0)
                {
                    pdf.Line("Notes", 12, bold: true);
                    foreach (var n in r.Notes) pdf.Line("• " + n, 9, indent: 6);
                    pdf.Gap(6);
                }

                pdf.Gap(6);
                pdf.HLine(0.5);
                pdf.Gap(4);
                foreach (var line in WrapText(Caveat, 95))
                    pdf.Line(line, 8);

                pdf.Save(save.FileName);
                TaskDialog.Show("Fluid Delivery", "Saved report to:\n" + save.FileName);
            }
        }

        private static IEnumerable<string> WrapText(string s, int width)
        {
            var words = s.Split(' ');
            var line = new StringBuilder();
            foreach (var w in words)
            {
                if (line.Length + w.Length + 1 > width) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 0) yield return line.ToString();
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "report";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        /// <summary>Allows the source pick to be a valve/accessory, pipe, or fitting.</summary>
        private class SourceFilter : ISelectionFilter
        {
            public bool AllowElement(Element e)
            {
                int c = e.Category?.Id.IntegerValue ?? 0;
                return c == (int)BuiltInCategory.OST_PipeAccessory
                    || c == (int)BuiltInCategory.OST_PipeCurves
                    || c == (int)BuiltInCategory.OST_PipeFitting
                    || c == (int)BuiltInCategory.OST_FlexPipeCurves;
            }
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
