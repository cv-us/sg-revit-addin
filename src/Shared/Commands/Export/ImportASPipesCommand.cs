using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Export
{
    /// <summary>
    /// Imports pipe geometry from an AutoSPRINK CSV export and creates Revit pipes.
    ///
    /// CSV format (AutoSPRINK export, coordinates in inches):
    ///   Row 0: header (skipped)
    ///   Rows 1+: [name, X1, Y1, Z1, X2, Y2, Z2, ...]
    ///
    /// The command divides all coordinate values by 12 to convert from inches to feet
    /// (Revit internal units).
    ///
    /// WORKFLOW:
    ///   1. User browses for the AutoSPRINK CSV file
    ///   2. Dialog: pick level, pipe type, and piping system type
    ///   3. Parse CSV — skip header row, read start/end XYZ from columns 1–6
    ///   4. Skip degenerate rows (zero-length, parse errors)
    ///   5. Create Revit pipes in a single transaction
    ///   6. Report count
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportASPipesCommand : IExternalCommand
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
                var pipeTypeNames = GetPipeTypeNames(doc);
                var systemTypeNames = GetSystemTypeNames(doc);

                if (pipeTypeNames.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Import AS Pipes", "No pipe types found in the project.");
                    return Result.Failed;
                }

                // ── Step 3: Show dialog ──
                using (var dialog = new ImportASPipesDialog(levelNames, pipeTypeNames, systemTypeNames))
                {
                    if (dialog.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    Level level = GetLevelByName(doc, dialog.SelectedLevelName);
                    if (level == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Import AS Pipes", $"Level '{dialog.SelectedLevelName}' not found.");
                        return Result.Failed;
                    }

                    PipeType pipeType = GetPipeTypeByName(doc, dialog.SelectedPipeTypeName);
                    if (pipeType == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Import AS Pipes", $"Pipe type '{dialog.SelectedPipeTypeName}' not found.");
                        return Result.Failed;
                    }

                    MEPSystemType systemType = GetSystemTypeByName(doc, dialog.SelectedSystemName);
                    if (systemType == null)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Import AS Pipes", $"Piping system '{dialog.SelectedSystemName}' not found.");
                        return Result.Failed;
                    }

                    // ── Step 4: Parse CSV ──
                    var segments = ParseCsvSegments(csvPath);
                    if (segments.Count == 0)
                    {
                        Autodesk.Revit.UI.TaskDialog.Show("Import AS Pipes", "No valid pipe segments found in the CSV file.\n" +
                            "Expected columns: [name, X1, Y1, Z1, X2, Y2, Z2] with coordinates in inches.");
                        return Result.Failed;
                    }

                    // ── Step 5: Create pipes ──
                    int created = 0;
                    int skipped = 0;

                    using (var tw = new TransactionWrapper(doc, "Import AutoSPRINK Pipes"))
                    {
                        foreach (var (start, end) in segments)
                        {
                            // Skip zero-length segments
                            if (start.DistanceTo(end) < 0.001)
                            {
                                skipped++;
                                continue;
                            }

                            try
                            {
                                Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, start, end);
                                created++;
                            }
                            catch
                            {
                                skipped++;
                            }
                        }

                        tw.Commit();
                    }

                    Autodesk.Revit.UI.TaskDialog.Show("Import AS Pipes — Complete",
                        $"Created {created} pipe{(created != 1 ? "s" : "")}." +
                        (skipped > 0 ? $"\n{skipped} row{(skipped != 1 ? "s" : "")} skipped (zero-length or invalid)." : ""));
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
        /// Opens a file browser dialog to select a CSV file.
        /// Returns the selected path, or null/empty if cancelled.
        /// </summary>
        private string BrowseForCsv()
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "Select AutoSPRINK Pipe CSV Export",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FilterIndex = 1
            })
            {
                return ofd.ShowDialog() == DialogResult.OK ? ofd.FileName : string.Empty;
            }
        }

        /// <summary>
        /// Parses the CSV file and returns a list of (start, end) point pairs in feet.
        /// Expects: header row (skipped), then rows with [name, X1, Y1, Z1, X2, Y2, Z2, ...]
        /// Coordinates are in inches and are divided by 12 to produce feet.
        /// </summary>
        private List<(XYZ start, XYZ end)> ParseCsvSegments(string filePath)
        {
            var result = new List<(XYZ, XYZ)>();

            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch { return result; }

            // Skip header row
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] cols = line.Split(',');
                if (cols.Length < 7) continue;

                // Columns 1–6: X1,Y1,Z1,X2,Y2,Z2 in inches
                if (!TryParseInchesToFeet(cols, 1, out double x1)) continue;
                if (!TryParseInchesToFeet(cols, 2, out double y1)) continue;
                if (!TryParseInchesToFeet(cols, 3, out double z1)) continue;
                if (!TryParseInchesToFeet(cols, 4, out double x2)) continue;
                if (!TryParseInchesToFeet(cols, 5, out double y2)) continue;
                if (!TryParseInchesToFeet(cols, 6, out double z2)) continue;

                result.Add((new XYZ(x1, y1, z1), new XYZ(x2, y2, z2)));
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

        private IList<string> GetPipeTypeNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .Select(t => t.Name)
                .OrderBy(n => n)
                .ToList();
        }

        private IList<string> GetSystemTypeNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(MEPSystemType))
                .Cast<MEPSystemType>()
                .Where(s => s.SystemClassification == MEPSystemClassification.SupplyHydronic
                         || s.SystemClassification == MEPSystemClassification.ReturnHydronic
                         || s.SystemClassification == MEPSystemClassification.FireProtectWet
                         || s.SystemClassification == MEPSystemClassification.FireProtectDry
                         || s.SystemClassification == MEPSystemClassification.FireProtectPreaction
                         || s.SystemClassification == MEPSystemClassification.FireProtectOther
                         || s.SystemClassification == MEPSystemClassification.OtherPipe)
                .Select(s => s.Name)
                .OrderBy(n => n)
                .ToList();
        }

        private Level GetLevelByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == name);
        }

        private PipeType GetPipeTypeByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipeType))
                .Cast<PipeType>()
                .FirstOrDefault(t => t.Name == name);
        }

        private MEPSystemType GetSystemTypeByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(MEPSystemType))
                .Cast<MEPSystemType>()
                .FirstOrDefault(s => s.Name == name);
        }
    }
}
