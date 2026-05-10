using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Synchronizes pipe hangers to their closest pipes by:
    ///   - Moving each hanger to the closest point on its nearest pipe
    ///   - Rotating the hanger to match the pipe direction
    ///   - Setting "Nominal Diameter" from the pipe's diameter
    ///   - Setting "Additional Stocklist Information (Hydratec)" = "CON1," + pipeElementId
    ///
    /// Does NOT adjust rod lengths — use AutoSync Hangers to Structural for that.
    ///
    /// WORKFLOW:
    ///   1. User selects both pipes and hangers (pre-selection or pick)
    ///   2. Separate by family name: any of "-Pipe Hanger", "-Pipe Trapeze",
    ///      "-Basic Adjustable", "Ring Hanger" or "Adjustable Ring Hanger"
    ///      → hangers; the rest of OST_PipeCurves → pipes
    ///   3. Filter out vertical/steep pipes (>60° from horizontal)
    ///   4. For each hanger, find closest pipe by distance to centerline
    ///   5. Move hanger to closest point on matched pipe
    ///   6. Rotate hanger to match pipe direction in plan
    ///   7. Set Nominal Diameter and Stocklist Info parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncHangersToPipesCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0; // degrees from horizontal

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Get selected elements (pipes + hangers together) ──
            List<Element> selectedElements = GetSelectedElements(uidoc);
            if (selectedElements == null)
                return Result.Cancelled;

            // ── Separate into hangers and pipes by family name ──
            var hangers = new List<FamilyInstance>();
            var pipes = new List<Element>();

            foreach (var elem in selectedElements)
            {
                if (elem is FamilyInstance fi && IsHangerFamily(fi))
                {
                    hangers.Add(fi);
                    continue;
                }

                // Check if it's a pipe (category OST_PipeCurves)
                if (elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves)
                    pipes.Add(elem);
            }

            if (hangers.Count == 0)
            {
                TaskDialog.Show("Sync Hangers to Pipes",
                    "No pipe hangers found in the selection.\n" +
                    "Select hanger family instances (family name contains " +
                    "\"-Pipe Hanger\", \"-Pipe Trapeze\", \"-Basic Adjustable\", " +
                    "\"Ring Hanger\", or \"Adjustable Ring Hanger\") together with their pipes.");
                return Result.Failed;
            }

            if (pipes.Count == 0)
            {
                TaskDialog.Show("Sync Hangers to Pipes",
                    "No pipes found in the selection.\n" +
                    "Select both pipes and hangers together.");
                return Result.Failed;
            }

            // ── Filter out vertical/steep pipes ──
            var validPipes = FilterSteepPipes(pipes);

            if (validPipes.Count == 0)
            {
                TaskDialog.Show("Sync Hangers to Pipes",
                    "All selected pipes are vertical or too steep (>60° from horizontal).");
                return Result.Failed;
            }

            // ── Get pipe centerlines ──
            var pipeCurves = new List<(Element pipe, Curve curve)>();
            foreach (var pipe in validPipes)
            {
                LocationCurve locCurve = pipe.Location as LocationCurve;
                if (locCurve?.Curve != null)
                    pipeCurves.Add((pipe, locCurve.Curve));
            }

            if (pipeCurves.Count == 0)
            {
                TaskDialog.Show("Sync Hangers to Pipes",
                    "Could not extract centerlines from selected pipes.");
                return Result.Failed;
            }

            // ── Confirm ──
            TaskDialogResult confirm = TaskDialog.Show("Sync Hangers to Pipes",
                $"Sync {hangers.Count} hanger{(hangers.Count != 1 ? "s" : "")} to " +
                $"{pipeCurves.Count} pipe{(pipeCurves.Count != 1 ? "s" : "")}?\n\n" +
                "Each hanger will be moved to its closest pipe, rotated to match pipe direction, " +
                "and have Nominal Diameter and Stocklist Info updated.",
                TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);

            if (confirm != TaskDialogResult.Ok)
                return Result.Cancelled;

            // ── Process hangers ──
            int syncedCount = 0;
            int failedCount = 0;

            using (var tw = new TransactionWrapper(doc, "Sync Hangers to Pipes"))
            {
                try
                {
                    foreach (var hanger in hangers)
                    {
                        try
                        {
                            // Get hanger location
                            LocationPoint hangerLoc = hanger.Location as LocationPoint;
                            if (hangerLoc == null) { failedCount++; continue; }

                            XYZ hangerPoint = hangerLoc.Point;

                            // Find closest pipe
                            var match = FindClosestPipe(hangerPoint, pipeCurves);
                            if (match == null) { failedCount++; continue; }

                            Element matchedPipe = match.Value.pipe;
                            XYZ closestPoint = match.Value.closestPoint;
                            Curve pipeCurve = match.Value.curve;

                            // 1. Move hanger to closest point on pipe
                            hangerLoc.Point = closestPoint;

                            // 2. Rotate hanger to match pipe direction
                            double rotationDeg = ComputePipeRotation(pipeCurve);
                            SetHangerRotation(doc, hanger, closestPoint, rotationDeg);

                            // 3. Set Nominal Diameter from pipe
                            double pipeDiameter = GetDoubleParameter(matchedPipe, "Diameter");
                            double roundedDiameter = Math.Round(pipeDiameter, 3);
                            SetParameter(hanger, "Nominal Diameter", roundedDiameter);

                            // 4. Set Additional Stocklist Information
                            string stocklistInfo = "CON1," + matchedPipe.Id.ToString();
                            SetParameter(hanger, "Additional Stocklist Information (Hydratec)", stocklistInfo);

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
            string summary = $"Synced {syncedCount} hanger{(syncedCount != 1 ? "s" : "")} to pipes.";
            if (failedCount > 0)
                summary += $"\n{failedCount} hanger{(failedCount != 1 ? "s" : "")} could not be synced.";

            TaskDialog.Show("Sync Hangers to Pipes", summary);
            return Result.Succeeded;
        }

        /// <summary>
        /// Gets elements from pre-selection or prompts user to pick.
        /// Allows both pipe accessories and pipe curves.
        /// </summary>
        private List<Element> GetSelectedElements(UIDocument uidoc)
        {
            Document doc = uidoc.Document;

            // Check pre-selection
            var preSelected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .Where(e =>
                {
                    int catId = e.Category?.Id.IntegerValue ?? 0;
                    return catId == (int)BuiltInCategory.OST_PipeAccessory ||
                           catId == (int)BuiltInCategory.OST_PipeCurves;
                })
                .ToList();

            if (preSelected.Count >= 2) // need at least 1 hanger + 1 pipe
                return preSelected;

            // Prompt user to pick
            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeAndHangerSelectionFilter(),
                    "Select PIPES and HANGERS to synchronize, then press Finish.");

                return refs
                    .Select(r => doc.GetElement(r.ElementId))
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Filters out pipes that are vertical or steeper than 60° from horizontal.
        /// Two checks: (1) Slope parameter not empty, (2) angle from horizontal <= 60°.
        /// </summary>
        private List<Element> FilterSteepPipes(List<Element> pipes)
        {
            var result = new List<Element>();

            foreach (var pipe in pipes)
            {
                // Check 1: Slope parameter should exist and not be empty
                Parameter slopeParam = pipe.LookupParameter("Slope");
                if (slopeParam != null)
                {
                    string slopeStr = slopeParam.AsValueString();
                    if (string.IsNullOrEmpty(slopeStr))
                        continue; // vertical pipe, skip
                }

                // Check 2: Compute angle from horizontal
                LocationCurve locCurve = pipe.Location as LocationCurve;
                if (locCurve?.Curve == null) continue;

                Line pipeLine = locCurve.Curve as Line;
                if (pipeLine == null) continue;

                XYZ dir = pipeLine.Direction;
                XYZ flatDir = new XYZ(dir.X, dir.Y, 0);

                if (flatDir.GetLength() < 1e-6)
                    continue; // purely vertical

                double angleRad = dir.AngleTo(flatDir);
                double angleDeg = angleRad * (180.0 / Math.PI);

                if (angleDeg <= MaxSlopeAngle)
                    result.Add(pipe);
            }

            return result;
        }

        /// <summary>
        /// Finds the closest pipe centerline to a hanger point.
        /// Returns the matched pipe, closest point, and curve.
        /// </summary>
        private (Element pipe, XYZ closestPoint, Curve curve)?
            FindClosestPipe(XYZ hangerPoint, List<(Element pipe, Curve curve)> pipeCurves)
        {
            Element bestPipe = null;
            XYZ bestPoint = null;
            Curve bestCurve = null;
            double bestDist = double.MaxValue;

            foreach (var (pipe, curve) in pipeCurves)
            {
                IntersectionResult projResult = curve.Project(hangerPoint);
                if (projResult == null) continue;

                double dist = projResult.Distance;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPipe = pipe;
                    bestPoint = projResult.XYZPoint;
                    bestCurve = curve;
                }
            }

            if (bestPipe == null)
                return null;

            return (bestPipe, bestPoint, bestCurve);
        }

        /// <summary>
        /// Computes the rotation angle (degrees, 0-360) of a pipe's direction
        /// projected onto the XY plane, measured from the X-axis about the Z-axis.
        /// </summary>
        private double ComputePipeRotation(Curve pipeCurve)
        {
            Line pipeLine = pipeCurve as Line;
            if (pipeLine == null) return 0;

            XYZ dir = pipeLine.Direction;
            double angle = Math.Atan2(dir.Y, dir.X);
            double degrees = angle * (180.0 / Math.PI);

            // Normalize to 0-360
            if (degrees < 0) degrees += 360.0;

            return Math.Round(degrees, 3);
        }

        /// <summary>
        /// Sets a hanger's rotation about the Z-axis at its location.
        /// Uses ElementTransformUtils to rotate from 0 to the target angle.
        /// </summary>
        private void SetHangerRotation(Document doc, FamilyInstance hanger, XYZ point, double degrees)
        {
            // First reset any existing rotation by getting current and compensating
            // Compute the delta from current rotation to set absolute rotation
            LocationPoint locPt = hanger.Location as LocationPoint;
            if (locPt == null) return;

            // Get current rotation
            double currentRotation = locPt.Rotation; // radians

            // Target rotation in radians
            double targetRadians = degrees * (Math.PI / 180.0);

            // Compute delta
            double deltaRadians = targetRadians - currentRotation;

            if (Math.Abs(deltaRadians) > 1e-6)
            {
                Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, hanger.Id, axis, deltaRadians);
            }
        }

        /// <summary>
        /// Gets a double parameter value by name, returning 0 if not found.
        /// </summary>
        private double GetDoubleParameter(Element elem, string paramName)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null) return 0;
            if (param.StorageType == StorageType.Double)
                return param.AsDouble();
            return 0;
        }

        /// <summary>
        /// Sets a parameter by name on an element.
        /// </summary>
        private void SetParameter(Element elem, string paramName, object value)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (value is double dVal)
                param.Set(dVal);
            else if (value is string sVal)
                param.Set(sVal);
        }

        /// <summary>
        /// Selection filter that allows pipe accessories and pipe curves.
        /// </summary>
        private class PipeAndHangerSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                int catId = elem.Category?.Id.IntegerValue ?? 0;
                return catId == (int)BuiltInCategory.OST_PipeAccessory ||
                       catId == (int)BuiltInCategory.OST_PipeCurves;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        /// <summary>
        /// Recognises a FamilyInstance as a pipe hanger by its family name.
        /// Covers both the SG family naming conventions ("-Pipe Hanger",
        /// "-Pipe Trapeze", "-Basic Adjustable") and the HydraCAD ones
        /// ("Adjustable Ring Hanger", "Ring Hanger", etc.). Same filter
        /// the newer hanger commands (Match Sizes, Replace Sizes, Hanger
        /// Gap Check) use.
        /// </summary>
        private static bool IsHangerFamily(FamilyInstance fi)
        {
            string familyName = fi.Symbol?.Family?.Name ?? "";
            return familyName.IndexOf("-Pipe Hanger", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Pipe Trapeze", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Basic Adjustable", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Ring Hanger", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Adjustable Ring", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

