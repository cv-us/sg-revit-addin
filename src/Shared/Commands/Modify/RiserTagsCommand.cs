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
    /// "Riser Tags" — places a rotatable annotation symbol (your riser-nipple symbol,
    /// with its mask) at the TOP of vertical pipes, centered on the pipe in plan and
    /// auto-rotated so the line runs along the horizontal pipe connected at the top
    /// (the higher pipe) and the solid semicircle marks the lower, vertical side.
    ///
    /// It places a Generic Annotation / Sprinkler Tags family INSTANCE (via
    /// NewFamilyInstance, like the Pretty Sprinklers head overlays) rather than a pipe
    /// tag — because Revit pipe tags (IndependentTag) can't be rotated to an arbitrary
    /// angle, but annotation-symbol instances can. Supply the riser-nipple symbol in a
    /// Generic Annotation family; the command places and rotates it.
    ///
    /// Invoked from the Modify-tab SG button via <see cref="DeferredActionHandler"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RiserTagsCommand : IExternalCommand
    {
        private const double VerticalTolDeg = 30.0;
        private const double DupTolFt = 0.20;   // an existing symbol this close to the top counts as already placed
        private static readonly StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        private static readonly BuiltInCategory[] SymbolCats =
        {
            BuiltInCategory.OST_GenericAnnotation,
            BuiltInCategory.OST_SprinklerTags
        };

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

            var catIds = new HashSet<int>(SymbolCats.Select(c => (int)c));
            var allSyms = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null && catIds.Contains(fs.Category.Id.IntegerValue))
                .ToList();
            if (allSyms.Count == 0)
            {
                TaskDialog.Show("Riser Tags",
                    "No Generic Annotation or Sprinkler Tags symbol families are loaded.\n\n" +
                    "Put your riser-nipple symbol (with its mask) into a Generic Annotation family and load it, " +
                    "then run Riser Tags.");
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
            { TaskDialog.Show("Riser Tags", $"Symbol \"{dlg.SelFamily} : {dlg.SelType}\" not found."); return; }

            if (dlg.Action == RiserTagsDialog.RiserAction.Remove)
            {
                RemoveRiserSymbols(doc, view, sym.Family?.Name);
                return;
            }

            // ── Collect + filter pipes ──
            List<Pipe> pipes = CollectPipes(uidoc, dlg.Scope);
            if (dlg.DropsOnly) pipes = pipes.Where(IsDrop).ToList();
            else if (dlg.VerticalOnly) pipes = pipes.Where(IsVertical).ToList();
            pipes = pipes.GroupBy(p => p.Id).Select(g => g.First()).ToList();

            // Never tag sprigs — a vertical pipe off the TOP of the branch line that
            // just feeds a single head above needs no riser-nipple symbol.
            int skSprig = pipes.Count;
            pipes = pipes.Where(p => !IsSprig(p)).ToList();
            skSprig -= pipes.Count;

            if (pipes.Count == 0)
            {
                string why = skSprig > 0
                    ? $"All {skSprig} matching vertical pipe(s) are sprigs (a head above, branch line below) — sprigs aren't tagged."
                    : dlg.Scope == RiserTagsDialog.RiserScope.Selection
                        ? "No matching pipes in the selection (check the Vertical/Drops filters)."
                        : "No matching vertical pipes found in the chosen scope.";
                TaskDialog.Show("Riser Tags", why);
                return;
            }

            double nudgeX = dlg.CenterNudgeXin / 12.0;
            double nudgeY = dlg.CenterNudgeYin / 12.0;
            double rotOff = dlg.RotationOffsetDeg * Math.PI / 180.0;

            var existing = ExistingSymbolLocations(doc, view, sym.Family?.Name);

            int placed = 0, skDup = 0, skNoTop = 0, rotated = 0;

            using (var tw = new TransactionWrapper(doc, "Riser Tags"))
            {
                if (!sym.IsActive) sym.Activate();

                foreach (var pipe in pipes)
                {
                    XYZ top = TopPoint(pipe);
                    if (top == null) { skNoTop++; continue; }
                    XYZ head = new XYZ(top.X + nudgeX, top.Y + nudgeY, top.Z);

                    if (existing.Any(e => Flat(e).DistanceTo(Flat(head)) <= DupTolFt)) { skDup++; continue; }

                    try
                    {
                        var inst = doc.Create.NewFamilyInstance(head, sym, view);
                        if (inst == null) continue;
                        placed++;
                        existing.Add(head);

                        double angle = rotOff;
                        if (dlg.AutoRotate)
                        {
                            XYZ dir = BranchDir(pipe);
                            // The family's default line points +Y (north) toward the source. Rotate so that
                            // line points along the branch it comes from (dir): theta = atan2(dir) - 90deg.
                            if (dir != null) angle = Math.Atan2(dir.Y, dir.X) - Math.PI / 2.0 + rotOff;
                        }
                        if (Math.Abs(angle) > 1e-9)
                        {
                            try
                            {
                                ElementTransformUtils.RotateElement(doc, inst.Id,
                                    Line.CreateUnbound(head, XYZ.BasisZ), angle);
                                rotated++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
                tw.Commit();
            }

            string report = "Riser Tags\n\n" +
                            $"Symbols placed: {placed}\n" +
                            $"Pipes considered: {pipes.Count}\n";
            if (dlg.AutoRotate || Math.Abs(rotOff) > 1e-9) report += $"Rotated: {rotated}\n";
            if (skSprig > 0) report += $"Skipped (sprigs feeding a head above): {skSprig}\n";
            if (skDup > 0) report += $"Skipped (a symbol already at the top): {skDup}\n";
            if (skNoTop > 0) report += $"Skipped (no location): {skNoTop}\n";
            report += "\nIf the rotation is off, adjust Rotate + in the dialog (it works now — these are " +
                      "rotatable annotation symbols, not pipe tags). Center nudge shifts the symbol if the " +
                      "family origin isn't centered.";
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

        /// <summary>The endpoint of the pipe farther from <paramref name="from"/> — the
        /// direction the branch runs away from the drop connection.</summary>
        private static XYZ FarEnd(Pipe pipe, XYZ from)
        {
            var lc = pipe.Location as LocationCurve;
            if (lc?.Curve == null) return null;
            XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
            return a.DistanceTo(from) >= b.DistanceTo(from) ? a : b;
        }

        private static XYZ Flat(XYZ p) => new XYZ(p.X, p.Y, 0);

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

        // ── Line direction ──
        //
        // Symbol convention: the LINE represents the horizontal-and-HIGHER pipe; the
        // solid semicircle marks the vertical-and-lower side. So the line must run
        // along the horizontal pipe connected at the TOP of the vertical, extending
        // away from the vertical. E.g. branch -> riser up -> armover -> drop down:
        // both ends tag with the line on the INSIDE of the armover and the
        // semicircles on the outsides.

        private static XYZ BranchDir(Pipe pipe)
        {
            Pipe hp = ConnectedHorizontalPipe(pipe, out XYZ origin, out bool atTop);
            if (hp == null || origin == null) return null;

            if (atTop)
            {
                // Line = along the higher horizontal pipe, extending away from the
                // vertical (for an armover this puts the line on the inside and the
                // semicircle on the outside, at both the riser and the drop end).
                XYZ feTop = FarEnd(hp, origin);
                XYZ away = feTop == null ? null : FlattenDir(feTop - origin);
                if (away != null) return away;
            }

            // Bottom-connection fallback (e.g. a rise straight off the branch line
            // with a head on top — no horizontal pipe above): keep the previous
            // behavior, line along the branch toward its junction side.
            XYZ d = TowardBranchJunction(hp, origin);
            if (d != null) return d;
            XYZ fe = FarEnd(hp, origin);
            return fe == null ? null : FlattenDir(fe - origin);
        }

        /// <summary>The horizontal pipe the vertical riser connects to — directly or
        /// through its adjacent fitting (elbow/tee) — with the connection point.
        /// Checks the TOP connector first (the symbol's line represents the
        /// horizontal-and-higher pipe); <paramref name="atTop"/> reports which end
        /// the returned pipe was found at.</summary>
        private static Pipe ConnectedHorizontalPipe(Pipe pipe, out XYZ origin, out bool atTop)
        {
            origin = null;
            atTop = false;
            var cm = pipe.ConnectorManager?.Connectors;
            if (cm == null) return null;

            var ends = cm.Cast<Connector>()
                .Where(c => c.ConnectorType == ConnectorType.End)
                .OrderByDescending(c => c.Origin.Z)
                .ToList();
            double topZ = ends.Count > 0 ? ends[0].Origin.Z : 0;

            foreach (Connector c in ends)
            {
                Pipe hp = HorizontalPipeFrom(c, pipe.Id);
                if (hp != null)
                {
                    origin = c.Origin;
                    atTop = ends.Count < 2 || c.Origin.Z >= topZ - 1e-6;
                    return hp;
                }
            }
            return null;
        }

        /// <summary>The horizontal pipe reachable from an end connector — directly or
        /// through the adjacent fitting (elbow/tee).</summary>
        private static Pipe HorizontalPipeFrom(Connector c, ElementId selfId)
        {
            ConnectorSet refs; try { refs = c.AllRefs; } catch { return null; }
            if (refs == null) return null;

            foreach (Connector o in refs)
            {
                Element owner = o.Owner;
                if (owner == null || owner.Id == selfId) continue;

                if (owner is Pipe bp && !IsVertical(bp)) return bp;

                if ((owner.Category?.Id.IntegerValue ?? 0) == (int)BuiltInCategory.OST_PipeFitting)
                {
                    var cs = GetConnectors(owner);
                    if (cs == null) continue;
                    foreach (Connector fc in cs.Cast<Connector>())
                    {
                        ConnectorSet frefs; try { frefs = fc.AllRefs; } catch { continue; }
                        if (frefs == null) continue;
                        foreach (Connector fo in frefs)
                            if (fo.Owner is Pipe fbp && fbp.Id != selfId && !IsVertical(fbp)) return fbp;
                    }
                }
            }
            return null;
        }

        /// <summary>A sprig: a vertical pipe off the TOP of the branch line that feeds
        /// a single sprinkler above (upright head) — the head above and the horizontal
        /// pipe below distinguish it from a riser nipple (horizontal pipe above) and a
        /// pendent drop (horizontal pipe above, head below).</summary>
        private static bool IsSprig(Pipe pipe)
        {
            if (!IsVertical(pipe)) return false;
            var cm = pipe.ConnectorManager?.Connectors;
            if (cm == null) return false;
            var ends = cm.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End)
                .OrderByDescending(c => c.Origin.Z).ToList();
            if (ends.Count < 2) return false;

            if (!ReachesSprinkler(ends[0], new HashSet<ElementId> { pipe.Id }, 2)) return false;
            return ReachesHorizontalPipe(ends[ends.Count - 1], new HashSet<ElementId> { pipe.Id }, 2);
        }

        /// <summary>Whether a horizontal pipe is reachable from a connector, directly or
        /// through up to <paramref name="hops"/> fittings — symmetric with
        /// <see cref="ReachesSprinkler"/> so a sprig based on a tee + auto-inserted
        /// reducer (two fitting hops to the branch line) is still recognized.</summary>
        private static bool ReachesHorizontalPipe(Connector from, HashSet<ElementId> visited, int hops)
        {
            if (from == null) return false;
            ConnectorSet refs; try { refs = from.AllRefs; } catch { return false; }
            if (refs == null) return false;
            foreach (Connector other in refs)
            {
                Element owner = other.Owner;
                if (owner == null || visited.Contains(owner.Id)) continue;
                if (owner is Pipe bp && !IsVertical(bp)) return true;
                if (hops > 0 && (owner.Category?.Id.IntegerValue ?? 0) == (int)BuiltInCategory.OST_PipeFitting)
                {
                    visited.Add(owner.Id);
                    var cs = GetConnectors(owner);
                    if (cs != null)
                        foreach (Connector oc in cs.Cast<Connector>())
                            if (ReachesHorizontalPipe(oc, visited, hops - 1)) return true;
                }
            }
            return false;
        }

        /// <summary>Direction from <paramref name="origin"/> toward the end of
        /// <paramref name="hp"/> that joins a branch junction (a 3+-connector fitting,
        /// i.e. a tee/cross) — back toward the branch line.</summary>
        private static XYZ TowardBranchJunction(Pipe hp, XYZ origin)
        {
            var cs = hp.ConnectorManager?.Connectors;
            if (cs == null) return null;

            foreach (Connector c in cs.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End))
            {
                ConnectorSet refs; try { refs = c.AllRefs; } catch { continue; }
                if (refs == null) continue;
                foreach (Connector r in refs)
                {
                    Element owner = r.Owner;
                    if (owner == null) continue;
                    if ((owner.Category?.Id.IntegerValue ?? 0) != (int)BuiltInCategory.OST_PipeFitting) continue;
                    var fcs = GetConnectors(owner);
                    if (fcs == null || fcs.Size < 3) continue;   // tee/cross = a branch junction
                    XYZ d = FlattenDir(c.Origin - origin);
                    if (d != null) return d;
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

        // ── Symbol helpers ──

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

        /// <summary>Plan-view locations of existing instances of the family (for de-dup).</summary>
        private static List<XYZ> ExistingSymbolLocations(Document doc, View view, string family)
        {
            var locs = new List<XYZ>();
            if (string.IsNullOrEmpty(family)) return locs;
            foreach (var fi in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                if (!string.Equals(fi.Symbol?.Family?.Name, family, OIC)) continue;
                XYZ p = (fi.Location as LocationPoint)?.Point;
                if (p == null) { var bb = fi.get_BoundingBox(view); if (bb != null) p = (bb.Min + bb.Max) / 2.0; }
                if (p != null) locs.Add(p);
            }
            return locs;
        }

        private static void RemoveRiserSymbols(Document doc, View view, string family)
        {
            if (string.IsNullOrEmpty(family))
            { TaskDialog.Show("Riser Tags", "Pick the symbol family to remove first."); return; }

            var toDelete = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(fi => string.Equals(fi.Symbol?.Family?.Name, family, OIC))
                .Select(fi => fi.Id).ToList();

            int removed = 0;
            using (var tw = new TransactionWrapper(doc, "Remove Riser Tags"))
            {
                foreach (var id in toDelete) { try { doc.Delete(id); removed++; } catch { } }
                tw.Commit();
            }
            TaskDialog.Show("Riser Tags",
                removed > 0 ? $"Removed {removed} \"{family}\" riser symbol(s) from this view."
                            : $"No \"{family}\" riser symbols found in this view.");
        }
    }
}
