using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Inserts flexible drop length tags on sprinkler heads, automatically
    /// calculating the standard length from the actual flex pipe connected
    /// to each sprinkler. Supports Wet and Dry system types with different
    /// standard length thresholds.
    ///
    /// KEY DIFFERENCE from InsertFlexDropLengthsCommand:
    ///   Standard version: user picks one fixed length for all sprinklers
    ///   Dalmatian version: auto-reads each sprinkler's connected flex pipe
    ///   and assigns the correct standard length per Wet/Dry thresholds
    ///
    /// WET thresholds:
    ///   pipe <= 3'-6" (3.5 ft)  → "48"
    ///   pipe <= 4'-6" (4.5 ft)  → "60"
    ///   pipe <= 5'-6" (5.5 ft)  → "72"
    ///   pipe > 5'-6"            → flagged "Exceeds 5.5 Ft Length"
    ///
    /// DRY thresholds:
    ///   pipe <= 2'-8" (32/12 ft)  → "38"
    ///   pipe <= 3'-8" (44/12 ft)  → "50"
    ///   pipe <= 4'-4" (52/12 ft)  → "58"
    ///   pipe > 4'-4"              → flagged "Exceeds 4'-4\" Length"
    ///
    /// WORKFLOW:
    ///   1. User selects sprinkler heads
    ///   2. Dialog: pick Wet or Dry system type, tag orientation
    ///   3. For each sprinkler, find connected flex pipe and read its length
    ///   4. Assign standard length string from threshold table
    ///   5. Delete existing flex drop tags on selected sprinklers
    ///   6. Write "Flex Pipe Length" parameter, create tag
    ///   7. Flag sprinklers with too-long flex pipes
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FlexDropLengthsDalmatianCommand : IExternalCommand
    {
        private const string FlexPipeLengthParam = "Flex Pipe Length";

        // ── Wet system thresholds (feet → standard length string) ──
        private static readonly (double MaxFeet, string LengthTag)[] WetThresholds = new[]
        {
            (3.5,  "48"),    // <= 3'-6" → 48"
            (4.5,  "60"),    // <= 4'-6" → 60"
            (5.5,  "72"),    // <= 5'-6" → 72"
        };
        private const double WetMaxFeet = 5.5;  // 66/12
        private const string WetExceedsMsg = "Exceeds 5.5 Ft Length";

        // ── Dry system thresholds (feet → standard length string) ──
        private static readonly (double MaxFeet, string LengthTag)[] DryThresholds = new[]
        {
            (32.0 / 12.0,  "38"),    // <= 2'-8" → 38"
            (44.0 / 12.0,  "50"),    // <= 3'-8" → 50"
            (52.0 / 12.0,  "58"),    // <= 4'-4" → 58"
        };
        private const double DryMaxFeet = 52.0 / 12.0;  // 4'-4"
        private const string DryExceedsMsg = "Exceeds 4'-4\" Length";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Step 1: Select sprinklers ──
                IList<Reference> sprinklerRefs;
                try
                {
                    sprinklerRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new CategorySelectionFilter(BuiltInCategory.OST_Sprinklers),
                        "Select sprinkler heads to add flexible drop lengths to, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (sprinklerRefs == null || sprinklerRefs.Count == 0)
                {
                    TaskDialog.Show("Flex Drop Lengths", "No sprinklers selected.");
                    return Result.Cancelled;
                }

                var sprinklers = sprinklerRefs
                    .Select(r => doc.GetElement(r) as FamilyInstance)
                    .Where(fi => fi != null)
                    .ToList();

                if (sprinklers.Count == 0)
                {
                    TaskDialog.Show("Flex Drop Lengths", "No valid sprinkler instances found.");
                    return Result.Cancelled;
                }

                // ── Step 2: Show dialog ──
                using (var dialog = new FlexDropLengthsDalmatianDialog(sprinklers.Count))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    bool isWet = dialog.IsWetSystem;
                    var thresholds = isWet ? WetThresholds : DryThresholds;
                    double maxLength = isWet ? WetMaxFeet : DryMaxFeet;
                    string exceedsMsg = isWet ? WetExceedsMsg : DryExceedsMsg;

                    // ── Step 3: Find flex pipe connected to each sprinkler ──
                    var sprinklerResults = new List<SprinklerFlexResult>();
                    var tooLongSprinklers = new List<FamilyInstance>();

                    foreach (var sprinkler in sprinklers)
                    {
                        double flexLength = FindConnectedFlexPipeLength(doc, sprinkler);
                        string lengthTag;

                        if (flexLength < 0)
                        {
                            // No flex pipe found — skip or use smallest bucket
                            lengthTag = thresholds[0].LengthTag;
                        }
                        else if (flexLength > maxLength)
                        {
                            // Too long — flag it
                            lengthTag = exceedsMsg;
                            tooLongSprinklers.Add(sprinkler);
                        }
                        else
                        {
                            // Find the right threshold bucket
                            lengthTag = thresholds[thresholds.Length - 1].LengthTag;
                            for (int i = 0; i < thresholds.Length; i++)
                            {
                                if (flexLength <= thresholds[i].MaxFeet)
                                {
                                    lengthTag = thresholds[i].LengthTag;
                                    break;
                                }
                            }
                        }

                        sprinklerResults.Add(new SprinklerFlexResult
                        {
                            Sprinkler = sprinkler,
                            FlexLengthFeet = flexLength,
                            AssignedLengthTag = lengthTag,
                            ExceedsMax = flexLength > maxLength
                        });
                    }

                    // ── Step 4: Find tag family ──
                    // Dalmatian style: tag family name built from sprinkler Description + "-Flex Drop Length Tag"
                    // Fallback: use generic "-Flex Drop Length Tag" if specific not found
                    var tagFamilyCache = new Dictionary<string, FamilySymbol>();

                    // ── Step 5: Process in transaction ──
                    int tagsDeleted = 0;
                    int tagsCreated = 0;
                    int paramsWritten = 0;

                    using (var tw = new TransactionWrapper(doc, "Insert Flex Drop Lengths (Dalmatian)"))
                    {
                        // Delete existing flex drop tags on selected sprinklers in this view
                        var selectedIds = new HashSet<ElementId>(sprinklers.Select(s => s.Id));
                        tagsDeleted = DeleteExistingFlexTagsOnSprinklers(doc, activeView, selectedIds);

                        foreach (var result in sprinklerResults)
                        {
                            var sprinkler = result.Sprinkler;

                            // Write "Flex Pipe Length" parameter
                            string paramValue = result.AssignedLengthTag;
                            var param = sprinkler.LookupParameter(FlexPipeLengthParam);
                            if (param != null && !param.IsReadOnly)
                            {
                                if (param.StorageType == StorageType.String)
                                    param.Set(paramValue);
                                else if (param.StorageType == StorageType.Double)
                                {
                                    double val;
                                    if (double.TryParse(paramValue, out val))
                                        param.Set(val);
                                }
                                paramsWritten++;
                            }

                            // Get sprinkler location
                            XYZ location = GetElementLocation(sprinkler);
                            if (location == null) continue;

                            // Resolve tag family symbol
                            FamilySymbol tagSymbol = ResolveTagSymbol(doc, sprinkler, tagFamilyCache);
                            if (tagSymbol == null) continue;

                            if (!tagSymbol.IsActive)
                                tagSymbol.Activate();

                            // Create tag
                            try
                            {
                                var tagRef = new Reference(sprinkler);
                                IndependentTag tag = IndependentTag.Create(
                                    doc,
                                    activeView.Id,
                                    tagRef,
                                    false,                              // addLeader
                                    TagMode.TM_ADDBY_CATEGORY,
                                    TagOrientation.Horizontal,
                                    location);

                                if (tag != null)
                                {
                                    tag.ChangeTypeId(tagSymbol.Id);
                                    tagsCreated++;
                                }
                            }
                            catch (Exception)
                            {
                                // Skip sprinklers that can't be tagged
                            }
                        }

                        tw.Commit();
                    }

                    // ── Step 6: Highlight too-long flex pipes ──
                    if (tooLongSprinklers.Count > 0)
                    {
                        uidoc.Selection.SetElementIds(
                            tooLongSprinklers.Select(s => s.Id).ToList());

                        string longList = string.Join("\n",
                            tooLongSprinklers.Select(s =>
                            {
                                double len = FindConnectedFlexPipeLength(doc, s);
                                return $"  Sprinkler {s.Id}: flex pipe = {len:F2} ft";
                            }));

                        TaskDialog.Show("Long Flex Drops",
                            $"{tooLongSprinklers.Count} sprinkler(s) have flex pipes exceeding " +
                            $"the {(isWet ? "5'-6\" (Wet)" : "4'-4\" (Dry)")} maximum:\n\n" +
                            longList + "\n\n" +
                            "These are highlighted in the current selection.");
                    }

                    // ── Step 7: Summary ──
                    var summary = BuildSummary(sprinklerResults, isWet);
                    string summaryMsg = $"Flex Drop Summary ({(isWet ? "Wet" : "Dry")} System):\n\n" +
                                        summary + "\n\n" +
                                        $"Tags deleted: {tagsDeleted}\n" +
                                        $"Tags created: {tagsCreated}\n" +
                                        $"Parameters written: {paramsWritten}";

                    if (tooLongSprinklers.Count > 0)
                        summaryMsg += $"\nExceeding max length: {tooLongSprinklers.Count}";

                    TaskDialog.Show("Flex Drop Lengths — Complete", summaryMsg);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Flex Drop Lengths (Dalmatian) failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  CONNECTED FLEX PIPE FINDING
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the flex pipe connected to a sprinkler head and returns its length in feet.
        /// Traverses through fittings if needed (sprinkler → fitting → flex pipe).
        /// Returns -1 if no connected flex pipe is found.
        /// </summary>
        private double FindConnectedFlexPipeLength(Document doc, FamilyInstance sprinkler)
        {
            // Get connectors on the sprinkler
            ConnectorSet connectors = sprinkler.MEPModel?.ConnectorManager?.Connectors;
            if (connectors == null) return -1;

            // First pass: look for direct flex pipe connection
            foreach (Connector conn in connectors)
            {
                if (!conn.IsConnected) continue;

                foreach (Connector other in conn.AllRefs)
                {
                    Element connected = other.Owner;
                    if (connected == null) continue;

                    // Direct flex pipe connection
                    if (connected is Autodesk.Revit.DB.Plumbing.FlexPipe flexPipe)
                    {
                        return GetFlexPipeLength(flexPipe);
                    }
                }
            }

            // Second pass: traverse through fittings (sprinkler → fitting → flex pipe)
            foreach (Connector conn in connectors)
            {
                if (!conn.IsConnected) continue;

                foreach (Connector other in conn.AllRefs)
                {
                    Element fitting = other.Owner;
                    if (fitting == null) continue;

                    // Check if it's a pipe fitting
                    if (fitting.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeFitting)
                        continue;

                    // Search the fitting's other connectors for a flex pipe
                    FamilyInstance fittingInst = fitting as FamilyInstance;
                    if (fittingInst == null) continue;

                    ConnectorSet fittingConns = fittingInst.MEPModel?.ConnectorManager?.Connectors;
                    if (fittingConns == null) continue;

                    foreach (Connector fConn in fittingConns)
                    {
                        if (!fConn.IsConnected) continue;

                        foreach (Connector fOther in fConn.AllRefs)
                        {
                            if (fOther.Owner is Autodesk.Revit.DB.Plumbing.FlexPipe fp)
                            {
                                return GetFlexPipeLength(fp);
                            }
                        }
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Get the length of a flex pipe from its "Length" parameter (in feet).
        /// </summary>
        private double GetFlexPipeLength(Autodesk.Revit.DB.Plumbing.FlexPipe flexPipe)
        {
            Parameter lengthParam = flexPipe.LookupParameter("Length");
            if (lengthParam != null && lengthParam.StorageType == StorageType.Double)
                return lengthParam.AsDouble();

            // Fallback: try built-in parameter
            Parameter bp = flexPipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (bp != null)
                return bp.AsDouble();

            return -1;
        }

        // ══════════════════════════════════════════════════════════════
        //  TAG FAMILY RESOLUTION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve the tag family symbol for a sprinkler.
        /// Dalmatian style: tries [SprinklerDescription]-Flex Drop Length Tag first,
        /// then falls back to generic "-Flex Drop Length Tag".
        /// </summary>
        private FamilySymbol ResolveTagSymbol(
            Document doc, FamilyInstance sprinkler,
            Dictionary<string, FamilySymbol> cache)
        {
            // Try to get sprinkler Description for dynamic tag naming
            string description = ParameterHelpers.GetParamValueAsString(sprinkler, "Description");
            string specificTagName = !string.IsNullOrEmpty(description)
                ? description + "-Flex Drop Length Tag"
                : null;

            // Check cache first
            string cacheKey = specificTagName ?? "-Flex Drop Length Tag";
            if (cache.TryGetValue(cacheKey, out FamilySymbol cached))
                return cached;

            // Try specific tag family
            FamilySymbol symbol = null;
            if (specificTagName != null)
            {
                symbol = FindTagFamilySymbol(doc, specificTagName);
            }

            // Fallback to generic
            if (symbol == null)
            {
                symbol = FindTagFamilySymbol(doc, "-Flex Drop Length Tag");
            }

            if (symbol != null)
                cache[cacheKey] = symbol;

            return symbol;
        }

        /// <summary>
        /// Finds a tag family symbol by family name.
        /// </summary>
        private FamilySymbol FindTagFamilySymbol(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_SprinklerTags)
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family.Name == familyName);
        }

        // ══════════════════════════════════════════════════════════════
        //  TAG DELETION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Deletes existing flex drop length tags on the selected sprinklers in the active view.
        /// Only deletes tags whose tagged element is in the selectedIds set.
        /// </summary>
        private int DeleteExistingFlexTagsOnSprinklers(
            Document doc, View activeView, HashSet<ElementId> selectedIds)
        {
            int deleted = 0;

            var tags = new FilteredElementCollector(doc, activeView.Id)
                .OfCategory(BuiltInCategory.OST_SprinklerTags)
                .WhereElementIsNotElementType()
                .Cast<IndependentTag>()
                .ToList();

            foreach (var tag in tags)
            {
                // Check if this tag is a flex drop length tag
                var typeElem = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                if (typeElem == null) continue;
                string familyName = typeElem.Family?.Name ?? "";
                if (!familyName.Contains("Flex Drop Length Tag")) continue;

                // Check if the tagged element is one of our selected sprinklers
                try
                {
                    // GetTaggedElementIds returns LinkElementId objects;
                    // for local (non-linked) elements, HostElementId has the element ID
                    var taggedIds = tag.GetTaggedElementIds();
                    foreach (var linkElId in taggedIds)
                    {
                        if (selectedIds.Contains(linkElId.HostElementId))
                        {
                            doc.Delete(tag.Id);
                            deleted++;
                            break;
                        }
                    }
                }
                catch { }
            }

            return deleted;
        }

        // ══════════════════════════════════════════════════════════════
        //  SUMMARY
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Build a grouped summary of flex drop length assignments.
        /// </summary>
        private string BuildSummary(List<SprinklerFlexResult> results, bool isWet)
        {
            var groups = results
                .GroupBy(r => r.AssignedLengthTag)
                .OrderBy(g => g.Key)
                .Select(g => $"  {g.Key}: {g.Count()} sprinkler{(g.Count() != 1 ? "s" : "")}")
                .ToList();

            return string.Join("\n", groups);
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Result of processing one sprinkler.
        /// </summary>
        private class SprinklerFlexResult
        {
            public FamilyInstance Sprinkler { get; set; }
            public double FlexLengthFeet { get; set; }
            public string AssignedLengthTag { get; set; }
            public bool ExceedsMax { get; set; }
        }

        /// <summary>
        /// Gets the XYZ location point of a family instance.
        /// </summary>
        private XYZ GetElementLocation(FamilyInstance fi)
        {
            if (fi.Location is LocationPoint lp)
                return lp.Point;

            var bb = fi.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) / 2.0;

            return null;
        }
    }
}
