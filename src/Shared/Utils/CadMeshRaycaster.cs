using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Manual ray-vs-triangle raycaster for <see cref="ImportInstance"/>
    /// geometry. Bypasses <c>ReferenceIntersector</c>, which returns
    /// element-extent / bbox proximity (not triangle proximity) for CAD
    /// imports — that's the root cause of "rod overshoots first geometry
    /// by multiple feet" in <c>SyncHangersRaybounceCommand</c>.
    ///
    /// USAGE:
    ///   1. Construct with the doc + 3D view (visibility honored).
    ///   2. Call <see cref="Build"/> once before the per-hanger loop —
    ///      collects every triangle of every ImportInstance in the host
    ///      doc + every linked Revit doc, in world coordinates, with a
    ///      per-import AABB for early reject.
    ///   3. Call <see cref="FindClosestHit"/> per ray. Returns the
    ///      smallest positive distance to a triangle, or null if no hit.
    ///
    /// IMPLEMENTATION NOTES:
    ///   • For an <see cref="ImportInstance"/> in the host doc, the outer
    ///     <see cref="GeometryInstance"/> returned by <c>get_Geometry()</c>
    ///     already has its placement baked in (per TBC #0605); we DO NOT
    ///     re-apply <c>imp.Transform</c>. We descend via
    ///     <see cref="GeometryInstance.GetInstanceGeometry()"/> which
    ///     returns project-coord copies — no transform math needed for
    ///     host-doc imports.
    ///   • For an <see cref="ImportInstance"/> inside a linked Revit doc,
    ///     we pre-multiply each vertex by
    ///     <see cref="RevitLinkInstance.GetTotalTransform"/> (true-north
    ///     and shared coords are included).
    ///   • Triangulation: <c>Solid.Faces</c> get <c>Face.Triangulate()</c>;
    ///     <c>Mesh</c> objects are used directly. Curves/PolyLines/Points
    ///     are ignored — they're 2D / non-occluding.
    ///   • Möller-Trumbore is the standard ray-triangle test. Tolerances:
    ///     a determinant epsilon to reject parallel-ray edge cases, and a
    ///     minimum-distance epsilon to skip self-coincident hits at the
    ///     ray origin.
    /// </summary>
    public class CadMeshRaycaster
    {
        private const double DET_EPSILON = 1e-12;
        private const double MIN_HIT_DISTANCE = 1e-6;
        private const double DEGENERATE_AREA = 1e-18; // squared length of cross product

        private readonly Document _doc;
        private readonly View3D _view;
        private readonly List<CadImport> _imports = new List<CadImport>();

        public CadMeshRaycaster(Document doc, View3D view)
        {
            _doc = doc;
            _view = view;
        }

        /// <summary>Number of ImportInstance elements that contributed triangles.</summary>
        public int ImportCount => _imports.Count;

        /// <summary>Total triangle count across all imports — useful for diagnostics.</summary>
        public int TriangleCount => _imports.Sum(i => i.Triangles.Count);

        /// <summary>
        /// Enumerates every <see cref="ImportInstance"/> in the host doc
        /// and every linked Revit doc, triangulates its geometry into
        /// world coordinates, and caches AABB + triangle list per import.
        /// Idempotent if called more than once.
        /// </summary>
        public void Build()
        {
            _imports.Clear();

            // Host-doc imports — no link transform.
            foreach (var imp in CollectImports(_doc))
                BuildOne(imp, null);

            // Imports nested in linked Revit docs — pre-multiply by the
            // link's total transform so vertices land in host world coords.
            foreach (var rli in new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>())
            {
                Document linkDoc = rli.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform linkXform = rli.GetTotalTransform();
                if (linkXform == null || linkXform.IsIdentity) linkXform = null;

                foreach (var imp in CollectImports(linkDoc))
                    BuildOne(imp, linkXform);
            }
        }

        private static IEnumerable<ImportInstance> CollectImports(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>();
        }

        private void BuildOne(ImportInstance imp, Transform linkXform)
        {
            // View-bound options so layer/category visibility in the
            // raybounce view is respected. ComputeReferences=false: we
            // don't need API References — we're doing distance math only.
            Options opts;
            try
            {
                opts = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    View = _view
                };
            }
            catch
            {
                // View may not support detail-level options — fall back.
                opts = new Options { ComputeReferences = false };
            }

            GeometryElement geom;
            try { geom = imp.get_Geometry(opts); }
            catch { return; }
            if (geom == null) return;

            var tris = new List<Triangle>();
            var aabb = new MutableAabb();
            CollectFromGeometry(geom, linkXform, tris, aabb);
            if (tris.Count == 0) return;

            _imports.Add(new CadImport
            {
                Triangles = tris,
                Min = aabb.Min,
                Max = aabb.Max
            });
        }

        private static void CollectFromGeometry(GeometryElement elem, Transform linkXform,
            List<Triangle> tris, MutableAabb aabb)
        {
            if (elem == null) return;

            foreach (GeometryObject obj in elem)
            {
                if (obj == null) continue;

                if (obj is Solid solid)
                {
                    if (solid.Faces == null || solid.Faces.Size == 0) continue;
                    foreach (Face face in solid.Faces)
                    {
                        Mesh meshFromFace;
                        try { meshFromFace = face.Triangulate(); }
                        catch { continue; }
                        if (meshFromFace != null)
                            EmitMesh(meshFromFace, linkXform, tris, aabb);
                    }
                }
                else if (obj is Mesh mesh)
                {
                    EmitMesh(mesh, linkXform, tris, aabb);
                }
                else if (obj is GeometryInstance gi)
                {
                    // Outer GeometryInstance returned by ImportInstance.get_Geometry()
                    // is ALREADY in owning-doc coords (TBC #0605 double-transform
                    // pitfall) — GetInstanceGeometry returns copies in those same
                    // coords. We never multiply by gi.Transform here.
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    CollectFromGeometry(inner, linkXform, tris, aabb);
                }
                // Curve / PolyLine / Point: not occluding geometry — skip.
            }
        }

        private static void EmitMesh(Mesh mesh, Transform linkXform,
            List<Triangle> tris, MutableAabb aabb)
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

                if (linkXform != null)
                {
                    a = linkXform.OfPoint(a);
                    b = linkXform.OfPoint(b);
                    c = linkXform.OfPoint(c);
                }

                // Skip degenerate triangles (zero area) — they'd give NaN
                // determinant in Möller-Trumbore.
                XYZ ab = b - a;
                XYZ ac = c - a;
                XYZ cross = ab.CrossProduct(ac);
                if (cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z < DEGENERATE_AREA)
                    continue;

                tris.Add(new Triangle { A = a, B = b, C = c });
                aabb.Expand(a);
                aabb.Expand(b);
                aabb.Expand(c);
            }
        }

        /// <summary>
        /// Returns the smallest positive ray-triangle distance from
        /// <paramref name="origin"/> in the direction
        /// <paramref name="direction"/>, across every cached
        /// <see cref="ImportInstance"/>. Null if no triangle is hit.
        /// </summary>
        public double? FindClosestHit(XYZ origin, XYZ direction)
        {
            double bestT = double.PositiveInfinity;

            foreach (var ci in _imports)
            {
                if (!RayHitsAabb(origin, direction, ci.Min, ci.Max, bestT))
                    continue;

                foreach (var tri in ci.Triangles)
                {
                    if (TryMollerTrumbore(origin, direction, tri.A, tri.B, tri.C, out double t)
                        && t > MIN_HIT_DISTANCE
                        && t < bestT)
                    {
                        bestT = t;
                    }
                }
            }

            return double.IsPositiveInfinity(bestT) ? (double?)null : bestT;
        }

        /// <summary>
        /// Slab test — does the ray <c>origin + t * direction</c> for
        /// some <c>t in [0, maxT]</c> intersect the AABB? Used as a
        /// cheap reject before the per-triangle inner loop.
        /// </summary>
        private static bool RayHitsAabb(XYZ origin, XYZ dir, XYZ min, XYZ max, double maxT)
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
        /// Möller–Trumbore ray-triangle intersection on <see cref="XYZ"/>.
        /// Out param <paramref name="t"/> = parametric distance along the
        /// ray; only valid when the method returns true.
        /// </summary>
        private static bool TryMollerTrumbore(XYZ origin, XYZ direction,
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

        private struct Triangle
        {
            public XYZ A;
            public XYZ B;
            public XYZ C;
        }

        private class CadImport
        {
            public List<Triangle> Triangles;
            public XYZ Min;
            public XYZ Max;
        }

        /// <summary>
        /// Mutable AABB used during the build pass. Tracks min/max
        /// component-wise; <see cref="Min"/> / <see cref="Max"/> snapshot
        /// to immutable <see cref="XYZ"/> for the cached entry.
        /// </summary>
        private class MutableAabb
        {
            private bool _set;
            private double _minX, _minY, _minZ, _maxX, _maxY, _maxZ;

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
    }
}
