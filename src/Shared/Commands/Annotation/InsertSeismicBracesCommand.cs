using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Automatically places seismic brace family instances along selected welded
    /// fire protection pipe mains. Supports lateral, longitudinal, or both brace types
    /// with configurable spacing per NFPA 13 requirements.
    ///
    /// Migrated from: "AutoInsert - Seismic Braces On Welded Mains.dyn"
    ///
    /// WORKFLOW:
    ///   1. Dialog: brace type, families, spacing, orientation, linked model
    ///   2. User selects pipes
    ///   3. For each pipe: calculate brace points at spacing intervals
    ///   4. For each point: find structure above via linked floor/roof intersection
    ///   5. Place brace, rotate to pipe direction, set parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InsertSeismicBracesCommand : IExternalCommand
    {
        private const double SearchHeightFt = 20.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Collect seismic brace families ──
                var allBraceFamilies = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family.Name.IndexOf("-SeismicBrace", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                var lateralFamilies = allBraceFamilies
                    .Where(fs => fs.Family.Name.IndexOf("Lateral", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                var longitudinalFamilies = allBraceFamilies
                    .Where(fs => fs.Family.Name.IndexOf("Longitudinal", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                var lateralNames = lateralFamilies.Select(fs => fs.Family.Name + " : " + fs.Name).ToList();
                var longNames = longitudinalFamilies.Select(fs => fs.Family.Name + " : " + fs.Name).ToList();

                // Collect links
                var linkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(li => li.GetLinkDocument() != null)
                    .ToList();
                var linkNames = linkInstances.Select(li => li.Name).ToList();

                // ── Step 2: Show dialog ──
                var dialog = new InsertSeismicBracesDialog(lateralNames, longNames, linkNames);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                bool doLateral = dialog.BraceMode == 0 || dialog.BraceMode == 2;
                bool doLongitudinal = dialog.BraceMode == 1 || dialog.BraceMode == 2;

                FamilySymbol latSymbol = doLateral ? lateralFamilies[dialog.LateralFamilyIndex] : null;
                FamilySymbol longSymbol = doLongitudinal ? longitudinalFamilies[dialog.LongitudinalFamilyIndex] : null;

                // ── Step 3: Select pipes ──
                IList<Reference> pipeRefs;
                try
                {
                    var filter = new PipeSelectionFilter();
                    pipeRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element, filter,
                        "Select welded main pipes to brace, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (pipeRefs == null || pipeRefs.Count == 0)
                {
                    TaskDialog.Show("Seismic Braces", "No pipes selected.");
                    return Result.Cancelled;
                }

                // ── Step 4: Collect linked floor/roof solids for structure detection ──
                var archLink = linkInstances[dialog.ArchLinkIndex];
                var structureSolids = CollectLinkedFloorRoofSolids(archLink);

                // ── Step 5: Collect levels ──
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.ProjectElevation)
                    .ToList();

                // ── Step 6: Process pipes and place braces ──
                int lateralCount = 0;
                int longitudinalCount = 0;
                int skipped = 0;

                using (var tw = new TransactionWrapper(doc, "Insert Seismic Braces"))
                {
                    if (latSymbol != null && !latSymbol.IsActive) latSymbol.Activate();
                    if (longSymbol != null && !longSymbol.IsActive) longSymbol.Activate();

                    foreach (var pipeRef in pipeRefs)
                    {
                        Element pipeElem = doc.GetElement(pipeRef);
                        if (pipeElem == null) { skipped++; continue; }

                        LocationCurve pipeLoc = pipeElem.Location as LocationCurve;
                        if (pipeLoc == null) { skipped++; continue; }
                        Curve pipeCurve = pipeLoc.Curve;

                        // Skip vertical or steeply sloped pipes
                        if (IsSteepPipe(pipeCurve)) { skipped++; continue; }

                        double pipeLength = pipeCurve.Length;
                        if (pipeLength < 0.5) { skipped++; continue; } // Too short

                        // Get pipe properties
                        double pipeDiaFeet = GetPipeDiameter(pipeElem);
                        Level pipeLevel = GetPipeLevel(doc, pipeElem);
                        double pipeAngle = GetPipeAngle(pipeCurve);

                        // ── Place lateral braces ──
                        if (doLateral && latSymbol != null)
                        {
                            var latPoints = CalculateLateralBracePoints(
                                pipeCurve, dialog.LateralSpacingFt, dialog.LateralDistFromEndFt);

                            foreach (var pt in latPoints)
                            {
                                double latAngle = ComputeLateralRotation(pipeAngle, dialog.LateralOrientation);

                                var placed = PlaceBrace(doc, latSymbol, pt, pipeLevel, levels,
                                    latAngle, pipeDiaFeet, structureSolids, pipeElem.Id);
                                if (placed) lateralCount++;
                            }
                        }

                        // ── Place longitudinal braces ──
                        if (doLongitudinal && longSymbol != null)
                        {
                            var longPoints = CalculateLongitudinalBracePoints(
                                pipeCurve, dialog.LongitudinalSpacingFt);

                            foreach (var pt in longPoints)
                            {
                                double longAngle = ComputeLongitudinalRotation(pipeAngle, dialog.LongitudinalOrientation);

                                var placed = PlaceBrace(doc, longSymbol, pt, pipeLevel, levels,
                                    longAngle, pipeDiaFeet, structureSolids, pipeElem.Id);
                                if (placed) longitudinalCount++;
                            }
                        }
                    }

                    tw.Commit();
                }

                // ── Summary ──
                string summary = "Seismic Brace Summary:\n\n";
                if (doLateral)
                    summary += $"  Lateral braces placed: {lateralCount}\n" +
                               $"  Max spacing: {dialog.LateralSpacingFt} ft\n" +
                               $"  Max dist from end: {dialog.LateralDistFromEndFt} ft\n\n";
                if (doLongitudinal)
                    summary += $"  Longitudinal braces placed: {longitudinalCount}\n" +
                               $"  Max spacing: {dialog.LongitudinalSpacingFt} ft\n\n";
                if (skipped > 0)
                    summary += $"  Pipes skipped (vertical/short): {skipped}\n";

                TaskDialog.Show("Seismic Braces", summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Seismic Braces failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  Brace Point Calculation
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Calculates lateral brace points along a pipe.
        /// Starts from midpoint outward at spacing intervals, plus within distFromEnd of each pipe end.
        /// </summary>
        private List<XYZ> CalculateLateralBracePoints(Curve pipe, double maxSpacingFt, double distFromEndFt)
        {
            var points = new List<XYZ>();
            double length = pipe.Length;

            if (length < 0.5) return points;

            // Number of segments to divide into
            int segments = (int)Math.Ceiling(length / maxSpacingFt);
            if (segments < 1) segments = 1;

            double actualSpacing = length / segments;

            // Generate evenly spaced points from midpoint outward
            double midParam = 0.5;
            XYZ midPoint = pipe.Evaluate(midParam, true);

            if (segments == 1)
            {
                // Single brace at midpoint
                points.Add(midPoint);
            }
            else
            {
                // Multiple braces at even spacing
                for (int i = 0; i <= segments; i++)
                {
                    double dist = i * actualSpacing;
                    double param = dist / length;
                    if (param > 1.0) param = 1.0;
                    points.Add(pipe.Evaluate(param, true));
                }
            }

            // Ensure a brace within distFromEnd of start
            XYZ startPt = pipe.GetEndPoint(0);
            bool hasNearStart = points.Any(p => p.DistanceTo(startPt) <= distFromEndFt + 0.1);
            if (!hasNearStart && length > distFromEndFt)
            {
                double param = distFromEndFt / length;
                points.Add(pipe.Evaluate(param, true));
            }

            // Ensure a brace within distFromEnd of end
            XYZ endPt = pipe.GetEndPoint(1);
            bool hasNearEnd = points.Any(p => p.DistanceTo(endPt) <= distFromEndFt + 0.1);
            if (!hasNearEnd && length > distFromEndFt)
            {
                double param = 1.0 - (distFromEndFt / length);
                points.Add(pipe.Evaluate(param, true));
            }

            return points;
        }

        /// <summary>
        /// Calculates longitudinal brace points along a pipe.
        /// Evenly spaced from start to end.
        /// </summary>
        private List<XYZ> CalculateLongitudinalBracePoints(Curve pipe, double maxSpacingFt)
        {
            var points = new List<XYZ>();
            double length = pipe.Length;

            if (length < 0.5) return points;

            int segments = (int)Math.Ceiling(length / maxSpacingFt);
            if (segments < 1) segments = 1;

            double actualSpacing = length / segments;

            for (int i = 0; i <= segments; i++)
            {
                double dist = i * actualSpacing;
                double param = dist / length;
                if (param > 1.0) param = 1.0;
                points.Add(pipe.Evaluate(param, true));
            }

            return points;
        }

        // ══════════════════════════════════════════════════════════
        //  Brace Placement
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Places a single brace instance and sets its parameters.
        /// </summary>
        private bool PlaceBrace(Document doc, FamilySymbol symbol, XYZ point,
            Level pipeLevel, List<Level> allLevels, double rotationDeg,
            double pipeDiaFeet, List<Solid> structureSolids, ElementId pipeId)
        {
            // Find structure above
            double? structureZ = FindStructureAbove(point, structureSolids);
            double braceHeight = structureZ.HasValue ? structureZ.Value - point.Z : 0;

            // Rod length rounded up to nearest 0.5 ft
            double rodLength = braceHeight > 0
                ? Math.Ceiling(braceHeight / 0.5) * 0.5
                : 0;

            // Determine reference level
            Level refLevel = pipeLevel ?? FindReferenceLevel(allLevels, point.Z);
            double levelElev = refLevel?.ProjectElevation ?? 0;
            double elevFromLevel = point.Z - levelElev;

            // Place instance
            FamilyInstance brace;
            try
            {
                if (refLevel != null)
                {
                    brace = doc.Create.NewFamilyInstance(
                        new XYZ(point.X, point.Y, elevFromLevel),
                        symbol, refLevel, StructuralType.NonStructural);
                }
                else
                {
                    brace = doc.Create.NewFamilyInstance(
                        point, symbol, StructuralType.NonStructural);
                }
            }
            catch
            {
                return false;
            }

            if (brace == null) return false;

            // Rotate to pipe direction
            try
            {
                double radians = rotationDeg * Math.PI / 180.0;
                Line rotAxis = Line.CreateBound(point, point + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, brace.Id, rotAxis, radians);
            }
            catch { }

            // Set parameters
            SetSleeveParam(brace, "XDistanceToAnchor", rodLength);
            SetSleeveParam(brace, "BraceHeight", braceHeight);
            SetSleeveParam(brace, "Nominal Diameter", pipeDiaFeet);

            // Hydratec stocklist info
            string stockInfo = "CON1," + pipeId.IntegerValue.ToString();
            SetParamSafe(brace, "Additional Stocklist Information (Hydratec)", stockInfo);

            return true;
        }

        // ══════════════════════════════════════════════════════════
        //  Rotation Angle Calculation
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Gets the horizontal angle of a pipe curve in degrees (0-360).
        /// </summary>
        private double GetPipeAngle(Curve pipe)
        {
            XYZ p0 = pipe.GetEndPoint(0);
            XYZ p1 = pipe.GetEndPoint(1);
            double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180.0 / Math.PI;
            if (angle < 0) angle += 360;
            return angle;
        }

        /// <summary>
        /// Computes lateral brace rotation (perpendicular to pipe).
        /// orientation: 0 = Left/Above, 1 = Right/Below
        /// </summary>
        private double ComputeLateralRotation(double pipeAngle, int orientation)
        {
            double angle;
            if (orientation == 0)
            {
                // Left of pipe / above: +90 from pipe direction
                angle = pipeAngle < 90 ? pipeAngle + 90 :
                        Math.Abs(pipeAngle - 90) < 0.01 ? pipeAngle + 270 :
                        pipeAngle >= 270 ? pipeAngle - 270 :
                        pipeAngle + 90;
            }
            else
            {
                // Right of pipe / below: -90 from pipe direction
                angle = pipeAngle < 90 ? pipeAngle + 270 :
                        Math.Abs(pipeAngle - 90) < 0.01 ? pipeAngle + 90 :
                        pipeAngle >= 270 ? pipeAngle - 90 :
                        pipeAngle - 90;
            }

            // Normalize to 0-360
            while (angle < 0) angle += 360;
            while (angle >= 360) angle -= 360;
            return angle;
        }

        /// <summary>
        /// Computes longitudinal brace rotation (aligned with pipe).
        /// orientation: 0 = Right/Upward, 1 = Left/Downward
        /// </summary>
        private double ComputeLongitudinalRotation(double pipeAngle, int orientation)
        {
            double angle;
            if (orientation == 0)
            {
                // Right or upward along pipe
                angle = pipeAngle < 90 ? pipeAngle + 270 :
                        Math.Abs(pipeAngle - 90) < 0.01 ? pipeAngle + 90 :
                        pipeAngle - 90;
            }
            else
            {
                // Left or downward along pipe (reverse)
                angle = pipeAngle < 90 ? pipeAngle + 90 :
                        Math.Abs(pipeAngle - 90) < 0.01 ? pipeAngle + 270 :
                        pipeAngle >= 270 ? pipeAngle - 270 :
                        pipeAngle + 90;
            }

            while (angle < 0) angle += 360;
            while (angle >= 360) angle -= 360;
            return angle;
        }

        // ══════════════════════════════════════════════════════════
        //  Structure Detection (linked model intersection)
        // ══════════════════════════════════════════════════════════

        private List<Solid> CollectLinkedFloorRoofSolids(RevitLinkInstance linkInstance)
        {
            var solids = new List<Solid>();
            Document linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return solids;

            Transform transform = linkInstance.GetTotalTransform();
            var opts = new Options { DetailLevel = ViewDetailLevel.Medium };

            var categories = new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs };

            foreach (var cat in categories)
            {
                var elems = new FilteredElementCollector(linkedDoc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var elem in elems)
                {
                    GeometryElement geom = elem.get_Geometry(opts);
                    if (geom == null) continue;

                    foreach (var solid in ExtractSolids(geom))
                    {
                        if (solid.Volume > 0.0001)
                        {
                            try { solids.Add(SolidUtils.CreateTransformed(solid, transform)); }
                            catch { }
                        }
                    }
                }
            }

            return solids;
        }

        /// <summary>
        /// Finds the Z coordinate of the nearest structure above a point
        /// by intersecting a vertical line with linked floor/roof solids.
        /// </summary>
        private double? FindStructureAbove(XYZ point, List<Solid> solids)
        {
            Line searchLine;
            try
            {
                searchLine = Line.CreateBound(point, new XYZ(point.X, point.Y, point.Z + SearchHeightFt));
            }
            catch { return null; }

            double? lowestHit = null;

            foreach (var solid in solids)
            {
                // Quick BB check
                BoundingBoxXYZ bb = solid.GetBoundingBox();
                if (bb != null)
                {
                    if (point.X < bb.Min.X - 1 || point.X > bb.Max.X + 1 ||
                        point.Y < bb.Min.Y - 1 || point.Y > bb.Max.Y + 1 ||
                        bb.Max.Z < point.Z || bb.Min.Z > point.Z + SearchHeightFt)
                        continue;
                }

                foreach (Face face in solid.Faces)
                {
                    try
                    {
                        // Only check downward-facing faces (underside of deck)
                        UV midUV = new UV(0.5, 0.5);
                        XYZ normal = face.ComputeNormal(midUV);
                        if (normal.Z > -0.5) continue; // Want down-facing (underside)

                        IntersectionResultArray results;
                        SetComparisonResult cmp = face.Intersect(searchLine, out results);

                        if (cmp == SetComparisonResult.Overlap && results != null)
                        {
                            foreach (IntersectionResult ir in results)
                            {
                                double hitZ = ir.XYZPoint.Z;
                                if (hitZ > point.Z - 0.01)
                                {
                                    if (!lowestHit.HasValue || hitZ < lowestHit.Value)
                                        lowestHit = hitZ;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            return lowestHit;
        }

        // ══════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════

        private IEnumerable<Solid> ExtractSolids(GeometryElement geom)
        {
            foreach (GeometryObject gObj in geom)
            {
                if (gObj is Solid solid) yield return solid;
                else if (gObj is GeometryInstance gi)
                    foreach (var s in ExtractSolids(gi.GetInstanceGeometry()))
                        yield return s;
            }
        }

        private bool IsSteepPipe(Curve pipe)
        {
            try
            {
                XYZ p0 = pipe.GetEndPoint(0);
                XYZ p1 = pipe.GetEndPoint(1);
                XYZ dir = p1 - p0;
                double horiz = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                if (horiz < 0.001) return true;
                return Math.Atan2(Math.Abs(dir.Z), horiz) > (Math.PI / 3.0);
            }
            catch { return false; }
        }

        private double GetPipeDiameter(Element pipe)
        {
            var param = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (param != null && param.HasValue) return param.AsDouble();
            var named = pipe.LookupParameter("Diameter");
            if (named != null && named.HasValue && named.StorageType == StorageType.Double)
                return named.AsDouble();
            return 0;
        }

        private Level GetPipeLevel(Document doc, Element pipe)
        {
            var lp = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            if (lp != null && lp.HasValue)
                return doc.GetElement(lp.AsElementId()) as Level;
            return null;
        }

        private Level FindReferenceLevel(List<Level> sortedLevels, double z)
        {
            Level best = null;
            foreach (var level in sortedLevels)
            {
                if (level.ProjectElevation <= z + 0.01) best = level;
                else break;
            }
            return best;
        }

        private void SetSleeveParam(Element elem, string paramName, double value)
        {
            var param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;
            if (param.StorageType == StorageType.Double) param.Set(value);
            else if (param.StorageType == StorageType.String) param.Set(value.ToString("F2"));
            else if (param.StorageType == StorageType.Integer) param.Set((int)Math.Round(value));
        }

        private void SetParamSafe(Element elem, string paramName, string value)
        {
            var param = elem.LookupParameter(paramName);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                param.Set(value);
        }

        private class PipeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem?.Category == null) return false;
                return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves;
            }
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
