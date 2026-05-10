using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Creates dependent views from selected parent floor and/or ceiling plan views.
    /// Two modes:
    ///   1. Apply Scope Boxes — creates one dependent per scope box per parent view,
    ///      names them "LEVEL - SCOPE_BOX" and assigns the scope box parameter.
    ///   2. Blank Copies — creates N dependent copies per parent view without scope boxes.
    ///
    /// WORKFLOW:
    ///   1. Collect all non-template floor/ceiling plan views
    ///   2. Collect all scope boxes in the project
    ///   3. Show dialog for user to select views, mode, scope boxes, and copy count
    ///   4. Duplicate each selected view as dependent
    ///   5. (If scope box mode) rename and assign scope boxes
    ///   6. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateDependentViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                // ── Collect non-template floor plan views ──
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate)
                    .ToList();

                var floorViews = allViews
                    .Where(v => v.ViewType == ViewType.FloorPlan)
                    .OrderBy(v => v.Name)
                    .ToList();

                var ceilingViews = allViews
                    .Where(v => v.ViewType == ViewType.CeilingPlan)
                    .OrderBy(v => v.Name)
                    .ToList();

                // ── Collect scope boxes ──
                var scopeBoxes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .OrderBy(e => e.Name)
                    .ToList();

                if (floorViews.Count == 0 && ceilingViews.Count == 0)
                {
                    TaskDialog.Show("Create Dependent Views",
                        "No non-template floor or ceiling plan views found in the project.");
                    return Result.Succeeded;
                }

                // ── Show dialog ──
                var floorNames = floorViews.Select(v => v.Name).ToList();
                var ceilingNames = ceilingViews.Select(v => v.Name).ToList();
                var scopeBoxNames = scopeBoxes.Select(e => e.Name).ToList();

                using (var dlg = new CreateDependentViewsDialog(floorNames, ceilingNames, scopeBoxNames))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // Map selected names back to elements
                    var selectedFloor = floorViews
                        .Where(v => dlg.SelectedFloorViewNames.Contains(v.Name))
                        .ToList();
                    var selectedCeiling = ceilingViews
                        .Where(v => dlg.SelectedCeilingViewNames.Contains(v.Name))
                        .ToList();
                    var selectedScopeBoxes = scopeBoxes
                        .Where(e => dlg.SelectedScopeBoxNames.Contains(e.Name))
                        .ToList();

                    int totalParents = selectedFloor.Count + selectedCeiling.Count;
                    if (totalParents == 0)
                    {
                        TaskDialog.Show("Create Dependent Views", "No views selected.");
                        return Result.Cancelled;
                    }

                    bool applyScopeBoxes = dlg.ApplyScopeBoxes;
                    int copyCount = dlg.CopyCount;

                    if (applyScopeBoxes && selectedScopeBoxes.Count == 0)
                    {
                        TaskDialog.Show("Create Dependent Views",
                            "No scope boxes selected. Please select at least one scope box or use 'Blank Copies' mode.");
                        return Result.Cancelled;
                    }

                    // ── Create dependent views ──
                    int created = 0;
                    int renamed = 0;
                    var errors = new List<string>();

                    using (Transaction tx = new Transaction(doc, "Create Dependent Views"))
                    {
                        tx.Start();

                        var allParents = new List<ViewPlan>();
                        allParents.AddRange(selectedFloor);
                        allParents.AddRange(selectedCeiling);

                        foreach (var parentView in allParents)
                        {
                            if (applyScopeBoxes)
                            {
                                created += CreateWithScopeBoxes(
                                    doc, parentView, selectedScopeBoxes,
                                    ref renamed, errors);
                            }
                            else
                            {
                                created += CreateBlankCopies(
                                    doc, parentView, copyCount, errors);
                            }
                        }

                        tx.Commit();
                    }

                    // ── Report ──
                    string report = $"Created {created} dependent views from {totalParents} parent views.";
                    if (applyScopeBoxes)
                        report += $"\nRenamed {renamed} views with scope box names.";
                    if (errors.Count > 0)
                        report += $"\n\n{errors.Count} errors:\n" +
                            string.Join("\n", errors.Take(10));

                    TaskDialog.Show("Create Dependent Views", report);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Create one dependent view per scope box for the given parent view.
        /// Names each dependent "LEVEL - SCOPE_BOX" and assigns the scope box.
        /// </summary>
        private int CreateWithScopeBoxes(
            Document doc, ViewPlan parentView, List<Element> scopeBoxes,
            ref int renamed, List<string> errors)
        {
            int count = 0;

            // Get the associated level name for naming
            string levelName = parentView.GenLevel?.Name ?? parentView.Name;

            foreach (var scopeBox in scopeBoxes)
            {
                try
                {
                    ElementId depId = parentView.Duplicate(ViewDuplicateOption.AsDependent);
                    View depView = doc.GetElement(depId) as View;
                    if (depView == null) continue;

                    count++;

                    // Name: "LEVEL - SCOPE_BOX" (uppercased)
                    string newName = $"{levelName} - {scopeBox.Name}".ToUpper();

                    try
                    {
                        depView.Name = newName;
                        renamed++;
                    }
                    catch
                    {
                        // Name collision — try with suffix
                        try
                        {
                            depView.Name = newName + " (2)";
                            renamed++;
                        }
                        catch { /* leave default name */ }
                    }

                    // Assign scope box
                    Parameter scopeBoxParam = depView.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (scopeBoxParam != null && !scopeBoxParam.IsReadOnly)
                    {
                        scopeBoxParam.Set(scopeBox.Id);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{parentView.Name} × {scopeBox.Name}: {ex.Message}");
                }
            }

            return count;
        }

        /// <summary>
        /// Create N blank dependent copies of the given parent view.
        /// </summary>
        private int CreateBlankCopies(
            Document doc, ViewPlan parentView, int copyCount, List<string> errors)
        {
            int count = 0;

            for (int i = 0; i < copyCount; i++)
            {
                try
                {
                    parentView.Duplicate(ViewDuplicateOption.AsDependent);
                    count++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{parentView.Name} copy {i + 1}: {ex.Message}");
                }
            }

            return count;
        }
    }
}

