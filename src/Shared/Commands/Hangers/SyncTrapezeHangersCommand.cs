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
    /// Synchronizes trapeze hanger parameters to their closest pipes and the
    /// structural elements above. For each trapeze hanger, calculates:
    ///   - Rotation angle from the closest pipe's direction
    ///   - Rod 1 and Rod 2 positions (perpendicular to pipe, offset by diameter)
    ///   - Rod top elevations by raybouncing from rod positions to structure above
    ///   - Rod offsets, pipe diameter, nominal diameter, and stocklist info
    ///
    /// WORKFLOW:
    ///   1. User selects trapeze hangers (pre-selection or pick)
    ///   2. Dialog: min clearance distance, rod position (closest/middle)
    ///   3. Match each hanger to its closest pipe
    ///   4. Compute rotation from pipe direction
    ///   5. Compute rod positions perpendicular to pipe direction
    ///   6. Raybounce from rod positions to find structural elements above
    ///   7. Write all parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncTrapezeHangersCommand : IExternalCommand
    {
        private const string TrapezeFamilyPattern = "-Pipe Trapeze";

        /// <summary>
        /// Structural family name patterns to include (W-flange and bar joists).
        /// </summary>
        private static readonly string[] StructuralFamilyPatterns = new[]
        {
            "W-Wide Flange",
            "Bar Joist"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Get selected pipe accessories ──
            List<FamilyInstance> selectedAccessories = GetSelectedPipeAccessories(uidoc);
            if (selectedAccessories == null)
                return Result.Cancelled;

            // ── Filter to trapeze hangers ──
            var hangers = selectedAccessories
                .Where(fi =>
                {
                    string name = fi.Symbol?.Family?.Name ?? "";
                    return name.IndexOf(TrapezeFamilyPattern, StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();

            if (hangers.Count == 0)
            {
                TaskDialog.Show("Sync Trapeze Hangers",
                    "No trapeze hangers found in the selection.\n\n" +
                    "Select elements whose family name contains \"-Pipe Trapeze\".");
                return Result.Failed;
            }

            // ── Show dialog ──
            using (var dlg = new SyncTrapezeHangersDialog(hangers.Count))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                double minClearanceFeet = dlg.MinClearanceInches / 12.0;
                bool useClosestSide = dlg.UseClosestSide;

                // ── Collect all pipes in model ──
                var allPipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (allPipes.Count == 0)
                {
                    TaskDialog.Show("Sync Trapeze Hangers", "No pipes found in the model.");
                    return Result.Failed;
                }

                // Build pipe curve cache
                var pipeCurves = new List<(Element pipe, Curve curve)>();
                foreach (var pipe in allPipes)
                {
                    LocationCurve lc = pipe.Location as LocationCurve;
                    if (lc?.Curve != null)
                        pipeCurves.Add((pipe, lc.Curve));
                }

                // ── Find or create 3D view for raybounce ──
                View3D raybounceView = null;
                using (var tw = new TransactionWrapper(doc, "Setup Raybounce View"))
                {
                    try
                    {
                        raybounceView = FindOrCreate3DView(doc);
                        tw.Commit();
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                        return Result.Failed;
                    }
                }

                if (raybounceView == null)
                {
                    TaskDialog.Show("Sync Trapeze Hangers",
                        "Could not find or create a 3D view for raybounce.");
                    return Result.Failed;
                }

                // ── Build structural category filter ──
                var structCategoryFilter = new ElementMulticategoryFilter(
                    new List<BuiltInCategory>
                    {
                        BuiltInCategory.OST_StructuralFraming,
                        BuiltInCategory.OST_Floors,
                        BuiltInCategory.OST_Roofs
                    });

                // ── Process hangers ──
                int syncedCount = 0;
                int failedCount = 0;
                int noMatchCount = 0;

                using (var tw = new TransactionWrapper(doc, "Sync Trapeze Hangers"))
                {
                    try
                    {
                        foreach (var hanger in hangers)
                        {
                            try
                            {
                                bool ok = ProcessHanger(doc, hanger, pipeCurves, raybounceView,
                                    structCategoryFilter, minClearanceFeet, useClosestSide);
                                if (ok)
                                    syncedCount++;
                                else
                                    noMatchCount++;
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
                var lines = new List<string>();
                lines.Add($"{syncedCount} trapeze hanger{(syncedCount != 1 ? "s" : "")} synchronized.");
                if (noMatchCount > 0)
                    lines.Add($"{noMatchCount} hanger{(noMatchCount != 1 ? "s" : "")} had no matching pipe or structural element.");
                if (failedCount > 0)
                    lines.Add($"{failedCount} hanger{(failedCount != 1 ? "s" : "")} failed.");

                TaskDialog.Show("Sync Trapeze Hangers", string.Join("\n", lines));
                return Result.Succeeded;
            }
        }

        /// <summary>
        /// Processes a single trapeze hanger: finds closest pipe, computes
        /// rotation and rod positions, raybounces to structure, writes params.
        /// </summary>
        private bool ProcessHanger(Document doc, FamilyInstance hanger,
            List<(Element pipe, Curve curve)> pipeCurves, View3D view3D,
            ElementFilter structFilter, double minClearanceFeet, bool useClosestSide)
        {
            // ── Get hanger location ──
            XYZ hangerPoint = GetHangerPoint(hanger);
            if (hangerPoint == null) return false;

            // ── Find closest pipe ──
            var pipeMatch = FindClosestPipe(hangerPoint, pipeCurves);
            if (pipeMatch == null) return false;

            Element pipe = pipeMatch.Value.pipe;
            Curve pipeCurve = pipeMatch.Value.curve;
            XYZ closestPipePoint = pipeMatch.Value.closestPoint;

            // ── Get pipe properties ──
            double pipeDiameter = GetDoubleParam(pipe, "Diameter");
            double pipeNomDia = Math.Round(pipeDiameter, 3);

            // ── Compute rotation angle from pipe direction ──
            double rotationDeg = ComputePipeRotation(pipeCurve);

            // Handle mirrored instances
            bool isMirrored = hanger.Mirrored;
            double hangerRotation = isMirrored ? (360.0 - rotationDeg) : rotationDeg;

            // ── Compute rod positions (perpendicular to pipe direction) ──
            // Rod offset = (pipe diameter + 8 inches) / 2 from center, in feet
            double rodSpreadFeet = (pipeDiameter + 8.0 / 12.0) / 2.0;

            double pipeAngleRad = rotationDeg * (Math.PI / 180.0);
            // Perpendicular directions (+90° and -90° from pipe direction)
            double rod1AngleRad = pipeAngleRad + Math.PI / 2.0;
            double rod2AngleRad = pipeAngleRad - Math.PI / 2.0;

            if (isMirrored)
            {
                rod1AngleRad = pipeAngleRad - Math.PI / 2.0;
                rod2AngleRad = pipeAngleRad + Math.PI / 2.0;
            }

            XYZ rod1Dir = new XYZ(Math.Cos(rod1AngleRad), Math.Sin(rod1AngleRad), 0);
            XYZ rod2Dir = new XYZ(Math.Cos(rod2AngleRad), Math.Sin(rod2AngleRad), 0);

            XYZ rod1Point = hangerPoint + rod1Dir * rodSpreadFeet;
            XYZ rod2Point = hangerPoint + rod2Dir * rodSpreadFeet;

            // ── Raybounce from each rod position to find structural above ──
            var rod1Hit = ShootRayUp(doc, view3D, rod1Point, structFilter);
            var rod2Hit = ShootRayUp(doc, view3D, rod2Point, structFilter);

            // Fall back to shooting from hanger center if rods miss
            if (rod1Hit == null)
                rod1Hit = ShootRayUp(doc, view3D, hangerPoint, structFilter);
            if (rod2Hit == null)
                rod2Hit = ShootRayUp(doc, view3D, hangerPoint, structFilter);

            // ── Compute rod top elevations ──
            double rod1TopElev = 0;
            double rod2TopElev = 0;
            double rod1Offset = rodSpreadFeet;
            double rod2Offset = rodSpreadFeet;

            if (rod1Hit != null)
            {
                // Rod top elevation = structural hit Z minus clearance minus half-inch
                rod1TopElev = rod1Hit.Value.hitPoint.Z - minClearanceFeet - (0.5 / 12.0);
            }

            if (rod2Hit != null)
            {
                rod2TopElev = rod2Hit.Value.hitPoint.Z - minClearanceFeet - (0.5 / 12.0);
            }

            // If using middle of structural, adjust rod positions
            if (!useClosestSide && rod1Hit != null)
            {
                // "Middle" mode: rod attaches at center of structural element
                // Keep the computed elevation but don't offset to closest side
            }

            // ── Compute pipe elevation relative to level ──
            double pipeElevation = closestPipePoint.Z;
            Level hangerLevel = doc.GetElement(hanger.LevelId) as Level;
            double levelElev = hangerLevel?.Elevation ?? 0;
            double supportedPipeElev = pipeElevation - levelElev;

            // ── Write parameters ──
            // Rotation
            SetParameter(hanger, "Supported Pipe Rotation Angle", hangerRotation);

            // Rod lengths and offsets
            SetParameter(hanger, "Rod 1 Top Elevation", rod1TopElev);
            SetParameter(hanger, "Rod 1 Offset", isMirrored ? -rod1Offset : rod1Offset);
            SetParameter(hanger, "Rod 2 Top Elevation", rod2TopElev);
            SetParameter(hanger, "Rod 2 Offset", isMirrored ? -rod2Offset : rod2Offset);

            // Pipe properties
            SetParameter(hanger, "Diameter", pipeDiameter);
            SetParameter(hanger, "Nominal Diameter", pipeNomDia);
            SetParameter(hanger, "Supported Pipe Elevation", supportedPipeElev);

            // Tracking
            string stocklistInfo = "CON1," + pipe.Id.ToString();
            SetParameter(hanger, "Additional Stocklist Information (Hydratec)", stocklistInfo);
            SetParameter(hanger, "Comments", stocklistInfo);

            return true;
        }

        /// <summary>
        /// Finds the closest pipe to a hanger point.
        /// </summary>
        private (Element pipe, Curve curve, XYZ closestPoint)?
            FindClosestPipe(XYZ point, List<(Element pipe, Curve curve)> pipeCurves)
        {
            Element bestPipe = null;
            Curve bestCurve = null;
            XYZ bestPoint = null;
            double bestDist = double.MaxValue;

            foreach (var (pipe, curve) in pipeCurves)
            {
                IntersectionResult proj = curve.Project(point);
                if (proj == null) continue;

                if (proj.Distance < bestDist)
                {
                    bestDist = proj.Distance;
                    bestPipe = pipe;
                    bestCurve = curve;
                    bestPoint = proj.XYZPoint;
                }
            }

            if (bestPipe == null) return null;
            return (bestPipe, bestCurve, bestPoint);
        }

        /// <summary>
        /// Shoots a ray straight up from the given point to find structural elements.
        /// </summary>
        private (XYZ hitPoint, double distance)?
            ShootRayUp(Document doc, View3D view3D, XYZ origin, ElementFilter filter)
        {
            try
            {
                var intersector = new ReferenceIntersector(filter, FindReferenceTarget.Face, view3D);
                intersector.FindReferencesInRevitLinks = true;

                ReferenceWithContext result = intersector.FindNearest(origin, XYZ.BasisZ);
                if (result == null) return null;

                double distance = result.Proximity;
                XYZ hitPoint = origin + XYZ.BasisZ * distance;

                return (hitPoint, distance);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Computes the rotation angle (0-360 degrees) of a pipe's direction
        /// projected onto the XY plane.
        /// </summary>
        private double ComputePipeRotation(Curve pipeCurve)
        {
            Line pipeLine = pipeCurve as Line;
            if (pipeLine == null) return 0;

            XYZ dir = pipeLine.Direction;
            double angle = Math.Atan2(dir.Y, dir.X);
            double degrees = angle * (180.0 / Math.PI);
            if (degrees < 0) degrees += 360.0;
            return Math.Round(degrees, 2);
        }

        /// <summary>
        /// Gets the hanger's location point. For line-based hangers, uses midpoint.
        /// </summary>
        private XYZ GetHangerPoint(FamilyInstance hanger)
        {
            LocationPoint locPt = hanger.Location as LocationPoint;
            if (locPt != null) return locPt.Point;

            LocationCurve locCurve = hanger.Location as LocationCurve;
            if (locCurve?.Curve != null)
                return locCurve.Curve.Evaluate(0.5, true);

            return null;
        }

        /// <summary>
        /// Finds or creates the "3D-Raybounce" view.
        /// </summary>
        private View3D FindOrCreate3DView(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate &&
                    v.Name.Equals("3D-Raybounce", StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            ViewFamilyType vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null) return null;

            View3D newView = View3D.CreateIsometric(doc, vft.Id);
            newView.Name = "3D-Raybounce";

            Parameter detailParam = newView.get_Parameter(BuiltInParameter.VIEW_DETAIL_LEVEL);
            if (detailParam != null && !detailParam.IsReadOnly)
                detailParam.Set(3); // Fine

            Parameter styleParam = newView.get_Parameter(BuiltInParameter.MODEL_GRAPHICS_STYLE);
            if (styleParam != null && !styleParam.IsReadOnly)
                styleParam.Set(2); // Hidden Line

            return newView;
        }

        /// <summary>
        /// Gets selected pipe accessories from pre-selection or prompts to pick.
        /// </summary>
        private List<FamilyInstance> GetSelectedPipeAccessories(UIDocument uidoc)
        {
            Document doc = uidoc.Document;

            var preSelected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilyInstance>()
                .Where(fi => fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                .ToList();

            if (preSelected.Count > 0) return preSelected;

            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeAccessoryFilter(),
                    "Select TRAPEZE HANGERS to sync, then press Finish.");

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

        private double GetDoubleParam(Element elem, string paramName)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param != null && param.StorageType == StorageType.Double)
                return param.AsDouble();
            return 0;
        }

        private void SetParameter(Element elem, string paramName, object value)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (value is double dVal) param.Set(dVal);
            else if (value is string sVal) param.Set(sVal);
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

