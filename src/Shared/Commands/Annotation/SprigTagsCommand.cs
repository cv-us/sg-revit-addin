using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Re-types the Hydratec vertical-pipe direction tag (the one that reads
    /// "UP" / "DN" / "RN") to a dedicated SPRIG tag on small sprig pipes, so
    /// the field doesn't confuse a 1" sprig with a riser nipple or a drop.
    ///
    /// A candidate tag is converted only when ALL of these hold for its host
    /// pipe:
    ///   • host is a Pipe (the tag tags a pipe, not a fitting/sprinkler),
    ///   • the pipe is VERTICAL (within <see cref="VerticalToleranceDeg"/> of
    ///     plumb),
    ///   • the pipe's nominal diameter is ≤ the size chosen in the dialog
    ///     (default 1"),
    ///   • a SPRINKLER is reachable from the pipe's UPPER end (directly or
    ///     through a reducing coupling / short nipple) — i.e. it's a sprig-up,
    ///     not a pendent drop (sprinkler at the bottom) and not a riser nipple
    ///     (no sprinkler at all).
    ///
    /// The sprig-vs-drop test is purely geometric (which end the sprinkler is
    /// on) so it doesn't depend on what the existing tag currently reads.
    ///
    /// WORKFLOW:
    ///   1. Gather candidate pipe tags from the current selection; if no tag
    ///      (or tagged pipe) is selected, scan the active view instead.
    ///   2. Dialog: max host pipe size, optional "only this tag family" guard,
    ///      and the SPRIG type to convert to (a dropdown of loaded tag types).
    ///   3. For each qualifying sprig tag, ChangeTypeId to the chosen SPRIG
    ///      type inside a single transaction.
    ///   4. Report converted / skipped counts with reasons.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SprigTagsCommand : IExternalCommand
    {
        /// <summary>Sentinel shown in the family dropdown for "don't filter by family".</summary>
        public const string AnyFamily = "(Any tag family)";

        /// <summary>A pipe counts as vertical if it's within this many degrees of plumb.</summary>
        private const double VerticalToleranceDeg = 30.0;

        /// <summary>How many intermediate elements (couplings/short pipes) to traverse looking for a sprinkler.</summary>
        private const int MaxHops = 2;

        /// <summary>A pipe this short (ft) is treated as a nipple and traversed through during the sprinkler walk.</summary>
        private const double ShortPipeFt = 1.5;

        // Categories that can hold a tag type the user might pick as the SPRIG type.
        private static readonly BuiltInCategory[] TagCategories =
        {
            BuiltInCategory.OST_PipeTags,
            BuiltInCategory.OST_PipeFittingTags,
            BuiltInCategory.OST_PipeAccessoryTags,
            BuiltInCategory.OST_SprinklerTags,
            BuiltInCategory.OST_GenericAnnotation
        };

        private enum SprigClass { Sprig, Drop, Other }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Step 1: Gather candidate pipe tags ──
                bool fromSelection;
                var candidates = GatherCandidateTags(uidoc, doc, activeView, out fromSelection);

                if (candidates.Count == 0)
                {
                    TaskDialog.Show("Sprig Tags",
                        "No pipe tags found.\n\n" +
                        "Select the vertical-pipe tags (or their pipes) you want to convert, " +
                        "or open a view that shows them, then run this command again.");
                    return Result.Cancelled;
                }

                // Families present among the candidate tags → "only this family" guard.
                var fromFamilies = candidates
                    .Select(t => GetSymbol(doc, t)?.Family?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Categories of the candidate tags so the "convert to" list always
                // includes the same category as the tags being converted.
                var candidateCatIds = new HashSet<int>(candidates
                    .Select(t => GetSymbol(doc, t)?.Category?.Id.IntegerValue ?? 0)
                    .Where(i => i != 0));

                // ── Step 2: Build the list of tag types to convert TO ──
                var tagSymbols = CollectTagSymbols(doc, candidateCatIds);
                if (tagSymbols.Count == 0)
                {
                    TaskDialog.Show("Sprig Tags",
                        "No tag types are loaded that could be used as the SPRIG tag.\n\n" +
                        "Load (or create) your SPRIG tag type, then run this command again.");
                    return Result.Cancelled;
                }
                var tagDisplays = tagSymbols
                    .Select(s => $"{s.Family.Name}  :  {s.Name}")
                    .ToList();

                // ── Step 3: Dialog ──
                string fromFamily;
                int targetIndex;
                double maxSizeIn;
                using (var dlg = new SprigTagsDialog(candidates.Count, fromSelection, fromFamilies, tagDisplays))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    fromFamily = dlg.FromFamily;
                    targetIndex = dlg.TargetTypeIndex;
                    maxSizeIn = dlg.MaxSizeInches;
                }

                if (targetIndex < 0 || targetIndex >= tagSymbols.Count)
                {
                    TaskDialog.Show("Sprig Tags", "No SPRIG tag type was chosen.");
                    return Result.Cancelled;
                }

                FamilySymbol targetSymbol = tagSymbols[targetIndex];
                double maxSizeFt = UnitConversion.InchesToFeet(maxSizeIn);
                bool filterFamily = !string.IsNullOrEmpty(fromFamily) &&
                                    !string.Equals(fromFamily, AnyFamily, StringComparison.OrdinalIgnoreCase);

                // ── Step 4: Convert in a transaction ──
                int converted = 0, skTooBig = 0, skNotVertical = 0,
                    skDrop = 0, skNoSprinkler = 0, skWrongFamily = 0, skAlready = 0;

                using (var tw = new TransactionWrapper(doc, "Convert Sprig Tags"))
                {
                    if (!targetSymbol.IsActive)
                        targetSymbol.Activate();

                    foreach (var tag in candidates)
                    {
                        Pipe pipe = GetHostPipe(tag, doc);
                        if (pipe == null) continue;     // not a pipe tag — leave it

                        // Family guard
                        if (filterFamily)
                        {
                            string fam = GetSymbol(doc, tag)?.Family?.Name ?? "";
                            if (!string.Equals(fam, fromFamily, StringComparison.OrdinalIgnoreCase))
                            {
                                skWrongFamily++;
                                continue;
                            }
                        }

                        // Size guard
                        if (pipe.Diameter > maxSizeFt + 1e-4) { skTooBig++; continue; }

                        // Vertical guard
                        if (!IsVertical(pipe)) { skNotVertical++; continue; }

                        // Sprig vs drop vs riser-nipple
                        SprigClass cls = Classify(pipe);
                        if (cls == SprigClass.Drop) { skDrop++; continue; }
                        if (cls == SprigClass.Other) { skNoSprinkler++; continue; }

                        // Already the target type?
                        if (tag.GetTypeId() == targetSymbol.Id) { skAlready++; continue; }

                        try
                        {
                            tag.ChangeTypeId(targetSymbol.Id);
                            converted++;
                        }
                        catch (Exception)
                        {
                            // A tag that refuses the new type (wrong taggable category) is skipped.
                            skWrongFamily++;
                        }
                    }

                    tw.Commit();
                }

                // ── Step 5: Report ──
                int considered = candidates.Count;
                string report =
                    $"Sprig Tags\n\n" +
                    $"Tags considered ({(fromSelection ? "from selection" : "active view")}): {considered}\n" +
                    $"Converted to \"{targetSymbol.Family.Name} : {targetSymbol.Name}\": {converted}\n";

                if (skAlready > 0)      report += $"Already that type:            {skAlready}\n";
                if (skTooBig > 0)       report += $"Host pipe larger than {maxSizeIn:0.##}\":     {skTooBig}\n";
                if (skNotVertical > 0)  report += $"Host pipe not vertical:       {skNotVertical}\n";
                if (skDrop > 0)         report += $"Drops (sprinkler at bottom):  {skDrop}\n";
                if (skNoSprinkler > 0)  report += $"No sprinkler (riser nipple):  {skNoSprinkler}\n";
                if (skWrongFamily > 0)  report += $"Other tag family (skipped):   {skWrongFamily}\n";

                TaskDialog.Show("Sprig Tags", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Sprig Tags failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  CANDIDATE GATHERING
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the pipe tags to consider. Prefers the user's selection:
        /// selected tags (whose host is a pipe), or — if pipes were selected
        /// instead — the active-view tags hosting those pipes. Falls back to
        /// every pipe tag in the active view when nothing useful is selected.
        /// </summary>
        private List<IndependentTag> GatherCandidateTags(
            UIDocument uidoc, Document doc, View activeView, out bool fromSelection)
        {
            fromSelection = false;
            var result = new List<IndependentTag>();

            var selIds = uidoc.Selection.GetElementIds();
            var selectedPipeIds = new HashSet<ElementId>();

            if (selIds != null && selIds.Count > 0)
            {
                foreach (var id in selIds)
                {
                    var el = doc.GetElement(id);
                    if (el is IndependentTag t)
                    {
                        if (GetHostPipe(t, doc) != null) result.Add(t);
                    }
                    else if (el is Pipe)
                    {
                        selectedPipeIds.Add(id);
                    }
                }

                if (result.Count > 0) { fromSelection = true; return result; }

                if (selectedPipeIds.Count > 0)
                {
                    foreach (var t in ActiveViewPipeTags(doc, activeView))
                    {
                        var p = GetHostPipe(t, doc);
                        if (p != null && selectedPipeIds.Contains(p.Id)) result.Add(t);
                    }
                    if (result.Count > 0) { fromSelection = true; return result; }
                }
            }

            // Fall back to scanning the active view.
            result = ActiveViewPipeTags(doc, activeView)
                .Where(t => GetHostPipe(t, doc) != null)
                .ToList();
            fromSelection = false;
            return result;
        }

        private IEnumerable<IndependentTag> ActiveViewPipeTags(Document doc, View activeView)
        {
            return new FilteredElementCollector(doc, activeView.Id)
                .OfClass(typeof(IndependentTag))
                .WhereElementIsNotElementType()
                .Cast<IndependentTag>();
        }

        // ══════════════════════════════════════════════════════════════
        //  HOST PIPE / GEOMETRY
        // ══════════════════════════════════════════════════════════════

        private Pipe GetHostPipe(IndependentTag tag, Document doc)
        {
            try
            {
                foreach (var lid in tag.GetTaggedElementIds())
                {
                    ElementId hostId = lid.HostElementId;
                    if (hostId == null || hostId == ElementId.InvalidElementId) continue;
                    if (doc.GetElement(hostId) is Pipe p) return p;
                }
            }
            catch { }
            return null;
        }

        private FamilySymbol GetSymbol(Document doc, IndependentTag tag)
        {
            return doc.GetElement(tag.GetTypeId()) as FamilySymbol;
        }

        private bool IsVertical(Pipe pipe)
        {
            if (!(pipe.Location is LocationCurve lc)) return false;
            if (!(lc.Curve is Line line)) return false;

            XYZ d = line.GetEndPoint(1) - line.GetEndPoint(0);
            double dz = Math.Abs(d.Z);
            double dxy = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (dz < 1e-9) return false;   // perfectly horizontal

            double angleFromVertical = Math.Atan2(dxy, dz) * 180.0 / Math.PI;
            return angleFromVertical <= VerticalToleranceDeg;
        }

        /// <summary>
        /// Classifies a vertical pipe by where its sprinkler is: reachable from
        /// the UPPER end → sprig; from the LOWER end → drop; neither → other
        /// (riser nipple / stub).
        /// </summary>
        private SprigClass Classify(Pipe pipe)
        {
            var endConns = pipe.ConnectorManager?.Connectors?
                .Cast<Connector>()
                .Where(c => c.ConnectorType == ConnectorType.End)
                .OrderBy(c => c.Origin.Z)
                .ToList();

            if (endConns == null || endConns.Count < 2) return SprigClass.Other;

            Connector lower = endConns.First();
            Connector upper = endConns.Last();

            if (ReachesSprinkler(upper, new HashSet<ElementId> { pipe.Id }, MaxHops))
                return SprigClass.Sprig;
            if (ReachesSprinkler(lower, new HashSet<ElementId> { pipe.Id }, MaxHops))
                return SprigClass.Drop;

            return SprigClass.Other;
        }

        /// <summary>
        /// Depth-limited walk from a pipe-end connector outward looking for a
        /// sprinkler, passing through pipe fittings (reducing couplings, etc.)
        /// and short nipple pipes. The visited set is keyed by element id so we
        /// never loop or walk back into the pipe we came from.
        /// </summary>
        private bool ReachesSprinkler(Connector from, HashSet<ElementId> visited, int hopsLeft)
        {
            if (from == null) return false;

            ConnectorSet refs;
            try { refs = from.AllRefs; }
            catch { return false; }
            if (refs == null) return false;

            foreach (Connector other in refs)
            {
                Element owner = other.Owner;
                if (owner == null) continue;
                if (visited.Contains(owner.Id)) continue;

                int cat = owner.Category?.Id.IntegerValue ?? 0;
                if (cat == (int)BuiltInCategory.OST_Sprinklers) return true;

                if (hopsLeft > 0)
                {
                    bool fitting = cat == (int)BuiltInCategory.OST_PipeFitting;
                    bool shortPipe = (owner is Pipe op && SafePipeLength(op) <= ShortPipeFt)
                                     || owner is FlexPipe;

                    if (fitting || shortPipe)
                    {
                        visited.Add(owner.Id);
                        ConnectorSet conns = GetConnectors(owner);
                        if (conns != null)
                        {
                            foreach (Connector oc in conns)
                            {
                                if (ReachesSprinkler(oc, visited, hopsLeft - 1))
                                    return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private ConnectorSet GetConnectors(Element e)
        {
            if (e is Pipe p) return p.ConnectorManager?.Connectors;
            if (e is FlexPipe fp) return fp.ConnectorManager?.Connectors;
            if (e is FamilyInstance fi) return fi.MEPModel?.ConnectorManager?.Connectors;
            return null;
        }

        private double SafePipeLength(Pipe p)
        {
            try { return PipeHelpers.GetPipeLength(p); }
            catch { return double.MaxValue; }
        }

        // ══════════════════════════════════════════════════════════════
        //  TAG TYPE COLLECTION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Collects every loaded FamilySymbol in a tag/annotation category that
        /// the user could pick as the SPRIG type — the fixed tag categories
        /// plus whatever categories the candidate tags themselves use.
        /// </summary>
        private List<FamilySymbol> CollectTagSymbols(Document doc, HashSet<int> extraCatIds)
        {
            var catIds = new HashSet<int>(extraCatIds);
            foreach (var bic in TagCategories) catIds.Add((int)bic);

            var seen = new HashSet<ElementId>();
            var result = new List<FamilySymbol>();

            foreach (var fs in new FilteredElementCollector(doc)
                         .OfClass(typeof(FamilySymbol))
                         .Cast<FamilySymbol>())
            {
                int? cid = fs.Category?.Id.IntegerValue;
                if (cid == null || !catIds.Contains(cid.Value)) continue;
                if (seen.Add(fs.Id)) result.Add(fs);
            }

            return result
                .OrderBy(f => f.Family.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
