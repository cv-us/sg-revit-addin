using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace SgRevitAddin.Utils
{
    /// <summary>A single world-space triangle used for manual raycasting.</summary>
    public struct RayTri
    {
        public XYZ A;
        public XYZ B;
        public XYZ C;
    }

    /// <summary>
    /// Tallies of geometry-object kinds seen while triangulating — appended
    /// to command summaries to diagnose "nothing to bounce off" cases.
    /// </summary>
    public class GeomCounters
    {
        public int Solids;
        public int Meshes;
        public int Instances;
        public int Curves;
        public int Others;
    }

    /// <summary>
    /// Mutable axis-aligned bounding box grown point by point during a
    /// triangulation pass. <see cref="Min"/>/<see cref="Max"/> snapshot to
    /// immutable <see cref="XYZ"/>.
    /// </summary>
    public class GrowableBounds
    {
        private bool _set;
        private double _minX, _minY, _minZ, _maxX, _maxY, _maxZ;

        public bool HasData => _set;
        public XYZ Min => _set ? new XYZ(_minX, _minY, _minZ) : XYZ.Zero;
        public XYZ Max => _set ? new XYZ(_maxX, _maxY, _maxZ) : XYZ.Zero;

        public void Expand(XYZ p)
        {
            if (!_set)
            {
                _minX = _maxX = p.X;
                _minY = _maxY = p.Y;
                _minZ = _maxZ = p.Z;
                _set = true;
                return;
            }
            if (p.X < _minX) _minX = p.X; else if (p.X > _maxX) _maxX = p.X;
            if (p.Y < _minY) _minY = p.Y; else if (p.Y > _maxY) _maxY = p.Y;
            if (p.Z < _minZ) _minZ = p.Z; else if (p.Z > _maxZ) _maxZ = p.Z;
        }
    }

    /// <summary>
    /// Shared ray/triangle math for the manual raycasting paths
    /// (<see cref="CadMeshRaycaster"/> for CAD imports,
    /// <see cref="StructureRayScanner"/> for hit verification and the
    /// DirectShape/IFC mesh index). One implementation of Möller-Trumbore,
    /// AABB slab-testing, and Revit-geometry triangulation.
    /// </summary>
    public static class RayMeshMath
    {
        public const double DET_EPSILON = 1e-12;
        public const double MIN_HIT_DISTANCE = 1e-6;
        public const double DEGENERATE_AREA = 1e-18; // squared length of cross product

        /// <summary>
        /// Slab test — does the ray <c>origin + t * direction</c> for some
        /// <c>t in [0, maxT]</c> intersect the AABB? Cheap reject before the
        /// per-triangle inner loop.
        /// </summary>
        public static bool RayHitsAabb(XYZ origin, XYZ dir, XYZ min, XYZ max, double maxT)
        {
            double tNear = 0;
            double tFar = maxT;

            for (int axis = 0; axis < 3; axis++)
            {
                double o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                double d = axis == 0 ? dir.X    : axis == 1 ? dir.Y    : dir.Z;
                double mn = axis == 0 ? min.X   : axis == 1 ? min.Y   : min.Z;
                double mx = axis == 0 ? max.X   : axis == 1 ? max.Y   : max.Z;

                if (Math.Abs(d) < 1e-12)
                {
                    if (o < mn || o > mx) return false;
                }
                else
                {
                    double t1 = (mn - o) / d;
                    double t2 = (mx - o) / d;
                    if (t1 > t2) { double tmp = t1; t1 = t2; t2 = tmp; }
                    if (t1 > tNear) tNear = t1;
                    if (t2 < tFar)  tFar = t2;
                    if (tNear > tFar) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Möller–Trumbore ray-triangle intersection. Out param
        /// <paramref name="t"/> = parametric distance along the ray; only
        /// valid when the method returns true.
        /// </summary>
        public static bool TryMollerTrumbore(XYZ origin, XYZ direction,
            XYZ v0, XYZ v1, XYZ v2, out double t)
        {
            t = 0;

            XYZ edge1 = v1 - v0;
            XYZ edge2 = v2 - v0;
            XYZ h = direction.CrossProduct(edge2);
            double a = edge1.DotProduct(h);
            if (a > -DET_EPSILON && a < DET_EPSILON) return false; // parallel

            double f = 1.0 / a;
            XYZ s = origin - v0;
            double u = f * s.DotProduct(h);
            if (u < 0.0 || u > 1.0) return false;

            XYZ q = s.CrossProduct(edge1);
            double v = f * direction.DotProduct(q);
            if (v < 0.0 || u + v > 1.0) return false;

            t = f * edge2.DotProduct(q);
            return true;
        }

        /// <summary>
        /// Smallest positive distance from <paramref name="origin"/> along
        /// <paramref name="direction"/> to any triangle in the list, or null
        /// if none is hit within <paramref name="maxT"/>.
        /// </summary>
        public static double? ClosestHit(XYZ origin, XYZ direction,
            List<RayTri> tris, double maxT = double.PositiveInfinity)
        {
            double best = maxT;
            bool found = false;

            foreach (var tri in tris)
            {
                if (TryMollerTrumbore(origin, direction, tri.A, tri.B, tri.C, out double t)
                    && t > MIN_HIT_DISTANCE
                    && t < best)
                {
                    best = t;
                    found = true;
                }
            }
            return found ? best : (double?)null;
        }

        /// <summary>
        /// Triangulates an element's geometry (Fine detail, visible objects)
        /// into world-space triangles. Pass the link's total transform via
        /// <paramref name="xform"/> for elements living in a linked document;
        /// null for host-doc elements.
        /// </summary>
        public static List<RayTri> TriangulateElement(Element e, Transform xform,
            GrowableBounds bounds = null, GeomCounters counters = null)
        {
            var tris = new List<RayTri>();
            if (e == null) return tris;

            var opts = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geom = null;
            try { geom = e.get_Geometry(opts); } catch { }
            if (geom == null) return tris;

            CollectTriangles(geom, xform, tris, bounds, counters);
            return tris;
        }

        /// <summary>
        /// Walks a <see cref="GeometryElement"/> collecting triangles from
        /// Solids (via Face.Triangulate) and Meshes. Nested
        /// <see cref="GeometryInstance"/>s are descended via
        /// <c>GetInstanceGeometry()</c>, which returns owning-doc-coordinate
        /// copies — we never multiply by <c>gi.Transform</c> (TBC #0605
        /// double-transform pitfall). Curves/PolyLines are non-occluding and
        /// skipped.
        /// </summary>
        public static void CollectTriangles(GeometryElement elem, Transform xform,
            List<RayTri> tris, GrowableBounds bounds = null, GeomCounters counters = null)
        {
            if (elem == null) return;

            foreach (GeometryObject obj in elem)
            {
                if (obj == null) continue;

                if (obj is Solid solid)
                {
                    if (counters != null) counters.Solids++;
                    if (solid.Faces == null || solid.Faces.Size == 0) continue;
                    foreach (Face face in solid.Faces)
                    {
                        Mesh meshFromFace;
                        try { meshFromFace = face.Triangulate(); }
                        catch { continue; }
                        if (meshFromFace != null)
                            EmitMesh(meshFromFace, xform, tris, bounds);
                    }
                }
                else if (obj is Mesh mesh)
                {
                    if (counters != null) counters.Meshes++;
                    EmitMesh(mesh, xform, tris, bounds);
                }
                else if (obj is GeometryInstance gi)
                {
                    if (counters != null) counters.Instances++;
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    CollectTriangles(inner, xform, tris, bounds, counters);
                }
                else if (obj is Curve || obj is PolyLine)
                {
                    if (counters != null) counters.Curves++;
                }
                else
                {
                    if (counters != null) counters.Others++;
                }
            }
        }

        /// <summary>
        /// Appends a mesh's triangles (skipping degenerates that would give a
        /// NaN determinant in Möller-Trumbore), applying <paramref name="xform"/>
        /// to each vertex when non-null.
        /// </summary>
        public static void EmitMesh(Mesh mesh, Transform xform,
            List<RayTri> tris, GrowableBounds bounds = null)
        {
            if (mesh == null) return;

            int n = mesh.NumTriangles;
            for (int i = 0; i < n; i++)
            {
                MeshTriangle mt;
                try { mt = mesh.get_Triangle(i); }
                catch { continue; }
                if (mt == null) continue;

                XYZ a = mt.get_Vertex(0);
                XYZ b = mt.get_Vertex(1);
                XYZ c = mt.get_Vertex(2);
                if (a == null || b == null || c == null) continue;

                if (xform != null)
                {
                    a = xform.OfPoint(a);
                    b = xform.OfPoint(b);
                    c = xform.OfPoint(c);
                }

                XYZ ab = b - a;
                XYZ ac = c - a;
                XYZ cross = ab.CrossProduct(ac);
                if (cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z < DEGENERATE_AREA)
                    continue;

                tris.Add(new RayTri { A = a, B = b, C = c });
                if (bounds != null)
                {
                    bounds.Expand(a);
                    bounds.Expand(b);
                    bounds.Expand(c);
                }
            }
        }
    }
}
