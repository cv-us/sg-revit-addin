using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Setup
{
    /// <summary>
    /// Copies levels and/or grids from a selected linked Revit model into the
    /// host document. Detects and skips duplicates (by name), reassigns grid
    /// types, and optionally pins the new elements.
    ///
    /// WORKFLOW:
    ///   1. Dialog: select link, import mode, grid type, selection options, pin
    ///   2. Collect levels/grids from the linked model
    ///   3. Filter out duplicates already existing in the host
    ///   4. Optional: show secondary dialog for user to pick specific items
    ///   5. Copy via CopyElements from linked document
    ///   6. Reassign grid type on copied grids
    ///   7. Optionally pin all new elements
    ///   8. Summary dialog
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CopyLinkLevelsGridsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Gather link instances ──
                var linkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(li => li.GetLinkDocument() != null)
                    .ToList();

                if (linkInstances.Count == 0)
                {
                    TaskDialog.Show("Copy Link Levels & Grids",
                        "No loaded Revit links found in this project.");
                    return Result.Failed;
                }

                var linkNames = linkInstances
                    .Select(li => li.GetLinkDocument().Title.Replace(".rvt", ""))
                    .ToList();

                // ── Step 2: Get grid types in host ──
                var gridTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Grids)
                    .WhereElementIsElementType()
                    .ToList();
                var gridTypeNames = gridTypes.Select(gt => gt.Name).OrderBy(n => n).ToList();

                // ── Step 3: Show main dialog ──
                using (var dialog = new CopyLinkLevelsGridsDialog(linkNames, gridTypeNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // Find the selected link
                    int linkIndex = linkNames.IndexOf(dialog.SelectedLinkName);
                    if (linkIndex < 0) linkIndex = 0;
                    RevitLinkInstance selectedLink = linkInstances[linkIndex];
                    Document linkDoc = selectedLink.GetLinkDocument();
                    Transform linkTransform = selectedLink.GetTotalTransform();

                    bool doLevels = dialog.Mode != CopyLinkLevelsGridsDialog.ImportMode.GridsOnly;
                    bool doGrids = dialog.Mode != CopyLinkLevelsGridsDialog.ImportMode.LevelsOnly;

                    // ── Step 4: Collect existing host levels/grids for duplicate detection ──
                    var existingLevelNames = new HashSet<string>(
                        new FilteredElementCollector(doc)
                            .OfClass(typeof(Level))
                            .Select(e => e.Name),
                        StringComparer.OrdinalIgnoreCase);

                    var existingGridNames = new HashSet<string>(
                        new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Grids)
                            .WhereElementIsNotElementType()
                            .Select(e => e.Name),
                        StringComparer.OrdinalIgnoreCase);

                    // ── Step 5: Get link levels and grids ──
                    var linkLevels = doLevels
                        ? new FilteredElementCollector(linkDoc)
                            .OfClass(typeof(Level))
                            .Cast<Level>()
                            .OrderBy(l => l.Elevation)
                            .ToList()
                        : new List<Level>();

                    var linkGrids = doGrids
                        ? new FilteredElementCollector(linkDoc)
                            .OfCategory(BuiltInCategory.OST_Grids)
                            .WhereElementIsNotElementType()
                            .Cast<Grid>()
                            .OrderBy(g => g.Name)
                            .ToList()
                        : new List<Grid>();

                    // ── Step 6: Filter out duplicates ──
                    var newLevels = linkLevels.Where(l => !existingLevelNames.Contains(l.Name)).ToList();
                    var skippedLevels = linkLevels.Where(l => existingLevelNames.Contains(l.Name)).ToList();
                    var newGrids = linkGrids.Where(g => !existingGridNames.Contains(g.Name)).ToList();
                    var skippedGrids = linkGrids.Where(g => existingGridNames.Contains(g.Name)).ToList();

                    // ── Step 7: Optional selection dialogs ──
                    if (doLevels && newLevels.Count > 0 &&
                        dialog.LevelSelectionMode == CopyLinkLevelsGridsDialog.SelectionMode.SelectSpecific)
                    {
                        var levelDisplayNames = newLevels
                            .Select(l => $"{l.Name}  (Elev: {FormatElevation(l.Elevation)})")
                            .ToList();

                        using (var selDlg = new SelectItemsDialog("Select Levels to Import", levelDisplayNames))
                        {
                            if (selDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                                return Result.Cancelled;

                            var selectedSet = new HashSet<string>(selDlg.SelectedItems);
                            newLevels = newLevels
                                .Where((l, i) => selectedSet.Contains(levelDisplayNames[i]))
                                .ToList();
                        }
                    }

                    if (doGrids && newGrids.Count > 0 &&
                        dialog.GridSelectionMode == CopyLinkLevelsGridsDialog.SelectionMode.SelectSpecific)
                    {
                        var gridDisplayNames = newGrids.Select(g => g.Name).ToList();

                        using (var selDlg = new SelectItemsDialog("Select Grids to Import", gridDisplayNames))
                        {
                            if (selDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                                return Result.Cancelled;

                            var selectedSet = new HashSet<string>(selDlg.SelectedItems);
                            newGrids = newGrids.Where(g => selectedSet.Contains(g.Name)).ToList();
                        }
                    }

                    if (newLevels.Count == 0 && newGrids.Count == 0)
                    {
                        string skipMsg = "";
                        if (skippedLevels.Count > 0)
                            skipMsg += $"{skippedLevels.Count} level(s) already exist.\n";
                        if (skippedGrids.Count > 0)
                            skipMsg += $"{skippedGrids.Count} grid(s) already exist.\n";

                        TaskDialog.Show("Copy Link Levels & Grids",
                            "Nothing to import.\n\n" + skipMsg);
                        return Result.Succeeded;
                    }

                    // ── Step 8: Copy elements in a transaction ──
                    int levelsCopied = 0;
                    int gridsCopied = 0;
                    var copiedElementIds = new List<ElementId>();

                    // Find the target grid type
                    Element targetGridType = gridTypes
                        .FirstOrDefault(gt => gt.Name == dialog.SelectedGridTypeName);

                    using (var tw = new TransactionWrapper(doc, "Copy Link Levels and Grids"))
                    {
                        // Copy levels
                        if (newLevels.Count > 0)
                        {
                            foreach (var linkLevel in newLevels)
                            {
                                try
                                {
                                    // Create a new level at the link level's elevation
                                    // Adjust for link transform if needed
                                    double elevation = linkLevel.Elevation;
                                    if (linkTransform != null && !linkTransform.IsIdentity)
                                    {
                                        XYZ transformed = linkTransform.OfPoint(new XYZ(0, 0, elevation));
                                        elevation = transformed.Z;
                                    }

                                    Level newLevel = Level.Create(doc, elevation);
                                    if (newLevel != null)
                                    {
                                        newLevel.Name = linkLevel.Name;
                                        copiedElementIds.Add(newLevel.Id);
                                        levelsCopied++;
                                    }
                                }
                                catch (Exception)
                                {
                                    // Skip levels that fail (e.g., name conflict race condition)
                                }
                            }
                        }

                        // Regenerate before creating grids (they may reference levels)
                        if (levelsCopied > 0)
                            doc.Regenerate();

                        // Copy grids
                        if (newGrids.Count > 0)
                        {
                            foreach (var linkGrid in newGrids)
                            {
                                try
                                {
                                    Curve gridCurve = linkGrid.Curve;
                                    if (gridCurve == null) continue;

                                    // Transform curve to host coordinates
                                    Curve hostCurve = gridCurve;
                                    if (linkTransform != null && !linkTransform.IsIdentity)
                                    {
                                        hostCurve = gridCurve.CreateTransformed(linkTransform);
                                    }

                                    Grid newGrid = null;
                                    if (hostCurve is Line line)
                                    {
                                        newGrid = Grid.Create(doc, line);
                                    }
                                    else if (hostCurve is Arc arc)
                                    {
                                        newGrid = Grid.Create(doc, arc);
                                    }

                                    if (newGrid != null)
                                    {
                                        newGrid.Name = linkGrid.Name;

                                        // Reassign grid type
                                        if (targetGridType != null)
                                        {
                                            try
                                            {
                                                newGrid.ChangeTypeId(targetGridType.Id);
                                            }
                                            catch { }
                                        }

                                        copiedElementIds.Add(newGrid.Id);
                                        gridsCopied++;
                                    }
                                }
                                catch (Exception)
                                {
                                    // Skip grids that fail
                                }
                            }
                        }

                        // Pin elements if requested
                        if (dialog.PinElements && copiedElementIds.Count > 0)
                        {
                            foreach (ElementId id in copiedElementIds)
                            {
                                Element elem = doc.GetElement(id);
                                if (elem != null)
                                {
                                    try { elem.Pinned = true; }
                                    catch { }
                                }
                            }
                        }

                        tw.Commit();
                    }

                    // ── Step 9: Summary ──
                    string summary = "Import Complete:\n\n";

                    if (doLevels)
                    {
                        summary += $"Levels copied: {levelsCopied}\n";
                        if (skippedLevels.Count > 0)
                        {
                            summary += $"Levels skipped (already exist): {skippedLevels.Count}\n";
                            summary += "  " + string.Join(", ", skippedLevels.Select(l => l.Name)) + "\n";
                        }
                    }

                    if (doGrids)
                    {
                        summary += $"\nGrids copied: {gridsCopied}\n";
                        if (skippedGrids.Count > 0)
                        {
                            summary += $"Grids skipped (already exist): {skippedGrids.Count}\n";
                            summary += "  " + string.Join(", ", skippedGrids.Select(g => g.Name)) + "\n";
                        }
                    }

                    if (dialog.PinElements && copiedElementIds.Count > 0)
                        summary += $"\nAll {copiedElementIds.Count} new element(s) pinned.";

                    if (targetGridType != null && gridsCopied > 0)
                        summary += $"\nGrid type set to: {dialog.SelectedGridTypeName}";

                    TaskDialog.Show("Link Levels and Grids — Complete", summary);
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
        /// Format an elevation value for display.
        /// Whole numbers display without decimals; others strip trailing zeros.
        /// </summary>
        private string FormatElevation(double elevationFeet)
        {
            if (Math.Abs(elevationFeet - Math.Round(elevationFeet)) < 0.001)
                return ((int)Math.Round(elevationFeet)).ToString();

            string s = elevationFeet.ToString("F4");
            return s.TrimEnd('0').TrimEnd('.');
        }
    }
}
