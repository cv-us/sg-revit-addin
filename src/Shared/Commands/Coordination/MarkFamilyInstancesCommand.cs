using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Places an orange DirectShape sphere at the center of every instance of
    /// a chosen family — useful for spotting where a particular family lives
    /// in a busy model.
    ///
    /// Workflow:
    ///   1. Pre-scan every FamilyInstance in the project, group by Family.
    ///   2. Show a searchable dialog with the family list, scope choice
    ///      (active view vs. whole project), and Place / Delete All buttons.
    ///   3. Place: sphere at the bounding-box center of each instance in
    ///      scope. Does NOT delete prior markers — placements accumulate.
    ///   4. Delete All: removes every Family Instance Marker in the project.
    ///
    /// Markers are DirectShape spheres (12" diameter), bright orange, in the
    /// Generic Models category, tagged with a distinct ApplicationDataId so
    /// they never collide with the Hanger Gap Check (blue), resize-drift
    /// (orange — but a different ApplicationDataId), or Type Review (magenta)
    /// markers.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MarkFamilyInstancesCommand : IExternalCommand
    {
        private const string MarkerAppId = "SgRevitAddin";
        private const string MarkerAppDataId = "FamilyInstanceMarker";
        private const string MarkerMaterialName = "SG_FamilyInstanceMarker";

        /// <summary>Sphere radius in feet (6 inches → 12-inch diameter).</summary>
        private const double SphereRadius = 6.0 / 12.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Pre-scan project-wide family inventory.
                var families = ScanProjectFamilies(doc);

                // Existing marker count (for the dialog's display).
                int existingMarkers = GetAllMarkerIds(doc).Count;

                MarkFamilyInstancesDialog.MarkAction action;
                FamilyMarkerInfo selectedFamily;
                bool activeViewOnly;

                using (var dlg = new MarkFamilyInstancesDialog(families, existingMarkers))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    action = dlg.Action;
                    selectedFamily = dlg.SelectedFamily;
                    activeViewOnly = dlg.ActiveViewOnly;
                }

                if (action == MarkFamilyInstancesDialog.MarkAction.DeleteAll)
                {
                    int cleared = DeleteMarkers(doc, GetAllMarkerIds(doc));
                    TaskDialog.Show("Mark Family Instances",
                        $"Cleared {cleared} marker{(cleared != 1 ? "s" : "")}.");
                    return Result.Succeeded;
                }

                if (action != MarkFamilyInstancesDialog.MarkAction.Place || selectedFamily == null)
                    return Result.Cancelled;

                // Collect instances of the chosen family within scope.
                ElementId familyId = selectedFamily.FamilyId;
                FilteredElementCollector scopeCollector = activeViewOnly
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                    : new FilteredElementCollector(doc);

                var instances = scopeCollector
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol?.Family?.Id == familyId)
                    .ToList();

                int placed = 0;
                int skipped = 0;

                using (var tx = new Transaction(doc, "Mark Family Instances"))
                {
                    tx.Start();
                    ElementId materialId = GetOrCreateMarkerMaterial(doc);

                    foreach (var fi in instances)
                    {
                        XYZ center = GetInstanceCenter(fi);
                        if (center == null) { skipped++; continue; }

                        try
                        {
                            CreateSphere(doc, center, SphereRadius, materialId);
                            placed++;
                        }
                        catch { skipped++; }
                    }

                    tx.Commit();
                }

                string scopeLabel = activeViewOnly
                    ? $"active view ({doc.ActiveView.Name})"
                    : "whole project";

                string report =
                    $"Mark Family Instances\n\n" +
                    $"Family:           {selectedFamily.FamilyName}\n" +
                    $"Category:         {selectedFamily.CategoryName}\n" +
                    $"Scope:            {scopeLabel}\n" +
                    $"Instances found:  {instances.Count}\n" +
                    $"Markers placed:   {placed}\n";
                if (skipped > 0)
                    report += $"Skipped (no location or build error): {skipped}\n";
                report += $"\nTotal markers in project now: {GetAllMarkerIds(doc).Count}";

                TaskDialog.Show("Mark Family Instances", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Family inventory ──

        /// <summary>
        /// Lightweight DTO passed to the dialog. Made public+nested-namespace
        /// so the dialog file can reference it without changing layout.
        /// </summary>
        public class FamilyMarkerInfo
        {
            public ElementId FamilyId { get; set; }
            public string FamilyName { get; set; }
            public string CategoryName { get; set; }
            public int InstanceCount { get; set; }

            public override string ToString()
                => $"{FamilyName}    [{CategoryName}]    ×{InstanceCount}";
        }

        private List<FamilyMarkerInfo> ScanProjectFamilies(Document doc)
        {
            var byFamily = new Dictionary<ElementId, (FamilyInstance sample, int count)>();

            foreach (var fi in new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>())
            {
                var family = fi.Symbol?.Family;
                if (family == null) continue;
                if (byFamily.TryGetValue(family.Id, out var tuple))
                    byFamily[family.Id] = (tuple.sample, tuple.count + 1);
                else
                    byFamily[family.Id] = (fi, 1);
            }

            return byFamily
                .Select(kvp => new FamilyMarkerInfo
                {
                    FamilyId = kvp.Key,
                    FamilyName = kvp.Value.sample.Symbol.Family.Name,
                    CategoryName = kvp.Value.sample.Category?.Name ?? "(no category)",
                    InstanceCount = kvp.Value.count
                })
                .OrderBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ── Sphere geometry ──

        /// <summary>
        /// Builds a sphere via revolved-geometry: a half-circle arc in the
        /// world XZ plane, closed by a line along the Z axis, revolved 360°
        /// around the Z axis through <paramref name="center"/>.
        /// </summary>
        private void CreateSphere(Document doc, XYZ center, double radius, ElementId materialId)
        {
            // Half-circle in the world XZ plane: bottom pole → equator → top pole.
            var arc = Arc.Create(
                center, radius,
                -Math.PI / 2, Math.PI / 2,
                XYZ.BasisX, XYZ.BasisZ);
            var top = center + new XYZ(0, 0, radius);
            var bottom = center + new XYZ(0, 0, -radius);
            // Closing line back along the Z axis (top → bottom).
            var line = Line.CreateBound(top, bottom);
            var profile = CurveLoop.Create(new List<Curve> { arc, line });

            var frame = new Frame(center, XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ);
            var opts = new SolidOptions(materialId, ElementId.InvalidElementId);
            Solid sphere = GeometryCreationUtilities.CreateRevolvedGeometry(
                frame, new List<CurveLoop> { profile }, 0, 2 * Math.PI, opts);

            var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = MarkerAppId;
            ds.ApplicationDataId = MarkerAppDataId;
            ds.SetShape(new GeometryObject[] { sphere });
        }

        private ElementId GetOrCreateMarkerMaterial(Document doc)
        {
            var orange = new Color(255, 130, 0);

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, MarkerMaterialName,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                ApplyMaterialProps(existing, orange);
                return existing.Id;
            }

            ElementId newId = Material.Create(doc, MarkerMaterialName);
            if (doc.GetElement(newId) is Material newMat)
                ApplyMaterialProps(newMat, orange);
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

        private int DeleteMarkers(Document doc, List<ElementId> ids)
        {
            if (ids.Count == 0) return 0;
            using (var tx = new Transaction(doc, "Delete Family Instance Markers"))
            {
                tx.Start();
                doc.Delete(ids);
                tx.Commit();
            }
            return ids.Count;
        }

        private List<ElementId> GetAllMarkerIds(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(ds => ds.ApplicationId == MarkerAppId
                          && ds.ApplicationDataId == MarkerAppDataId)
                .Select(ds => ds.Id)
                .ToList();
        }

        /// <summary>Bounding-box center — the geometric middle of the instance.</summary>
        private XYZ GetInstanceCenter(Element element)
        {
            BoundingBoxXYZ bb = element.get_BoundingBox(null);
            if (bb != null)
            {
                return new XYZ(
                    (bb.Min.X + bb.Max.X) * 0.5,
                    (bb.Min.Y + bb.Max.Y) * 0.5,
                    (bb.Min.Z + bb.Max.Z) * 0.5);
            }
            // Fallback to LocationPoint for elements without a bounding box
            return (element.Location as LocationPoint)?.Point;
        }
    }
}
