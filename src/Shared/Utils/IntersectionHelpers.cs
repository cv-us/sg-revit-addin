using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Analytical 2D line-line intersection math.
    ///
    /// These methods work in plan view (XY plane) and ignore Z coordinates
    /// for intersection testing. The actual 3D placement point is calculated
    /// by projecting the 2D intersection back onto the pipe's 3D centerline.
    ///
    /// Pure math approach — much faster than projecting geometry onto planes
    /// and using DoesIntersect, since there are no Revit geometry calls.
    /// </summary>
    public static class IntersectionHelpers
    {
        /// <summary>
        /// Find the 2D intersection point of two line segments (ignoring Z).
        /// Returns null if the lines don't intersect within their segment lengths.
        /// </summary>
        public static XYZ GetSegmentIntersection2D(Line line1, Line line2)
        {
            XYZ p1 = line1.GetEndPoint(0);
            XYZ p2 = line1.GetEndPoint(1);
            XYZ p3 = line2.GetEndPoint(0);
            XYZ p4 = line2.GetEndPoint(1);

            double x1 = p1.X, y1 = p1.Y;
            double x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y;
            double x4 = p4.X, y4 = p4.Y;

            double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-10) return null; // parallel or coincident

            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / denom;

            // Check both parameters are in [0,1] — intersection is within both segments
            if (t < -1e-6 || t > 1.0 + 1e-6 || u < -1e-6 || u > 1.0 + 1e-6)
                return null;

            double ix = x1 + t * (x2 - x1);
            double iy = y1 + t * (y2 - y1);

            return new XYZ(ix, iy, 0);
        }

        /// <summary>
        /// For a 2D intersection point, find the corresponding 3D point on a pipe.
        /// Projects the XY intersection point onto the pipe's 3D centerline
        /// to get the correct Z elevation.
        /// </summary>
        /// <param name="intersection2D">The 2D intersection point (Z=0)</param>
        /// <param name="pipeLine">The pipe's 3D centerline</param>
        /// <returns>The 3D point on the pipe at the intersection's XY location</returns>
        public static XYZ ProjectToPipeLine(XYZ intersection2D, Line pipeLine)
        {
            XYZ pipeStart = pipeLine.GetEndPoint(0);
            XYZ pipeEnd = pipeLine.GetEndPoint(1);
            XYZ pipeDir = (pipeEnd - pipeStart);
            double pipeLength = pipeDir.GetLength();
            if (pipeLength < 1e-10) return pipeStart;

            pipeDir = pipeDir.Normalize();

            // Project intersection onto pipe direction (in XY)
            XYZ toPoint = new XYZ(intersection2D.X - pipeStart.X, intersection2D.Y - pipeStart.Y, 0);
            XYZ pipeDirXY = new XYZ(pipeDir.X, pipeDir.Y, 0);
            double pipeDirXYLen = pipeDirXY.GetLength();
            if (pipeDirXYLen < 1e-10) return pipeStart; // vertical pipe

            double param = toPoint.DotProduct(pipeDirXY) / (pipeDirXYLen * pipeDirXYLen);

            // Clamp to pipe length
            param = Math.Max(0, Math.Min(1, param / (pipeLength / pipeDirXYLen * pipeDirXYLen)));

            return pipeStart + param * pipeLength * pipeDir;
        }

        /// <summary>
        /// Find all points where a pipe line crosses any of the CAD lines.
        /// Returns 3D points on the pipe centerline at each crossing.
        /// </summary>
        public static List<XYZ> FindPipeCADIntersections(Line pipeLine, IList<Line> cadLines)
        {
            var results = new List<XYZ>();

            // Quick bounding box pre-check for the pipe (expanded slightly)
            XYZ pStart = pipeLine.GetEndPoint(0);
            XYZ pEnd = pipeLine.GetEndPoint(1);
            double pMinX = Math.Min(pStart.X, pEnd.X) - 0.1;
            double pMaxX = Math.Max(pStart.X, pEnd.X) + 0.1;
            double pMinY = Math.Min(pStart.Y, pEnd.Y) - 0.1;
            double pMaxY = Math.Max(pStart.Y, pEnd.Y) + 0.1;

            foreach (Line cadLine in cadLines)
            {
                // Bounding box pre-filter — skip CAD lines that can't possibly intersect
                XYZ cStart = cadLine.GetEndPoint(0);
                XYZ cEnd = cadLine.GetEndPoint(1);
                double cMinX = Math.Min(cStart.X, cEnd.X);
                double cMaxX = Math.Max(cStart.X, cEnd.X);
                double cMinY = Math.Min(cStart.Y, cEnd.Y);
                double cMaxY = Math.Max(cStart.Y, cEnd.Y);

                if (cMaxX < pMinX || cMinX > pMaxX || cMaxY < pMinY || cMinY > pMaxY)
                    continue;

                XYZ intersection = GetSegmentIntersection2D(pipeLine, cadLine);
                if (intersection != null)
                {
                    XYZ point3D = ProjectToPipeLine(intersection, pipeLine);
                    results.Add(point3D);
                }
            }

            return results;
        }
    }
}
