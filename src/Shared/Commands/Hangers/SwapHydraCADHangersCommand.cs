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
    /// Replaces HydraCAD pipe hanger family instances ("Adjustable Ring Hanger")
    /// with SSG "-Pipe Hanger - Standard" family instances. Transfers
    /// parameters and computes correct position, rotation, and rod length.
    ///
    /// WORKFLOW:
    ///   1. User selects pipe accessories (pre-selection or pick)
    ///   2. Filter to HydraCAD hangers (family contains "Adjustable Ring Hanger")
    ///   3. Dialog confirms count and delete option
    ///   4. For each hanger: find intersecting pipe, compute position/rotation
    ///   5. Create new "-Pipe Hanger - Standard" instances with transferred parameters
    ///   6. Optionally delete original HydraCAD hangers
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SwapHydraCADHangersCommand : IExternalCommand
    {
        private const string HydraCADFamilyPattern = "Adjustable Ring Hanger";
        private const string ReplacementFamilyName = "-Pipe Hanger - Standard";
        private const double BBoxSearchOffset = 5.0; // feet, vertical search range for pipe finding

        /// <summary>
        /// Data extracted from each HydraCAD hanger before replacement.
        /// </summary>
        private class HangerRecord
        {
            public ElementId OriginalId { get; set; }
            public XYZ MidPoint { get; set; }
            public double Diameter { get; set; }
            public double RodLength { get; set; }
            public string TypeCode { get; set; }
            public string HcadSystem { get; set; }
            public ElementId LevelId { get; set; }
            public double LevelElevation { get; set; }

            // Computed from intersecting pipe
            public XYZ PipeClosestPoint { get; set; }
            public double PipeRotationDegrees { get; set; }
            public ElementId PipeElementId { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // ── Get selected pipe accessories ──
            List<FamilyInstance> selectedAccessories = GetSelectedPipeAccessories(uidoc);
            if (selectedAccessories == null)
                return Result.Cancelled;

            // ── Filter to HydraCAD hangers ──
            var hydraCADHangers = selectedAccessories
                .Where(fi =>
                {
                    string familyName = fi.Symbol?.Family?.Name ?? "";
                    return familyName.IndexOf(HydraCADFamilyPattern, StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();

            if (hydraCADHangers.Count == 0)
            {
                TaskDialog.Show("AutoSwap HydraCAD Hangers",
                    "No HydraCAD hangers (\"Adjustable Ring Hanger\") found in the selection.\n\n" +
                    "Select HydraCAD pipe hanger instances and run the command again.");
                return Result.Failed;
            }

            // ── Find replacement family ──
            FamilySymbol replacementType = FindReplacementFamilyType(doc);
            if (replacementType == null)
            {
                TaskDialog.Show("AutoSwap HydraCAD Hangers",
                    $"Replacement family \"{ReplacementFamilyName}\" not found in the project.\n\n" +
                    "Load the family and run the command again.");
                return Result.Failed;
            }

            // ── Show dialog ──
            using (var dlg = new SwapHydraCADHangersDialog(hydraCADHangers.Count))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                bool deleteOriginals = dlg.DeleteOriginals;

                // ── Collect all pipes in active view for intersection search ──
                var pipesInView = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .ToList();

                // ── Build hanger records ──
                var records = new List<HangerRecord>();
                int skippedNoPipe = 0;

                foreach (var hanger in hydraCADHangers)
                {
                    var record = BuildHangerRecord(doc, hanger, pipesInView);
                    if (record == null)
                    {
                        skippedNoPipe++;
                        continue;
                    }
                    records.Add(record);
                }

                if (records.Count == 0)
                {
                    TaskDialog.Show("AutoSwap HydraCAD Hangers",
                        "No valid hangers to process. Could not find intersecting pipes " +
                        "for any of the selected hangers.");
                    return Result.Failed;
                }

                // ── Execute in transaction ──
                int createdCount = 0;
                int deletedCount = 0;
                int failedCount = 0;

                using (var tw = new TransactionWrapper(doc, "AutoSwap HydraCAD Hangers"))
                {
                    try
                    {
                        // Ensure replacement type is activated
                        if (!replacementType.IsActive)
                            replacementType.Activate();

                        foreach (var rec in records)
                        {
                            try
                            {
                                // Place new hanger at pipe closest point on same level
                                FamilyInstance newHanger = doc.Create.NewFamilyInstance(
                                    rec.PipeClosestPoint,
                                    replacementType,
                                    doc.GetElement(rec.LevelId) as Level,
                                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                // Set rotation to match pipe direction
                                SetHangerRotation(doc, newHanger, rec.PipeClosestPoint, rec.PipeRotationDegrees);

                                // Compute adjusted rod length
                                double elevationAdjustment = rec.MidPoint.Z - rec.PipeClosestPoint.Z;
                                double adjustedRodLength = rec.RodLength + elevationAdjustment;

                                // Compute elevation from level
                                double elevationFromLevel = rec.PipeClosestPoint.Z - rec.LevelElevation;

                                // Set parameters on new hanger
                                SetParameter(newHanger, "Nominal Diameter", Math.Round(rec.Diameter, 3));
                                SetParameter(newHanger, "Rod Length", adjustedRodLength);
                                SetParameter(newHanger, "Type Code (Hydratec)", rec.TypeCode);
                                SetParameter(newHanger, "HCAD-System", rec.HcadSystem);
                                SetParameter(newHanger, "Elevation from Level", elevationFromLevel);

                                // Stocklist info: "CON1," + pipe element ID
                                string stocklistInfo = "CON1," + rec.PipeElementId.ToString();
                                SetParameter(newHanger, "Additional Stocklist Information (Hydratec)", stocklistInfo);

                                createdCount++;
                            }
                            catch
                            {
                                failedCount++;
                            }
                        }

                        // Delete originals if requested
                        if (deleteOriginals)
                        {
                            foreach (var rec in records)
                            {
                                try
                                {
                                    doc.Delete(rec.OriginalId);
                                    deletedCount++;
                                }
                                catch { }
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
                string summary = $"Created {createdCount} replacement hanger{(createdCount != 1 ? "s" : "")}.";
                if (deleteOriginals)
                    summary += $"\nDeleted {deletedCount} original HydraCAD hanger{(deletedCount != 1 ? "s" : "")}.";
                if (skippedNoPipe > 0)
                    summary += $"\nSkipped {skippedNoPipe} hanger{(skippedNoPipe != 1 ? "s" : "")} (no intersecting pipe found).";
                if (failedCount > 0)
                    summary += $"\n{failedCount} hanger{(failedCount != 1 ? "s" : "")} failed to create.";

                TaskDialog.Show("AutoSwap HydraCAD Hangers", summary);
                return Result.Succeeded;
            }
        }

        /// <summary>
        /// Builds a HangerRecord from a HydraCAD hanger instance.
        /// Finds the intersecting pipe and computes geometry.
        /// Returns null if no intersecting pipe is found.
        /// </summary>
        private HangerRecord BuildHangerRecord(Document doc, FamilyInstance hanger, List<Element> pipesInView)
        {
            // ── Get hanger midpoint from geometry ──
            XYZ hangerPoint = GetHangerMidPoint(hanger);
            if (hangerPoint == null)
            {
                // Fallback to location point
                LocationPoint locPt = hanger.Location as LocationPoint;
                if (locPt == null) return null;
                hangerPoint = locPt.Point;
            }

            // ── Find intersecting pipe ──
            Element pipe = FindIntersectingPipe(hangerPoint, pipesInView);
            if (pipe == null)
                return null;

            // ── Get closest point on pipe to hanger ──
            XYZ pipeClosestPoint = GetClosestPointOnPipe(pipe, hangerPoint);
            if (pipeClosestPoint == null)
                pipeClosestPoint = hangerPoint;

            // ── Compute rotation from pipe direction ──
            double rotationDegrees = ComputePipeRotation(pipe);

            // ── Read parameters from HydraCAD hanger ──
            double diameter = GetDoubleParameter(hanger, "Diameter");
            double rodLength = GetDoubleParameter(hanger, "Rod Length");
            string typeCode = GetStringParameter(hanger, "Type Code (Hydratec)");
            string hcadSystem = GetStringParameter(hanger, "HCAD-System");

            // ── Get reference level ──
            ElementId levelId = hanger.LevelId;
            double levelElevation = 0;

            // Try from "Reference Level" parameter first
            Parameter refLevelParam = hanger.LookupParameter("Reference Level");
            if (refLevelParam != null && refLevelParam.AsElementId() != null &&
                refLevelParam.AsElementId() != ElementId.InvalidElementId)
            {
                levelId = refLevelParam.AsElementId();
            }

            Level level = doc.GetElement(levelId) as Level;
            if (level != null)
                levelElevation = level.Elevation;

            return new HangerRecord
            {
                OriginalId = hanger.Id,
                MidPoint = hangerPoint,
                Diameter = diameter,
                RodLength = rodLength,
                TypeCode = typeCode ?? "",
                HcadSystem = hcadSystem ?? "",
                LevelId = levelId,
                LevelElevation = levelElevation,
                PipeClosestPoint = pipeClosestPoint,
                PipeRotationDegrees = rotationDegrees,
                PipeElementId = pipe.Id
            };
        }

        /// <summary>
        /// Extracts the midpoint of the hanger's line geometry.
        /// Finds Line geometry within the element, then gets PointAtParameter(0.5).
        /// </summary>
        private XYZ GetHangerMidPoint(FamilyInstance hanger)
        {
            Options geomOptions = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Coarse
            };

            GeometryElement geomElem = hanger.get_Geometry(geomOptions);
            if (geomElem == null)
                return null;

            // Search for line geometry in the hanger
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Line line)
                    return line.Evaluate(0.5, true);

                if (geomObj is GeometryInstance geomInst)
                {
                    foreach (GeometryObject instObj in geomInst.GetInstanceGeometry())
                    {
                        if (instObj is Line instLine)
                            return instLine.Evaluate(0.5, true);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a pipe that intersects with the hanger location using a vertical bounding box search.
        /// Creates a search region +/- 5 feet vertically from the hanger point.
        /// </summary>
        private Element FindIntersectingPipe(XYZ hangerPoint, List<Element> pipesInView)
        {
            // Create a small search bounding box around the hanger point
            double tolerance = 0.5; // feet, horizontal tolerance
            XYZ minPt = new XYZ(
                hangerPoint.X - tolerance,
                hangerPoint.Y - tolerance,
                hangerPoint.Z - BBoxSearchOffset);
            XYZ maxPt = new XYZ(
                hangerPoint.X + tolerance,
                hangerPoint.Y + tolerance,
                hangerPoint.Z + BBoxSearchOffset);

            // Find pipes whose bounding box overlaps
            Element closest = null;
            double closestDist = double.MaxValue;

            foreach (var pipe in pipesInView)
            {
                BoundingBoxXYZ pipeBB = pipe.get_BoundingBox(null);
                if (pipeBB == null) continue;

                // Check if bounding boxes overlap
                if (pipeBB.Max.X < minPt.X || pipeBB.Min.X > maxPt.X) continue;
                if (pipeBB.Max.Y < minPt.Y || pipeBB.Min.Y > maxPt.Y) continue;
                if (pipeBB.Max.Z < minPt.Z || pipeBB.Min.Z > maxPt.Z) continue;

                // Get distance from hanger to pipe centerline
                LocationCurve locCurve = pipe.Location as LocationCurve;
                if (locCurve?.Curve == null) continue;

                IntersectionResult result = locCurve.Curve.Project(hangerPoint);
                if (result == null) continue;

                double dist = result.Distance;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = pipe;
                }
            }

            return closest;
        }

        /// <summary>
        /// Gets the closest point on a pipe's centerline to a given point.
        /// </summary>
        private XYZ GetClosestPointOnPipe(Element pipe, XYZ point)
        {
            LocationCurve locCurve = pipe.Location as LocationCurve;
            if (locCurve?.Curve == null) return null;

            IntersectionResult result = locCurve.Curve.Project(point);
            return result?.XYZPoint;
        }

        /// <summary>
        /// Computes the rotation angle (degrees, 0-360) of a pipe's direction
        /// projected onto the XY plane, measured from the X-axis around the Z-axis.
        /// </summary>
        private double ComputePipeRotation(Element pipe)
        {
            LocationCurve locCurve = pipe.Location as LocationCurve;
            if (locCurve?.Curve == null) return 0;

            Line pipeLine = locCurve.Curve as Line;
            if (pipeLine == null) return 0;

            XYZ dir = pipeLine.Direction;

            // Flatten to XY plane
            double angle = Math.Atan2(dir.Y, dir.X);
            double degrees = angle * (180.0 / Math.PI);

            // Normalize to 0-360
            if (degrees < 0) degrees += 360.0;

            return Math.Round(degrees, 2);
        }

        /// <summary>
        /// Sets the rotation of a family instance about the Z-axis at its location point.
        /// </summary>
        private void SetHangerRotation(Document doc, FamilyInstance hanger, XYZ point, double degrees)
        {
            double radians = degrees * (Math.PI / 180.0);
            Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, hanger.Id, axis, radians);
        }

        /// <summary>
        /// Gets selected pipe accessories from pre-selection or prompts user to pick.
        /// Returns null if cancelled.
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
                    new PipeAccessorySelectionFilter(),
                    "Select HydraCAD hangers (Pipe Accessories), then press Finish.");

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

        /// <summary>
        /// Finds the first type of the "-Pipe Hanger - Standard" family in the project.
        /// </summary>
        private FamilySymbol FindReplacementFamilyType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family.Name == ReplacementFamilyName);
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
        /// Gets a string parameter value by name, returning null if not found.
        /// </summary>
        private string GetStringParameter(Element elem, string paramName)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null) return null;
            return param.AsString() ?? param.AsValueString();
        }

        /// <summary>
        /// Sets a parameter by name on an element. Handles both double and string types.
        /// </summary>
        private void SetParameter(Element elem, string paramName, object value)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (value is double dVal)
                param.Set(dVal);
            else if (value is string sVal)
                param.Set(sVal);
            else if (value is int iVal)
                param.Set(iVal);
        }

        /// <summary>
        /// Selection filter that only allows pipe accessories.
        /// </summary>
        private class PipeAccessorySelectionFilter : ISelectionFilter
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
