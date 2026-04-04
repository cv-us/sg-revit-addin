using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Calculates and populates AFF and BBD (TOS) elevation parameters on pipe sleeves
    /// by finding geometric intersections with linked architectural floors (AFF) and
    /// linked structural decks (BBD).
    ///
    /// Migrated from: "AutoInsert - Pipe Sleeve Elevations.dyn"
    ///
    /// WORKFLOW:
    ///   1. User picks architectural link (for AFF) and structural link (for BBD)
    ///   2. User selects pipe sleeve family instances
    ///   3. For each sleeve:
    ///      a. Cast vertical line downward → intersect linked floor surfaces → AFF
    ///      b. Cast vertical line upward → intersect linked deck surfaces → BBD
    ///      c. Format elevation strings
    ///      d. Write PipeElevationAFF, PipeElevationTOS, Sleeve Elevation AFF, Sleeve Elevation DATUM
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InsertSleeveElevationsCommand : IExternalCommand
    {
        private const double SearchDistance = 50.0; // feet above/below sleeve to search

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Collect loaded Revit link instances ──
                var linkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .Where(li => li.GetLinkDocument() != null)
                    .ToList();

                if (linkInstances.Count == 0)
                {
                    TaskDialog.Show("Sleeve Elevations",
                        "No loaded Revit links found.\n\n" +
                        "This command requires linked architectural and/or structural models " +
                        "containing floors and decks to compute elevations.");
                    return Result.Cancelled;
                }

                var linkNames = linkInstances.Select(li => li.Name).ToList();

                // ── Step 2: Show dialog ──
                var dialog = new InsertSleeveElevationsDialog(linkNames);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                // ── Step 3: Select sleeves ──
                IList<Reference> sleeveRefs;
                try
                {
                    var filter = new SleeveSelectionFilter();
                    sleeveRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element, filter,
                        "Select pipe sleeves to populate AFF & BBD elevations, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (sleeveRefs == null || sleeveRefs.Count == 0)
                {
                    TaskDialog.Show("Sleeve Elevations", "No sleeves selected.");
                    return Result.Cancelled;
                }

                // ── Step 4: Pre-collect linked floor/roof solids ──
                var affLink = linkInstances[dialog.AFFLinkIndex];
                var bbdLink = linkInstances[dialog.BBDLinkIndex];

                var affSolids = CollectLinkedFloorSolids(affLink);
                var bbdSolids = CollectLinkedFloorSolids(bbdLink);

                if (affSolids.Count == 0 && bbdSolids.Count == 0)
                {
                    TaskDialog.Show("Sleeve Elevations",
                        "No floor or roof geometry found in the selected linked models.");
                    return Result.Cancelled;
                }

                // ── Step 5: Process each sleeve ──
                int updated = 0;
                int skipped = 0;
                bool useDecimal = dialog.UseDecimalFeet;

                using (var tw = new TransactionWrapper(doc, "Insert Pipe Sleeve Elevations"))
                {
                    foreach (var r in sleeveRefs)
                    {
                        Element elem = doc.GetElement(r);
                        if (elem == null) { skipped++; continue; }

                        // Get sleeve center point
                        XYZ sleevePoint = GetSleeveLocation(elem);
                        if (sleevePoint == null) { skipped++; continue; }

                        double sleeveZ = sleevePoint.Z;

                        // Find floor below (AFF) — intersect upward-facing surfaces
                        double? floorTopZ = FindNearestSurface(
                            sleevePoint, affSolids, searchDown: true, upFacing: true);

                        // Find deck above (BBD) — intersect downward-facing surfaces
                        double? deckBottomZ = FindNearestSurface(
                            sleevePoint, bbdSolids, searchDown: false, upFacing: false);

                        // Calculate elevations
                        double? affElevation = floorTopZ.HasValue ? sleeveZ - floorTopZ.Value : null;
                        double? bbdElevation = deckBottomZ.HasValue ? deckBottomZ.Value - sleeveZ : null;

                        // Format strings
                        string affStr = FormatElevation(affElevation, "AFF", useDecimal);
                        string bbdStr = FormatElevation(bbdElevation, "BBD", useDecimal);

                        // Write parameters
                        SetParamSafe(elem, "PipeElevationAFF", affStr);
                        SetParamSafe(elem, "PipeElevationTOS", bbdStr);
                        SetParamSafe(elem, "Sleeve Elevation AFF", affStr);
                        SetParamSafe(elem, "Sleeve Elevation DATUM", bbdStr);

                        updated++;
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Sleeve Elevations",
                    $"Completed:\n" +
                    $"  {updated} sleeve(s) updated\n" +
                    (skipped > 0 ? $"  {skipped} element(s) skipped\n" : "") +
                    $"\nParameters written:\n" +
                    $"  PipeElevationAFF, PipeElevationTOS\n" +
                    $"  Sleeve Elevation AFF, Sleeve Elevation DATUM\n" +
                    $"\nFormat: {(useDecimal ? "Decimal Feet" : "Feet and Inches")}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Pipe Sleeve Elevations failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Collects all solid geometry from floors and roofs in a linked model,
        /// transformed to host document coordinates.
        /// </summary>
        private List<Solid> CollectLinkedFloorSolids(RevitLinkInstance linkInstance)
        {
            var solids = new List<Solid>();
            Document linkedDoc = linkInstance.GetLinkDocument();
            if (linkedDoc == null) return solids;

            Transform transform = linkInstance.GetTotalTransform();
            var opts = new Options { DetailLevel = ViewDetailLevel.Fine };

            // Collect floors
            var floors = new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements();

            // Collect roofs
            var roofs = new FilteredElementCollector(linkedDoc)
                .OfCategory(BuiltInCategory.OST_Roofs)
                .WhereElementIsNotElementType()
                .ToElements();

            var allElements = floors.Concat(roofs);

            foreach (var elem in allElements)
            {
                GeometryElement geom = elem.get_Geometry(opts);
                if (geom == null) continue;

                foreach (var solid in ExtractSolids(geom))
                {
                    if (solid.Volume > 0.0001)
                    {
                        try
                        {
                            Solid transformed = SolidUtils.CreateTransformed(solid, transform);
                            solids.Add(transformed);
                        }
                        catch { /* Skip solids that fail to transform */ }
                    }
                }
            }

            return solids;
        }

        /// <summary>
        /// Recursively extracts Solid objects from geometry, handling GeometryInstance nesting.
        /// </summary>
        private IEnumerable<Solid> ExtractSolids(GeometryElement geom)
        {
            foreach (GeometryObject gObj in geom)
            {
                if (gObj is Solid solid)
                {
                    yield return solid;
                }
                else if (gObj is GeometryInstance gi)
                {
                    foreach (var s in ExtractSolids(gi.GetInstanceGeometry()))
                        yield return s;
                }
            }
        }

        /// <summary>
        /// Finds the nearest horizontal surface above or below a point by intersecting
        /// vertical lines with solid faces from linked models.
        /// </summary>
        /// <param name="point">The sleeve center point</param>
        /// <param name="solids">Transformed solids from linked floor/roof elements</param>
        /// <param name="searchDown">True = search below point (for AFF), False = search above (for BBD)</param>
        /// <param name="upFacing">True = look for up-facing surfaces (floor tops), False = down-facing (deck bottoms)</param>
        /// <returns>Z coordinate of the nearest matching surface, or null if none found</returns>
        private double? FindNearestSurface(XYZ point, List<Solid> solids, bool searchDown, bool upFacing)
        {
            double x = point.X;
            double y = point.Y;
            double z = point.Z;

            // Create vertical search line
            XYZ lineStart, lineEnd;
            if (searchDown)
            {
                lineStart = new XYZ(x, y, z);
                lineEnd = new XYZ(x, y, z - SearchDistance);
            }
            else
            {
                lineStart = new XYZ(x, y, z);
                lineEnd = new XYZ(x, y, z + SearchDistance);
            }

            Line searchLine;
            try
            {
                searchLine = Line.CreateBound(lineStart, lineEnd);
            }
            catch
            {
                return null;
            }

            double? bestZ = null;

            foreach (var solid in solids)
            {
                // Quick bounding box check — skip solids that can't possibly intersect
                BoundingBoxXYZ bb = solid.GetBoundingBox();
                if (bb != null)
                {
                    if (x < bb.Min.X - 1 || x > bb.Max.X + 1 ||
                        y < bb.Min.Y - 1 || y > bb.Max.Y + 1)
                        continue;

                    if (searchDown && bb.Min.Z > z)
                        continue; // Solid is entirely above us
                    if (!searchDown && bb.Max.Z < z)
                        continue; // Solid is entirely below us
                }

                foreach (Face face in solid.Faces)
                {
                    // Check if face is horizontal in the right direction
                    try
                    {
                        UV midUV = new UV(0.5, 0.5);
                        XYZ normal = face.ComputeNormal(midUV);

                        if (upFacing && normal.Z < 0.9) continue;     // Want up-facing (Z ≈ +1)
                        if (!upFacing && normal.Z > -0.9) continue;   // Want down-facing (Z ≈ -1)
                    }
                    catch
                    {
                        continue;
                    }

                    // Intersect face with vertical line
                    try
                    {
                        IntersectionResultArray results;
                        SetComparisonResult result = face.Intersect(searchLine, out results);

                        if (result != SetComparisonResult.Overlap || results == null)
                            continue;

                        foreach (IntersectionResult ir in results)
                        {
                            double hitZ = ir.XYZPoint.Z;

                            if (searchDown)
                            {
                                // Looking for floor below: want highest hit below sleeve
                                if (hitZ < z + 0.01 && (!bestZ.HasValue || hitZ > bestZ.Value))
                                    bestZ = hitZ;
                            }
                            else
                            {
                                // Looking for deck above: want lowest hit above sleeve
                                if (hitZ > z - 0.01 && (!bestZ.HasValue || hitZ < bestZ.Value))
                                    bestZ = hitZ;
                            }
                        }
                    }
                    catch { /* Skip faces that fail intersection */ }
                }
            }

            return bestZ;
        }

        /// <summary>
        /// Gets the location point of a sleeve family instance.
        /// </summary>
        private XYZ GetSleeveLocation(Element elem)
        {
            if (elem.Location is LocationPoint lp)
                return lp.Point;

            // Fallback: bounding box center
            var bb = elem.get_BoundingBox(null);
            if (bb != null)
                return new XYZ(
                    (bb.Min.X + bb.Max.X) / 2.0,
                    (bb.Min.Y + bb.Max.Y) / 2.0,
                    (bb.Min.Z + bb.Max.Z) / 2.0);

            return null;
        }

        /// <summary>
        /// Formats an elevation value as a string.
        /// Supports decimal feet (+10.50' AFF) or feet-and-inches (+10'-6" AFF) format.
        /// </summary>
        private string FormatElevation(double? elevation, string suffix, bool useDecimal)
        {
            if (!elevation.HasValue)
                return "None";

            double value = elevation.Value;
            string sign = value >= 0 ? "+" : "-";
            double absValue = Math.Abs(value);

            if (useDecimal)
            {
                // Decimal feet format: +10.50' AFF
                return $"{sign}{absValue:F2}' {suffix}";
            }
            else
            {
                // Feet-and-inches format: +10'-6 1/2" AFF
                double totalInches = absValue * 12.0;
                double roundedInches = Math.Round(totalInches / 0.25) * 0.25;

                int wholeFeet = (int)Math.Floor(roundedInches / 12.0);
                double remainingInches = roundedInches - (wholeFeet * 12.0);

                int wholeInches = (int)Math.Floor(remainingInches);
                double fraction = remainingInches - wholeInches;

                string fractionStr = "";
                if (Math.Abs(fraction - 0.25) < 0.01)
                    fractionStr = " 1/4";
                else if (Math.Abs(fraction - 0.5) < 0.01)
                    fractionStr = " 1/2";
                else if (Math.Abs(fraction - 0.75) < 0.01)
                    fractionStr = " 3/4";

                return $"{sign}{wholeFeet}'-{wholeInches}{fractionStr}\" {suffix}";
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

        /// <summary>
        /// Selection filter that accepts pipe accessories with sleeve family names.
        /// Matches families containing "Sleeve-Wall", "Sleeve-Beam", or just "Sleeve".
        /// </summary>
        private class SleeveSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem?.Category == null) return false;
                int catId = elem.Category.Id.IntegerValue;

                if (catId != (int)BuiltInCategory.OST_PipeAccessory)
                    return false;

                // Check family name contains "Sleeve"
                string familyName = GetFamilyName(elem);
                if (string.IsNullOrEmpty(familyName)) return false;

                return familyName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public bool AllowReference(Reference reference, XYZ position) => false;

            private string GetFamilyName(Element elem)
            {
                if (elem is FamilyInstance fi)
                    return fi.Symbol?.Family?.Name ?? "";

                var param = elem.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                return param?.AsValueString() ?? "";
            }
        }
    }
}
