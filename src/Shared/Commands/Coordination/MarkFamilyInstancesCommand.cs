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
    /// in a busy model. Optionally filtered by workset.
    ///
    /// Workflow:
    ///   1. Pre-scan every FamilyInstance in the project, group by Family and
    ///      by Workset.
    ///   2. Show a searchable dialog with the family list, a per-workset
    ///      checklist, a scope choice (active view vs. whole project), and
    ///      Place / Delete All buttons.
    ///   3. Place: sphere at the bounding-box center of each instance in
    ///      scope that's also on a selected workset. Does NOT delete prior
    ///      markers — placements accumulate.
    ///   4. Delete All: removes every Family Instance Marker in the project.
    ///
    /// Markers are DirectShape spheres (12" diameter), bright orange, in the
    /// Generic Models category, tagged with a distinct ApplicationDataId so
    /// they never collide with the Hanger Gap Check (blue), resize-drift,
    /// or Type Review (magenta) markers.
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
                var worksets = ScanUserWorksets(doc);
                var families = ScanProjectFamilies(doc);

                int existingMarkers = GetAllMarkerIds(doc).Count;

                MarkFamilyInstancesDialog.MarkAction action;
                FamilyMarkerInfo selectedFamily;
                bool activeViewOnly;
                HashSet<int> selectedWorksetIds;

                using (var dlg = new MarkFamilyInstancesDialog(families, worksets, existingMarkers))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    action = dlg.Action;
                    selectedFamily = dlg.SelectedFamily;
                    activeViewOnly = dlg.ActiveViewOnly;
                    selectedWorksetIds = dlg.SelectedWorksetIds;
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

                // Apply workset filter (only when there are user worksets — for
                // non-workshared docs, selectedWorksetIds is null and every
                // instance is treated as included).
                if (selectedWorksetIds != null)
                {
                    instances = instances
                        .Where(fi => selectedWorksetIds.Contains(fi.WorksetId.IntegerValue))
                        .ToList();
                }

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

                string worksetLabel = selectedWorksetIds == null
                    ? "(not workshared — all instances)"
                    : $"{selectedWorksetIds.Count} of {worksets.Count} worksets";

                string report =
                    $"Mark Family Instances\n\n" +
                    $"Family:           {selectedFamily.FamilyName}\n" +
                    $"Category:         {selectedFamily.CategoryName}\n" +
                    $"Scope:            {scopeLabel}\n" +
                    $"Workset filter:   {worksetLabel}\n" +
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

        // ── Inventory ──

        /// <summary>
        /// Family record passed to the dialog. Stores instance counts per
        /// workset so the family list can dynamically reflect the workset
        /// filter without re-scanning the project.
        /// </summary>
        public class FamilyMarkerInfo
        {
            public ElementId FamilyId { get; set; }
            public string FamilyName { get; set; }
            public string CategoryName { get; set; }
            /// <summary>Workset id (int) → count of instances on that workset.</summary>
            public Dictionary<int, int> CountsByWorkset { get; set; } = new Dictionary<int, int>();

            public int TotalCount => CountsByWorkset.Values.Sum();

            public int CountInSelectedWorksets(HashSet<int> selectedIds)
            {
                if (selectedIds == null) return TotalCount;
                int sum = 0;
                foreach (var kvp in CountsByWorkset)
                    if (selectedIds.Contains(kvp.Key)) sum += kvp.Value;
                return sum;
            }
        }

        public class WorksetInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public override string ToString() => Name;
        }

        private List<FamilyMarkerInfo> ScanProjectFamilies(Document doc)
        {
            // First pass: tally counts keyed by (familyId, worksetId).
            var byFamily = new Dictionary<ElementId, FamilyMarkerInfo>();

            foreach (var fi in new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>())
            {
                var family = fi.Symbol?.Family;
                if (family == null) continue;

                if (!byFamily.TryGetValue(family.Id, out var info))
                {
                    info = new FamilyMarkerInfo
                    {
                        FamilyId = family.Id,
                        FamilyName = family.Name,
                        CategoryName = fi.Category?.Name ?? "(no category)"
                    };
                    byFamily[family.Id] = info;
                }

                int wsId = fi.WorksetId.IntegerValue;
                info.CountsByWorkset[wsId] = info.CountsByWorkset.TryGetValue(wsId, out int c)
                    ? c + 1
                    : 1;
            }

            return byFamily.Values
                .OrderBy(f => f.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns the user worksets in this project, ordered by name. Empty
        /// list if the project is not workshared.
        /// </summary>
        private List<WorksetInfo> ScanUserWorksets(Document doc)
        {
            if (!doc.IsWorkshared) return new List<WorksetInfo>();

            return new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .Select(ws => new WorksetInfo
                {
                    Id = ws.Id.IntegerValue,
                    Name = ws.Name
                })
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
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
            var arc = Arc.Create(
                center, radius,
                -Math.PI / 2, Math.PI / 2,
                XYZ.BasisX, XYZ.BasisZ);
            var top = center + new XYZ(0, 0, radius);
            var bottom = center + new XYZ(0, 0, -radius);
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
            return (element.Location as LocationPoint)?.Point;
        }
    }
}
