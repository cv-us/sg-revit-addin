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
    /// Synchronizes pipe hanger rod lengths to the structural elements above
    /// using bounding box clash detection and surface intersection — without
    /// raybounce. Creates a search volume above each hanger, finds structural
    /// elements in that volume, then intersects a vertical line with their
    /// bottom surfaces to compute rod length.
    ///
    /// WORKFLOW:
    ///   1. User selects pipe hangers (pre-selection or pick)
    ///   2. Dialog: type codes, clash height, framing offset, top/bottom framing attach
    ///   3. Collect structural elements (floors, roofs, framing, stairs) from host + links
    ///   4. For each hanger, find structural elements within bounding box search volume
    ///   5. Intersect vertical line with structural faces to compute rod length
    ///   6. Set Rod Length, Type Code, Comments
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncHangersSurfaceCommand : IExternalCommand
    {
        /// <summary>
        /// Family name patterns that identify valid pipe hangers.
        /// </summary>
        private static readonly string[] HangerFamilyPatterns = new[]
        {
            "-Pipe Hanger",
            "Ring Hanger",
            "-Basic Adjustable"
        };

        /// <summary>
        /// Family name substrings to exclude from structural framing
        /// (angles, hollows, C-channels are not hangable structure).
        /// </summary>
        private static readonly string[] FramingExcludePatterns = new[]
        {
            "ANGLE",
            "HOLLOW",
            "C-CHANNEL"
        };

        /// <summary>
        /// Structural categories to search for above hangers.
        /// </summary>
        private static readonly BuiltInCategory[] TargetCategories = new[]
        {
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Stairs
        };

        /// <summary>
        /// Represents a structural face found above a hanger.
        /// </summary>
        private class StructuralHit
        {
            public double DistanceAbove { get; set; }
            public BuiltInCategory Category { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
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
                    TaskDialog.Show("Sync Hangers to Structural",
                        "No valid pipe hangers found in the selection.\n\n" +
                        "Select elements whose family name contains \"-Pipe Hanger\", " +
                        "\"Ring Hanger\", or \"-Basic Adjustable\".");
                    return Result.Failed;
                }

                // ── Read global parameter defaults ──
                string gpFloors = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Hanger Type - Floors") ?? "02";
                string gpRoofs = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Hanger Type - Roofs") ?? "01";
                string gpFraming = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Hanger Type - Framing") ?? "03";
                string gpStairs = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Hanger Type - Stairs") ?? "04";
                string gpClashHeight = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Clash Height Distance") ?? "10";
                string gpFramingOffset = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Framing Offset Distance") ?? "1";
                string gpFramingSyncTo = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Framing Hangers Sync'd To") ?? "Bottom";
                string gpKeepTypes = ReadGlobalParam(doc, "Dynamo Setting - AutoSync Keep Hanger Types") ?? "true";

                double clashHeightFt;
                if (!double.TryParse(gpClashHeight, out clashHeightFt))
                    clashHeightFt = 10.0;

                double framingOffsetIn;
                if (!double.TryParse(gpFramingOffset, out framingOffsetIn))
                    framingOffsetIn = 1.0;
                double framingOffsetFt = framingOffsetIn / 12.0;

                bool framingSyncToBottom = gpFramingSyncTo.IndexOf("Bottom", StringComparison.OrdinalIgnoreCase) >= 0
                                        || gpFramingSyncTo.Equals("B", StringComparison.OrdinalIgnoreCase);

                bool keepTypes = gpKeepTypes.Equals("true", StringComparison.OrdinalIgnoreCase);

                // ── Show dialog ──
                using (var dlg = new SyncHangersSurfaceDialog(
                    hangers.Count, gpFloors, gpStairs, gpRoofs, gpFraming,
                    clashHeightFt, framingOffsetIn, framingSyncToBottom, keepTypes))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    double searchHeight = dlg.ClashHeightFeet;
                    double offsetFt = dlg.FramingOffsetInches / 12.0;
                    bool syncToBottom = dlg.FramingSyncToBottom;

                    // ── Collect structural elements from host document ──
                    var structuralElements = new List<StructuralElementInfo>();
                    CollectHostStructural(doc, structuralElements);

                    // ── Collect structural elements from linked models ──
                    var links = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .ToList();

                    foreach (var link in links)
                    {
                        Document linkDoc = link.GetLinkDocument();
                        if (linkDoc == null) continue;
                        Transform linkTransform = link.GetTotalTransform();
                        CollectLinkedStructural(linkDoc, linkTransform, structuralElements);
                    }

                    if (structuralElements.Count == 0)
                    {
                        TaskDialog.Show("Sync Hangers to Structural",
                            "No structural elements (floors, roofs, framing, stairs) found in the model or linked models.");
                        return Result.Failed;
                    }

                    // ── Process each hanger ──
                    int syncedCount = 0;
                    int unchangedCount = 0;
                    int failedCount = 0;
                    var misses = new List<FamilyInstance>();
                    var categoryCounts = new Dictionary<string, int>();

                    using (var tw = new TransactionWrapper(doc, "Sync Hangers to Structural (Surface)"))
                    {
                        // Write global parameters back if changed
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Hanger Type - Floors", dlg.TypeCodeFloors);
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Hanger Type - Roofs", dlg.TypeCodeRoofs);
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Hanger Type - Framing", dlg.TypeCodeFraming);
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Hanger Type - Stairs", dlg.TypeCodeStairs);
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Clash Height Distance", dlg.ClashHeightFeet.ToString());
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Framing Offset Distance", dlg.FramingOffsetInches.ToString());
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Framing Hangers Sync'd To", dlg.FramingSyncToBottom ? "Bottom" : "Top");
                        WriteGlobalParamIfChanged(doc, "Dynamo Setting - AutoSync Keep Hanger Types", dlg.KeepHangerTypes ? "true" : "false");

                        foreach (var hanger in hangers)
                        {
                            XYZ hangerPoint = GetHangerPoint(hanger);
                            if (hangerPoint == null)
                            {
                                misses.Add(hanger);
                                continue;
                            }

                            // Find the closest structural face above using bounding box + face intersection
                            var hit = FindClosestStructuralAbove(
                                hangerPoint, searchHeight, offsetFt, syncToBottom, structuralElements);

                            if (hit == null)
                            {
                                misses.Add(hanger);
                                continue;
                            }

                            try
                            {
                                double rodLength = hit.DistanceAbove;

                                // Check if rod length actually changed
                                Parameter existingRod = hanger.LookupParameter("Rod Length");
                                double oldRodLength = existingRod != null ? existingRod.AsDouble() : -1;

                                if (Math.Abs(rodLength - oldRodLength) < 1.0 / 256.0) // ~0.004" tolerance
                                {
                                    unchangedCount++;
                                    // Still count the category
                                    string label = GetCategoryLabel(hit.Category);
                                    if (!categoryCounts.ContainsKey(label))
                                        categoryCounts[label] = 0;
                                    continue;
                                }

                                // Set Rod Length
                                SetParameter(hanger, "Rod Length", rodLength);
                                SetParameter(hanger, "Y Grip", rodLength);

                                // Set Type Code and Comments unless keeping existing
                                if (!dlg.KeepHangerTypes)
                                {
                                    string typeCode = GetTypeCode(hit.Category, dlg);
                                    SetParameter(hanger, "Type Code (Hydratec)", typeCode);
                                    SetParameter(hanger, "Comments", typeCode);
                                }

                                syncedCount++;

                                string catLabel = GetCategoryLabel(hit.Category);
                                if (categoryCounts.ContainsKey(catLabel))
                                    categoryCounts[catLabel]++;
                                else
                                    categoryCounts[catLabel] = 1;
                            }
                            catch
                            {
                                failedCount++;
                            }
                        }

                        tw.Commit();
                    }

                    // ── Highlight misses ──
                    if (misses.Count > 0)
                    {
                        var missIds = misses.Select(m => m.Id).ToList();
                        uidoc.Selection.SetElementIds(missIds);
                    }

                    // ── Summary ──
                    var summaryLines = new List<string>();

                    if (syncedCount > 0)
                    {
                        summaryLines.Add($"A Total Of {syncedCount} Hangers Have Been Re-Sync'd!");
                        summaryLines.Add("");
                        summaryLines.Add("Totals By Structural Category:");
                        foreach (var kvp in categoryCounts.OrderBy(k => k.Key))
                        {
                            if (kvp.Value > 0)
                                summaryLines.Add($"  {kvp.Key}: {kvp.Value}");
                        }
                    }
                    else
                    {
                        summaryLines.Add("All Hanger Lengths Remained The Same.");
                        summaryLines.Add("No Hangers Required Synchronizing!");
                    }

                    if (unchangedCount > 0)
                        summaryLines.Add($"\n{unchangedCount} hanger{(unchangedCount != 1 ? "s" : "")} already had correct rod lengths.");

                    if (misses.Count > 0)
                    {
                        summaryLines.Add($"\n{misses.Count} hanger{(misses.Count != 1 ? "s" : "")} couldn't be synchronized " +
                                        "(no structural element found above). These are now highlighted.");
                    }

                    if (failedCount > 0)
                        summaryLines.Add($"{failedCount} hanger{(failedCount != 1 ? "s" : "")} failed.");

                    TaskDialog.Show("Hanger Sync To Structural Summary:",
                        string.Join("\n", summaryLines));

                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        #region Structural Element Collection

        /// <summary>
        /// Info about a structural element and its geometry, pre-processed for intersection tests.
        /// </summary>
        private class StructuralElementInfo
        {
            public BuiltInCategory Category { get; set; }
            public BoundingBoxXYZ BBox { get; set; }
            public List<FaceInfo> Faces { get; set; } = new List<FaceInfo>();
        }

        /// <summary>
        /// A planar face with its normal, origin, and edges for point-in-face testing.
        /// </summary>
        private class FaceInfo
        {
            public XYZ Normal { get; set; }
            public XYZ Origin { get; set; }
            public double Elevation { get; set; } // Z of the face plane
            public bool IsDownFacing { get; set; }
            public bool IsUpFacing { get; set; }
            public List<XYZ> Vertices { get; set; } = new List<XYZ>();
        }

        /// <summary>
        /// Collects structural elements from the host document.
        /// </summary>
        private void CollectHostStructural(Document doc, List<StructuralElementInfo> results)
        {
            foreach (var cat in TargetCategories)
            {
                var elems = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var elem in elems)
                {
                    if (cat == BuiltInCategory.OST_StructuralFraming && ShouldExcludeFraming(elem))
                        continue;

                    var info = ExtractStructuralInfo(elem, cat, Transform.Identity);
                    if (info != null)
                        results.Add(info);
                }
            }
        }

        /// <summary>
        /// Collects structural elements from a linked document.
        /// </summary>
        private void CollectLinkedStructural(Document linkDoc, Transform linkTransform,
            List<StructuralElementInfo> results)
        {
            foreach (var cat in TargetCategories)
            {
                var elems = new FilteredElementCollector(linkDoc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .ToList();

                foreach (var elem in elems)
                {
                    if (cat == BuiltInCategory.OST_StructuralFraming && ShouldExcludeFraming(elem))
                        continue;

                    var info = ExtractStructuralInfo(elem, cat, linkTransform);
                    if (info != null)
                        results.Add(info);
                }
            }
        }

        /// <summary>
        /// Checks if a structural framing element should be excluded
        /// (angles, hollows, C-channels).
        /// </summary>
        private bool ShouldExcludeFraming(Element elem)
        {
            string familyName = "";
            if (elem is FamilyInstance fi)
                familyName = fi.Symbol?.Family?.Name ?? "";
            else
                familyName = elem.Name ?? "";

            string upper = familyName.ToUpper();
            foreach (var pattern in FramingExcludePatterns)
            {
                if (upper.Contains(pattern))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Extracts geometry info from a structural element, applying the given transform.
        /// </summary>
        private StructuralElementInfo ExtractStructuralInfo(Element elem, BuiltInCategory cat, Transform transform)
        {
            try
            {
                BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
                if (bbox == null) return null;

                // Transform bounding box
                XYZ bMin = transform.OfPoint(bbox.Min);
                XYZ bMax = transform.OfPoint(bbox.Max);
                var tBbox = new BoundingBoxXYZ
                {
                    Min = new XYZ(Math.Min(bMin.X, bMax.X), Math.Min(bMin.Y, bMax.Y), Math.Min(bMin.Z, bMax.Z)),
                    Max = new XYZ(Math.Max(bMin.X, bMax.X), Math.Max(bMin.Y, bMax.Y), Math.Max(bMin.Z, bMax.Z))
                };

                var info = new StructuralElementInfo
                {
                    Category = cat,
                    BBox = tBbox
                };

                // Extract faces from geometry
                var options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse };
                GeometryElement geomElem = elem.get_Geometry(options);
                if (geomElem == null) return info;

                ExtractFaces(geomElem, transform, info.Faces);

                return info.Faces.Count > 0 ? info : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Recursively extracts planar horizontal faces from geometry.
        /// </summary>
        private void ExtractFaces(GeometryElement geomElem, Transform transform, List<FaceInfo> faces)
        {
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace planar)) continue;

                        XYZ normal = transform.OfVector(planar.FaceNormal).Normalize();

                        // Only keep roughly horizontal faces (normal Z > 0.9 or < -0.9)
                        bool isDown = normal.Z < -0.9;
                        bool isUp = normal.Z > 0.9;
                        if (!isDown && !isUp) continue;

                        XYZ origin = transform.OfPoint(planar.Origin);

                        // Get face vertices for point-in-polygon testing
                        var vertices = new List<XYZ>();
                        EdgeArrayArray edgeLoops = face.EdgeLoops;
                        if (edgeLoops.Size > 0)
                        {
                            EdgeArray outerLoop = edgeLoops.get_Item(0);
                            foreach (Edge edge in outerLoop)
                            {
                                IList<XYZ> pts = edge.Tessellate();
                                for (int i = 0; i < pts.Count - 1; i++)
                                    vertices.Add(transform.OfPoint(pts[i]));
                            }
                        }

                        if (vertices.Count < 3) continue;

                        faces.Add(new FaceInfo
                        {
                            Normal = normal,
                            Origin = origin,
                            Elevation = origin.Z,
                            IsDownFacing = isDown,
                            IsUpFacing = isUp,
                            Vertices = vertices
                        });
                    }
                }
                else if (geomObj is GeometryInstance gInst)
                {
                    GeometryElement instGeom = gInst.GetInstanceGeometry(transform);
                    if (instGeom != null)
                    {
                        // Pass Identity transform since GetInstanceGeometry already applies it
                        ExtractFaces(instGeom, Transform.Identity, faces);
                    }
                }
            }
        }

        #endregion

        #region Intersection Logic

        /// <summary>
        /// Finds the closest structural face above a hanger point using
        /// bounding box pre-filtering and vertical line-face intersection.
        /// </summary>
        private StructuralHit FindClosestStructuralAbove(
            XYZ hangerPoint, double searchHeight, double offsetFt,
            bool framingSyncToBottom, List<StructuralElementInfo> allStructural)
        {
            double hx = hangerPoint.X;
            double hy = hangerPoint.Y;
            double hz = hangerPoint.Z;
            double hzTop = hz + searchHeight;

            // Search radius for XY proximity (for framing offset)
            double searchRadius = Math.Max(offsetFt, 0.5); // at least 6"

            StructuralHit bestHit = null;
            double bestDistance = double.MaxValue;

            foreach (var structInfo in allStructural)
            {
                // ── Bounding box pre-filter ──
                var bb = structInfo.BBox;

                // Check XY overlap (with search radius)
                if (hx + searchRadius < bb.Min.X || hx - searchRadius > bb.Max.X) continue;
                if (hy + searchRadius < bb.Min.Y || hy - searchRadius > bb.Max.Y) continue;

                // Check Z overlap — structural element must be above hanger (or overlapping)
                if (bb.Max.Z < hz) continue;       // entirely below hanger
                if (bb.Min.Z > hzTop) continue;    // entirely above search volume

                // ── Face intersection ──
                foreach (var face in structInfo.Faces)
                {
                    // For floors, roofs, stairs: use down-facing faces (bottom surface)
                    // For framing: use down-facing if syncToBottom, up-facing if syncToTop
                    bool useFace;
                    if (structInfo.Category == BuiltInCategory.OST_StructuralFraming)
                    {
                        useFace = framingSyncToBottom ? face.IsDownFacing : face.IsUpFacing;
                    }
                    else
                    {
                        // Floors, roofs, stairs: always use down-facing (underside)
                        useFace = face.IsDownFacing;
                    }

                    if (!useFace) continue;

                    // Face must be above the hanger
                    double faceZ = face.Elevation;
                    double dist = faceZ - hz;
                    if (dist <= 0) continue;           // face is at or below hanger
                    if (dist > searchHeight) continue;  // face is above search volume

                    // Check if hanger XY point projects inside the face polygon
                    if (!PointInPolygonXY(hx, hy, face.Vertices))
                        continue;

                    // This face is a valid hit
                    if (dist < bestDistance)
                    {
                        bestDistance = dist;
                        bestHit = new StructuralHit
                        {
                            DistanceAbove = dist,
                            Category = structInfo.Category
                        };
                    }
                }
            }

            return bestHit;
        }

        /// <summary>
        /// 2D point-in-polygon test (XY plane) using ray casting.
        /// </summary>
        private bool PointInPolygonXY(double px, double py, List<XYZ> polygon)
        {
            int n = polygon.Count;
            if (n < 3) return false;

            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = polygon[i].Y;
                double yj = polygon[j].Y;
                double xi = polygon[i].X;
                double xj = polygon[j].X;

                if ((yi > py) != (yj > py) &&
                    px < (xj - xi) * (py - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Gets the hanger's location point.
        /// </summary>
        private XYZ GetHangerPoint(FamilyInstance hanger)
        {
            LocationPoint locPt = hanger.Location as LocationPoint;
            if (locPt != null)
                return locPt.Point;

            LocationCurve locCurve = hanger.Location as LocationCurve;
            if (locCurve?.Curve != null)
                return locCurve.Curve.Evaluate(0.5, true);

            return null;
        }

        /// <summary>
        /// Checks if a family instance is a valid pipe hanger.
        /// </summary>
        private bool IsValidHanger(FamilyInstance fi)
        {
            if (fi.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeAccessory)
                return false;

            string familyName = fi.Symbol?.Family?.Name ?? "";
            foreach (var pattern in HangerFamilyPatterns)
            {
                if (familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Maps a hit category to the user-specified type code.
        /// </summary>
        private string GetTypeCode(BuiltInCategory hitCat, SyncHangersSurfaceDialog dlg)
        {
            switch (hitCat)
            {
                case BuiltInCategory.OST_Floors: return dlg.TypeCodeFloors;
                case BuiltInCategory.OST_Stairs: return dlg.TypeCodeStairs;
                case BuiltInCategory.OST_Roofs: return dlg.TypeCodeRoofs;
                case BuiltInCategory.OST_StructuralFraming: return dlg.TypeCodeFraming;
                default: return "";
            }
        }

        private string GetCategoryLabel(BuiltInCategory cat)
        {
            switch (cat)
            {
                case BuiltInCategory.OST_Floors: return "Floors";
                case BuiltInCategory.OST_Stairs: return "Stairs";
                case BuiltInCategory.OST_Roofs: return "Roofs";
                case BuiltInCategory.OST_StructuralFraming: return "Structural Framing";
                default: return "Other";
            }
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
        /// Reads a global parameter string value, or null if not found.
        /// </summary>
        private string ReadGlobalParam(Document doc, string name)
        {
            try
            {
                ElementId id = GlobalParametersManager.FindByName(doc, name);
                if (id == null || id == ElementId.InvalidElementId)
                    return null;
                GlobalParameter gp = doc.GetElement(id) as GlobalParameter;
                if (gp == null) return null;
                var val = gp.GetValue() as StringParameterValue;
                return val?.Value;
            }
            catch { return null; }
        }

        /// <summary>
        /// Writes a global parameter string value if it differs from current.
        /// </summary>
        private void WriteGlobalParamIfChanged(Document doc, string name, string newValue)
        {
            try
            {
                ElementId id = GlobalParametersManager.FindByName(doc, name);
                if (id == null || id == ElementId.InvalidElementId) return;
                GlobalParameter gp = doc.GetElement(id) as GlobalParameter;
                if (gp == null) return;
                var current = gp.GetValue() as StringParameterValue;
                if (current != null && current.Value == newValue) return;
                gp.SetValue(new StringParameterValue(newValue));
            }
            catch { }
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

            if (preSelected.Count > 0)
                return preSelected;

            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeAccessoryFilter(),
                    "Select PIPE HANGERS to sync to structural, then press Finish.");

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

        #endregion
    }
}
