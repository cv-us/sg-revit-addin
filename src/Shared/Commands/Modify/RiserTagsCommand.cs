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
    /// "Riser Tags" — places a chosen pipe-tag family (your riser-nipple symbol, with
    /// its mask) at the TOP of vertical pipes, centered on the pipe in plan and
    /// auto-rotated to the branch it comes from. The intrinsic rise/drop symbol on a
    /// pipe stops showing once the run is sloped off-plumb; this drops the tag in as a
    /// reliable stand-in without the manual centering/rotation.
    ///
    /// Invoked from the Modify-tab SG button via <see cref="DeferredActionHandler"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RiserTagsCommand : IExternalCommand
    {
        private const double VerticalTolDeg = 30.0;
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

            var allSyms = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_PipeTags)
                .Cast<FamilySymbol>().ToList();
            if (allSyms.Count == 0)
            {
                TaskDialog.Show("Riser Tags", "No Pipe Tag families are loaded in this project.\n\nLoad your riser-nipple pipe tag first.");
                return;
            }

            var famToTypes = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var fs in allSyms)
            {
                string fam = fs.Family?.Name;
                if (string.IsNullOrEmpty(fam)) continue;
                if (!famToTypes.TryGetValue(fam, out var list)) { list = new List<string>(); famToTypes[fam] = list; }
                list.Add(fs.Name);
            }
            foreach (var kv in famToTypes) ((List<string>)kv.Value).Sort(StringComparer.OrdinalIgnoreCase);
            var familyNames = famToTypes.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            RiserTagsDialog dlg;
            using (dlg = new RiserTagsDialog(familyNames, famToTypes))
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            FamilySymbol sym = ResolveSymbol(allSyms, dlg.SelFamily, dlg.SelType);
            if (sym == null)
            { TaskDialog.Show("Riser Tags", $"Tag \"{dlg.SelFamily} : {dlg.SelType}\" not found."); return; }

            if (dlg.Action == RiserTagsDialog.RiserAction.Remove)
            {
                RemoveRiserTags(doc, view, sym.Family?.Name);
                return;
            }

            // ── Collect + filter pipes ──
            List<Pipe> pipes = CollectPipes(uidoc, dlg.Scope);
            if (dlg.DropsOnly) pipes = pipes.Where(IsDrop).ToList();
            else if (dlg.VerticalOnly) pipes = pipes.Where(IsVertical).ToList();
            pipes = pipes.GroupBy(p => p.Id).Select(g => g.First()).ToList();

            if (pipes.Count == 0)
            {
                TaskDialog.Show("Riser Tags",
                    dlg.Scope == RiserTagsDialog.RiserScope.Selection
                        ? "No matching pipes in the selection (check the Vertical/Drops filters)."
                        : "No matching vertical pipes found in the chosen scope.");
                return;
            }

            double nudgeX = dlg.CenterNudgeXin / 12.0;
            double nudgeY = dlg.CenterNudgeYin / 12.0;
            double rotOff = dlg.RotationOffsetDeg * Math.PI / 180.0;

            var alreadyTagged = PipesTaggedByFamily(doc, view, sym.Family?.Name);

            int placed = 0, skDup = 0, skNoTop = 0, rotated = 0;

            using (var tw = new TransactionWrapper(doc, "Riser Tags"))
            {
                if (!sym.IsActive) sym.Activate();

                foreach (var pipe in pipes)
                {
                    if (alreadyTagged.Contains(pipe.Id)) { skDup++; continue; }

                    XYZ top = TopPoint(pipe);
                    if (top == null) { skNoTop++; continue; }
                    XYZ head = new XYZ(top.X + nudgeX, top.Y + nudgeY, top.Z);

                    try
                    {
                        var tag = IndependentTag.Create(doc, view.Id, new Reference(pipe),
                            false, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, head);
                        if (tag == null) continue;
                        tag.ChangeTypeId(sym.Id);
                        try { tag.TagHeadPosition = head; } catch { }
                        alreadyTagged.Add(pipe.Id);
                        placed++;

                        if (dlg.AutoRotate)
                        {
                            XYZ dir = BranchDir(pipe);
                            if (dir != null)
                            {
                                double ang = Math.Atan2(dir.Y, dir.X) + rotOff;
                                try
                                {
                                    ElementTransformUtils.RotateElement(doc, tag.Id, Line.CreateUnbound(head, XYZ.BasisZ), ang);
                                    rotated++;
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
                tw.Commit();
            }

            string report = "Riser Tags\n\n" +
                            $"Tags placed: {placed}\n" +
                            $"Pipes considered: {pipes.Count}\n";
            if (dlg.AutoRotate) report += $"Auto-rotated to branch: {rotated}\n";
            if (skDup > 0) report += $"Skipped (already tagged with this family): {skDup}\n";
            if (skNoTop > 0) report += $"Skipped (no location): {skNoTop}\n";
            report += "\nIf the tag isn't centered or the rotation is off, adjust the " +
                      "Center nudge / Rotate offset in the dialog for your family.";
            TaskDialog.Show("Riser Tags", report);
        }

        // ── Pipe collection + geometry ──

        private static List<Pipe> CollectPipes(UIDocument uidoc, RiserTagsDialog.RiserScope scope)
        {
            Document doc = uidoc.Document;
            switch (scope)
            {
                case RiserTagsDialog.RiserScope.ActiveView:
                    return new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
                case RiserTagsDialog.RiserScope.WholeModel:
                    return new FilteredElementCollector(doc)
                        .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
                default:
                    return uidoc.Selection.GetElementIds()
                        .Select(id => doc.GetElement(id) as Pipe).Where(p => p != null).ToList();
            }
        }

        private static XYZ TopPoint(Pipe pipe)
        {
            if (!(pipe.Location is LocationCurve lc) || lc.Curve == null) return null;
            XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
            return a.Z >= b.Z ? a : b;
        }

        private static XYZ MidPoint(Pipe pipe)
        {
            var lc = pipe.Location as LocationCurve;
            return lc?.Curve?.Evaluate(0.5, true);
        }

        private static bool IsVertical(Pipe pipe)
        {
            if (!(pipe.Location is LocationCurve lc) || !(lc.Curve is Line line)) return false;
            XYZ d = line.GetEndPoint(1) - line.GetEndPoint(0);
            double dz = Math.Abs(d.Z), dxy = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (dz < 1e-9) return false;
            return Math.Atan2(dxy, dz) * 180.0 / Math.PI <= VerticalTolDeg;
        }

        private static bool IsDrop(Pipe pipe)
        {
            if (!IsVertical(pipe)) return false;
            var cm = pipe.ConnectorManager?.Connectors;
            if (cm == null) return false;
            foreach (Connector c in cm.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End))
                if (ReachesSprinkler(c, new HashSet<ElementId> { pipe.Id }, 2)) return true;
            return false;
        }

        // ── Branch direction (what the riser comes from) ──

        private static XYZ BranchDir(Pipe pipe)
        {
            var cm = pipe.ConnectorManager?.Connectors;
            if (cm == null) return null;

            foreach (Connector c in cm.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End))
            {
                XYZ origin = c.Origin;
                ConnectorSet refs; try { refs = c.AllRefs; } catch { continue; }
                if (refs == null) continue;

                foreach (Connector o in refs)
                {
                    Element owner = o.Owner;
                    if (owner == null || owner.Id == pipe.Id) continue;

                    // Directly connected horizontal pipe → point toward its body.
                    if (owner is Pipe bp && !IsVertical(bp))
                    {
                        XYZ d = FlattenDir(MidPoint(bp) - origin);
                        if (d != null) return d;
                    }

                    // Through a fitting → its connected horizontal pipe.
                    if ((owner.Category?.Id.IntegerValue ?? 0) == (int)BuiltInCategory.OST_PipeFitting)
                    {
                        var cs = GetConnectors(owner);
                        if (cs == null) continue;
                        foreach (Connector fc in cs.Cast<Connector>())
                        {
                            ConnectorSet frefs; try { frefs = fc.AllRefs; } catch { continue; }
                            if (frefs == null) continue;
                            foreach (Connector fo in frefs)
                            {
                                if (fo.Owner is Pipe fbp && fbp.Id != pipe.Id && !IsVertical(fbp))
                                {
                                    XYZ d = FlattenDir(MidPoint(fbp) - origin);
                                    if (d != null) return d;
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        private static XYZ FlattenDir(XYZ v)
        {
            if (v == null) return null;
            var f = new XYZ(v.X, v.Y, 0);
            return f.GetLength() > 1e-6 ? f.Normalize() : null;
        }

        private static bool ReachesSprinkler(Connector from, HashSet<ElementId> visited, int hops)
        {
            if (from == null) return false;
            ConnectorSet refs; try { refs = from.AllRefs; } catch { return false; }
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

        // ── Tag helpers ──

        private static FamilySymbol ResolveSymbol(List<FamilySymbol> syms, string family, string type)
        {
            if (string.IsNullOrEmpty(family)) return null;
            var inFam = syms.Where(s => string.Equals(s.Family?.Name, family, OIC)).ToList();
            if (inFam.Count == 0) return null;
            if (!string.IsNullOrEmpty(type))
            {
                var m = inFam.FirstOrDefault(s => string.Equals(s.Name, type, OIC));
                if (m != null) return m;
            }
            return inFam.First();
        }

        private static HashSet<ElementId> PipesTaggedByFamily(Document doc, View view, string family)
        {
            var set = new HashSet<ElementId>();
            if (string.IsNullOrEmpty(family)) return set;
            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                var sym = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                if (!string.Equals(sym?.Family?.Name, family, OIC)) continue;
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

        private static void RemoveRiserTags(Document doc, View view, string family)
        {
            if (string.IsNullOrEmpty(family))
            { TaskDialog.Show("Riser Tags", "Pick the tag family to remove first."); return; }

            var toDelete = new List<ElementId>();
            foreach (var tag in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                var sym = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                if (!string.Equals(sym?.Family?.Name, family, OIC)) continue;
                bool hostsPipe = false;
                try
                {
                    foreach (var lid in tag.GetTaggedElementIds())
                        if (lid.HostElementId != ElementId.InvalidElementId &&
                            doc.GetElement(lid.HostElementId) is Pipe) { hostsPipe = true; break; }
                }
                catch { }
                if (hostsPipe) toDelete.Add(tag.Id);
            }

            int removed = 0;
            using (var tw = new TransactionWrapper(doc, "Remove Riser Tags"))
            {
                foreach (var id in toDelete) { try { doc.Delete(id); removed++; } catch { } }
                tw.Commit();
            }
            TaskDialog.Show("Riser Tags",
                removed > 0 ? $"Removed {removed} \"{family}\" riser tag(s) from this view."
                            : $"No \"{family}\" riser tags found in this view.");
        }
    }
}
