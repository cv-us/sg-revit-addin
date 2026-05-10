using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Automatically places pipe sleeve family instances at every intersection between
    /// user-selected pipes and floors/stairs/roofs from a linked Revit model.
    /// Sleeves are sized per NFPA annular clearance rules. Sleeve length matches
    /// the penetrated deck thickness (or extends 2" above for wet areas).
    ///
    /// WORKFLOW:
    ///   1. User selects linked model and sleeve length option (same/extend)
    ///   2. User selects pipes
    ///   3. Command finds pipe-deck intersections in linked model
    ///   4. Places sized sleeves with metadata parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipeSleevesAtDecksCommand : IExternalCommand
    {
        private const string SleeveFamily = "-Pipe Sleeve-Deck";
        private const string SleeveType = "Standard";
        private const double WetAreaExtensionFeet = 2.0 / 12.0; // 2 inches in feet

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
                    TaskDialog.Show("Deck Sleeves",
                        "No loaded Revit links found.\n\n" +
                        "This command requires a linked model containing floors, stairs, or roofs.");
                    return Result.Cancelled;
                }

                var linkNames = linkInstances.Select(li => li.Name).ToList();

                // ── Step 2: Show dialog ──
                var dialog = new PipeSleevesAtDecksDialog(linkNames);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                // ── Step 3: Verify sleeve family is loaded ──
                FamilySymbol sleeveSymbol = FindSleeveSymbol(doc);
                if (sleeveSymbol == null)
                {
                    TaskDialog.Show("Deck Sleeves",
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
                    TaskDialog.Show("Deck Sleeves", "No pipes selected.");
                    return Result.Cancelled;
                }

                // ── Step 5: Collect linked floor/roof/stair solids ──
                var structLink = linkInstances[dialog.SelectedLinkIndex];
                var deckDataList = CollectLinkedDecks(structLink);

                if (deckDataList.Count == 0)
                {
                    TaskDialog.Show("Deck Sleeves",
                        "No floors, stairs, or roofs found in the selected linked model.");
                    return Result.Cancelled;
                }

                // ── Step 6: Collect host model levels for reference level assignment ──
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.ProjectElevation)
                    .ToList();

                // ── Step 7: Find intersections and place sleeves ──
                int sleevesPlaced = 0;
                bool extendWet = dialog.ExtendForWetAreas;

                using (var tw = new TransactionWrapper(doc, "Insert Pipe Sleeves at Decks"))
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

                        // Get pipe diameter
                        double pipeDiaFeet = GetPipeDiameter(pipeElem);
                        double pipeDiaInches = Math.Round(pipeDiaFeet * 12.0, 2);

                        // Get pipe bounding box
                        BoundingBoxXYZ pipeBB = pipeElem.get_BoundingBox(null);

                        foreach (var deck in deckDataList)
                        {
                            // Coarse bounding box check
                            if (pipeBB != null && deck.BoundingBox != null)
                            {
                                if (!BoundingBoxesOverlap(pipeBB, deck.BoundingBox))
                                    continue;
                            }

                            // Precise intersection: find where pipe passes through deck
                            var intersection = FindPipeDeckIntersection(pipeCurve, deck.Solids);
                            if (intersection == null) continue;

                            XYZ placementPoint = intersection.Value.MidPoint;
                            double deckThicknessFeet = intersection.Value.Thickness;
                            double deckThicknessInches = deckThicknessFeet * 12.0;

                            // Compute sleeve diameter per NFPA
                            double sleeveDiaInches = ComputeSleeveDiameter(pipeDiaInches);

                            // Compute sleeve length
                            double sleeveLengthFeet = deckThicknessFeet;
                            if (extendWet)
                                sleeveLengthFeet += WetAreaExtensionFeet;

                            // Find reference level (nearest level at or below intersection Z)
                            Level refLevel = FindReferenceLevel(levels, placementPoint.Z);
                            double levelElev = refLevel?.ProjectElevation ?? 0;
                            double elevFromLevel = placementPoint.Z - levelElev;

                            // Place sleeve instance
                            FamilyInstance sleeve;
                            if (refLevel != null)
                            {
                                sleeve = doc.Create.NewFamilyInstance(
                                    new XYZ(placementPoint.X, placementPoint.Y, elevFromLevel),
                                    sleeveSymbol, refLevel, StructuralType.NonStructural);
                            }
                            else
                            {
                                sleeve = doc.Create.NewFamilyInstance(
                                    placementPoint, sleeveSymbol, StructuralType.NonStructural);
                            }

                            if (sleeve == null) continue;

                            // Set parameters
                            SetSleeveParam(sleeve, "Sleeve Pipe Size", sleeveDiaInches);
                            SetSleeveParam(sleeve, "Sleeve Pipe Length", sleeveLengthFeet);
                            SetParamSafe(sleeve, "Comments", deck.TypeName);
                            SetSleeveParam(sleeve, "Deck Thickness", deckThicknessInches);
                            SetSleeveParam(sleeve, "Elevation from Level", elevFromLevel);

                            // Format elevation datum string
                            string datumStr = FormatElevationDecimal(placementPoint.Z);
                            SetParamSafe(sleeve, "Sleeve Elevation DATUM", datumStr);

                            // Set reference level name
                            if (refLevel != null)
                                SetParamSafe(sleeve, "Reference Level", refLevel.Name);

                            sleevesPlaced++;
                        }
                    }

                    tw.Commit();
                }

                // ── Summary ──
                if (sleevesPlaced > 0)
                {
                    TaskDialog.Show("Deck Sleeves",
                        $"A total of {sleevesPlaced} deck sleeve(s) were added.\n\n" +
                        $"Sleeve length: {(extendWet ? "Deck thickness + 2\" (wet areas)" : "Same as deck thickness")}\n\n" +
                        $"Parameters set:\n" +
                        $"  Sleeve Pipe Size, Sleeve Pipe Length\n" +
                        $"  Comments (deck type), Deck Thickness\n" +
                        $"  Sleeve Elevation DATUM, Reference Level\n" +
                        $"  Elevation from Level");
                }
                else
                {
                    TaskDialog.Show("Deck Sleeves",
                        "No deck sleeves were added.\n\n" +
                        "Verify that selected pipes actually intersect floors/roofs in the linked model.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Pipe Sleeves at Decks failed:\n" + ex.Message);
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
        //  Linked Deck Collection
        // ══════════════════════════════════════════════════════════

        private class DeckData
        {
            public List<Solid> Solids { get; set; }
            public BoundingBoxXYZ BoundingBox { get; set; }
            public string TypeName { get; set; }
        }

        private List<DeckData> CollectLinkedDecks(RevitLinkInstance linkInstance)
        {
            var result = new List<DeckData>();
            Document linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return result;

            Transform transform = linkInstance.GetTotalTransform();
            var opts = new Options { DetailLevel = ViewDetailLevel.Medium };

            // Collect Floors, Stairs, and Roofs
            var categories = new[]
            {
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Roofs
            };

            foreach (var cat in categories)
            {
                var elems = new FilteredElementCollector(linkedDoc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToElements();

                foreach (var elem in elems)
                {
                    var solids = new List<Solid>();
                    GeometryElement geom = elem.get_Geometry(opts);
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

                    BoundingBoxXYZ bb = ComputeSolidsBoundingBox(solids);
                    string typeName = elem.Name ?? "";

                    result.Add(new DeckData
                    {
                        Solids = solids,
                        BoundingBox = bb,
                        TypeName = typeName
                    });
                }
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

        private struct IntersectionData
        {
            public XYZ MidPoint;
            public double Thickness;
        }

        /// <summary>
        /// Finds where a pipe curve passes through a deck solid.
        /// Returns the midpoint between entry and exit, and the deck thickness
        /// (vertical distance through the slab).
        /// </summary>
        private IntersectionData? FindPipeDeckIntersection(Curve pipeCurve, List<Solid> deckSolids)
        {
            // Extend pipe curve to handle edge cases
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

            foreach (var solid in deckSolids)
            {
                foreach (Face face in solid.Faces)
                {
                    try
                    {
                        // For deck intersections, we check all faces (both horizontal top/bottom)
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
            {
                // Single intersection — use a nominal thickness
                return new IntersectionData
                {
                    MidPoint = hitPoints[0],
                    Thickness = 0.5 // Default 6" if can't determine
                };
            }

            // Multiple hits: find the entry/exit pair with greatest Z separation
            // (top and bottom of the deck slab)
            double minZ = hitPoints.Min(p => p.Z);
            double maxZ = hitPoints.Max(p => p.Z);

            // Find points at min and max Z
            XYZ bottomPoint = hitPoints.OrderBy(p => p.Z).First();
            XYZ topPoint = hitPoints.OrderByDescending(p => p.Z).First();

            double thickness = maxZ - minZ;

            // If thickness is too small (nearly horizontal intersection), use distance instead
            if (thickness < 0.01)
            {
                // Pipe is nearly horizontal through the deck — use max distance between hits
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

                thickness = maxDist;
                return new IntersectionData
                {
                    MidPoint = new XYZ(
                        (best1.X + best2.X) / 2.0,
                        (best1.Y + best2.Y) / 2.0,
                        (best1.Z + best2.Z) / 2.0),
                    Thickness = thickness
                };
            }

            // Standard case: midpoint between top and bottom
            return new IntersectionData
            {
                MidPoint = new XYZ(
                    (bottomPoint.X + topPoint.X) / 2.0,
                    (bottomPoint.Y + topPoint.Y) / 2.0,
                    (bottomPoint.Z + topPoint.Z) / 2.0),
                Thickness = thickness
            };
        }

        // ══════════════════════════════════════════════════════════
        //  NFPA Sleeve Sizing
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// Computes the sleeve diameter based on NFPA annular clearance rules.
        /// Input and output are in inches.
        ///
        /// Logic:
        ///   1. Snap pipe dia: 3.25" → 3.5", 4.5" → 5" (standard sizes)
        ///   2. Add clearance: pipe &lt; 3.5" gets +2", pipe >= 3.5" gets +4"
        /// </summary>
        private double ComputeSleeveDiameter(double pipeDiaInches)
        {
            double dia = pipeDiaInches;

            // Step 1: Snap to standard pipe sizes
            if (Math.Abs(dia - 3.25) < 0.01) dia = 3.5;
            if (Math.Abs(dia - 4.5) < 0.01) dia = 5.0;

            // Step 2: Add annular clearance
            double sleeve = dia < 3.5 ? dia + 2.0 : dia + 4.0;

            return sleeve;
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

        /// <summary>
        /// Finds the nearest level at or below the given Z elevation.
        /// </summary>
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

        private string FormatElevationDecimal(double elevationFeet)
        {
            string sign = elevationFeet >= 0 ? "+" : "-";
            return $"{sign}{Math.Abs(elevationFeet):F2}'";
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

