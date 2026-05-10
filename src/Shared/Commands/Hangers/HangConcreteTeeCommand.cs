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
    /// Places pipe hangers on the sides of concrete double tee stems at
    /// user-marked detail line locations.
    ///
    /// The user draws detail lines across pipe runs, parallel to and near
    /// tee stem locations. For each detail line × pipe intersection the
    /// command finds the closest tee stem, offsets from its vertical face,
    /// and places a hanger anchored to the side of the stem.
    ///
    /// WORKFLOW:
    ///   1. User draws detail lines across pipes near tee stems
    ///   2. User selects BOTH pipes AND detail lines
    ///   3. Dialog: pipe filter, hanger family/type, stem offset/anchor settings
    ///   4. Find linked structural model containing double tees
    ///   5. Extract vertical stem surfaces from tee elements
    ///   6. For each detail line × pipe intersection:
    ///      a. Find closest stem surface
    ///      b. Offset from stem face by rod offset distance
    ///      c. Compute anchor point on stem side at specified height above bottom
    ///      d. Calculate rod length as horizontal distance from pipe to stem face
    ///   7. Place hangers, rotate to pipe direction, write parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangConcreteTeeCommand : IExternalCommand
    {
        /// <summary>Hanger families must contain this pattern.</summary>
        private static readonly string[] HangerFamilyPatterns = new[]
        {
            "-Pipe Hanger",
            "Ring Hanger",
            "-Basic Adjustable"
        };

        /// <summary>
        /// Structural families that represent double tee stems.
        /// </summary>
        private static readonly string[] DoubleTeePatterns = new[]
        {
            "DOUBLE_TEE", "DOUBLE TEE", "DBL_TEE", "DBL TEE"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Select pipes AND detail lines ──
                IList<Reference> selRefs;
                try
                {
                    selRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new PipesAndLinesFilter(),
                        "Select PIPES and DETAIL LINES near tee stems, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (selRefs == null || selRefs.Count == 0)
                {
                    TaskDialog.Show("Concrete Tee Hang", "No elements selected.");
                    return Result.Cancelled;
                }

                // Separate pipes from detail lines
                var pipes = new List<Element>();
                var detailLines = new List<Element>();

                foreach (Reference r in selRefs)
                {
                    Element elem = doc.GetElement(r);
                    if (elem == null) continue;

                    if (elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves)
                        pipes.Add(elem);
                    else if (elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Lines)
                        detailLines.Add(elem);
                }

                if (pipes.Count == 0)
                {
                    TaskDialog.Show("Concrete Tee Hang", "No pipes selected. Select both pipes AND detail lines.");
                    return Result.Cancelled;
                }
                if (detailLines.Count == 0)
                {
                    TaskDialog.Show("Concrete Tee Hang",
                        "No detail lines selected.\n\nDraw detail lines across pipes near tee stems, " +
                        "then select both the pipes and the detail lines.");
                    return Result.Cancelled;
                }

                // ── Step 2: Get hanger families and pipe types for dialog ──
                IList<string> hangerFamilies = GetHangerFamilyNames(doc);
                if (hangerFamilies.Count == 0)
                {
                    TaskDialog.Show("Concrete Tee Hang", "No pipe hanger families loaded in this project.");
                    return Result.Failed;
                }
                IList<string> pipeTypeNames = GetPipeTypeNames(doc);

                // ── Step 3: Show dialog ──
                using (var dialog = new HangConcreteTeeDialog(
                    pipes.Count, detailLines.Count, hangerFamilies, pipeTypeNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // Apply pipe type filter
                    if (dialog.PipeTypeFilter != "ALL Pipes")
                    {
                        pipes = pipes.Where(p =>
                        {
                            string tn = ParameterHelpers.GetParamValueAsString(p, "Type Name");
                            string ft = ParameterHelpers.GetParamValueAsString(p, "Family and Type");
                            return tn.IndexOf(dialog.PipeTypeFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   ft.IndexOf(dialog.PipeTypeFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        }).ToList();

                        if (pipes.Count == 0)
                        {
                            TaskDialog.Show("Concrete Tee Hang", "No pipes match the selected type filter.");
                            return Result.Cancelled;
                        }
                    }

                    // Find hanger family symbol
                    FamilySymbol hangerType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (hangerType == null)
                    {
                        TaskDialog.Show("Concrete Tee Hang",
                            $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    // ── Step 4: Find linked double tee model ──
                    double rodOffsetFt = dialog.RodOffsetFromStemInches / 12.0;
                    double anchorAboveFt = dialog.AnchorAboveBottomInches / 12.0;
                    string linkKeyword = dialog.LinkedModelKeyword;

                    var stemSurfaces = GetStemSurfacesFromLinks(doc, linkKeyword);
                    if (stemSurfaces.Count == 0)
                    {
                        TaskDialog.Show("Concrete Tee Hang",
                            $"No double tee stem surfaces found.\n\n" +
                            $"Searched linked models for keyword '{linkKeyword}'.\n" +
                            "Make sure the linked structural model is loaded and contains " +
                            "structural framing families with double tee stems.");
                        return Result.Failed;
                    }

                    // ── Step 5: Find detail line × pipe intersections ──
                    var intersections = FindDetailLinePipeIntersections(doc, detailLines, pipes);
                    if (intersections.Count == 0)
                    {
                        TaskDialog.Show("Concrete Tee Hang",
                            "No intersections found between detail lines and pipes.\n\n" +
                            "Make sure the detail lines cross the pipes in plan view.");
                        return Result.Cancelled;
                    }

                    // ── Step 6: Place hangers ──
                    int hangersPlaced = 0;
                    int missedCount = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Hang Concrete Tee Stems"))
                    {
                        if (!hangerType.IsActive)
                            hangerType.Activate();

                        doc.Regenerate();

                        foreach (var loc in intersections)
                        {
                            Element pipe = loc.Pipe;
                            XYZ pipePoint = loc.Point;

                            // Get pipe direction
                            Line pipeLine = GetPipeCenterline(pipe);
                            if (pipeLine == null) continue;
                            XYZ pipeDir = (pipeLine.GetEndPoint(1) - pipeLine.GetEndPoint(0)).Normalize();

                            // Get pipe diameter
                            double pipeDiameter = ParameterHelpers.GetPipeDiameterValue(pipe);

                            // Get reference level
                            ElementId levelId = pipe.LookupParameter("Reference Level")
                                ?.AsElementId() ?? pipe.LevelId;
                            Level level = doc.GetElement(levelId) as Level;
                            if (level == null) continue;

                            // Find closest stem surface to this pipe point
                            var stemResult = FindClosestStemSurface(pipePoint, stemSurfaces);
                            if (stemResult == null)
                            {
                                missedCount++;
                                continue;
                            }

                            // Compute hanger placement point on the stem side
                            XYZ hangerPoint = ComputeStemSidePoint(
                                pipePoint, pipeLine, stemResult, rodOffsetFt, anchorAboveFt);

                            if (hangerPoint == null)
                            {
                                missedCount++;
                                continue;
                            }

                            // Rod length = horizontal distance from pipe center to stem face
                            double rodLength = ComputeRodLength(pipePoint, stemResult.ClosestPoint, rodOffsetFt);

                            // Pipe rotation angle
                            double pipeAngle = Math.Atan2(pipeDir.Y, pipeDir.X);

                            // Place hanger at the pipe's intersection point (on the pipe)
                            FamilyInstance hanger = doc.Create.NewFamilyInstance(
                                pipePoint, hangerType, level,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                            if (hanger == null)
                            {
                                missedCount++;
                                continue;
                            }

                            // Rotate to match pipe direction
                            Line rotAxis = Line.CreateBound(
                                pipePoint,
                                new XYZ(pipePoint.X, pipePoint.Y, pipePoint.Z + 1));
                            ElementTransformUtils.RotateElement(doc, hanger.Id, rotAxis, pipeAngle);

                            // ── Write parameters ──
                            SetParamSafe(hanger, "Nominal Diameter", pipeDiameter);
                            SetParamSafe(hanger, "Rod Length", Math.Round(rodLength, 4));

                            double elevFromLevel = pipePoint.Z - level.Elevation;
                            SetParamSafe(hanger, "Elevation from Level", elevFromLevel);

                            SetParamSafe(hanger, "Type Code (Hydratec)", dialog.HangerTypeCode);
                            SetParamSafe(hanger, "Additional Stocklist Information (Hydratec)",
                                "CON1," + pipe.Id.ToString());
                            SetParamSafe(hanger, "C Clamp",
                                "CON1," + Math.Round(pipeDiameter * 12, 3).ToString());
                            SetParamSafe(hanger, "Comments", "Concrete Tee Stem");

                            hangersPlaced++;
                        }

                        tw.Commit();
                    }

                    // Summary
                    string summary = $"Placed {hangersPlaced} hanger{(hangersPlaced != 1 ? "s" : "")} " +
                                     $"on concrete tee stems.\n\n" +
                                     $"Detail lines: {detailLines.Count}\n" +
                                     $"Pipe intersections found: {intersections.Count}\n" +
                                     $"Stem surfaces available: {stemSurfaces.Count}";
                    if (missedCount > 0)
                        summary += $"\n\nMissed: {missedCount} (no stem surface found nearby)";

                    TaskDialog.Show("Concrete Tee Hang — Complete", summary);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  LINKED MODEL — STEM SURFACE EXTRACTION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// A vertical stem surface from a linked double tee element.
        /// Stores the face center, normal, and bounding Z range.
        /// </summary>
        private class StemSurface
        {
            /// <summary>Center point of the stem face (in host coordinates).</summary>
            public XYZ Center { get; set; }
            /// <summary>Outward normal of the vertical face.</summary>
            public XYZ Normal { get; set; }
            /// <summary>Bottom Z of the stem face.</summary>
            public double BottomZ { get; set; }
            /// <summary>Top Z of the stem face.</summary>
            public double TopZ { get; set; }
            /// <summary>The planar face geometry (in host coordinates).</summary>
            public XYZ PointA { get; set; }
            /// <summary>Second sample point for the stem center line.</summary>
            public XYZ PointB { get; set; }
            /// <summary>Closest point to the query (set during search).</summary>
            public XYZ ClosestPoint { get; set; }
            /// <summary>Distance to the query point (set during search).</summary>
            public double Distance { get; set; }
        }

        /// <summary>
        /// Searches all loaded Revit links for structural framing elements matching
        /// the keyword, extracts their vertical faces (tee stems), and returns them
        /// in host model coordinates.
        /// </summary>
        private List<StemSurface> GetStemSurfacesFromLinks(Document doc, string linkKeyword)
        {
            var results = new List<StemSurface>();

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            foreach (RevitLinkInstance linkInst in links)
            {
                Document linkDoc = linkInst.GetLinkDocument();
                if (linkDoc == null) continue;

                // Check if link name matches keyword
                string linkName = linkDoc.Title ?? "";
                if (!ContainsAny(linkName, new[] { linkKeyword }, true))
                    continue;

                Transform linkTransform = linkInst.GetTotalTransform();

                // Get structural framing elements that are double tees
                var structFraming = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (Element elem in structFraming)
                {
                    string familyName = GetFamilyName(elem);
                    if (!ContainsAny(familyName, DoubleTeePatterns, true))
                        continue;

                    // Extract vertical faces from this element
                    var faces = GetVerticalFaces(elem, linkTransform);
                    results.AddRange(faces);
                }
            }

            return results;
        }

        /// <summary>
        /// Extract vertical faces (stem sides) from a structural element.
        /// Only returns faces whose normal is approximately horizontal (vertical surfaces).
        /// </summary>
        private List<StemSurface> GetVerticalFaces(Element elem, Transform linkTransform)
        {
            var results = new List<StemSurface>();

            Options options = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geom = elem.get_Geometry(options);
            if (geom == null) return results;

            foreach (GeometryObject gObj in geom)
            {
                Solid solid = gObj as Solid;
                if (solid == null)
                {
                    // Handle geometry instances (transformed geometry)
                    GeometryInstance gInst = gObj as GeometryInstance;
                    if (gInst != null)
                    {
                        foreach (GeometryObject innerObj in gInst.GetInstanceGeometry())
                        {
                            solid = innerObj as Solid;
                            if (solid != null && solid.Faces.Size > 0)
                                ExtractVerticalFacesFromSolid(solid, linkTransform, results);
                        }
                    }
                    continue;
                }

                if (solid.Faces.Size > 0)
                    ExtractVerticalFacesFromSolid(solid, linkTransform, results);
            }

            return results;
        }

        /// <summary>
        /// Extract vertical faces from a solid. A face is "vertical" if its
        /// outward normal has a Z component near zero (within tolerance).
        /// </summary>
        private void ExtractVerticalFacesFromSolid(
            Solid solid, Transform linkTransform, List<StemSurface> results)
        {
            const double verticalTolerance = 0.1; // max Z component of normal

            foreach (Face face in solid.Faces)
            {
                PlanarFace planar = face as PlanarFace;
                if (planar == null) continue;

                // Transform normal to host coordinates
                XYZ normalInHost = linkTransform.OfVector(planar.FaceNormal);

                // Check if face is approximately vertical
                if (Math.Abs(normalInHost.Z) > verticalTolerance) continue;

                // Get face bounding box for Z range
                BoundingBoxUV bbUV = face.GetBoundingBox();

                // Sample points at 20% and 80% along V parameter
                double uMid = (bbUV.Min.U + bbUV.Max.U) / 2.0;
                double v20 = bbUV.Min.V + (bbUV.Max.V - bbUV.Min.V) * 0.2;
                double v80 = bbUV.Min.V + (bbUV.Max.V - bbUV.Min.V) * 0.8;

                XYZ ptA = linkTransform.OfPoint(face.Evaluate(new UV(uMid, v20)));
                XYZ ptB = linkTransform.OfPoint(face.Evaluate(new UV(uMid, v80)));

                // Get center and Z range
                XYZ center = linkTransform.OfPoint(
                    face.Evaluate(new UV(
                        (bbUV.Min.U + bbUV.Max.U) / 2.0,
                        (bbUV.Min.V + bbUV.Max.V) / 2.0)));

                // Compute Z range from corner points
                double minZ = double.MaxValue;
                double maxZ = double.MinValue;
                UV[] corners = new UV[]
                {
                    new UV(bbUV.Min.U, bbUV.Min.V),
                    new UV(bbUV.Max.U, bbUV.Min.V),
                    new UV(bbUV.Min.U, bbUV.Max.V),
                    new UV(bbUV.Max.U, bbUV.Max.V)
                };
                foreach (UV uv in corners)
                {
                    XYZ pt = linkTransform.OfPoint(face.Evaluate(uv));
                    if (pt.Z < minZ) minZ = pt.Z;
                    if (pt.Z > maxZ) maxZ = pt.Z;
                }

                results.Add(new StemSurface
                {
                    Center = center,
                    Normal = normalInHost.Normalize(),
                    BottomZ = minZ,
                    TopZ = maxZ,
                    PointA = ptA,
                    PointB = ptB
                });
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  INTERSECTION FINDING
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Data class for a detail line × pipe intersection.
        /// </summary>
        private class HangerLocation
        {
            public Element Pipe { get; set; }
            public XYZ Point { get; set; }
        }

        /// <summary>
        /// Find all intersection points between detail lines and pipes in plan (2D XY).
        /// Each crossing produces a hanger location at the pipe's Z elevation.
        /// </summary>
        private List<HangerLocation> FindDetailLinePipeIntersections(
            Document doc, List<Element> detailLines, List<Element> pipes)
        {
            var results = new List<HangerLocation>();

            foreach (Element detailLine in detailLines)
            {
                Curve lineCurve = (detailLine.Location as LocationCurve)?.Curve;
                if (lineCurve == null) continue;

                XYZ lineStart = lineCurve.GetEndPoint(0);
                XYZ lineEnd = lineCurve.GetEndPoint(1);

                // Flatten to 2D
                XYZ ls2D = new XYZ(lineStart.X, lineStart.Y, 0);
                XYZ le2D = new XYZ(lineEnd.X, lineEnd.Y, 0);

                foreach (Element pipe in pipes)
                {
                    Curve pipeCurve = (pipe.Location as LocationCurve)?.Curve;
                    if (pipeCurve == null) continue;

                    XYZ pipeStart = pipeCurve.GetEndPoint(0);
                    XYZ pipeEnd = pipeCurve.GetEndPoint(1);

                    // Flatten pipe to 2D
                    XYZ ps2D = new XYZ(pipeStart.X, pipeStart.Y, 0);
                    XYZ pe2D = new XYZ(pipeEnd.X, pipeEnd.Y, 0);

                    XYZ intersection = LineLineIntersection2D(ls2D, le2D, ps2D, pe2D);
                    if (intersection != null)
                    {
                        // Project intersection onto pipe curve to get correct Z
                        double pipeZ = (pipeStart.Z + pipeEnd.Z) / 2.0;
                        IntersectionResult proj = pipeCurve.Project(
                            new XYZ(intersection.X, intersection.Y, pipeZ));
                        XYZ hangerPt = proj != null ? proj.XYZPoint :
                            new XYZ(intersection.X, intersection.Y, pipeZ);

                        results.Add(new HangerLocation { Pipe = pipe, Point = hangerPt });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 2D line-line intersection using parametric form.
        /// Returns the intersection point if the lines cross within both segments, null otherwise.
        /// </summary>
        private XYZ LineLineIntersection2D(XYZ a1, XYZ a2, XYZ b1, XYZ b2)
        {
            double dx1 = a2.X - a1.X;
            double dy1 = a2.Y - a1.Y;
            double dx2 = b2.X - b1.X;
            double dy2 = b2.Y - b1.Y;

            double denom = dx1 * dy2 - dy1 * dx2;
            if (Math.Abs(denom) < 1e-10) return null; // parallel

            double t = ((b1.X - a1.X) * dy2 - (b1.Y - a1.Y) * dx2) / denom;
            double u = ((b1.X - a1.X) * dy1 - (b1.Y - a1.Y) * dx1) / denom;

            // Allow small overshoot for lines near pipe ends
            double tolerance = 0.01;
            if (t < -tolerance || t > 1 + tolerance || u < -tolerance || u > 1 + tolerance)
                return null;

            return new XYZ(a1.X + t * dx1, a1.Y + t * dy1, 0);
        }

        // ══════════════════════════════════════════════════════════════
        //  STEM SURFACE MATCHING
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Find the closest stem surface to a given point.
        /// Only considers stems whose Z range overlaps with the point's Z ± search distance.
        /// </summary>
        private StemSurface FindClosestStemSurface(XYZ point, List<StemSurface> stems)
        {
            const double maxSearchDist = 10.0; // max horizontal distance to search (feet)
            const double zSearchDist = 5.0;    // vertical overlap tolerance (feet)

            StemSurface closest = null;
            double minDist = double.MaxValue;

            foreach (var stem in stems)
            {
                // Check Z overlap — the stem must be at or above the pipe
                if (stem.TopZ < point.Z - zSearchDist) continue;
                if (stem.BottomZ > point.Z + zSearchDist) continue;

                // Horizontal distance from point to stem center line
                double dist = HorizontalDistanceToStemLine(point, stem);
                if (dist < minDist && dist < maxSearchDist)
                {
                    minDist = dist;
                    closest = stem;
                    closest.Distance = dist;
                    closest.ClosestPoint = ClosestPointOnStemLine(point, stem);
                }
            }

            return closest;
        }

        /// <summary>
        /// Compute horizontal distance from a point to the stem center line (A→B).
        /// </summary>
        private double HorizontalDistanceToStemLine(XYZ point, StemSurface stem)
        {
            // Flatten to 2D
            XYZ p = new XYZ(point.X, point.Y, 0);
            XYZ a = new XYZ(stem.PointA.X, stem.PointA.Y, 0);
            XYZ b = new XYZ(stem.PointB.X, stem.PointB.Y, 0);

            XYZ ab = b - a;
            double len = ab.GetLength();
            if (len < 1e-10) return p.DistanceTo(a);

            // Project p onto line segment AB
            double t = (p - a).DotProduct(ab) / (len * len);
            t = Math.Max(0, Math.Min(1, t));
            XYZ proj = a + t * ab;
            return p.DistanceTo(proj);
        }

        /// <summary>
        /// Get the closest point on the stem center line (A→B) to the given point.
        /// Returns the 3D point on the stem at the pipe's Z.
        /// </summary>
        private XYZ ClosestPointOnStemLine(XYZ point, StemSurface stem)
        {
            XYZ a = stem.PointA;
            XYZ b = stem.PointB;
            XYZ ab = b - a;
            double len = ab.GetLength();
            if (len < 1e-10) return a;

            double t = (point - a).DotProduct(ab) / (len * len);
            t = Math.Max(0, Math.Min(1, t));
            XYZ proj = a + t * ab;

            // Return at the pipe's Z elevation
            return new XYZ(proj.X, proj.Y, point.Z);
        }

        // ══════════════════════════════════════════════════════════════
        //  HANGER POINT COMPUTATION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Compute the hanger placement point on the stem side.
        ///
        /// The hanger goes on the pipe at the intersection point. The rod goes
        /// horizontally from the hanger to the stem face. The rod length is the
        /// horizontal distance from pipe center to the stem face minus the offset.
        /// </summary>
        private XYZ ComputeStemSidePoint(
            XYZ pipePoint, Line pipeLine, StemSurface stem,
            double rodOffsetFt, double anchorAboveFt)
        {
            // The hanger is placed at the pipe intersection point.
            // The stem provides the rod length calculation.
            // The anchor point on the stem is offset from the face and above the bottom.

            // Direction from pipe to stem (horizontal)
            XYZ stemPt = stem.ClosestPoint;
            XYZ toStem = new XYZ(stemPt.X - pipePoint.X, stemPt.Y - pipePoint.Y, 0);
            double dist = toStem.GetLength();
            if (dist < 1e-10) return pipePoint;

            return pipePoint;
        }

        /// <summary>
        /// Compute rod length from pipe center to the stem face.
        /// This is the horizontal distance minus the rod offset.
        /// </summary>
        private double ComputeRodLength(XYZ pipePoint, XYZ stemPoint, double rodOffsetFt)
        {
            // Horizontal distance from pipe center to stem center line
            double dx = stemPoint.X - pipePoint.X;
            double dy = stemPoint.Y - pipePoint.Y;
            double horizontalDist = Math.Sqrt(dx * dx + dy * dy);

            // Rod length = horizontal distance + offset from stem face
            // (the rod extends from the hanger at the pipe outward to the stem)
            return Math.Max(0, horizontalDist + rodOffsetFt);
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Get the centerline curve of a pipe element.
        /// </summary>
        private Line GetPipeCenterline(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc?.Curve as Line;
        }

        /// <summary>
        /// Get the family name of an element.
        /// </summary>
        private string GetFamilyName(Element elem)
        {
            if (elem is FamilyInstance fi)
                return fi.Symbol?.Family?.Name ?? "";
            return "";
        }

        /// <summary>
        /// Check if a string contains any of the patterns (case-insensitive option).
        /// </summary>
        private bool ContainsAny(string source, string[] patterns, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(source)) return false;
            string s = ignoreCase ? source.ToUpperInvariant() : source;
            foreach (var p in patterns)
            {
                string pat = ignoreCase ? p.ToUpperInvariant() : p;
                if (s.Contains(pat)) return true;
            }
            return false;
        }

        /// <summary>
        /// Get all pipe hanger family names from the document.
        /// </summary>
        private IList<string> GetHangerFamilyNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategoryId?.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                .Where(f =>
                {
                    string name = f.Name ?? "";
                    return HangerFamilyPatterns.Any(p =>
                        name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                })
                .Select(f => f.Name)
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>
        /// Get distinct pipe type names from the document.
        /// </summary>
        private IList<string> GetPipeTypeNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType))
                .Select(e => e.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>
        /// Find the first FamilySymbol for a hanger family by name.
        /// </summary>
        private FamilySymbol FindHangerFamilyType(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    fs.Family?.Name == familyName &&
                    fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory);
        }

        /// <summary>
        /// Safe parameter setter — silently skips if parameter doesn't exist.
        /// </summary>
        private void SetParamSafe(Element elem, string paramName, double value)
        {
            ParameterHelpers.SetParamValue(elem, paramName, value);
        }

        private void SetParamSafe(Element elem, string paramName, string value)
        {
            ParameterHelpers.SetParamValue(elem, paramName, value);
        }

        /// <summary>
        /// Selection filter allowing pipes and detail lines.
        /// </summary>
        private class PipesAndLinesFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem.Category == null) return false;
                int catId = elem.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_PipeCurves ||
                       catId == (int)BuiltInCategory.OST_Lines;
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}

