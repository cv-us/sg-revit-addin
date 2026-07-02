using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// "Tag Pipes" — places pipe length/stocklist tags, recreating HydraCAD's Tag
    /// Pipes workflow with our own loaded tag families.
    ///
    /// Each of the four tag types (Center-to-Center, Cut, Dynamic, Stocklisting)
    /// just places the family the user picked for it — the family's own label reads
    /// whatever parameter it's built on (we don't compute lengths). Selection is
    /// either the user's pre-selected pipes or a System-Walker sweep of the whole
    /// connected piping network from the selected seed pipe(s). Drops (vertical
    /// pipes to a head) can be excluded, included, or tagged exclusively.
    ///
    /// Invoked from the Modify-tab SG button via <see cref="DeferredActionHandler"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagPipesCommand : IExternalCommand
    {
        // HydraCAD length parameters (for the Reset options). Read defensively —
        // if a project doesn't have them, those options simply no-op.
        private const string PCenterCenter = "Length-Center_Center (Hydratec)";
        private const string PCutLength = "Length-Cut_Length (Hydratec)";
        private const string PAdjustment = "Length-Adjustment (Hydratec)";

        private const double VerticalTolDeg = 30.0;
        private const int MaxWalk = 20000;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try { Run(commandData.Application); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        public static void Run(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Loaded pipe-tag families for the dropdowns.
            var pipeTagFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PipeTags)
                .Cast<FamilySymbol>()
                .Select(fs => fs.Family?.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pipeTagFamilies.Count == 0)
            {
                TaskDialog.Show("Tag Pipes",
                    "No Pipe Tag families are loaded in this project.\n\n" +
                    "Load your pipe tag families first, then run Tag Pipes.");
                return;
            }

            TagPipesDialog dlg;
            using (dlg = new TagPipesDialog(pipeTagFamilies))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            }

            // ── Seed pipes = current selection ──
            var seeds = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as Pipe)
                .Where(p => p != null)
                .ToList();

            if (seeds.Count == 0)
            {
                TaskDialog.Show("Tag Pipes",
                    dlg.UseSystemWalker
                        ? "Select one pipe to start the System Walker, then run Tag Pipes."
                        : "Select the pipes to tag, then run Tag Pipes.");
                return;
            }

            // ── Targets: walk the network, or use the selection ──
            List<Pipe> targets = dlg.UseSystemWalker ? WalkNetwork(seeds) : seeds;

            // ── Drops filter ──
            if (dlg.TagDropsOnly)
                targets = targets.Where(p => IsDrop(p)).ToList();
            else if (!dlg.IncludeDrops)
                targets = targets.Where(p => !IsDrop(p)).ToList();
            // else IncludeDrops → keep everything

            targets = targets.GroupBy(p => p.Id).Select(g => g.First()).ToList();
            if (targets.Count == 0)
            {
                TaskDialog.Show("Tag Pipes", "No pipes matched (check the Drops options).");
                return;
            }

            // ── Resolve tag symbols ──
            FamilySymbol mainSym = ResolveTagSymbol(doc, dlg.TagFamily, dlg.Transparent);
            if (mainSym == null)
            {
                TaskDialog.Show("Tag Pipes",
                    $"Tag family \"{dlg.TagFamily}\" not found in this project.");
                return;
            }
            FamilySymbol dropSym = string.IsNullOrEmpty(dlg.DropFamily)
                ? mainSym
                : (ResolveTagSymbol(doc, dlg.DropFamily, dlg.Transparent) ?? mainSym);

            int tagged = 0, skippedDup = 0, resetCount = 0, homogenized = 0;

            using (var tw = new TransactionWrapper(doc, "Tag Pipes"))
            {
                if (!mainSym.IsActive) mainSym.Activate();
                if (!dropSym.IsActive) dropSym.Activate();

                // Pipes already carrying a tag of the target family (skip duplicates).
                var alreadyTagged = PipesTaggedWithFamily(doc, view, mainSym.Family.Name);

                var placed = new List<IndependentTag>();
                foreach (var pipe in targets)
                {
                    bool drop = (dlg.TagDropsOnly || dlg.IncludeDrops) && IsDrop(pipe);
                    FamilySymbol sym = drop ? dropSym : mainSym;

                    if (alreadyTagged.Contains(pipe.Id)) { skippedDup++; continue; }

                    XYZ mid = MidPoint(pipe);
                    if (mid == null) continue;

                    try
                    {
                        var tag = IndependentTag.Create(doc, view.Id, new Reference(pipe),
                            false, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, mid);
                        if (tag != null)
                        {
                            tag.ChangeTypeId(sym.Id);
                            placed.Add(tag);
                            alreadyTagged.Add(pipe.Id);
                            tagged++;
                        }
                    }
                    catch { /* view can't tag this pipe — skip */ }

                    if (dlg.ResetTakeOut && ResetTakeOut(pipe)) resetCount++;
                    else if (dlg.ResetCut && ResetCut(pipe)) resetCount++;
                }

                // Homogenize: retype every existing pipe tag in the view to the chosen type.
                if (dlg.Homogenize)
                    homogenized = HomogenizePipeTags(doc, view, mainSym);

                // Cleanup: first-pass anti-overlap on the placed tags.
                if (dlg.RunCleanup && placed.Count > 1)
                    DecollideTags(doc, view, placed);

                tw.Commit();
            }

            string report = "Tag Pipes\n\n" +
                            $"Tags placed: {tagged}\n" +
                            $"Pipes considered: {targets.Count}" +
                            (dlg.UseSystemWalker ? "  (System Walker)" : "  (User Selection)") + "\n";
            if (skippedDup > 0) report += $"Skipped (already tagged): {skippedDup}\n";
            if (dlg.Homogenize) report += $"Existing pipe tags re-typed: {homogenized}\n";
            if (dlg.ResetTakeOut || dlg.ResetCut) report += $"Lengths reset: {resetCount}\n";
            if (dlg.RunCleanup) report += "Cleanup: overlap pass run on new tags\n";
            TaskDialog.Show("Tag Pipes", report);
        }

        // ══════════════════════════════════════════════════════════════
        //  NETWORK WALK
        // ══════════════════════════════════════════════════════════════

        private static List<Pipe> WalkNetwork(List<Pipe> seeds)
        {
            var found = new Dictionary<ElementId, Pipe>();
            var visited = new HashSet<ElementId>();
            var queue = new Queue<Element>();
            foreach (var s in seeds) { queue.Enqueue(s); }

            while (queue.Count > 0 && found.Count < MaxWalk)
            {
                Element cur = queue.Dequeue();
                if (cur == null || !visited.Add(cur.Id)) continue;
                if (cur is Pipe p) found[p.Id] = p;

                ConnectorSet conns = GetConnectors(cur);
                if (conns == null) continue;
                foreach (Connector c in conns)
                {
                    ConnectorSet refs;
                    try { refs = c.AllRefs; } catch { continue; }
                    if (refs == null) continue;
                    foreach (Connector other in refs)
                    {
                        Element owner = other.Owner;
                        if (owner == null || visited.Contains(owner.Id)) continue;
                        int cat = owner.Category?.Id.IntegerValue ?? 0;
                        // Traverse through pipes, flex pipes and pipe fittings only.
                        if (owner is Pipe || owner is FlexPipe ||
                            cat == (int)BuiltInCategory.OST_PipeFitting)
                            queue.Enqueue(owner);
                    }
                }
            }
            return found.Values.ToList();
        }

        // ══════════════════════════════════════════════════════════════
        //  DROP CLASSIFICATION
        // ══════════════════════════════════════════════════════════════

        private static bool IsDrop(Pipe pipe)
        {
            if (!IsVertical(pipe)) return false;
            // Vertical AND a sprinkler reachable within 2 hops of either end.
            var cm = pipe.ConnectorManager?.Connectors;
            if (cm == null) return false;
            foreach (Connector c in cm.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End))
                if (ReachesSprinkler(c, new HashSet<ElementId> { pipe.Id }, 2)) return true;
            return false;
        }

        private static bool IsVertical(Pipe pipe)
        {
            if (!(pipe.Location is LocationCurve lc) || !(lc.Curve is Line line)) return false;
            XYZ d = line.GetEndPoint(1) - line.GetEndPoint(0);
            double dz = Math.Abs(d.Z), dxy = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (dz < 1e-9) return false;
            return Math.Atan2(dxy, dz) * 180.0 / Math.PI <= VerticalTolDeg;
        }

        private static bool ReachesSprinkler(Connector from, HashSet<ElementId> visited, int hops)
        {
            if (from == null) return false;
            ConnectorSet refs;
            try { refs = from.AllRefs; } catch { return false; }
            if (refs == null) return false;
            foreach (Connector other in refs)
            {
                Element owner = other.Owner;
                if (owner == null || visited.Contains(owner.Id)) continue;
                int cat = owner.Category?.Id.IntegerValue ?? 0;
                if (cat == (int)BuiltInCategory.OST_Sprinklers) return true;
                if (hops > 0 && (cat == (int)BuiltInCategory.OST_PipeFitting || owner is FlexPipe))
                {
                    visited.Add(owner.Id);
                    var cs = GetConnectors(owner);
                    if (cs != null)
                        foreach (Connector oc in cs)
                            if (ReachesSprinkler(oc, visited, hops - 1)) return true;
                }
            }
            return false;
        }

        private static ConnectorSet GetConnectors(Element e)
        {
            if (e is Pipe p) return p.ConnectorManager?.Connectors;
            if (e is FlexPipe fp) return fp.ConnectorManager?.Connectors;
            if (e is FamilyInstance fi) return fi.MEPModel?.ConnectorManager?.Connectors;
            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  TAG SYMBOL RESOLUTION / EXISTING TAGS
        // ══════════════════════════════════════════════════════════════

        private static FamilySymbol ResolveTagSymbol(Document doc, string familyName, bool transparent)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            var syms = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PipeTags)
                .Cast<FamilySymbol>()
                .ToList();

            if (transparent)
            {
                // HydraCAD's transparent variant is a "-T" family. Prefer it if present.
                var t = syms.FirstOrDefault(s =>
                    string.Equals(s.Family?.Name, familyName + "-T", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Family?.Name, familyName + " -T", StringComparison.OrdinalIgnoreCase));
                if (t != null) return t;
            }
            return syms.FirstOrDefault(s =>
                string.Equals(s.Family?.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        private static HashSet<ElementId> PipesTaggedWithFamily(Document doc, View view, string familyName)
        {
            var set = new HashSet<ElementId>();
            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                var sym = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                if (sym?.Family?.Name != familyName) continue;
                try
                {
                    foreach (var lid in tag.GetTaggedElementIds())
                        if (lid.HostElementId != ElementId.InvalidElementId)
                            set.Add(lid.HostElementId);
                }
                catch { }
            }
            return set;
        }

        private static int HomogenizePipeTags(Document doc, View view, FamilySymbol target)
        {
            int n = 0;
            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList())
            {
                // Only re-type tags whose host is a pipe.
                bool hostsPipe = false;
                try
                {
                    foreach (var lid in tag.GetTaggedElementIds())
                        if (lid.HostElementId != ElementId.InvalidElementId &&
                            doc.GetElement(lid.HostElementId) is Pipe) { hostsPipe = true; break; }
                }
                catch { }
                if (!hostsPipe) continue;
                if (tag.GetTypeId() == target.Id) continue;
                try { tag.ChangeTypeId(target.Id); n++; } catch { }
            }
            return n;
        }

        // ══════════════════════════════════════════════════════════════
        //  LENGTH RESETS (arithmetic on existing HydraCAD params)
        // ══════════════════════════════════════════════════════════════

        private static bool ResetTakeOut(Pipe pipe)
        {
            double? cc = ReadDouble(pipe, PCenterCenter);
            double? cut = ReadDouble(pipe, PCutLength);
            if (cc == null || cut == null) return false;
            return WriteDouble(pipe, PAdjustment, cc.Value - cut.Value);   // adj = C-C − cut
        }

        private static bool ResetCut(Pipe pipe)
        {
            double? cc = ReadDouble(pipe, PCenterCenter);
            double? adj = ReadDouble(pipe, PAdjustment);
            if (cc == null || adj == null) return false;
            return WriteDouble(pipe, PCutLength, cc.Value - adj.Value);    // cut = C-C − adj
        }

        private static double? ReadDouble(Element e, string name)
        {
            var p = e.LookupParameter(name);
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
            return p.AsDouble();
        }

        private static bool WriteDouble(Element e, string name, double val)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
            try { p.Set(val); return true; } catch { return false; }
        }

        // ══════════════════════════════════════════════════════════════
        //  CLEANUP — first-pass vertical anti-overlap
        // ══════════════════════════════════════════════════════════════

        private static void DecollideTags(Document doc, View view, List<IndependentTag> tags)
        {
            // Snapshot each tag's bbox rectangle + head, then greedily push
            // overlapping ones up (+Y) using our own tracked rectangles (avoids a
            // Regenerate per move). Apply the accumulated dy at the end.
            var items = new List<(IndependentTag Tag, XYZ Head, double MinX, double MinY, double MaxX, double MaxY, double Dy)>();
            foreach (var t in tags)
            {
                BoundingBoxXYZ bb;
                try { bb = t.get_BoundingBox(view); } catch { bb = null; }
                if (bb == null) continue;
                XYZ head;
                try { head = t.TagHeadPosition; } catch { continue; }
                items.Add((t, head, bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y, 0.0));
            }
            if (items.Count < 2) return;

            // Process top-to-bottom, left-to-right.
            var order = items.OrderByDescending(i => i.MaxY).ThenBy(i => i.MinX).ToList();
            var settled = new List<(double MinX, double MinY, double MaxX, double MaxY)>();
            const double gap = 0.02; // ~1/4" model gap; scaled by tag geometry already

            for (int k = 0; k < order.Count; k++)
            {
                var it = order[k];
                double dy = 0;
                for (int guard = 0; guard < 200; guard++)
                {
                    bool hit = false;
                    foreach (var s in settled)
                    {
                        bool overlap = it.MinX < s.MaxX && it.MaxX > s.MinX &&
                                       (it.MinY + dy) < s.MaxY && (it.MaxY + dy) > s.MinY;
                        if (overlap)
                        {
                            dy += (s.MaxY - (it.MinY + dy)) + gap; // push above this one
                            hit = true;
                            break;
                        }
                    }
                    if (!hit) break;
                }
                settled.Add((it.MinX, it.MinY + dy, it.MaxX, it.MaxY + dy));
                if (Math.Abs(dy) > 1e-9)
                {
                    try { it.Tag.TagHeadPosition = it.Head + new XYZ(0, dy, 0); } catch { }
                }
            }
        }

        private static XYZ MidPoint(Pipe pipe)
        {
            var lc = pipe.Location as LocationCurve;
            return lc?.Curve?.Evaluate(0.5, true);
        }
    }
}
