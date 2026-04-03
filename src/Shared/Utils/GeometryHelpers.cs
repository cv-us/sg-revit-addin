using Autodesk.Revit.DB;
using System;

namespace SSG_FP_Suite.Utils
{
    /// <summary>
    /// Geometry and math helpers for working with Revit's XYZ coordinates.
    ///
    /// All coordinates in Revit are XYZ objects with values in feet:
    ///   - X and Y are horizontal (plan view) coordinates
    ///   - Z is vertical (elevation)
    ///
    /// USAGE:
    ///   XYZ headLocation = sprinkler.Location as LocationPoint;
    ///   XYZ pipeStart = PipeHelpers.GetPipeStartPoint(pipe);
    ///
    ///   double dist = GeometryHelpers.DistanceBetweenPoints(headLocation, pipeStart);
    ///   XYZ mid = GeometryHelpers.MidPoint(pipeStart, pipeEnd);
    ///
    ///   // For sprinkler spacing checks (ignore elevation differences):
    ///   double spacing = GeometryHelpers.HorizontalDistance(head1, head2);
    /// </summary>
    public static class GeometryHelpers
    {
        /// <summary>
        /// 3D distance between two points (includes elevation difference).
        /// Result is in feet.
        /// </summary>
        public static double DistanceBetweenPoints(XYZ point1, XYZ point2)
        {
            return point1.DistanceTo(point2);
        }

        /// <summary>
        /// Midpoint between two XYZ coordinates.
        /// Useful for placing elements between two pipes, finding center of a span, etc.
        /// </summary>
        public static XYZ MidPoint(XYZ point1, XYZ point2)
        {
            return (point1 + point2) / 2.0;
        }

        /// <summary>
        /// 2D horizontal distance (ignores Z/elevation).
        /// This is what you want for sprinkler spacing checks in plan view —
        /// two heads at different elevations but directly above each other
        /// would have a horizontal distance of 0.
        /// </summary>
        public static double HorizontalDistance(XYZ point1, XYZ point2)
        {
            XYZ flat1 = new XYZ(point1.X, point1.Y, 0);
            XYZ flat2 = new XYZ(point2.X, point2.Y, 0);
            return flat1.DistanceTo(flat2);
        }
    }
}
