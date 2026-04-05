using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.ViewsAndSheets
{
    /// <summary>
    /// Creates floor and/or ceiling plan views for selected levels.
    /// Applies a view template and names each view as "LEVEL NAME - SUFFIX".
    ///
    /// WORKFLOW:
    ///   1. Dialog: view type, level selection, templates, name suffix
    ///   2. Find default ViewFamilyType for FloorPlan and CeilingPlan
    ///   3. Create ViewPlan for each selected level
    ///   4. Apply view template
    ///   5. Rename to "LEVEL NAME - SUFFIX"
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreatePlanViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Collect levels ──
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderByDescending(l => l.Elevation)
                    .ToList();

                if (levels.Count == 0)
                {
                    TaskDialog.Show("Create Plan Views", "No levels found in the project.");
                    return Result.Failed;
                }

                var levelDisplayNames = levels
                    .Select(l => $"{l.Name}  (Elev: {FormatElevation(l.Elevation)})")
                    .ToList();
                var levelNames = levels.Select(l => l.Name).ToList();

                // ── Step 2: Collect view templates ──
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .ToList();

                var floorTemplates = allViews
                    .Where(v => v.ViewType == ViewType.FloorPlan)
                    .Select(v => v.Name)
                    .OrderBy(n => n)
                    .ToList();

                var ceilingTemplates = allViews
                    .Where(v => v.ViewType == ViewType.CeilingPlan)
                    .Select(v => v.Name)
                    .OrderBy(n => n)
                    .ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new CreatePlanViewsDialog(
                    levelDisplayNames, levelNames, floorTemplates, ceilingTemplates))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    if (dialog.SelectedLevelNames.Count == 0)
                    {
                        TaskDialog.Show("Create Plan Views", "No levels selected.");
                        return Result.Cancelled;
                    }

                    bool doFloor = dialog.SelectedViewType != CreatePlanViewsDialog.ViewTypeOption.CeilingOnly;
                    bool doCeiling = dialog.SelectedViewType != CreatePlanViewsDialog.ViewTypeOption.FloorOnly;
                    string suffix = dialog.ViewNameSuffix;

                    // ── Step 4: Find ViewFamilyTypes ──
                    var viewFamilyTypes = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .ToList();

                    ViewFamilyType floorVFT = viewFamilyTypes
                        .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);
                    ViewFamilyType ceilingVFT = viewFamilyTypes
                        .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.CeilingPlan);

                    if (doFloor && floorVFT == null)
                    {
                        TaskDialog.Show("Create Plan Views",
                            "No Floor Plan view family type found.");
                        return Result.Failed;
                    }
                    if (doCeiling && ceilingVFT == null)
                    {
                        TaskDialog.Show("Create Plan Views",
                            "No Ceiling Plan view family type found.");
                        return Result.Failed;
                    }

                    // Find view templates by name
                    View floorTemplateView = null;
                    View ceilingTemplateView = null;
                    if (!string.IsNullOrEmpty(dialog.FloorTemplate))
                    {
                        floorTemplateView = allViews
                            .FirstOrDefault(v => v.Name == dialog.FloorTemplate &&
                                                 v.ViewType == ViewType.FloorPlan);
                    }
                    if (!string.IsNullOrEmpty(dialog.CeilingTemplate))
                    {
                        ceilingTemplateView = allViews
                            .FirstOrDefault(v => v.Name == dialog.CeilingTemplate &&
                                                 v.ViewType == ViewType.CeilingPlan);
                    }

                    // ── Step 5: Create views ──
                    int floorCreated = 0;
                    int ceilingCreated = 0;
                    int skipped = 0;

                    // Get selected levels
                    var selectedLevelSet = new HashSet<string>(dialog.SelectedLevelNames);
                    var selectedLevels = levels.Where(l => selectedLevelSet.Contains(l.Name)).ToList();

                    using (var tw = new TransactionWrapper(doc, "Create Plan Views"))
                    {
                        foreach (var level in selectedLevels)
                        {
                            string viewName = level.Name.ToUpper();
                            if (!string.IsNullOrEmpty(suffix))
                                viewName += " - " + suffix;

                            // Create floor plan
                            if (doFloor)
                            {
                                try
                                {
                                    ViewPlan floorView = ViewPlan.Create(doc, floorVFT.Id, level.Id);
                                    if (floorView != null)
                                    {
                                        // Apply template
                                        if (floorTemplateView != null)
                                            floorView.ViewTemplateId = floorTemplateView.Id;

                                        // Rename
                                        try { floorView.Name = viewName; }
                                        catch { } // Name might already exist

                                        floorCreated++;
                                    }
                                }
                                catch (Exception)
                                {
                                    skipped++;
                                }
                            }

                            // Create ceiling plan
                            if (doCeiling)
                            {
                                try
                                {
                                    ViewPlan ceilingView = ViewPlan.Create(doc, ceilingVFT.Id, level.Id);
                                    if (ceilingView != null)
                                    {
                                        // Apply template
                                        if (ceilingTemplateView != null)
                                            ceilingView.ViewTemplateId = ceilingTemplateView.Id;

                                        // Rename
                                        try { ceilingView.Name = viewName; }
                                        catch { } // Name might already exist

                                        ceilingCreated++;
                                    }
                                }
                                catch (Exception)
                                {
                                    skipped++;
                                }
                            }
                        }

                        tw.Commit();
                    }

                    // ── Summary ──
                    string summary = "Plan Views Created:\n\n";
                    if (doFloor)
                        summary += $"Floor plans: {floorCreated}\n";
                    if (doCeiling)
                        summary += $"Ceiling plans: {ceilingCreated}\n";
                    if (skipped > 0)
                        summary += $"\nSkipped (errors): {skipped}";
                    summary += $"\nName format: LEVEL NAME - {suffix}";

                    if (floorTemplateView != null && doFloor)
                        summary += $"\nFloor template: {dialog.FloorTemplate}";
                    if (ceilingTemplateView != null && doCeiling)
                        summary += $"\nCeiling template: {dialog.CeilingTemplate}";

                    TaskDialog.Show("Create Plan Views — Complete", summary);
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
        /// Format elevation: whole numbers without decimals, others strip trailing zeros.
        /// </summary>
        private string FormatElevation(double elevFeet)
        {
            if (Math.Abs(elevFeet - Math.Round(elevFeet)) < 0.001)
                return ((int)Math.Round(elevFeet)).ToString();
            return elevFeet.ToString("F4").TrimEnd('0').TrimEnd('.');
        }
    }
}
