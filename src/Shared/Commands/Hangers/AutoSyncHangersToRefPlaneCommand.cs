using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Synchronizes pipe hanger rod lengths to a user-selected reference plane
    /// representing the underside of a structural slab/deck. For each hanger,
    /// projects its position vertically onto the reference plane and sets the
    /// "Rod Length" parameter to that vertical distance. Hangers above the plane
    /// are excluded.
    ///
    /// Migrated from: "AutoSync - Hangers To Reference Plane.dyn"
    ///
    /// WORKFLOW:
    ///   1. User selects pipe hangers (pre-selection or pick)
    ///   2. Dialog: pick reference plane from project
    ///   3. Filter to valid hanger families
    ///   4. For each hanger: get position, project onto plane, compute rod length
    ///   5. Write "Rod Length" and "Comments" on hangers that changed
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoSyncHangersToRefPlaneCommand : IExternalCommand
    {
        /// <summary>
        /// Family name patterns that identify valid pipe hangers.
        /// A hanger is valid if its family name contains any of these strings.
        /// </summary>
        private static readonly string[] HangerFamilyPatterns = new[]
        {
            "-Pipe Hanger",
            "-Basic Adjustable",
            "Adjustable Ring Hanger"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Get selected pipe accessories ──
            List<FamilyInstance> selectedAccessories = GetSelectedPipeAccessories(uidoc);
            if (selectedAccessories == null)
                return Result.Cancelled;

            // ── Filter to valid pipe hangers ──
            var hangers = selectedAccessories
                .Where(fi => IsValidHanger(fi))
                .ToList();

            if (hangers.Count == 0)
            {
                TaskDialog.Show("Sync Hangers to Reference Plane",
                    "No valid pipe hangers found in the selection.\n\n" +
                    "Select elements whose family name contains \"-Pipe Hanger\", " +
                    "\"-Basic Adjustable\", or \"Adjustable Ring Hanger\".");
                return Result.Failed;
            }

            // ── Collect all named reference planes ──
            var refPlanes = new FilteredElementCollector(doc)
                .OfClass(typeof(ReferencePlane))
                .Cast<ReferencePlane>()
                .Where(rp => !string.IsNullOrWhiteSpace(rp.Name))
                .OrderBy(rp => rp.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (refPlanes.Count == 0)
            {
                TaskDialog.Show("Sync Hangers to Reference Plane",
                    "No named reference planes found in the project.");
                return Result.Failed;
            }

            var refPlaneNames = refPlanes.Select(rp => rp.Name).ToList();

            // ── Show dialog ──
            using (var dlg = new AutoSyncHangersToRefPlaneDialog(hangers.Count, refPlaneNames))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                ReferencePlane selectedPlane = refPlanes[dlg.SelectedRefPlaneIndex];
                string planeName = selectedPlane.Name;

                // ── Get the reference plane's geometric plane ──
                Plane plane = selectedPlane.GetPlane();

                // ── Process hangers ──
                int syncedCount = 0;
                int unchangedCount = 0;
                int abovePlaneCount = 0;
                int failedCount = 0;

                using (var tw = new TransactionWrapper(doc, "Sync Hangers to Reference Plane"))
                {
                    try
                    {
                        foreach (var hanger in hangers)
                        {
                            try
                            {
                                // Get hanger position
                                XYZ hangerPoint = GetHangerPosition(doc, hanger);
                                if (hangerPoint == null) { failedCount++; continue; }

                                // Project hanger point vertically onto the reference plane
                                XYZ projectedPoint = ProjectPointOntoPlane(hangerPoint, plane);
                                if (projectedPoint == null) { failedCount++; continue; }

                                // Check if hanger is below the plane (valid for rod length)
                                if (hangerPoint.Z >= projectedPoint.Z)
                                {
                                    abovePlaneCount++;
                                    continue;
                                }

                                // Calculate rod length (vertical distance)
                                double rodLength = projectedPoint.Z - hangerPoint.Z;

                                // Read existing rod length to check if changed
                                Parameter rodLengthParam = hanger.LookupParameter("Rod Length");
                                if (rodLengthParam != null && !rodLengthParam.IsReadOnly)
                                {
                                    double existingRodLength = rodLengthParam.AsDouble();

                                    // Compare with tolerance (1/32 inch = ~0.003 ft)
                                    if (Math.Abs(existingRodLength - rodLength) < 0.003)
                                    {
                                        unchangedCount++;
                                        continue;
                                    }

                                    // Write new rod length
                                    rodLengthParam.Set(rodLength);
                                }

                                // Write Comments = "Reference Plane: {name}"
                                Parameter commentsParam = hanger.LookupParameter("Comments");
                                if (commentsParam != null && !commentsParam.IsReadOnly)
                                    commentsParam.Set("Reference Plane: " + planeName);

                                syncedCount++;
                            }
                            catch
                            {
                                failedCount++;
                            }
                        }

                        tw.Commit();
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                        return Result.Failed;
                    }
                }

                // ── Summary ──
                var summaryLines = new List<string>();
                summaryLines.Add($"A Total Of {hangers.Count} Hangers Selected.");
                summaryLines.Add($"{syncedCount} Hangers Have Been Re-Sync'd!");

                if (unchangedCount > 0)
                    summaryLines.Add($"{unchangedCount} Hangers Didn't Require Synchronizing.");
                if (abovePlaneCount > 0)
                    summaryLines.Add($"{abovePlaneCount} Hangers Above Plane Ignored.");
                if (failedCount > 0)
                    summaryLines.Add($"{failedCount} Hangers Failed.");

                if (syncedCount == 0 && failedCount == 0)
                {
                    TaskDialog.Show("Hanger Sync To Structural Summary:",
                        "Success: No Hangers Required Synchronizing!\n\n" +
                        string.Join("\n", summaryLines));
                }
                else
                {
                    TaskDialog.Show("Hanger Sync To Structural Summary:",
                        string.Join("\n", summaryLines));
                }

                return Result.Succeeded;
            }
        }

        /// <summary>
        /// Gets the hanger's actual position on its host pipe.
        /// Uses LocationPoint directly. Falls back to computing position from
        /// host pipe location + "Distance off End" parameter if needed.
        /// </summary>
        private XYZ GetHangerPosition(Document doc, FamilyInstance hanger)
        {
            // Try direct LocationPoint first
            LocationPoint locPt = hanger.Location as LocationPoint;
            if (locPt != null)
                return locPt.Point;

            // Fallback: compute from host pipe + Distance off End
            Element host = null;
            try
            {
                // Try to get host element
                ElementId hostId = hanger.Host?.Id ?? ElementId.InvalidElementId;
                if (hostId != ElementId.InvalidElementId)
                    host = doc.GetElement(hostId);
            }
            catch { }

            // Try SuperComponent as another host method
            if (host == null)
            {
                try
                {
                    host = doc.GetElement(hanger.SuperComponent?.Id ?? ElementId.InvalidElementId);
                }
                catch { }
            }

            if (host == null)
                return null;

            LocationCurve hostLocCurve = host.Location as LocationCurve;
            if (hostLocCurve?.Curve == null)
                return null;

            Line pipeLine = hostLocCurve.Curve as Line;
            if (pipeLine == null)
                return null;

            // Get "Distance off End" parameter
            Parameter distParam = hanger.LookupParameter("Distance off End");
            double distOffEnd = distParam?.AsDouble() ?? 0;

            // Translate pipe start point along pipe direction by distance off end
            XYZ pipeStart = pipeLine.GetEndPoint(0);
            XYZ pipeDir = pipeLine.Direction;

            return pipeStart + pipeDir * distOffEnd;
        }

        /// <summary>
        /// Projects a point vertically (Z-axis) onto a plane.
        /// For a horizontal plane, this is simply changing the Z to the plane's Z.
        /// For a sloped plane, uses proper projection math.
        /// </summary>
        private XYZ ProjectPointOntoPlane(XYZ point, Plane plane)
        {
            XYZ planeOrigin = plane.Origin;
            XYZ planeNormal = plane.Normal;

            // Check if the Z-axis ray intersects the plane
            // Parametric: point + t * ZAxis lies on the plane
            // (point + t * Z - planeOrigin) · planeNormal = 0
            // t = ((planeOrigin - point) · planeNormal) / (ZAxis · planeNormal)

            double denominator = XYZ.BasisZ.DotProduct(planeNormal);
            if (Math.Abs(denominator) < 1e-10)
                return null; // plane is vertical or parallel to Z-axis, can't project vertically

            double t = (planeOrigin - point).DotProduct(planeNormal) / denominator;
            return point + t * XYZ.BasisZ;
        }

        /// <summary>
        /// Checks if a family instance is a valid pipe hanger based on family name patterns.
        /// </summary>
        private bool IsValidHanger(FamilyInstance fi)
        {
            string familyName = fi.Symbol?.Family?.Name ?? "";
            foreach (var pattern in HangerFamilyPatterns)
            {
                if (familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets selected pipe accessories from pre-selection or prompts user to pick.
        /// </summary>
        private List<FamilyInstance> GetSelectedPipeAccessories(UIDocument uidoc)
        {
            Document doc = uidoc.Document;

            // Check pre-selection
            var preSelected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilyInstance>()
                .Where(fi => fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                .ToList();

            if (preSelected.Count > 0)
                return preSelected;

            // Prompt user to pick
            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeAccessoryFilter(),
                    "Select PIPE HANGERS to sync, then press Finish.");

                return refs
                    .Select(r => doc.GetElement(r.ElementId))
                    .OfType<FamilyInstance>()
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private class PipeAccessoryFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
