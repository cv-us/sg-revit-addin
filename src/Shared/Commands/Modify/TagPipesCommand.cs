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
    /// Pipes workflow with our own loaded tag families. Each tag type places the
    /// family AND type the user picked; the family's label reads whatever parameter
    /// it's built on (we don't compute lengths). Stocklisting splits into a "line"
    /// tag and a "main" tag chosen by the pipe's name. Selection is either the
    /// user's pre-selected pipes or a System-Walker sweep of the connected network.
    ///
    /// Invoked from the Modify-tab SG button via <see cref="DeferredActionHandler"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagPipesCommand : IExternalCommand
    {
        private const string PCenterCenter = "Length-Center_Center (Hydratec)";
        private const string PCutLength = "Length-Cut_Length (Hydratec)";
        private const string PAdjustment = "Length-Adjustment (Hydratec)";

        private const double VerticalTolDeg = 30.0;
        private const int MaxWalk = 20000;
        private static readonly StringComparison OIC = StringComparison.OrdinalIgnoreCase;

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

            // Loaded pipe-tag families → names + type lists for the dropdowns.
            var allSyms = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_PipeTags)
                .Cast<FamilySymbol>().ToList();

            var familyToTypes = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var fs in allSyms)
            {
                string fam = fs.Family?.Name;
                if (string.IsNullOrEmpty(fam)) continue;
                if (!familyToTypes.TryGetValue(fam, out var list)) { list = new List<string>(); familyToTypes[fam] = list; }
                list.Add(fs.Name);
            }
            foreach (var kv in familyToTypes) ((List<string>)kv.Value).Sort(StringComparer.OrdinalIgnoreCase);
            var familyNames = familyToTypes.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            if (familyNames.Count == 0)
            {
                TaskDialog.Show("Tag Pipes",
                    "No Pipe Tag families are loaded in this project.\n\nLoad your pipe tag families first.");
                return;
            }

            TagPipesDialog dlg;
            using (dlg = new TagPipesDialog(familyNames, familyToTypes))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            }

            bool stock = dlg.TagTypeIndex == 3;

            // Resolve the tag symbols we'll place.
            FamilySymbol mainSym = stock ? null : ResolveTagSymbol(allSyms, dlg.SelFamily, dlg.SelType, dlg.Transparent);
            FamilySymbol lineSym = stock ? ResolveTagSymbol(allSyms, dlg.StockLineFamily, dlg.StockLineType, dlg.Transparent) : null;
            FamilySymbol mainStockSym = stock ? ResolveTagSymbol(allSyms, dlg.StockMainFamily, dlg.StockMainType, dlg.Transparent) : null;
            FamilySymbol dropSym = ResolveTagSymbol(allSyms, dlg.DropFamily, dlg.DropType, dlg.Transparent);

            if (!stock && mainSym == null)
            { TaskDialog.Show("Tag Pipes", $"Tag family/type \"{dlg.SelFamily} : {dlg.SelType}\" not found."); return; }
            if (stock && lineSym == null && mainStockSym == null)
            { TaskDialog.Show("Tag Pipes", "No Stocklisting line/main tag family/type found."); return; }

            // Seed pipes = current selection.
            var seeds = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as Pipe).Where(p => p != null).ToList();
            if (seeds.Count == 0)
            {
                TaskDialog.Show("Tag Pipes",
                    dlg.UseSystemWalker ? "Select one pipe to start the System Walker, then run Tag Pipes."
                                        : "Select the pipes to tag, then run Tag Pipes.");
                return;
            }

            var targets = (dlg.UseSystemWalker ? WalkNetwork(seeds) : seeds);
            if (dlg.TagDropsOnly) targets = targets.Where(IsDrop).ToList();
            else if (!dlg.IncludeDrops) targets = targets.Where(p => !IsDrop(p)).ToList();
            targets = targets.GroupBy(p => p.Id).Select(g => g.First()).ToList();
            if (targets.Count == 0)
            { TaskDialog.Show("Tag Pipes", "No pipes matched (check the Drops options)."); return; }

            int tagged = 0, skDup = 0, skNoName = 0, skNoFam = 0, resetCount = 0, homogenized = 0;

            using (var tw = new TransactionWrapper(doc, "Tag Pipes"))
            {
                foreach (var s in new[] { mainSym, lineSym, mainStockSym, dropSym })
                    if (s != null && !s.IsActive) s.Activate();

                var taggedByFam = PipesTaggedByFamily(doc, view);   // family → pipe ids already tagged
                var placed = new List<IndependentTag>();

                foreach (var pipe in targets)
                {
                    bool isDrop = (dlg.TagDropsOnly || dlg.IncludeDrops) && IsDrop(pipe);
                    FamilySymbol sym;
                    if (isDrop && dropSym != null) sym = dropSym;
                    else if (stock)
                    {
                        if (NameContains(pipe, "main")) sym = mainStockSym;
                        else if (NameContains(pipe, "line")) sym = lineSym;
                        else { skNoName++; continue; }   // neither "line" nor "main"
                    }
                    else sym = mainSym;

                    if (sym == null) { skNoFam++; continue; }

                    string fam = sym.Family?.Name ?? "";
                    if (taggedByFam.TryGetValue(fam, out var set) && set.Contains(pipe.Id)) { skDup++; continue; }

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
                            if (!taggedByFam.ContainsKey(fam)) taggedByFam[fam] = new HashSet<ElementId>();
                            taggedByFam[fam].Add(pipe.Id);
                            tagged++;
                        }
                    }
                    catch { }

                    if (dlg.ResetTakeOut && ResetTakeOut(pipe)) resetCount++;
                    else if (dlg.ResetCut && ResetCut(pipe)) resetCount++;
                }

                if (dlg.Homogenize)
                {
                    FamilySymbol homoTarget = mainSym ?? lineSym ?? mainStockSym;
                    if (homoTarget != null) homogenized = HomogenizePipeTags(doc, view, homoTarget);
                }

                if (dlg.RunCleanup && placed.Count > 1) DecollideTags(doc, view, placed);

                tw.Commit();
            }

            string report = "Tag Pipes\n\n" +
                            $"Tags placed: {tagged}\n" +
                            $"Pipes considered: {targets.Count}" +
                            (dlg.UseSystemWalker ? "  (System Walker)" : "  (User Selection)") + "\n";
            if (skDup > 0) report += $"Skipped (already tagged): {skDup}\n";
            if (skNoName > 0) report += $"Skipped (name has neither 'line' nor 'main'): {skNoName}\n";
            if (skNoFam > 0) report += $"Skipped (no tag family/type resolved): {skNoFam}\n";
            if (dlg.Homogenize) report += $"Existing pipe tags re-typed: {homogenized}\n";
            if (dlg.ResetTakeOut || dlg.ResetCut) report += $"Lengths reset: {resetCount}\n";
            if (dlg.RunCleanup) report += "Cleanup: overlap pass run on new tags\n";
            TaskDialog.Show("Tag Pipes", report);
        }

        // ── Pipe-name classification for stocklisting ──
        private static bool NameContains(Pipe p, string token)
        {
            try
            {
                if ((p.Name ?? "").IndexOf(token, OIC) >= 0) return true;
                string tn = p.Document.GetElement(p.GetTypeId())?.Name ?? "";
                return tn.IndexOf(token, OIC) >= 0;
            }
            catch { return false; }
        }

        // ══════════════════════════════════════════════════════════════
        //  NETWORK WALK
        // ══════════════════════════════════════════════════════════════

        private static List<Pipe> WalkNetwork(List<Pipe> seeds)
        {
            var found = new Dictionary<ElementId, Pipe>();
            var visited = new HashSet<ElementId>();
            var queue = new Queue<Element>();
            foreach (var s in seeds) queue.Enqueue(s);

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
                        if (owner is Pipe || owner is FlexPipe || cat == (int)BuiltInCategory.OST_PipeFitting)
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

        private static FamilySymbol ResolveTagSymbol(List<FamilySymbol> syms, string familyName, string typeName, bool transparent)
        {
            if (string.IsNullOrEmpty(familyName)) return null;

            string fam = familyName;
            if (transparent && !familyName.EndsWith("-T", OIC))
            {
                string tName = familyName + "-T";
                if (syms.Any(s => string.Equals(s.Family?.Name, tName, OIC))) fam = tName;
            }

            var inFam = syms.Where(s => string.Equals(s.Family?.Name, fam, OIC)).ToList();
            if (inFam.Count == 0) inFam = syms.Where(s => string.Equals(s.Family?.Name, familyName, OIC)).ToList();
            if (inFam.Count == 0) return null;

            if (!string.IsNullOrEmpty(typeName))
            {
                var m = inFam.FirstOrDefault(s => string.Equals(s.Name, typeName, OIC));
                if (m != null) return m;
            }
            return inFam.First();
        }

        private static Dictionary<string, HashSet<ElementId>> PipesTaggedByFamily(Document doc, View view)
        {
            var map = new Dictionary<string, HashSet<ElementId>>();
            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                var sym = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                string fam = sym?.Family?.Name;
                if (string.IsNullOrEmpty(fam)) continue;
                try
                {
                    foreach (var lid in tag.GetTaggedElementIds())
                    {
                        if (lid.HostElementId == ElementId.InvalidElementId) continue;
                        if (!map.TryGetValue(fam, out var set)) { set = new HashSet<ElementId>(); map[fam] = set; }
                        set.Add(lid.HostElementId);
                    }
                }
                catch { }
            }
            return map;
        }

        private static int HomogenizePipeTags(Document doc, View view, FamilySymbol target)
        {
            int n = 0;
            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList())
            {
                bool hostsPipe = false;
                try
                {
                    foreach (var lid in tag.GetTaggedElementIds())
                        if (lid.HostElementId != ElementId.InvalidElementId &&
                            doc.GetElement(lid.HostElementId) is Pipe) { hostsPipe = true; break; }
                }
                catch { }
                if (!hostsPipe || tag.GetTypeId() == target.Id) continue;
                try { tag.ChangeTypeId(target.Id); n++; } catch { }
            }
            return n;
        }

        // ══════════════════════════════════════════════════════════════
        //  LENGTH RESETS
        // ══════════════════════════════════════════════════════════════

        private static bool ResetTakeOut(Pipe pipe)
        {
            double? cc = ReadDouble(pipe, PCenterCenter);
            double? cut = ReadDouble(pipe, PCutLength);
            if (cc == null || cut == null) return false;
            return WriteDouble(pipe, PAdjustment, cc.Value - cut.Value);
        }

        private static bool ResetCut(Pipe pipe)
        {
            double? cc = ReadDouble(pipe, PCenterCenter);
            double? adj = ReadDouble(pipe, PAdjustment);
            if (cc == null || adj == null) return false;
            return WriteDouble(pipe, PCutLength, cc.Value - adj.Value);
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
            var items = new List<(IndependentTag Tag, XYZ Head, double MinX, double MinY, double MaxX, double MaxY)>();
            foreach (var t in tags)
            {
                BoundingBoxXYZ bb;
                try { bb = t.get_BoundingBox(view); } catch { bb = null; }
                if (bb == null) continue;
                XYZ head;
                try { head = t.TagHeadPosition; } catch { continue; }
                items.Add((t, head, bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y));
            }
            if (items.Count < 2) return;

            var order = items.OrderByDescending(i => i.MaxY).ThenBy(i => i.MinX).ToList();
            var settled = new List<(double MinX, double MinY, double MaxX, double MaxY)>();
            const double gap = 0.02;

            foreach (var it in order)
            {
                double dy = 0;
                for (int guard = 0; guard < 200; guard++)
                {
                    bool hit = false;
                    foreach (var s in settled)
                    {
                        bool overlap = it.MinX < s.MaxX && it.MaxX > s.MinX &&
                                       (it.MinY + dy) < s.MaxY && (it.MaxY + dy) > s.MinY;
                        if (overlap) { dy += (s.MaxY - (it.MinY + dy)) + gap; hit = true; break; }
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
