using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Exports pipe hanger locations as Trimble-compatible CSV point files
    /// for field layout of hanger inserts before concrete pours.
    ///
    /// Output format: PointName,Northing,Easting,Elevation,Code
    /// (or PointName,Easting,Northing,Elevation,Code per user preference)
    ///
    /// Compatible with: Trimble FieldLink, Trimble Access (RTS total stations),
    /// Trimble Layout Manager, and any equipment accepting CSV point files.
    ///
    /// WORKFLOW:
    ///   1. Dialog: select scope, coordinate system, order, units, naming
    ///   2. Collect hangers from scope (selection, active view, or level)
    ///   3. Transform coordinates to shared/project basis
    ///   4. Apply unit conversion and elevation offset
    ///   5. Write CSV file with SaveFileDialog
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportTrimblePointsCommand : IExternalCommand
    {
        /// <summary>
        /// Family name patterns that identify valid pipe hangers.
        /// </summary>
        private static readonly string[] HangerFamilyPatterns = new[]
        {
            "-Pipe Hanger",
            "-Basic Adjustable",
            "Adjustable Ring Hanger"
        };

        private const double FeetToMeters = 0.3048;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Count pre-selected hangers ──
            int preSelCount = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilyInstance>()
                .Count(fi => IsValidHanger(fi));

            // ── Collect levels for the dialog ──
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            var levelNames = levels.Select(l => l.Name).ToList();

            // ── Show dialog ──
            using (var dlg = new ExportTrimblePointsDialog(preSelCount, levelNames))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                // ── Collect hangers based on scope ──
                List<FamilyInstance> hangers;

                switch (dlg.Scope)
                {
                    case ExportTrimblePointsDialog.ScopeMode.Selection:
                        hangers = uidoc.Selection.GetElementIds()
                            .Select(id => doc.GetElement(id))
                            .OfType<FamilyInstance>()
                            .Where(fi => IsValidHanger(fi))
                            .ToList();
                        break;

                    case ExportTrimblePointsDialog.ScopeMode.ActiveView:
                        hangers = new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .OfCategory(BuiltInCategory.OST_PipeAccessory)
                            .WhereElementIsNotElementType()
                            .OfType<FamilyInstance>()
                            .Where(fi => IsValidHanger(fi))
                            .ToList();
                        break;

                    case ExportTrimblePointsDialog.ScopeMode.ByLevel:
                        Level selectedLevel = levels[dlg.SelectedLevelIndex];
                        hangers = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_PipeAccessory)
                            .WhereElementIsNotElementType()
                            .OfType<FamilyInstance>()
                            .Where(fi => IsValidHanger(fi))
                            .Where(fi => fi.LevelId == selectedLevel.Id)
                            .ToList();
                        break;

                    default:
                        hangers = new List<FamilyInstance>();
                        break;
                }

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Export Trimble Points",
                        "No pipe hangers found in the selected scope.");
                    return Result.Failed;
                }

                // ── Get coordinate transform ──
                Transform coordTransform = Transform.Identity;
                if (dlg.UseSharedCoordinates)
                {
                    ProjectLocation projLoc = doc.ActiveProjectLocation;
                    coordTransform = projLoc.GetTotalTransform();
                }

                // ── Build point records ──
                var points = new List<TrimblePoint>();
                int seq = 1;

                // Sort hangers by level elevation then by location for consistent numbering
                var sortedHangers = hangers
                    .OrderBy(h =>
                    {
                        Level lvl = doc.GetElement(h.LevelId) as Level;
                        return lvl?.Elevation ?? 0;
                    })
                    .ThenBy(h =>
                    {
                        LocationPoint lp = h.Location as LocationPoint;
                        return lp?.Point.Y ?? 0;
                    })
                    .ThenBy(h =>
                    {
                        LocationPoint lp = h.Location as LocationPoint;
                        return lp?.Point.X ?? 0;
                    })
                    .ToList();

                foreach (var hanger in sortedHangers)
                {
                    LocationPoint locPt = hanger.Location as LocationPoint;
                    if (locPt == null) continue;

                    XYZ internalPoint = locPt.Point;

                    // Transform to shared coordinates if requested
                    XYZ worldPoint = coordTransform.OfPoint(internalPoint);

                    // Convert units (Revit internal = feet)
                    double unitFactor = dlg.UseFeet ? 1.0 : FeetToMeters;
                    double x = worldPoint.X * unitFactor;
                    double y = worldPoint.Y * unitFactor;
                    double z = worldPoint.Z * unitFactor;

                    // Apply elevation datum offset (entered in feet, convert if meters)
                    double elevOffset = dlg.UseFeet ? dlg.ElevationOffset : dlg.ElevationOffset * FeetToMeters;
                    z += elevOffset;

                    // Build point name: Prefix-NNN (zero-padded)
                    int padWidth = Math.Max(3, sortedHangers.Count.ToString().Length);
                    string pointName = $"{dlg.PointPrefix}-{seq.ToString().PadLeft(padWidth, '0')}";

                    // Get additional info for code column
                    string code = dlg.PointCode;
                    string nomDia = GetStringParam(hanger, "Nominal Diameter");
                    if (!string.IsNullOrEmpty(nomDia))
                        code += " " + nomDia;

                    points.Add(new TrimblePoint
                    {
                        Name = pointName,
                        Northing = y,  // In survey convention: Y = Northing
                        Easting = x,   // X = Easting
                        Elevation = z,
                        Code = code
                    });

                    seq++;
                }

                if (points.Count == 0)
                {
                    TaskDialog.Show("Export Trimble Points",
                        "Could not extract locations from the selected hangers.");
                    return Result.Failed;
                }

                // ── Save file dialog ──
                string defaultFileName = $"Trimble_Hangers_{DateTime.Now:yyyyMMdd}";
                using (var saveDialog = new System.Windows.Forms.SaveFileDialog
                {
                    Title = "Save Trimble Point File",
                    Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = defaultFileName,
                    OverwritePrompt = true
                })
                {
                    if (saveDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ── Write CSV ──
                    try
                    {
                        using (var writer = new StreamWriter(saveDialog.FileName, false,
                            System.Text.Encoding.UTF8))
                        {
                            foreach (var pt in points)
                            {
                                string line;
                                if (dlg.NorthingFirst)
                                {
                                    line = $"{pt.Name},{pt.Northing:F4},{pt.Easting:F4},{pt.Elevation:F4},{pt.Code}";
                                }
                                else
                                {
                                    line = $"{pt.Name},{pt.Easting:F4},{pt.Northing:F4},{pt.Elevation:F4},{pt.Code}";
                                }
                                writer.WriteLine(line);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Export Trimble Points",
                            $"Error writing file:\n{ex.Message}");
                        return Result.Failed;
                    }

                    // ── Summary ──
                    string coordLabel = dlg.NorthingFirst ? "Northing,Easting" : "Easting,Northing";
                    string unitLabel = dlg.UseFeet ? "US Feet" : "Meters";
                    string basisLabel = dlg.UseSharedCoordinates ? "Shared (survey)" : "Project internal";

                    TaskDialog.Show("Export Trimble Points",
                        $"Exported {points.Count} hanger point{(points.Count != 1 ? "s" : "")} to:\n" +
                        $"{saveDialog.FileName}\n\n" +
                        $"Format: PointName,{coordLabel},Elevation,Code\n" +
                        $"Coordinates: {basisLabel}\n" +
                        $"Units: {unitLabel}" +
                        (dlg.ElevationOffset != 0 ? $"\nElevation offset: {dlg.ElevationOffset:F2} ft" : ""));
                }

                return Result.Succeeded;
            }
        }

        /// <summary>
        /// Checks if a family instance is a valid pipe hanger.
        /// </summary>
        private bool IsValidHanger(FamilyInstance fi)
        {
            if (fi.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeAccessory)
                return false;

            string familyName = fi.Symbol?.Family?.Name ?? "";
            foreach (var pattern in HangerFamilyPatterns)
            {
                if (familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets a parameter value as a display string.
        /// </summary>
        private string GetStringParam(Element elem, string paramName)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null) return null;
            return param.AsValueString();
        }

        private class TrimblePoint
        {
            public string Name { get; set; }
            public double Northing { get; set; }
            public double Easting { get; set; }
            public double Elevation { get; set; }
            public string Code { get; set; }
        }
    }
}
