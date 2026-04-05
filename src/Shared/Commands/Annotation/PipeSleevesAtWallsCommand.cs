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
    /// Automatically places pipe sleeve family instances at every intersection between
    /// user-selected pipes and walls from a linked Revit model.
    /// Sleeves are sized per NFPA annular clearance rules with seismic/non-seismic tables.
    /// Wall types can be filtered by category (Interior/Exterior/Fire Rated/Structural).
    ///
    /// WORKFLOW:
    ///   1. User selects linked model, seismic area, wall type filters
    ///   2. User selects pipes
    ///   3. Command finds pipe-wall intersections via vertical face intersection
    ///   4. Places sized, rotated sleeves with metadata parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipeSleevesAtWallsCommand : IExternalCommand
    {
        private const string SleeveFamily = "-Pipe Sleeve-Wall-EndJustified";
        private const string SleeveType = "Standard";

        // ── NFPA Sizing Tables (pipe diameter in inches → sleeve diameter in inches) ──
        private static readonly Dictionary<double, double> NonSeismicSizing = new Dictionary<double, double>
        {
            { 1.0,   2.0 },
            { 1.25,  2.0 },
            { 1.5,   2.5 },
            { 2.0,   3.0 },
            { 2.5,   4.0 },
            { 3.0,   4.0 },
            { 4.0,   6.0 },
            { 6.0,   8.0 },
            { 8.0,  10.0 }
        };
        private const double NonSeismicDefault = 12.0;

        private static readonly Dictionary<double, double> SeismicSizing = new Dictionary<double, double>
        {
            { 1.0,   3.0 },
            { 1.25,  4.0 },
            { 1.5,   4.0 },
            { 2.0,   4.0 },
            { 2.5,   5.0 },
            { 3.0,   5.0 },
            { 4.0,   8.0 },
            { 6.0,  10.0 },
            { 8.0,  12.0 }
        };
        private const double SeismicDefault = 14.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Collect loaded links ──
                var linkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(li => li.GetLinkDocument() != null)
                    .ToList();

                if (linkInstances.Count == 0)
                {
                    TaskDialog.Show("Wall Sleeves",
                        "No loaded Revit links found.\n\n" +
                        "This command requires a linked model containing walls.");
                    return Result.Cancelled;
                }

                var linkNames = linkInstances.Select(li => li.Name).ToList();

                // Pre-collect wall type names from first link for dialog
                // (will re-collect from user-selected link if different)
                var wallTypeNames = CollectWallTypeNames(linkInstances[0]);

                // ── Step 2: Show dialog ──
                var dialog = new PipeSleevesAtWallsDialog(linkNames, wallTypeNames);

                // Re-populate wall types if link selection changes
                // (handled after dialog closes, before processing)

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                // ── Step 3: Verify sleeve family is loaded ──
                FamilySymbol sleeveSymbol = FindSleeveSymbol(doc);
                if (sleeveSymbol == null)
                {
                    TaskDialog.Show("Wall Sleeves",
                        $"Sleeve family not found:\n  {SleeveFamily} : {SleeveType}\n\n" +
                        "Load this family into the project before running this command.");
                    return Result.Cancelled;
                }

                // ── Step 4: Select pipes ──
                IList<Reference> pipeRefs;
                try
                {
                    var filter = new PipeSelectionFilter();
                    pipeRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element, filter,
                        "Select pipes to process, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (pipeRefs == null || pipeRefs.Count == 0)
                {
                    TaskDialog.Show("Wall Sleeves", "No pipes selected.");
                    return Result.Cancelled;
                }

                // ── Step 5: Collect linked walls ──
                var structLink = linkInstances[dialog.SelectedLinkIndex];
                var wallDataList = CollectLinkedWalls(structLink,
                    dialog.UseAllWalls, dialog.SelectedWallTypes);

                if (wallDataList.Count == 0)
                {
                    TaskDialog.Show("Wall Sleeves",
                        "No matching walls found in the selected linked model.");
                    return Result.Cancelled;
                }

                // ── Step 6: Collect levels for reference level assignment ──
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.ProjectElevation)
                    .ToList();

                // ── Step 7: Find intersections and place sleeves ──
                int sleevesPlaced = 0;
                bool isSeismic = dialog.IsSeismic;

                using (var tw = new TransactionWrapper(doc, "Insert Pipe Sleeves at Walls"))
                {
                    if (!sleeveSymbol.IsActive)
                        sleeveSymbol.Activate();

                    foreach (var pipeRef in pipeRefs)
                    {
                        Element pipeElem = doc.GetElement(pipeRef);
                        if (pipeElem == null) continue;

                        LocationCurve pipeLoc = pipeElem.Location as LocationCurve;
                        if (pipeLoc == null) continue;
                        Curve pipeCurve = pipeLoc.Curve;

                        // Skip steeply sloped pipes (> ~60 degrees from horizontal)
                        if (IsSteepPipe(pipeCurve)) continue;

                        // Get pipe diameter
                        double pipeDiaFeet = GetPipeDiameter(pipeElem);
                        double pipeDiaInches = Math.Round(pipeDiaFeet * 12.0, 2);

                        // Get pipe reference level
                        Level pipeLevel = GetPipeLevel(doc, pipeElem);

                        // Get pipe bounding box
                        BoundingBoxXYZ pipeBB = pipeElem.get_BoundingBox(null);

                        foreach (var wall in wallDataList)
                        {
                            // Coarse bounding box check
                            if (pipeBB != null && wall.BoundingBox != null)
                            {
                                if (!BoundingBoxesOverlap(pipeBB, wall.BoundingBox))
                                    continue;
                            }

                            // Precise intersection: pipe curve vs wall vertical faces
                            var intersection = FindPipeWallIntersection(pipeCurve, wall.Solids);
                            if (intersection == null) continue;

                            XYZ midPoint = intersection.Value.MidPoint;
                            XYZ entryPoint = intersection.Value.EntryPoint;
                            XYZ exitPoint = intersection.Value.ExitPoint;
                            double wallThicknessFeet = intersection.Value.PenetrationLength;

                            // Compute sleeve diameter per NFPA
                            double sleeveDiaInches = LookupSleeveDiameter(pipeDiaInches, isSeismic);

                            // Compute rotation angle (direction through wall)
                            double rotationAngle = ComputeRotationAngle(entryPoint, exitPoint);

                            // Reference level and elevation offset
                            Level refLevel = pipeLevel ?? FindReferenceLevel(levels, midPoint.Z);
                            double levelElev = refLevel?.ProjectElevation ?? 0;
                            double elevFromLevel = midPoint.Z - levelElev;

                            // Place sleeve instance
                            FamilyInstance sleeve;
                            if (refLevel != null)
                            {
                                sleeve = doc.Create.NewFamilyInstance(
                                    new XYZ(midPoint.X, midPoint.Y, elevFromLevel),
                                    sleeveSymbol, refLevel, StructuralType.NonStructural);
                            }
                            else
                            {
                                sleeve = doc.Create.NewFamilyInstance(
                                    midPoint, sleeveSymbol, StructuralType.NonStructural);
                            }

                            if (sleeve == null) continue;

                            // Rotate sleeve to align with wall penetration direction
                            try
                            {
                                Line rotAxis = Line.CreateBound(midPoint, midPoint + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, sleeve.Id, rotAxis, rotationAngle);
                            }
                            catch { }

                            // Set parameters
                            SetSleeveParam(sleeve, "Sleeve Pipe Size", sleeveDiaInches);
                            SetSleeveParam(sleeve, "Sleeve Pipe Length", wallThicknessFeet);

                            // Comments = wall type name + fire rating
                            string comments = wall.TypeName;
                            if (!string.IsNullOrEmpty(wall.FireRating))
                                comments += " " + wall.FireRating;
                            SetParamSafe(sleeve, "Comments", comments);

                            SetSleeveParam(sleeve, "Elevation from Level", elevFromLevel);

                            sleevesPlaced++;
                        }
                    }

                    tw.Commit();
                }

                // ── Summary ──
                if (sleevesPlaced > 0)
                {
                    TaskDialog.Show("Wall Sleeves",
                        $"A total of {sleevesPlaced} wall sleeve(s) were added.\n\n" +
                        $"Sizing: {(isSeismic ? "Seismic" : "Non-Seismic")} NFPA tables\n\n" +
                        $"Parameters set:\n" +
                        $"  Sleeve Pipe Size, Sleeve Pipe Length\n" +
                        $"  Comments (wall type + fire rating)\n" +
                        $"  Elevation from Level");
                }
                else
                {
                    TaskDialog.Show("Wall Sleeves",
                        "No wall sleeves were added.\n\n" +
                        "Verify that selected pipes actually intersect walls in the linked model.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Pipe Sleeves at Walls failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  Sleeve Family Lookup
        // ══════════════════════════════════════════════════════════

        private FamilySymbol FindSleeveSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    fs.Family.Name == SleeveFamily &&
                    fs.Name == SleeveType);
        }

        // ══════════════════════════════════════════════════════════
        //  NFPA Sizing Lookup
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Looks up the sleeve diameter from NFPA sizing tables.
        /// Uses seismic or non-seismic table based on user selection.
        /// </summary>
        private double LookupSleeveDiameter(double pipeDiaInches, bool isSeismic)
        {
            var table = isSeismic ? SeismicSizing : NonSeismicSizing;
            double defaultSize = isSeismic ? SeismicDefault : NonSeismicDefault;

            // Find closest match in table
            double bestKey = -1;
            double minDiff = double.MaxValue;

            foreach (var kvp in table)
            {
                double diff = Math.Abs(kvp.Key - pipeDiaInches);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestKey = kvp.Key;
                }
            }

            // Use exact or close match (within 0.1"), otherwise default
            if (bestKey >= 0 && minDiff < 0.15)
                return table[bestKey];

            return defaultSize;
        }

        // ══════════════════════════════════════════════════════════
        //  Linked Wall Collection
        // ══════════════════════════════════════════════════════════

        private class WallData
        {
            public List<Solid> Solids { get; set; }
            public BoundingBoxXYZ BoundingBox { get; set; }
            public string TypeName { get; set; }
            public string FireRating { get; set; }
        }

        private List<string> CollectWallTypeNames(RevitLinkInstance linkInstance)
        {
            Document linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return new List<string>();

            return new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Select(w => w.WallType?.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        private List<WallData> CollectLinkedWalls(RevitLinkInstance linkInstance,
            bool useAll, List<string> selectedTypes)
        {
            var result = new List<WallData>();
            Document linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return result;

            Transform transform = linkInstance.GetTotalTransform();
            var opts = new Options { DetailLevel = ViewDetailLevel.Medium };

            var walls = new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();

            // Filter by wall type if not using all
            var selectedSet = useAll ? null :
                new HashSet<string>(selectedTypes, StringComparer.OrdinalIgnoreCase);

            foreach (var wall in walls)
            {
                string typeName = wall.WallType?.Name ?? "";

                if (!useAll && selectedSet != null && !selectedSet.Contains(typeName))
                    continue;

                var solids = new List<Solid>();
                GeometryElement geom = wall.get_Geometry(opts);
                if (geom == null) continue;

                foreach (var solid in ExtractSolids(geom))
                {
                    if (solid.Volume > 0.0001)
                    {
                        try
                        {
                            solids.Add(SolidUtils.CreateTransformed(solid, transform));
                        }
                        catch { }
                    }
                }

                if (solids.Count == 0) continue;

                // Get fire rating
                string fireRating = "";
                var frParam = wall.get_Parameter(BuiltInParameter.FIRE_RATING);
                if (frParam != null && frParam.HasValue)
                    fireRating = frParam.AsString() ?? "";

                BoundingBoxXYZ bb = ComputeSolidsBoundingBox(solids);

                result.Add(new WallData
                {
                    Solids = solids,
                    BoundingBox = bb,
                    TypeName = typeName,
                    FireRating = fireRating
                });
            }

            return result;
        }

        private IEnumerable<Solid> ExtractSolids(GeometryElement geom)
        {
            foreach (GeometryObject gObj in geom)
            {
                if (gObj is Solid solid)
                    yield return solid;
                else if (gObj is GeometryInstance gi)
                {
                    foreach (var s in ExtractSolids(gi.GetInstanceGeometry()))
                        yield return s;
                }
            }
        }

        // ══════════════════════════════════════════════════════════
        //  Geometric Intersection
        // ══════════════════════════════════════════════════════════

        private struct WallIntersectionData
        {
            public XYZ MidPoint;
            public XYZ EntryPoint;
            public XYZ ExitPoint;
            public double PenetrationLength;
        }

        /// <summary>
        /// Finds where a pipe curve passes through a wall's vertical faces.
        /// Returns entry/exit points and penetration length, or null.
        /// </summary>
        private WallIntersectionData? FindPipeWallIntersection(Curve pipeCurve, List<Solid> wallSolids)
        {
            Line pipeLine = null;
            try
            {
                XYZ p0 = pipeCurve.GetEndPoint(0);
                XYZ p1 = pipeCurve.GetEndPoint(1);
                XYZ dir = (p1 - p0).Normalize();
                pipeLine = Line.CreateBound(p0 - dir * 1.0, p1 + dir * 1.0);
            }
            catch
            {
                return null;
            }

            var hitPoints = new List<XYZ>();

            foreach (var solid in wallSolids)
            {
                foreach (Face face in solid.Faces)
                {
                    try
                    {
                        // Only intersect with vertical faces (wall faces, not top/bottom)
                        UV midUV = new UV(0.5, 0.5);
                        XYZ normal = face.ComputeNormal(midUV);
                        if (Math.Abs(normal.Z) > 0.3) continue; // Skip non-vertical faces

                        IntersectionResultArray results;
                        SetComparisonResult cmp = face.Intersect(pipeLine, out results);

                        if (cmp == SetComparisonResult.Overlap && results != null)
                        {
                            foreach (IntersectionResult ir in results)
                            {
                                hitPoints.Add(ir.XYZPoint);
                            }
                        }
                    }
                    catch { }
                }
            }

            // Need exactly 2 hits (entry and exit through wall)
            if (hitPoints.Count < 2)
                return null;

            // Find the two most distant points (entry/exit)
            XYZ best1 = hitPoints[0];
            XYZ best2 = hitPoints[1];
            double maxDist = best1.DistanceTo(best2);

            for (int i = 0; i < hitPoints.Count; i++)
            {
                for (int j = i + 1; j < hitPoints.Count; j++)
                {
                    double d = hitPoints[i].DistanceTo(hitPoints[j]);
                    if (d > maxDist)
                    {
                        maxDist = d;
                        best1 = hitPoints[i];
                        best2 = hitPoints[j];
                    }
                }
            }

            return new WallIntersectionData
            {
                MidPoint = new XYZ(
                    (best1.X + best2.X) / 2.0,
                    (best1.Y + best2.Y) / 2.0,
                    (best1.Z + best2.Z) / 2.0),
                EntryPoint = best1,
                ExitPoint = best2,
                PenetrationLength = maxDist
            };
        }

        /// <summary>
        /// Computes the rotation angle (radians) about Z-axis for the penetration direction.
        /// </summary>
        private double ComputeRotationAngle(XYZ entry, XYZ exit)
        {
            XYZ dir = exit - entry;
            if (dir.GetLength() < 0.001) return 0;
            return Math.Atan2(dir.Y, dir.X);
        }

        /// <summary>
        /// Returns true if the pipe is steeply sloped (> ~60 degrees from horizontal).
        /// These pipes are skipped since wall sleeves are for roughly horizontal pipes.
        /// </summary>
        private bool IsSteepPipe(Curve pipeCurve)
        {
            try
            {
                XYZ p0 = pipeCurve.GetEndPoint(0);
                XYZ p1 = pipeCurve.GetEndPoint(1);
                XYZ dir = p1 - p0;

                double horizontalLength = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                double verticalLength = Math.Abs(dir.Z);

                if (horizontalLength < 0.001) return true; // Vertical pipe

                double slopeAngle = Math.Atan2(verticalLength, horizontalLength);
                return slopeAngle > (Math.PI / 3.0); // > 60 degrees
            }
            catch
            {
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════

        private double GetPipeDiameter(Element pipe)
        {
            var param = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (param != null && param.HasValue)
                return param.AsDouble();

            var namedParam = pipe.LookupParameter("Diameter");
            if (namedParam != null && namedParam.HasValue && namedParam.StorageType == StorageType.Double)
                return namedParam.AsDouble();

            return 0;
        }

        private Level GetPipeLevel(Document doc, Element pipe)
        {
            var levelParam = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
            if (levelParam != null && levelParam.HasValue)
            {
                ElementId levelId = levelParam.AsElementId();
                return doc.GetElement(levelId) as Level;
            }
            return null;
        }

        private Level FindReferenceLevel(List<Level> sortedLevels, double z)
        {
            Level best = null;
            foreach (var level in sortedLevels)
            {
                if (level.ProjectElevation <= z + 0.01)
                    best = level;
                else
                    break;
            }
            return best;
        }

        private bool BoundingBoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            const double tol = 0.5;
            return a.Min.X - tol <= b.Max.X && a.Max.X + tol >= b.Min.X
                && a.Min.Y - tol <= b.Max.Y && a.Max.Y + tol >= b.Min.Y
                && a.Min.Z - tol <= b.Max.Z && a.Max.Z + tol >= b.Min.Z;
        }

        private BoundingBoxXYZ ComputeSolidsBoundingBox(List<Solid> solids)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (var solid in solids)
            {
                foreach (Edge edge in solid.Edges)
                {
                    foreach (var pt in edge.Tessellate())
                    {
                        if (pt.X < minX) minX = pt.X;
                        if (pt.Y < minY) minY = pt.Y;
                        if (pt.Z < minZ) minZ = pt.Z;
                        if (pt.X > maxX) maxX = pt.X;
                        if (pt.Y > maxY) maxY = pt.Y;
                        if (pt.Z > maxZ) maxZ = pt.Z;
                    }
                }
            }

            if (minX > maxX) return null;

            var bb = new BoundingBoxXYZ();
            bb.Min = new XYZ(minX, minY, minZ);
            bb.Max = new XYZ(maxX, maxY, maxZ);
            return bb;
        }

        private void SetSleeveParam(Element elem, string paramName, double value)
        {
            var param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (param.StorageType == StorageType.Double)
                param.Set(value);
            else if (param.StorageType == StorageType.String)
                param.Set(value.ToString("F2"));
            else if (param.StorageType == StorageType.Integer)
                param.Set((int)Math.Round(value));
        }

        private void SetParamSafe(Element elem, string paramName, string value)
        {
            var param = elem.LookupParameter(paramName);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                param.Set(value);
        }

        // ══════════════════════════════════════════════════════════
        //  Selection Filter
        // ══════════════════════════════════════════════════════════

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
