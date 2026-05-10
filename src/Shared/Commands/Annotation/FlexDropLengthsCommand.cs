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
    /// Inserts flexible drop length tags on selected sprinkler heads and writes the
    /// "Flex Pipe Length" parameter with the user-selected standard length.
    ///
    /// WORKFLOW:
    ///   1. User selects sprinkler heads
    ///   2. Dialog: pick standard drop length (31/36/48/60/72") and tag orientation
    ///   3. Delete any existing "-Flex Drop Length Tag" tags in the active view
    ///   4. Create new tag for each sprinkler using "-Flex Drop Length Tag" family
    ///   5. Write "Flex Pipe Length" parameter on the sprinkler element
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FlexDropLengthsCommand : IExternalCommand
    {
        private const string TagFamilyName = "-Flex Drop Length Tag";
        private const string FlexPipeLengthParam = "Flex Pipe Length";

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
                        "Select sprinklers to add flexible drop lengths to, then press Finish");
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
                var dialog = new FlexDropLengthsDialog();
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                string lengthValue = dialog.SelectedLength;          // "31", "36", etc.
                string lengthDisplay = lengthValue + " Inches";      // "31 Inches", etc.

                // ── Step 3: Find tag family type ──
                FamilySymbol tagSymbol = FindTagFamilySymbol(doc);
                if (tagSymbol == null)
                {
                    TaskDialog.Show("Flex Drop Lengths",
                        "Tag family \"" + TagFamilyName + "\" not loaded in the project.\n\n" +
                        "Please load it before running this command.");
                    return Result.Failed;
                }

                // ── Step 4: Process in transaction ──
                int tagsDeleted = 0;
                int tagsCreated = 0;

                using (var tw = new TransactionWrapper(doc, "Insert Flex Drop Lengths"))
                {
                    // Activate tag symbol if needed
                    if (!tagSymbol.IsActive)
                        tagSymbol.Activate();

                    // Delete existing flex drop tags in view
                    tagsDeleted = DeleteExistingFlexTags(doc, activeView);

                    // Collect selected sprinkler IDs for quick lookup
                    var selectedIds = new HashSet<ElementId>(sprinklers.Select(s => s.Id));

                    // Create tags and set parameters
                    foreach (var sprinkler in sprinklers)
                    {
                        // Write "Flex Pipe Length" on the sprinkler
                        SetParamSafe(sprinkler, FlexPipeLengthParam, lengthDisplay);

                        // Get sprinkler location
                        XYZ location = GetElementLocation(sprinkler);
                        if (location == null) continue;

                        // Create tag
                        try
                        {
#if REVIT2025
                            var tagRef = new Reference(sprinkler);
                            IndependentTag tag = IndependentTag.Create(
                                doc,
                                activeView.Id,
                                tagRef,
                                false,                                    // addLeader
                                TagMode.TM_ADDBY_CATEGORY,
                                TagOrientation.Horizontal,
                                location);
#else
                            var tagRef = new Reference(sprinkler);
                            IndependentTag tag = IndependentTag.Create(
                                doc,
                                activeView.Id,
                                tagRef,
                                false,                                    // addLeader
                                TagMode.TM_ADDBY_CATEGORY,
                                TagOrientation.Horizontal,
                                location);
#endif

                            // Set the tag's type to our flex drop length tag
                            if (tag != null)
                            {
                                tag.ChangeTypeId(tagSymbol.Id);
                                tagsCreated++;
                            }
                        }
                        catch (Exception)
                        {
                            // Skip sprinklers that can't be tagged (e.g., wrong view type)
                        }
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Flex Drop Lengths",
                    $"Completed:\n" +
                    $"  {tagsDeleted} existing flex drop tag(s) removed\n" +
                    $"  {tagsCreated} new tag(s) created\n" +
                    $"  Length: {lengthDisplay}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Flex Drop Lengths failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Finds the "-Flex Drop Length Tag" family symbol in the document.
        /// Returns the first available type, or null if not loaded.
        /// </summary>
        private FamilySymbol FindTagFamilySymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_SprinklerTags)
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family.Name == TagFamilyName);
        }

        /// <summary>
        /// Deletes all "-Flex Drop Length Tag" instances in the active view.
        /// Returns the count of deleted tags.
        /// </summary>
        private int DeleteExistingFlexTags(Document doc, View activeView)
        {
            var existingTags = new FilteredElementCollector(doc, activeView.Id)
                .OfCategory(BuiltInCategory.OST_SprinklerTags)
                .WhereElementIsNotElementType()
                .Cast<IndependentTag>()
                .Where(t =>
                {
                    var typeElem = doc.GetElement(t.GetTypeId()) as FamilySymbol;
                    return typeElem?.Family?.Name == TagFamilyName;
                })
                .ToList();

            foreach (var tag in existingTags)
                doc.Delete(tag.Id);

            return existingTags.Count;
        }

        /// <summary>
        /// Gets the XYZ location point of a family instance.
        /// </summary>
        private XYZ GetElementLocation(FamilyInstance fi)
        {
            if (fi.Location is LocationPoint lp)
                return lp.Point;

            // Fallback: try bounding box center
            var bb = fi.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) / 2.0;

            return null;
        }

        /// <summary>
        /// Sets a string parameter by name, silently skipping if not found.
        /// </summary>
        private void SetParamSafe(Element elem, string paramName, string value)
        {
            var param = elem.LookupParameter(paramName);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                param.Set(value);
        }
    }
}

