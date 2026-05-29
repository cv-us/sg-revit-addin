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
    /// crosses plan-view cut planes and stands out in 3D.
    ///
    /// The dialog drives three actions:
    ///   • Place Markers       — needs a hanger selection; flags every hanger
    ///                            of the chosen Type Code. No-op if nothing is
    ///                            selected.
    ///   • Delete All Markers  — removes every review marker in the project.
    ///   • Delete by Type Code — removes only the markers placed for one Type
    ///                            Code (chosen from the codes that currently
    ///                            have markers).
    ///
    /// Each marker's Type Code is encoded into its ApplicationDataId
    /// ("TypeReviewMarker|02D") so delete-by-type can target the right ones.
    /// A distinct magenta material keeps these visually separate from the
    /// blue Hanger Gap Check markers and orange resize-drift markers.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MarkTypeForReviewCommand : IExternalCommand
    {
        private const string TypeCodeParam = "Type Code (Hydratec)";

        private const string MarkerAppId = "SgRevitAddin";
        private const string MarkerAppDataPrefix = "TypeReviewMarker";
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

                // Type codes available to place markers for (from selection).
                var availableCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in hangers)
                {
                    string tc = GetStringParam(h, TypeCodeParam)?.Trim();
                    if (!string.IsNullOrEmpty(tc))
                        availableCodes.Add(tc);
                }

                // Existing markers in the project, grouped by encoded type code.
                Dictionary<string, int> markerCounts = GetMarkerCountsByType(doc);

                string placeCode, deleteCode;
                double reachFt;
                MarkTypeForReviewDialog.MarkAction action;

                using (var dlg = new MarkTypeForReviewDialog(
                    hangers.Count, availableCodes.ToList(), markerCounts))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    action = dlg.Action;
                    placeCode = dlg.TypeCode?.Trim() ?? "";
                    deleteCode = dlg.DeleteTypeCode ?? "";
                    reachFt = dlg.ReachFeet;
                }

                switch (action)
                {
                    case MarkTypeForReviewDialog.MarkAction.DeleteAll:
                    {
                        int cleared = DeleteMarkers(doc, GetAllMarkerIds(doc));
                        TaskDialog.Show("Mark Type for Review",
                            $"Cleared {cleared} review marker{(cleared != 1 ? "s" : "")}.");
                        return Result.Succeeded;
                    }

                    case MarkTypeForReviewDialog.MarkAction.DeleteByType:
                    {
                        int cleared = DeleteMarkers(doc, GetMarkerIdsForType(doc, deleteCode));
                        string label = deleteCode.Length == 0 ? "(untagged)" : deleteCode;
                        TaskDialog.Show("Mark Type for Review",
                            $"Cleared {cleared} marker{(cleared != 1 ? "s" : "")} for Type Code \"{label}\".");
                        return Result.Succeeded;
                    }

                    case MarkTypeForReviewDialog.MarkAction.Place:
                    {
                        // Needs a selection — does nothing without one.
                        if (hangers.Count == 0 || string.IsNullOrEmpty(placeCode))
                            return Result.Cancelled;

                        var flaggedIds = new List<ElementId>();
                        int markersPlaced = 0;

                        using (var tx = new Transaction(doc, "Mark Type for Review"))
                        {
                            tx.Start();

                            // Replace any prior markers for this same code so a
                            // re-run with a new reach doesn't stack columns.
                            var prior = GetMarkerIdsForType(doc, placeCode);
                            if (prior.Count > 0) doc.Delete(prior);

                            ElementId materialId = GetOrCreateMarkerMaterial(doc);
                            string appDataId = MarkerAppDataPrefix + "|" + placeCode;

                            foreach (var hanger in hangers)
                            {
                                string current = GetStringParam(hanger, TypeCodeParam)?.Trim() ?? "";
                                if (!string.Equals(current, placeCode, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                XYZ center = GetVisualLocation(hanger);
                                if (center == null) continue;

                                try
                                {
                                    XYZ basePt = new XYZ(center.X, center.Y, center.Z - reachFt);
                                    CreateMarker(doc, basePt, reachFt * 2.0, materialId, appDataId);
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
                            $"Type Code \"{placeCode}\":  {markersPlaced} marked\n" +
                            $"Cylinder reach:        {reachFt:F1} ft above + below\n";
                        report += markersPlaced > 0
                            ? "\nMagenta cylinders placed and flagged hangers selected."
                            : "\nNo hangers matched that Type Code.";
                        TaskDialog.Show("Mark Type for Review", report);
                        return Result.Succeeded;
                    }

                    default:
                        return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Marker geometry ──

        private void CreateMarker(Document doc, XYZ basePoint, double height,
            ElementId materialId, string appDataId)
        {
            var arc1 = Arc.Create(basePoint, MarkerRadius, 0, Math.PI, XYZ.BasisX, XYZ.BasisY);
            var arc2 = Arc.Create(basePoint, MarkerRadius, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY);
            var profile = CurveLoop.Create(new List<Curve> { arc1, arc2 });

            var solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);
            Solid cylinder = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { profile }, XYZ.BasisZ, height, solidOptions);

            var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = MarkerAppId;
            ds.ApplicationDataId = appDataId;
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

        /// <summary>Deletes the given marker ids in their own transaction. Returns count.</summary>
        private int DeleteMarkers(Document doc, List<ElementId> ids)
        {
            if (ids.Count == 0) return 0;
            using (var tx = new Transaction(doc, "Delete Type Review Markers"))
            {
                tx.Start();
                doc.Delete(ids);
                tx.Commit();
            }
            return ids.Count;
        }

        /// <summary>All review-marker DirectShapes (ApplicationDataId starts with the prefix).</summary>
        private List<ElementId> GetAllMarkerIds(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(ds => ds.ApplicationId == MarkerAppId
                          && ds.ApplicationDataId != null
                          && ds.ApplicationDataId.StartsWith(MarkerAppDataPrefix, StringComparison.Ordinal))
                .Select(ds => ds.Id)
                .ToList();
        }

        /// <summary>Marker ids whose encoded Type Code equals <paramref name="code"/>.</summary>
        private List<ElementId> GetMarkerIdsForType(Document doc, string code)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(ds => ds.ApplicationId == MarkerAppId
                          && string.Equals(ParseMarkerCode(ds.ApplicationDataId), code,
                                StringComparison.OrdinalIgnoreCase))
                .Select(ds => ds.Id)
                .ToList();
        }

        /// <summary>Counts existing markers grouped by their encoded Type Code.</summary>
        private Dictionary<string, int> GetMarkerCountsByType(Document doc)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var markers = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(ds => ds.ApplicationId == MarkerAppId
                          && ds.ApplicationDataId != null
                          && ds.ApplicationDataId.StartsWith(MarkerAppDataPrefix, StringComparison.Ordinal));

            foreach (var ds in markers)
            {
                string code = ParseMarkerCode(ds.ApplicationDataId);
                counts[code] = counts.TryGetValue(code, out int c) ? c + 1 : 1;
            }
            return counts;
        }

        /// <summary>Extracts the Type Code from "TypeReviewMarker|02D" → "02D" (or "" if none).</summary>
        private string ParseMarkerCode(string appDataId)
        {
            if (string.IsNullOrEmpty(appDataId)) return "";
            int bar = appDataId.IndexOf('|');
            return bar < 0 ? "" : appDataId.Substring(bar + 1);
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
