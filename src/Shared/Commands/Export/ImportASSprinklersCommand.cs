using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Imports sprinkler head locations from an AutoSPRINK CSV export and places
    /// Revit sprinkler family instances at those coordinates.
    ///
    /// CSV format (AutoSPRINK export, coordinates in inches):
    ///   Row 0: header (skipped)
    ///   Rows 1+: [name, X, Y, Z, ...]
    ///
    /// The command divides all coordinate values by 12 to convert from inches to feet
    /// (Revit internal units).
    ///
    /// WORKFLOW:
    ///   1. User browses for the AutoSPRINK CSV file
    ///   2. Dialog: pick level, sprinkler family type, and optional Z offset
    ///   3. Parse CSV — skip header row, read XYZ from columns 1–3
    ///   4. Skip degenerate rows (parse errors)
    ///   5. Place sprinkler instances in a single transaction
    ///   6. Report count
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportASSprinklersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                // ── Step 1: Browse for CSV file ──
                string csvPath = BrowseForCsv();
                if (string.IsNullOrEmpty(csvPath))
                    return Result.Cancelled;

                // ── Step 2: Gather project data for dialog ──
                var levelNames = GetLevelNames(doc);
                var familyTypes = GetSprinklerFamilyTypes(doc);

                if (familyTypes.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Import AS Sprinklers", "No sprinkler family types found in the project.");
                    return Result.Failed;
                }

                var familyTypeNames = familyTypes.Select(ft => ft.FamilyName + " : " + ft.Name).ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new ImportASSprinklersDialog(levelNames, familyTypeNames))
                {
                    if (dialog.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    Level level = GetLevelByName(doc, dialog.SelectedLevelName);
                    if (level == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Import AS Sprinklers", $"Level '{dialog.SelectedLevelName}' not found.");
                        return Result.Failed;
                    }

                    FamilySymbol symbol = familyTypes.FirstOrDefault(ft =>
                        (ft.FamilyName + " : " + ft.Name) == dialog.SelectedFamilyTypeName);
                    if (symbol == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Import AS Sprinklers", $"Sprinkler type '{dialog.SelectedFamilyTypeName}' not found.");
                        return Result.Failed;
                    }

                    double zOffset = dialog.OffsetFromLevel;

                    // ── Step 4: Parse CSV ──
                    var points = ParseCsvPoints(csvPath, zOffset);
                    if (points.Count == 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Import AS Sprinklers", "No valid sprinkler locations found in the CSV file.\n" +
                            "Expected columns: [name, X, Y, Z] with coordinates in inches.");
                        return Result.Failed;
                    }

                    // ── Step 5: Place sprinklers ──
                    int created = 0;
                    int skipped = 0;

                    using (var tw = new TransactionWrapper(doc, "Import AutoSPRINK Sprinklers"))
                    {
                        // Activate symbol if needed
                        if (!symbol.IsActive)
                            symbol.Activate();

                        foreach (var point in points)
                        {
                            try
                            {
                                doc.Create.NewFamilyInstance(
                                    point,
                                    symbol,
                                    level,
                                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                created++;
                            }
                            catch
                            {
                                skipped++;
                            }
                        }

                        tw.Commit();
                    }

                    Autodesk.Revit.UI.TaskDialog.Show("Import AS Sprinklers — Complete",
                        $"Placed {created} sprinkler{(created != 1 ? "s" : "")}." +
                        (skipped > 0 ? $"\n{skipped} row{(skipped != 1 ? "s" : "")} skipped (invalid)." : ""));
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private string BrowseForCsv()
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "Select AutoSPRINK Sprinkler CSV Export",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FilterIndex = 1
            })
            {
                return ofd.ShowDialog() == DialogResult.OK ? ofd.FileName : string.Empty;
            }
        }

        /// <summary>
        /// Parses the CSV and returns XYZ points in feet.
        /// Expects: header row (skipped), then rows with [name, X, Y, Z, ...]
        /// Coordinates are in inches and divided by 12 to produce feet.
        /// zOffset (feet) is added to the Z value.
        /// </summary>
        private List<XYZ> ParseCsvPoints(string filePath, double zOffsetFeet)
        {
            var result = new List<XYZ>();

            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch { return result; }

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] cols = line.Split(',');
                if (cols.Length < 4) continue;

                if (!TryParseInchesToFeet(cols, 1, out double x)) continue;
                if (!TryParseInchesToFeet(cols, 2, out double y)) continue;
                if (!TryParseInchesToFeet(cols, 3, out double z)) continue;

                result.Add(new XYZ(x, y, z + zOffsetFeet));
            }

            return result;
        }

        private bool TryParseInchesToFeet(string[] cols, int index, out double feet)
        {
            feet = 0;
            if (!double.TryParse(cols[index].Trim(), out double inches)) return false;
            feet = inches / 12.0;
            return true;
        }

        private IList<string> GetLevelNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => l.Name)
                .ToList();
        }

        private List<FamilySymbol> GetSprinklerFamilyTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category?.Id?.IntegerValue == (int)BuiltInCategory.OST_Sprinklers)
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();
        }

        private Level GetLevelByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == name);
        }
    }
}
