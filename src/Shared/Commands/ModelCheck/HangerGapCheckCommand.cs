using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.ModelCheck
{
    /// <summary>
    /// Identifies pipe hangers whose vertical gap between top-of-pipe and the
    /// structure they hang from exceeds a configurable threshold (default 6").
    ///
    /// Per Hydratec / NFPA convention, when this gap exceeds 6 inches the
    /// hanger may need additional bracing or restraint. The command flags
    /// such hangers by placing a marker family at the hanger location so
    /// they're easy to spot in plan and 3D views.
    ///
    /// GAP MATH (by Type Code (Hydratec) prefix):
    ///   - Type 02* (adjustable ring + 1.5" hardware — covers 02, 02C, 02D, …):
    ///       gap = rod_length - 1.5" - (pipe_OD / 2)
    ///   - Type 03* and everything else (e.g. 03, 03A, 03B, 04, …):
    ///       gap = rod_length - (pipe_OD / 2)
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers
    ///   2. Dialog: pick which Type Codes to check, which pipe sizes,
    ///      and the gap threshold (default 6")
    ///   3. For each matching hanger, find the closest pipe centerline,
    ///      read pipe Outside Diameter and hanger Rod Length
    ///   4. Apply the type-code-specific math
    ///   5. If gap > threshold, place a DirectShape marker (a small
    ///      vertical cylinder) at the hanger's location and add to selection
    ///   6. Report summary
    ///
    /// MARKER GEOMETRY:
    ///   The marker is a Revit DirectShape — a built-in 3D shape created
    ///   directly in the project, with no family file required. It's a
    ///   vertical cylinder (~4" diameter × 4" tall) categorized as Generic
    ///   Model so it shows in both plan and 3D views. Markers are tagged
    ///   with ApplicationId/ApplicationDataId so the command can find and
    ///   delete them on re-run or via "Clear Markers Only".
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangerGapCheckCommand : IExternalCommand
    {
        private const string TypeCodeParam = "Type Code (Hydratec)";
        private const string RodLengthParam = "Rod Length";

        /// <summary>ApplicationId stamped on every marker DirectShape.</summary>
        private const string MarkerAppId = "SgRevitAddin";

        /// <summary>ApplicationDataId stamped on every marker so we can find ours specifically.</summary>
        private const string MarkerAppDataId = "HangerGapMarker";

        /// <summary>Hardware offset for Type 02 adjustable hangers, in feet (1.5 inches).</summary>
        private const double Type02HardwareOffset = 1.5 / 12.0;

        /// <summary>Vertical offset above the hanger location for the marker base, in feet.</summary>
        private const double MarkerZOffset = 0.5; // 6 inches

        /// <summary>Marker cylinder radius, in feet (4 inches → 8-inch diameter).</summary>
        private const double MarkerRadius = 4.0 / 12.0;

        /// <summary>Marker cylinder height, in feet (8 inches).</summary>
        private const double MarkerHeight = 8.0 / 12.0;

        /// <summary>
        /// Pipes whose direction is more vertical than this are excluded from
        /// the hanger-pipe matching step. 0.5 ≈ 30° off horizontal — keeps
        /// horizontal mains and sloped armovers, excludes sprigs/risers.
        /// </summary>
        private const double MaxPipeSlopeFromHorizontal = 0.5;

        /// <summary>Project material name for the blue marker fill.</summary>
        private const string MarkerMaterialName = "SSG_HangerGapMarker";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Collect hangers from current selection ──
                var selectedIds = uidoc.Selection.GetElementIds();
                var hangers = selectedIds
                    .Select(id => doc.GetElement(id))
                    .OfType<FamilyInstance>()
                    .Where(IsHanger)
                    .ToList();

                if (hangers.Count == 0)
                {
                    // No selection — offer to clear existing markers if any are present
                    int existingMarkerCount = CountExistingMarkers(doc);
                    if (existingMarkerCount > 0)
                    {
                        var td = new TaskDialog("Hanger Gap Check")
                        {
                            MainInstruction = "No hangers selected.",
                            MainContent = $"There {(existingMarkerCount == 1 ? "is" : "are")} " +
                                $"{existingMarkerCount} existing hanger gap " +
                                $"marker{(existingMarkerCount != 1 ? "s" : "")} in the project. " +
                                "Would you like to clear them?",
                            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                            DefaultButton = TaskDialogResult.Cancel
                        };
                        if (td.Show() == TaskDialogResult.Yes)
                        {
                            int cleared = ClearAllMarkers(doc);
                            TaskDialog.Show("Hanger Gap Check",
                                $"Cleared {cleared} marker{(cleared != 1 ? "s" : "")}.");
                            return Result.Succeeded;
                        }
                        return Result.Cancelled;
                    }

                    TaskDialog.Show("Hanger Gap Check",
                        "No pipe hangers found in the current selection.\n\n" +
                        "Select hanger family instances (family name contains \"-Pipe Hanger\" " +
                        "or \"-Pipe Trapeze\") and run the command again.\n\n" +
                        "(No existing markers were found in the project either.)");
                    return Result.Cancelled;
                }

                // ── Get pipe centerlines from the entire project (any view) ──
                // Exclude vertical sprigs and risers — hangers don't go on those,
                // and including them causes Curve.Project to clamp to a sprig
                // endpoint when a hanger's LocationPoint is at the structure.
                var pipeCurves = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .Select(p => (pipe: (Element)p, curve: (p.Location as LocationCurve)?.Curve))
                    .Where(t => t.curve != null && IsNearHorizontal(t.curve))
                    .ToList();

                if (pipeCurves.Count == 0)
                {
                    TaskDialog.Show("Hanger Gap Check",
                        "No near-horizontal pipes found in the project. " +
                        "Hangers are only matched against pipes within 30° of horizontal.");
                    return Result.Cancelled;
                }

                // ── Pre-scan: collect available type codes and sizes from the selection ──
                // (lets the dialog show only relevant choices)
                var availableTypeCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var availableNominalSizes = new SortedSet<double>();

                foreach (var hanger in hangers)
                {
                    string tc = GetParamString(hanger, TypeCodeParam);
                    if (!string.IsNullOrWhiteSpace(tc))
                        availableTypeCodes.Add(tc.Trim());

                    var nearestPipe = FindClosestPipe(GetVisualLocation(hanger), pipeCurves);
                    if (nearestPipe != null)
                    {
                        double diaFt = GetParamDouble(
                            nearestPipe.Value.pipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        if (diaFt > 0)
                            availableNominalSizes.Add(diaFt);
                    }
                }

                if (availableTypeCodes.Count == 0)
                {
                    TaskDialog.Show("Hanger Gap Check",
                        $"None of the {hangers.Count} selected hangers have a " +
                        $"\"{TypeCodeParam}\" parameter populated.\n\n" +
                        "Run \"Section IDs\" or set the parameter manually before running this check.");
                    return Result.Cancelled;
                }

                // ── Show dialog ──
                using (var dlg = new HangerGapCheckDialog(
                    hangers.Count, availableTypeCodes.ToList(), availableNominalSizes.ToList()))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ClearOnly mode short-circuits: wipe existing markers and report
                    if (dlg.Mode == HangerGapCheckDialog.ActionMode.ClearOnly)
                    {
                        int cleared = ClearAllMarkers(doc);
                        TaskDialog.Show("Hanger Gap Check",
                            $"Cleared {cleared} hanger gap " +
                            $"marker{(cleared != 1 ? "s" : "")} from the project.\n\n" +
                            "No gap check was run.");
                        return Result.Succeeded;
                    }

                    var selectedTypeCodes = new HashSet<string>(
                        dlg.SelectedTypeCodes, StringComparer.OrdinalIgnoreCase);
                    var selectedSizes = new HashSet<double>(dlg.SelectedSizes);
                    double thresholdFt = dlg.ThresholdInches / 12.0;

                    // ── Process each hanger ──
                    var flaggedIds = new List<ElementId>();
                    int matchedCount = 0;
                    int skippedNoPipe = 0;
                    int skippedNoRod = 0;
                    int markersPlaced = 0;
                    var worstOffenders = new List<(FamilyInstance hanger, double gapInches, string typeCode)>();

                    ElementId markerMaterialId;

                    using (var tx = new Transaction(doc, "Hanger Gap Check"))
                    {
                        tx.Start();

                        // Clear any previous markers (keeps re-runs clean)
                        ClearPreviousMarkers(doc);

                        // Ensure the blue marker material exists (idempotent — reuses
                        // existing one if present, creates it once otherwise)
                        markerMaterialId = GetOrCreateMarkerMaterial(doc);

                        foreach (var hanger in hangers)
                        {
                            string typeCode = GetParamString(hanger, TypeCodeParam)?.Trim() ?? "";
                            if (!selectedTypeCodes.Contains(typeCode)) continue;

                            XYZ hangerPt = GetVisualLocation(hanger);
                            if (hangerPt == null) continue;

                            var nearest = FindClosestPipe(hangerPt, pipeCurves);
                            if (nearest == null) { skippedNoPipe++; continue; }

                            // Filter by selected pipe sizes (nominal diameter, in feet)
                            double pipeDiaFt = GetParamDouble(
                                nearest.Value.pipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                            if (!selectedSizes.Any(s => Math.Abs(s - pipeDiaFt) < 0.001)) continue;

                            matchedCount++;

                            // Read rod length (feet)
                            double rodLengthFt = GetParamDouble(hanger, RodLengthParam);
                            if (rodLengthFt <= 0) { skippedNoRod++; continue; }

                            // Read actual outside diameter (feet)
                            double pipeODFt = GetPipeOutsideDiameter(nearest.Value.pipe, pipeDiaFt);

                            // Compute gap
                            double gapFt = ComputeGap(typeCode, rodLengthFt, pipeODFt);

                            if (gapFt > thresholdFt)
                            {
                                flaggedIds.Add(hanger.Id);
                                worstOffenders.Add((hanger, gapFt * 12.0, typeCode));

                                // Place DirectShape marker at the pipe centerline, not at
                                // the hanger's LocationPoint. SSG/Hydratec hanger families
                                // have their LocationPoint at the top of the rod (at the
                                // structure), which is rod_length above the pipe — placing
                                // the marker there would put it floating up near structure
                                // instead of at the hanger.
                                try
                                {
                                    XYZ pipePt = nearest.Value.closestPoint;
                                    XYZ markerBase = new XYZ(
                                        pipePt.X, pipePt.Y, pipePt.Z + MarkerZOffset);
                                    CreateMarker(doc, markerBase, markerMaterialId);
                                    markersPlaced++;
                                }
                                catch { /* non-critical, hanger still flagged via selection */ }
                            }
                        }

                        tx.Commit();
                    }

                    // ── Highlight flagged hangers ──
                    if (flaggedIds.Count > 0)
                        uidoc.Selection.SetElementIds(flaggedIds);

                    // ── Report ──
                    string report =
                        $"Hanger Gap Check Results\n\n" +
                        $"Hangers in selection:        {hangers.Count}\n" +
                        $"Matched filter (type+size):  {matchedCount}\n" +
                        $"Threshold:                   {dlg.ThresholdInches:F1}\"\n\n" +
                        $"FLAGGED (gap > threshold):   {flaggedIds.Count}\n";

                    if (skippedNoPipe > 0)
                        report += $"\nSkipped (no nearby pipe): {skippedNoPipe}";
                    if (skippedNoRod > 0)
                        report += $"\nSkipped (no rod length):  {skippedNoRod}";

                    if (markersPlaced > 0)
                        report += $"\n\nMarkers placed: {markersPlaced} (blue cylinders above hangers)";

                    if (worstOffenders.Count > 0)
                    {
                        report += "\n\nLargest gaps:";
                        foreach (var w in worstOffenders.OrderByDescending(o => o.gapInches).Take(5))
                            report += $"\n  ID {w.hanger.Id}: {w.gapInches:F2}\" (Type {w.typeCode})";
                    }

                    if (flaggedIds.Count > 0)
                        report += "\n\nFlagged hangers are highlighted in the selection.";

                    TaskDialog.Show("Hanger Gap Check", report);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Math ──

        /// <summary>
        /// Computes the vertical gap from top-of-pipe to bottom-of-structure.
        /// Type 02 adjustable hangers have an additional 1.5" hardware offset.
        /// </summary>
        private double ComputeGap(string typeCode, double rodLengthFt, double pipeODFt)
        {
            double gapFt = rodLengthFt - (pipeODFt / 2.0);

            // Type 02 family (02, 02C, 02D, …) shares the adjustable-ring +
            // 1.5" hardware offset. Everything else (Type 03 family — 03,
            // 03A, 03B, …, plus 04 etc.) just uses the rod-minus-half-OD
            // baseline.
            if (!string.IsNullOrEmpty(typeCode) &&
                typeCode.StartsWith("02", StringComparison.OrdinalIgnoreCase))
            {
                gapFt -= Type02HardwareOffset;
            }

            return gapFt;
        }

        // ── Helpers ──

        private bool IsHanger(FamilyInstance fi)
        {
            // Must be in PipeAccessory category. This rules out tag families
            // like "-Pipe Hanger Tag" (Generic Annotation) that would otherwise
            // match the substring filter below.
            if (fi.Category == null) return false;
            if (fi.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeAccessory)
                return false;

            string familyName = fi.Symbol?.Family?.Name ?? "";
            return familyName.IndexOf("-Pipe Hanger", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Pipe Trapeze", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Basic Adjustable", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Ring Hanger", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns true if the curve is within ~30° of horizontal. Used to
        /// exclude vertical sprigs and risers from the candidate-pipe list,
        /// since hangers are only placed on horizontal-ish pipes.
        /// </summary>
        private bool IsNearHorizontal(Curve curve)
        {
            try
            {
                XYZ direction = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                // |direction.Z| of 0 = fully horizontal, 1 = fully vertical
                return Math.Abs(direction.Z) < MaxPipeSlopeFromHorizontal;
            }
            catch
            {
                // Zero-length or degenerate curve — treat as not horizontal
                return false;
            }
        }

        private XYZ GetLocation(Element element)
        {
            var loc = element.Location as LocationPoint;
            return loc?.Point;
        }

        /// <summary>
        /// Returns the XYZ that best represents where a hanger visually sits
        /// in the model. Uses the bounding-box center rather than LocationPoint:
        /// some hanger families (notably Hydratec ones) are connector-hosted
        /// with their family origin at one end of the host pipe segment and a
        /// "distance from pipe end" parameter shifting the visible geometry
        /// along the pipe. LocationPoint for those families points at a pipe
        /// endpoint, not the actual hanger position. The bounding box always
        /// wraps the visible geometry, so its XY center is reliably above the
        /// pipe at the hanger's true location.
        /// </summary>
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
            // Fallback for elements without geometry (rare for hangers)
            return GetLocation(element);
        }

        private string GetParamString(Element element, string paramName)
        {
            var p = element.LookupParameter(paramName);
            if (p == null) return null;
            if (p.StorageType == StorageType.String) return p.AsString();
            return p.AsValueString();
        }

        private double GetParamDouble(Element element, string paramName)
        {
            var p = element.LookupParameter(paramName);
            return (p != null && p.HasValue) ? p.AsDouble() : 0.0;
        }

        private double GetParamDouble(Element element, BuiltInParameter bip)
        {
            var p = element.get_Parameter(bip);
            return (p != null && p.HasValue) ? p.AsDouble() : 0.0;
        }

        /// <summary>
        /// Returns the pipe's actual outside diameter in feet. Falls back to
        /// nominal diameter (also in feet) if the actual OD parameter is absent.
        /// </summary>
        private double GetPipeOutsideDiameter(Element pipe, double nominalFallbackFt)
        {
            // Built-in parameter for outside diameter (actual, not nominal)
            var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
            if (p != null && p.HasValue && p.AsDouble() > 0)
                return p.AsDouble();

            // Some content uses a shared parameter named "Outside Diameter"
            var named = pipe.LookupParameter("Outside Diameter");
            if (named != null && named.HasValue && named.AsDouble() > 0)
                return named.AsDouble();

            // Fallback to nominal
            return nominalFallbackFt;
        }

        private (Element pipe, XYZ closestPoint, Curve curve)?
            FindClosestPipe(XYZ hangerPoint, List<(Element pipe, Curve curve)> pipeCurves)
        {
            if (hangerPoint == null) return null;

            Element bestPipe = null;
            XYZ bestPoint = null;
            Curve bestCurve = null;
            double bestXyDist = double.MaxValue;

            foreach (var (pipe, curve) in pipeCurves)
            {
                IntersectionResult projResult = curve.Project(hangerPoint);
                if (projResult == null) continue;

                XYZ closest = projResult.XYZPoint;
                // Rank by XY distance only. The hanger's vertical offset from
                // the pipe (rod length, plus any BB-Z averaging) is not a
                // signal for which pipe it's on — what matters is which pipe
                // sits below the hanger's XY footprint.
                double xyDist = Math.Sqrt(
                    Math.Pow(closest.X - hangerPoint.X, 2) +
                    Math.Pow(closest.Y - hangerPoint.Y, 2));

                if (xyDist < bestXyDist)
                {
                    bestXyDist = xyDist;
                    bestPipe = pipe;
                    bestPoint = closest;
                    bestCurve = curve;
                }
            }

            // Reject if no pipe within 6" XY — a real hanger sits directly
            // above its pipe. A larger XY gap means we matched something that
            // isn't actually the hanger's host.
            if (bestPipe == null || bestXyDist > 0.5) return null;

            return (bestPipe, bestPoint, bestCurve);
        }

        // ── Marker DirectShape creation and cleanup ──

        /// <summary>
        /// Creates a DirectShape cylinder marker at the given base point.
        /// The cylinder is vertical, centered horizontally on the point,
        /// and extends MarkerHeight upward. Geometry is built with the
        /// blue marker material so it shows blue in shaded plan and 3D.
        /// Tagged with our ApplicationId/ApplicationDataId so the cleanup
        /// query can find it later. Must be called inside a transaction.
        /// </summary>
        private void CreateMarker(Document doc, XYZ basePoint, ElementId materialId)
        {
            // A full circle in Revit's CurveLoop API is two semi-circular arcs.
            var arc1 = Arc.Create(basePoint, MarkerRadius, 0, Math.PI,
                XYZ.BasisX, XYZ.BasisY);
            var arc2 = Arc.Create(basePoint, MarkerRadius, Math.PI, 2 * Math.PI,
                XYZ.BasisX, XYZ.BasisY);
            var profile = CurveLoop.Create(new List<Curve> { arc1, arc2 });

            // SolidOptions binds the material to every face of the resulting solid
            var solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);
            Solid cylinder = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { profile }, XYZ.BasisZ, MarkerHeight, solidOptions);

            var ds = DirectShape.CreateElement(doc,
                new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = MarkerAppId;
            ds.ApplicationDataId = MarkerAppDataId;
            ds.SetShape(new GeometryObject[] { cylinder });
        }

        /// <summary>
        /// Returns the ElementId of a project-wide material named MarkerMaterialName.
        /// Creates it bright blue if missing; refreshes the color on an existing one
        /// so projects from older versions of this command (where the material was
        /// red) get re-colored to match the current scheme. Idempotent across runs.
        /// Must be called inside a transaction.
        /// </summary>
        private ElementId GetOrCreateMarkerMaterial(Document doc)
        {
            var blue = new Color(40, 130, 255); // bright sky-blue, very visible

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, MarkerMaterialName,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                ApplyMarkerMaterialProperties(existing, blue);
                return existing.Id;
            }

            ElementId newId = Material.Create(doc, MarkerMaterialName);
            if (doc.GetElement(newId) is Material newMat)
                ApplyMarkerMaterialProperties(newMat, blue);
            return newId;
        }

        /// <summary>
        /// Sets the color, pattern colors, transparency and shininess on the
        /// marker material. Used both when creating the material and when
        /// refreshing an existing one (e.g. recoloring red → blue across runs).
        /// </summary>
        private void ApplyMarkerMaterialProperties(Material mat, Color color)
        {
            mat.Color = color;
            mat.SurfaceForegroundPatternColor = color;
            mat.CutForegroundPatternColor = color;
            mat.Transparency = 0;
            mat.Shininess = 0;
        }

        /// <summary>
        /// Deletes all existing marker DirectShapes from the project.
        /// Must be called inside a transaction. Returns count deleted.
        /// </summary>
        private int ClearPreviousMarkers(Document doc)
        {
            var ids = GetMarkerInstanceIds(doc);
            if (ids.Count > 0)
                doc.Delete(ids);
            return ids.Count;
        }

        /// <summary>
        /// Standalone marker-clear with its own transaction (used by the
        /// ClearOnly path and the no-selection prompt). Returns count deleted.
        /// </summary>
        private int ClearAllMarkers(Document doc)
        {
            var ids = GetMarkerInstanceIds(doc);
            if (ids.Count == 0) return 0;

            using (var tx = new Transaction(doc, "Clear Hanger Gap Markers"))
            {
                tx.Start();
                doc.Delete(ids);
                tx.Commit();
            }
            return ids.Count;
        }

        private int CountExistingMarkers(Document doc) => GetMarkerInstanceIds(doc).Count;

        /// <summary>
        /// Returns the IDs of all DirectShape elements stamped with our
        /// MarkerAppId/MarkerAppDataId (so we never delete unrelated DirectShapes
        /// that other addins may have placed in the project).
        /// </summary>
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
    }
}
