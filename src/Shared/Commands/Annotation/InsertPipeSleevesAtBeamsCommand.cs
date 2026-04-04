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
    /// user-selected pipes and structural beams from a linked Revit model.
    /// Sleeves are sized per NFPA annular clearance rules, rotated to match beam
    /// direction, and stamped with metadata parameters for scheduling.
    ///
    /// Migrated from: "AutoInsert - Pipe Sleeves at Intersecting Beams.dyn"
    ///
    /// WORKFLOW:
    ///   1. User selects structural link and enters sleeve length
    ///   2. User selects pipes
    ///   3. Command collects linked beams and finds pipe-beam intersections
    ///   4. Places sized, rotated sleeve instances with metadata parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InsertPipeSleevesAtBeamsCommand : IExternalCommand
    {
        private const string SleeveFamily = "-Pipe Sleeve-Beam-MiddleJustified";
        private const string SleeveType = "Standard";

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
                    TaskDialog.Show("Pipe Sleeves",
                        "No loaded Revit links found.\n\n" +
                        "This command requires a linked structural model containing beams.");
                    return Result.Cancelled;
                }

                var linkNames = linkInstances.Select(li => li.Name).ToList();

                // ── Step 2: Show dialog ──
                var dialog = new InsertPipeSleevesAtBeamsDialog(linkNames);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                double sleeveLengthFeet = dialog.SleeveLengthInches / 12.0;

                // ── Step 3: Verify sleeve family is loaded ──
                FamilySymbol sleeveSymbol = FindSleeveSymbol(doc);
                if (sleeveSymbol == null)
                {
                    TaskDialog.Show("Pipe Sleeves",
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
                    TaskDialog.Show("Pipe Sleeves", "No pipes selected.");
                    return Result.Cancelled;
                }

                // ── Step 5: Collect linked beams ──
                var structLink = linkInstances[dialog.SelectedLinkIndex];
                var beamDataList = CollectLinkedBeams(structLink);

                if (beamDataList.Count == 0)
                {
                    TaskDialog.Show("Pipe Sleeves",
                        "No structural framing (beams) found in the selected linked model.");
                    return Result.Cancelled;
                }

                // ── Step 6: Find intersections and place sleeves ──
                int sleevesPlaced = 0;

                using (var tw = new TransactionWrapper(doc, "Insert Pipe Sleeves at Beams"))
                {
                    // Activate the sleeve symbol if needed
                    if (!sleeveSymbol.IsActive)
                        sleeveSymbol.Activate();

                    foreach (var pipeRef in pipeRefs)
                    {
                        Element pipeElem = doc.GetElement(pipeRef);
                        if (pipeElem == null) continue;

                        // Get pipe curve
                        LocationCurve pipeLoc = pipeElem.Location as LocationCurve;
                        if (pipeLoc == null) continue;
                        Curve pipeCurve = pipeLoc.Curve;

                        // Get pipe diameter (internal units = feet)
                        double pipeDiaFeet = GetPipeDiameter(pipeElem);
                        double pipeDiaInches = pipeDiaFeet * 12.0;

                        // Get pipe reference level
                        Level pipeLevel = GetPipeLevel(doc, pipeElem);

                        // Get pipe bounding box for coarse filter
                        BoundingBoxXYZ pipeBB = pipeElem.get_BoundingBox(null);

                        foreach (var beam in beamDataList)
                        {
                            // Coarse bounding box check
                            if (pipeBB != null && beam.BoundingBox != null)
                            {
                                if (!BoundingBoxesOverlap(pipeBB, beam.BoundingBox))
                                    continue;
                            }

                            // Precise intersection: pipe curve vs beam faces
                            XYZ intersectionPoint = FindPipeBeamIntersection(
                                pipeCurve, beam.Solids);

                            if (intersectionPoint == null) continue;

                            // Compute sleeve diameter per NFPA sizing
                            double sleeveDiaInches = ComputeSleeveDiameter(pipeDiaInches);

                            // Compute elevation offset from level
                            double levelElev = pipeLevel?.ProjectElevation ?? 0;
                            double elevFromLevel = intersectionPoint.Z - levelElev;

                            // Place sleeve instance
                            FamilyInstance sleeve;
                            if (pipeLevel != null)
                            {
                                sleeve = doc.Create.NewFamilyInstance(
                                    new XYZ(intersectionPoint.X, intersectionPoint.Y, elevFromLevel),
                                    sleeveSymbol, pipeLevel, StructuralType.NonStructural);
                            }
                            else
                            {
                                sleeve = doc.Create.NewFamilyInstance(
                                    intersectionPoint, sleeveSymbol, StructuralType.NonStructural);
                            }

                            if (sleeve == null) continue;

                            // Rotate sleeve to match beam direction
                            if (beam.Direction != null)
                            {
                                double angle = Math.Atan2(beam.Direction.Y, beam.Direction.X);
                                Line rotAxis = Line.CreateBound(
                                    intersectionPoint,
                                    intersectionPoint + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, sleeve.Id, rotAxis, angle);
                            }

                            // Set parameters
                            SetSleeveParam(sleeve, "Sleeve Pipe Size", sleeveDiaInches);
                            SetSleeveParam(sleeve, "Sleeve Pipe Length", sleeveLengthFeet);
                            SetParamSafe(sleeve, "Comments", beam.TypeName);
                            SetSleeveParam(sleeve, "Elevation from Level", elevFromLevel);

                            sleevesPlaced++;
                        }
                    }

                    tw.Commit();
                }

                // ── Summary ──
                if (sleevesPlaced > 0)
                {
                    TaskDialog.Show("Pipe Sleeves",
                        $"A total of {sleevesPlaced} beam sleeve(s) were added.\n\n" +
                        $"Parameters set:\n" +
                        $"  Sleeve Pipe Size (NFPA annular clearance)\n" +
                        $"  Sleeve Pipe Length ({dialog.SleeveLengthInches}\")\n" +
                        $"  Comments (beam type name)\n" +
                        $"  Elevation from Level");
                }
                else
                {
                    TaskDialog.Show("Pipe Sleeves",
                        "No beam sleeves were added.\n\n" +
                        "Verify that selected pipes actually intersect beams in the linked model.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Pipe Sleeves at Beams failed:\n" + ex.Message);
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
        //  Linked Beam Collection
        // ══════════════════════════════════════════════════════════

        private class BeamData
        {
            public List<Solid> Solids { get; set; }
            public BoundingBoxXYZ BoundingBox { get; set; }
            public XYZ Direction { get; set; }
            public string TypeName { get; set; }
        }

        private List<BeamData> CollectLinkedBeams(RevitLinkInstance linkInstance)
        {
            var result = new List<BeamData>();
            Document linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return result;

            Transform transform = linkInstance.GetTotalTransform();
            var opts = new Options { DetailLevel = ViewDetailLevel.Medium };

            var beams = new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var beam in beams)
            {
                var solids = new List<Solid>();
                GeometryElement geom = beam.get_Geometry(opts);
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

                // Get beam direction from location curve
                XYZ direction = null;
                if (beam.Location is LocationCurve lc)
                {
                    XYZ start = transform.OfPoint(lc.Curve.GetEndPoint(0));
                    XYZ end = transform.OfPoint(lc.Curve.GetEndPoint(1));
                    XYZ dir = (end - start);
                    if (dir.GetLength() > 0.001)
                        direction = dir.Normalize();
                }

                // Compute transformed bounding box from solids
                BoundingBoxXYZ combinedBB = ComputeCombinedBoundingBox(solids);

                // Get beam type name
                string typeName = beam.Name ?? "";

                result.Add(new BeamData
                {
                    Solids = solids,
                    BoundingBox = combinedBB,
                    Direction = direction,
                    TypeName = typeName
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

        /// <summary>
        /// Finds the intersection point where a pipe curve passes through a beam's solid geometry.
        /// Returns the midpoint between entry and exit faces, or null if no intersection.
        /// </summary>
        private XYZ FindPipeBeamIntersection(Curve pipeCurve, List<Solid> beamSolids)
        {
            // Extend the pipe curve slightly to handle edge cases
            Line pipeLine = null;
            try
            {
                XYZ p0 = pipeCurve.GetEndPoint(0);
                XYZ p1 = pipeCurve.GetEndPoint(1);
                XYZ dir = (p1 - p0).Normalize();
                // Extend 1 foot each direction
                pipeLine = Line.CreateBound(p0 - dir * 1.0, p1 + dir * 1.0);
            }
            catch
            {
                return null;
            }

            var hitPoints = new List<XYZ>();

            foreach (var solid in beamSolids)
            {
                foreach (Face face in solid.Faces)
                {
                    try
                    {
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

            if (hitPoints.Count == 0)
                return null;

            if (hitPoints.Count == 1)
                return hitPoints[0];

            // Multiple hits: return midpoint of the two most distant points
            // (entry and exit through beam)
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

            return new XYZ(
                (best1.X + best2.X) / 2.0,
                (best1.Y + best2.Y) / 2.0,
                (best1.Z + best2.Z) / 2.0);
        }

        // ══════════════════════════════════════════════════════════
        //  NFPA Sleeve Sizing
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Computes the sleeve diameter based on NFPA annular clearance rules.
        /// Input and output are in inches.
        ///
        /// Rules:
        ///   - Pipe &lt; 3.5": sleeve = pipe + 2"
        ///   - Pipe >= 3.5": sleeve = pipe + 4"
        ///   - Snap 3.25" → 3.5" (standard size)
        ///   - Snap 4.5" → 5" (standard size)
        /// </summary>
        private double ComputeSleeveDiameter(double pipeDiaInches)
        {
            double rounded = Math.Round(pipeDiaInches, 2);

            double sleeve = rounded < 3.5 ? rounded + 2.0 : rounded + 4.0;

            // Snap to standard pipe sizes
            if (Math.Abs(sleeve - 3.25) < 0.01) sleeve = 3.5;
            if (Math.Abs(sleeve - 4.5) < 0.01) sleeve = 5.0;

            return sleeve;
        }

        // ══════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════

        private double GetPipeDiameter(Element pipe)
        {
            // Try Diameter parameter (internal units = feet)
            var param = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (param != null && param.HasValue)
                return param.AsDouble();

            // Fallback to named parameter
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

        private bool BoundingBoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            // Add small tolerance (0.5 ft)
            const double tol = 0.5;
            return a.Min.X - tol <= b.Max.X && a.Max.X + tol >= b.Min.X
                && a.Min.Y - tol <= b.Max.Y && a.Max.Y + tol >= b.Min.Y
                && a.Min.Z - tol <= b.Max.Z && a.Max.Z + tol >= b.Min.Z;
        }

        private BoundingBoxXYZ ComputeCombinedBoundingBox(List<Solid> solids)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (var solid in solids)
            {
                BoundingBoxXYZ bb = solid.GetBoundingBox();
                if (bb == null) continue;

                // The solid BB might be in its own coordinate system,
                // but since we already transformed the solid, use edge vertices
                foreach (Edge edge in solid.Edges)
                {
                    var pts = edge.Tessellate();
                    foreach (var pt in pts)
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

            if (minX > maxX) return null; // No geometry

            var result = new BoundingBoxXYZ();
            result.Min = new XYZ(minX, minY, minZ);
            result.Max = new XYZ(maxX, maxY, maxZ);
            return result;
        }

        /// <summary>
        /// Sets a numeric parameter (Length type stored in feet, or double).
        /// Falls back to string if the parameter is a string type.
        /// </summary>
        private void SetSleeveParam(Element elem, string paramName, double value)
        {
            var param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (param.StorageType == StorageType.Double)
            {
                param.Set(value);
            }
            else if (param.StorageType == StorageType.String)
            {
                param.Set(value.ToString("F2"));
            }
            else if (param.StorageType == StorageType.Integer)
            {
                param.Set((int)Math.Round(value));
            }
        }

        /// <summary>
        /// Sets a string parameter by name, silently skipping if not found.
        /// </summary>
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
