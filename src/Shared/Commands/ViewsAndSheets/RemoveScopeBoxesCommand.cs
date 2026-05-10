using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Removes (deletes) scope box elements from the project.
    ///
    /// The user may pre-select specific scope boxes before running the command.
    /// If no scope boxes are pre-selected the command collects all scope boxes
    /// in the entire project (not filtered to the active view).
    ///
    /// WORKFLOW:
    ///   1. Check pre-selection for OST_VolumeOfInterest elements
    ///   2. If none pre-selected, collect all scope boxes project-wide
    ///   3. If none found, show an info dialog and return
    ///   4. Confirm: "Remove {n} scope box(es)?"
    ///   5. Delete with TransactionWrapper and report the count
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RemoveScopeBoxesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Check pre-selection ──
                var preSelected = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e?.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_VolumeOfInterest)
                    .ToList();

                List<Element> scopeBoxes;

                if (preSelected.Count > 0)
                {
                    scopeBoxes = preSelected;
                }
                else
                {
                    // ── Step 2: Collect all scope boxes project-wide ──
                    scopeBoxes = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                        .WhereElementIsNotElementType()
                        .ToList();
                }

                // ── Step 3: Nothing to remove ──
                if (scopeBoxes.Count == 0)
                {
                    TaskDialog.Show("Remove Scope Boxes",
                        "No scope boxes found in the project.");
                    return Result.Succeeded;
                }

                // ── Step 4: Confirm with user ──
                TaskDialogResult confirm = TaskDialog.Show(
                    "Remove Scope Boxes",
                    $"Remove {scopeBoxes.Count} scope box{(scopeBoxes.Count != 1 ? "es" : "")}?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirm != TaskDialogResult.Yes)
                    return Result.Cancelled;

                // ── Step 5: Delete ──
                var ids = scopeBoxes.Select(e => e.Id).ToList();

                using (var tw = new TransactionWrapper(doc, "Remove Scope Boxes"))
                {
                    doc.Delete(ids);
                    tw.Commit();
                }

                TaskDialog.Show("Remove Scope Boxes",
                    $"Removed {ids.Count} scope box{(ids.Count != 1 ? "es" : "")}.");

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

