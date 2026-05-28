using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Marks hangers of a chosen Type Code (Hydratec) for review by placing a
    /// tall vertical DirectShape cylinder at each one. The cylinder extends a
    /// configurable distance above AND below the hanger's elevation so it
    /// crosses plan-view cut planes and stands out in 3D — making flagged
    /// hangers easy to find regardless of the view.
    ///
    /// Same marker mechanism as HangerGapCheckCommand (DirectShape +
    /// ApplicationId/ApplicationDataId tagging for clean re-runs), but:
    ///   - filtered by Type Code instead of gap math
    ///   - a tall column (reach above + below) instead of a short puck
    ///   - a distinct magenta material and ApplicationDataId so the two
    ///     commands' markers never collide
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers.
    ///   2. Dialog: pick the Type Code to flag and the vertical reach.
    ///      (Or choose "Clear Markers Only" to wipe existing review markers.)
    ///   3. A magenta cylinder is placed on every matching hanger and the
    ///      flagged hangers are added to the selection.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MarkTypeForReviewCommand : IExternalCommand
    {
        private const string TypeCodeParam = "Type Code (Hydratec)";

        private const string MarkerAppId = "SgRevitAddin";
        private const string MarkerAppDataId = "TypeReviewMarker";
        private const string MarkerMaterialName = "SG_TypeReviewMarker";

        /// <summary>Marker cylinder radius, in feet (3 inches → 6-inch diameter).</summary>
        private const double MarkerRadius = 3.0 / 12.0;

        private static readonly string[] HangerFamilyPatterns =
        {
            "-Pipe Hanger",
            "-Pipe Trapeze",
            "-Basic Adjustable",
            "Adjustable Ring Hanger",
            "Ring Hanger"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var selectedIds = uidoc.Selection.GetElementIds();
                var hangers = (selectedIds ?? new List<ElementId>())
                    .Select(id => doc.GetElement(id))
                    .OfType<FamilyInstance>()
                    .Where(IsHanger)
                    .ToList();

                if (hangers.Count == 0)
                {
                    // No hanger selection — offer to clear existing markers.
                    int existing = CountExistingMarkers(doc);
                    if (existing > 0)
                    {
                        var td = new TaskDialog("Mark Type for Review")
                        {
                            MainInstruction = "No hangers selected.",
                            MainContent = $"There {(existing == 1 ? "is" : "are")} {existing} existing " +
                                          $"review marker{(existing != 1 ? "s" : "")} in the project. Clear them?",
                            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                            DefaultButton = TaskDialogResult.Cancel
                        };
                        if (td.Show() == TaskDialogResult.Yes)
                        {
                            int cleared = ClearAllMarkers(doc);
                            TaskDialog.Show("Mark Type for Review",
                                $"Cleared {cleared} marker{(cleared != 1 ? "s" : "")}.");
                            return Result.Succeeded;
                        }
                        return Result.Cancelled;
                    }

                    TaskDialog.Show("Mark Type for Review",
                        "No pipe hangers found in the current selection.\n\n" +
                        "Select hangers and run the command again.");
                    return Result.Cancelled;
                }

                // Pre-scan distinct type codes in the selection.
                var availableCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in hangers)
                {
                    string tc = GetStringParam(h, TypeCodeParam)?.Trim();
                    if (!string.IsNullOrEmpty(tc))
                        availableCodes.Add(tc);
                }

                if (availableCodes.Count == 0)
                {
                    TaskDialog.Show("Mark Type for Review",
                        $"None of the {hangers.Count} selected hangers have a " +
                        $"\"{TypeCodeParam}\" value.");
                    return Result.Cancelled;
                }

                string targetCode;
                double reachFt;
                using (var dlg = new MarkTypeForReviewDialog(hangers.Count, availableCodes.ToList()))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    if (dlg.ClearOnly)
                    {
                        int cleared = ClearAllMarkers(doc);
                        TaskDialog.Show("Mark Type for Review",
                            $"Cleared {cleared} review marker{(cleared != 1 ? "s" : "")}.");
                        return Result.Succeeded;
                    }

                    targetCode = dlg.TypeCode?.Trim() ?? "";
                    reachFt = dlg.ReachFeet;
                }

                if (string.IsNullOrEmpty(targetCode))
                {
                    TaskDialog.Show("Mark Type for Review", "No Type Code selected.");
                    return Result.Cancelled;
                }

                var flaggedIds = new List<ElementId>();
                int markersPlaced = 0;

                using (var tx = new Transaction(doc, "Mark Type for Review"))
                {
                    tx.Start();

                    ClearPreviousMarkers(doc);
                    ElementId materialId = GetOrCreateMarkerMaterial(doc);

                    foreach (var hanger in hangers)
                    {
                        string current = GetStringParam(hanger, TypeCodeParam)?.Trim() ?? "";
                        if (!string.Equals(current, targetCode, StringComparison.OrdinalIgnoreCase))
                            continue;

                        XYZ center = GetVisualLocation(hanger);
                        if (center == null) continue;

                        try
                        {
                            XYZ basePt = new XYZ(center.X, center.Y, center.Z - reachFt);
                            CreateMarker(doc, basePt, reachFt * 2.0, materialId);
                            markersPlaced++;
                            flaggedIds.Add(hanger.Id);
                        }
                        catch { /* non-critical; keep going */ }
                    }

                    tx.Commit();
                }

                if (flaggedIds.Count > 0)
                    uidoc.Selection.SetElementIds(flaggedIds);

                string report =
                    $"Mark Type for Review\n\n" +
                    $"Hangers in selection:  {hangers.Count}\n" +
                    $"Type Code \"{targetCode}\":  {markersPlaced} marked\n" +
                    $"Cylinder reach:        {reachFt:F1} ft above + below\n";
                if (markersPlaced > 0)
                    report += "\nMagenta cylinders placed and flagged hangers selected.";
                else
                    report += "\nNo hangers matched that Type Code.";

                TaskDialog.Show("Mark Type for Review", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Marker geometry ──

        private void CreateMarker(Document doc, XYZ basePoint, double height, ElementId materialId)
        {
            var arc1 = Arc.Create(basePoint, MarkerRadius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
            var arc2 = Arc.Create(basePoint, MarkerRadius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
            var profile = CurveLoop.Create(new List<Curve> { arc1, arc2 });

            var solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);
            Solid cylinder = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { profile }, XYZ.BasisZ, height, solidOptions);

            var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = MarkerAppId;
            ds.ApplicationDataId = MarkerAppDataId;
            ds.SetShape(new GeometryObject[] { cylinder });
        }

        private ElementId GetOrCreateMarkerMaterial(Document doc)
        {
            var magenta = new Color(230, 0, 200);

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, MarkerMaterialName,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                ApplyMaterialProps(existing, magenta);
                return existing.Id;
            }

            ElementId newId = Material.Create(doc, MarkerMaterialName);
            if (doc.GetElement(newId) is Material newMat)
                ApplyMaterialProps(newMat, magenta);
            return newId;
        }

        private void ApplyMaterialProps(Material mat, Color color)
        {
            mat.Color = color;
            mat.SurfaceForegroundPatternColor = color;
            mat.CutForegroundPatternColor = color;
            mat.Transparency = 0;
            mat.Shininess = 0;
        }

        private int ClearPreviousMarkers(Document doc)
        {
            var ids = GetMarkerInstanceIds(doc);
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        private int ClearAllMarkers(Document doc)
        {
            var ids = GetMarkerInstanceIds(doc);
            if (ids.Count == 0) return 0;
            using (var tx = new Transaction(doc, "Clear Type Review Markers"))
            {
                tx.Start();
                doc.Delete(ids);
                tx.Commit();
            }
            return ids.Count;
        }

        private int CountExistingMarkers(Document doc) => GetMarkerInstanceIds(doc).Count;

        private List<ElementId> GetMarkerInstanceIds(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(ds => ds.ApplicationId == MarkerAppId
                          && ds.ApplicationDataId == MarkerAppDataId)
                .Select(ds => ds.Id)
                .ToList();
        }

        // ── Helpers ──

        private bool IsHanger(FamilyInstance fi)
        {
            if (fi.Category == null) return false;
            if (fi.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeAccessory)
                return false;

            string familyName = fi.Symbol?.Family?.Name ?? "";
            foreach (var pattern in HangerFamilyPatterns)
            {
                if (familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Bounding-box center — reliably above the pipe at the hanger's true location.</summary>
        private XYZ GetVisualLocation(Element element)
        {
            BoundingBoxXYZ bb = element.get_BoundingBox(null);
            if (bb != null)
            {
                return new XYZ(
                    (bb.Min.X + bb.Max.X) * 0.5,
                    (bb.Min.Y + bb.Max.Y) * 0.5,
                    (bb.Min.Z + bb.Max.Z) * 0.5);
            }
            return (element.Location as LocationPoint)?.Point;
        }

        private string GetStringParam(Element element, string paramName)
        {
            var p = element.LookupParameter(paramName);
            if (p == null || !p.HasValue) return null;
            if (p.StorageType == StorageType.String) return p.AsString();
            return p.AsValueString();
        }
    }
}
