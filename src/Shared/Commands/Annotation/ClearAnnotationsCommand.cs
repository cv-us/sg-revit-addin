using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Deletes all Generic Annotation family instances from the active view.
    ///
    /// WORKFLOW:
    ///   1. Get the active view
    ///   2. Collect all FamilyInstance elements in the view with category OST_GenericAnnotation
    ///   3. If none found, inform the user and return
    ///   4. Confirm deletion count with the user via TaskDialog
    ///   5. Delete all instances in a single transaction and report the count
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearAnnotationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Step 1 & 2: Collect Generic Annotation instances in the active view ──
                var instances = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                // ── Step 3: Nothing to do ──
                if (instances.Count == 0)
                {
                    TaskDialog.Show("Clear Annotations",
                        "No generic annotation instances found in the active view.");
                    return Result.Succeeded;
                }

                // ── Step 4: Confirm with user ──
                TaskDialogResult confirm = TaskDialog.Show(
                    "Clear Annotations",
                    $"Found {instances.Count} generic annotation instance{(instances.Count != 1 ? "s" : "")} " +
                    $"in this view.\n\nDelete all?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirm != TaskDialogResult.Yes)
                    return Result.Cancelled;

                // ── Step 5: Delete all instances ──
                var ids = instances.Select(fi => fi.Id).ToList();

                using (var tw = new TransactionWrapper(doc, "Clear Generic Annotations"))
                {
                    doc.Delete(ids);
                    tw.Commit();
                }

                TaskDialog.Show("Clear Annotations",
                    $"Deleted {ids.Count} generic annotation instance{(ids.Count != 1 ? "s" : "")}.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

