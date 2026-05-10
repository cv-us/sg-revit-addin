using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Helpers for working with structural framing elements (beams, joists, girders).
    /// Used by the Auto Hang at Structural command.
    ///
    /// Supports both local structural framing and elements from linked Revit models.
    /// </summary>
    public static class StructuralFramingHelpers
    {
        /// <summary>
        /// Structural family type names to INCLUDE (beams, girders, joists, etc.)
        /// </summary>
        private static readonly HashSet<string> IncludedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BEAM", "GIRDER", "JOIST", "TOP_CHORD", "PURLIN", "RIGID FRAME", "W-WIDE"
        };

        /// <summary>
        /// Structural family type names to EXCLUDE (angles, channels, misc)
        /// </summary>
        private static readonly HashSet<string> ExcludedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "L-ANGLE", "LL-DOUBLE ANGLE", "CHANNEL", "DOOR", "FLAT", "GRADE",
            "HP-BEARING PILE", "ROUND BARS", "SQUARE BARS", "TEE"
        };

        /// <summary>
        /// Get all structural framing elements in the document, filtered to beam-like types.
        /// </summary>
        public static IList<FamilyInstance> GetLocalStructuralFraming(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => IsBeamLikeType(fi))
                .ToList();
        }

        /// <summary>
        /// Get structural framing elements from a linked model that intersect a given bounding box.
        /// </summary>
        public static IList<Element> GetLinkedStructuralFraming(
            Document doc, RevitLinkInstance linkInstance, BoundingBoxXYZ searchBounds)
        {
            Document linkDoc = linkInstance.GetLinkDocument();
            if (linkDoc == null) return new List<Element>();

            Transform linkTransform = linkInstance.GetTotalTransform();

            return new FilteredElementCollector(linkDoc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(e => IsBeamLikeType(e))
                .ToList();
        }

        /// <summary>
        /// Get all loaded Revit link instances in the document.
        /// </summary>
        public static IList<RevitLinkInstance> GetRevitLinks(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(link => link.GetLinkDocument() != null)
                .ToList();
        }

        /// <summary>
        /// Check if a structural element is a beam-like type we want to hang from.
        /// Filters by family name / type name against include/exclude lists.
        /// Also accepts anything starting with "W" (W-shapes like W12x26).
        /// </summary>
        public static bool IsBeamLikeType(Element element)
        {
            string familyName = "";
            string typeName = "";

            if (element is FamilyInstance fi)
            {
                familyName = fi.Symbol?.Family?.Name?.ToUpper() ?? "";
                typeName = fi.Symbol?.Name?.ToUpper() ?? "";
            }
            else
            {
                familyName = ParameterHelpers.GetParamValueAsString(element, "Family")?.ToUpper() ?? "";
                typeName = ParameterHelpers.GetParamValueAsString(element, "Type Name")?.ToUpper() ?? "";
            }

            string combined = familyName + " " + typeName;

            // Exclude first
            foreach (string excluded in ExcludedTypes)
            {
                if (combined.Contains(excluded)) return false;
            }

            // Include if matches known types or starts with W (W-shapes)
            foreach (string included in IncludedTypes)
            {
                if (combined.Contains(included)) return true;
            }

            // W-shapes: W12x26, W8x10, etc.
            if (typeName.StartsWith("W") && typeName.Length > 1 && char.IsDigit(typeName[1]))
                return true;

            // Default: include (better to hang than miss)
            return true;
        }

        /// <summary>
        /// Get the centerline of a structural framing element as a Line.
        /// Works for both local and linked elements.
        /// </summary>
        public static Line GetFramingCenterline(Element element, Transform linkTransform = null)
        {
            LocationCurve lc = element.Location as LocationCurve;
            if (lc == null) return null;

            Curve curve = lc.Curve;
            if (linkTransform != null && !linkTransform.IsIdentity)
                curve = curve.CreateTransformed(linkTransform);

            return curve as Line;
        }

        /// <summary>
        /// Get the top and bottom Z elevations of a structural element's bounding box.
        /// Used to determine top/bottom flange positions.
        /// </summary>
        /// <param name="element">The structural element</param>
        /// <param name="linkTransform">Transform if from a linked model (null for local)</param>
        /// <returns>(topZ, bottomZ) in project coordinates</returns>
        public static (double topZ, double bottomZ) GetFramingElevations(
            Element element, Transform linkTransform = null)
        {
            BoundingBoxXYZ bb = element.get_BoundingBox(null);
            if (bb == null) return (0, 0);

            double topZ = bb.Max.Z;
            double bottomZ = bb.Min.Z;

            if (linkTransform != null && !linkTransform.IsIdentity)
            {
                XYZ transformedMax = linkTransform.OfPoint(bb.Max);
                XYZ transformedMin = linkTransform.OfPoint(bb.Min);
                topZ = Math.Max(transformedMax.Z, transformedMin.Z);
                bottomZ = Math.Min(transformedMax.Z, transformedMin.Z);
            }

            return (topZ, bottomZ);
        }

        /// <summary>
        /// Data class holding information about a structural member for hanger placement.
        /// </summary>
        public class FramingInfo
        {
            public Element Element { get; set; }
            public Line Centerline { get; set; }
            public double TopZ { get; set; }
            public double BottomZ { get; set; }
            public string Name { get; set; }
            public Transform LinkTransform { get; set; }
        }

        /// <summary>
        /// Build FramingInfo objects for a collection of structural elements.
        /// </summary>
        public static List<FramingInfo> BuildFramingInfoList(
            IEnumerable<Element> elements, Transform linkTransform = null)
        {
            var results = new List<FramingInfo>();
            foreach (Element elem in elements)
            {
                Line centerline = GetFramingCenterline(elem, linkTransform);
                if (centerline == null) continue;

                var (topZ, bottomZ) = GetFramingElevations(elem, linkTransform);

                string name = "";
                if (elem is FamilyInstance fi)
                    name = $"{fi.Symbol?.Family?.Name} : {fi.Symbol?.Name}";
                else
                    name = ParameterHelpers.GetParamValueAsString(elem, "Type Name");

                results.Add(new FramingInfo
                {
                    Element = elem,
                    Centerline = centerline,
                    TopZ = topZ,
                    BottomZ = bottomZ,
                    Name = name,
                    LinkTransform = linkTransform
                });
            }
            return results;
        }

        /// <summary>
        /// Calculate the clamp angle — the corrective angle from the hanger's pipe-aligned
        /// rotation to point the C-clamp toward the structural member's centerline.
        /// </summary>
        /// <param name="hangerPoint">Where the hanger is placed</param>
        /// <param name="pipeRotation">The hanger's rotation angle (radians, matching pipe direction)</param>
        /// <param name="framingCenterline">The structural member's centerline</param>
        /// <returns>Clamp angle in degrees</returns>
        public static double CalculateClampAngle(XYZ hangerPoint, double pipeRotation, Line framingCenterline)
        {
            // Find the closest point on the framing centerline to the hanger
            XYZ framingStart = framingCenterline.GetEndPoint(0);
            XYZ framingEnd = framingCenterline.GetEndPoint(1);
            XYZ framingDir = (framingEnd - framingStart).Normalize();

            // Project hanger point onto framing line to get closest point
            XYZ toHanger = hangerPoint - framingStart;
            double param = toHanger.DotProduct(framingDir);
            XYZ closestPoint = framingStart + param * framingDir;

            // Direction from hanger to framing center (in XY)
            XYZ toCenter = new XYZ(closestPoint.X - hangerPoint.X, closestPoint.Y - hangerPoint.Y, 0);
            if (toCenter.GetLength() < 1e-10) return 0;

            double angleToCenter = Math.Atan2(toCenter.Y, toCenter.X);

            // Clamp angle is relative to the pipe direction
            double clampAngle = (angleToCenter - pipeRotation) * 180.0 / Math.PI + 90.0;

            // Normalize to 0-360
            while (clampAngle < 0) clampAngle += 360;
            while (clampAngle >= 360) clampAngle -= 360;

            return Math.Round(clampAngle, 2);
        }
    }
}
