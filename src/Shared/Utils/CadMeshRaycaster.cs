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

        // ── Diagnostics (populated during Build) ──
        private int _importsSeen;       // total ImportInstance elements enumerated
        private int _importsWithGeom;   // imports that yielded ≥1 triangle
        private int _solidObjs;         // Solid GeometryObjects encountered
        private int _meshObjs;          // Mesh GeometryObjects encountered
        private int _giObjs;            // nested GeometryInstance encountered
        private int _curveObjs;         // Curve / PolyLine / Line (ignored, but tallied)
        private int _otherObjs;         // anything else (Point, etc.)

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
        /// Human-readable summary of what the build pass saw — append to a
        /// command's summary dialog to diagnose "rod still overshoots".
        ///   • importsSeen=0 → no ImportInstance in doc/links at all.
        ///   • triangleCount=0 with solids/meshes=0 → the CAD came in as
        ///     curves/wireframe only (nothing to ray-hit). Fix is CAD-side.
        ///   • triangleCount&gt;0 but no hangers used CAD → geometry is
        ///     placed wrong (transform) or off the vertical rays.
        /// </summary>
        public string Diagnostics =>
            $"imports seen={_importsSeen}, with geometry={_importsWithGeom}; " +
            $"objects → Solid={_solidObjs}, Mesh={_meshObjs}, " +
            $"GeomInstance={_giObjs}, Curve/PolyLine={_curveObjs}, other={_otherObjs}; " +
            $"triangles cached={TriangleCount}";

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
            _importsSeen++;

            // Retrieve RAW model geometry at Fine detail — NO view binding.
            // Earlier we passed View=_view so the raybounce view's layer
            // visibility was respected, but that meant a DWG whose layers
            // were off in the auto-created 3D-Raybounce view returned ZERO
            // geometry — and the rod fell through to native structure
            // (the "still overshoots" bug). Raw geometry removes that
            // dependency; we'd rather risk hitting a hidden layer than
            // miss the geometry entirely.
            var tris = new List<Triangle>();
            var aabb = new MutableAabb();

            // Try a few option sets — different Revit builds are picky about
            // which combination yields geometry for a linked ImportInstance.
            foreach (var opts in GeometryOptionVariants())
            {
                GeometryElement geom;
                try { geom = imp.get_Geometry(opts); }
                catch { continue; }
                if (geom == null) continue;

                CollectFromGeometry(geom, linkXform, tris, aabb);
                if (tris.Count > 0) break; // got something — stop trying variants
            }

            if (tris.Count == 0) return;

            _importsWithGeom++;
            _imports.Add(new CadImport
            {
                Triangles = tris,
                Min = aabb.Min,
                Max = aabb.Max
            });
        }

        /// <summary>
        /// Geometry option sets tried in order until one yields triangles.
        /// Raw (no-view) Fine first; then with non-visible objects included
        /// (catches geometry on layers Revit considers "not visible"); then
        /// the view-bound variant as a last resort.
        /// </summary>
        private IEnumerable<Options> GeometryOptionVariants()
        {
            yield return new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };
            yield return new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };
            if (_view != null)
                yield return new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = true,
                    View = _view
                };
        }

        private void CollectFromGeometry(GeometryElement elem, Transform linkXform,
            List<Triangle> tris, MutableAabb aabb)
        {
            if (elem == null) return;

            foreach (GeometryObject obj in elem)
            {
                if (obj == null) continue;

                if (obj is Solid solid)
                {
                    _solidObjs++;
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
                    _meshObjs++;
                    EmitMesh(mesh, linkXform, tris, aabb);
                }
                else if (obj is GeometryInstance gi)
                {
                    _giObjs++;
                    // Outer GeometryInstance returned by ImportInstance.get_Geometry()
                    // is ALREADY in owning-doc coords (TBC #0605 double-transform
                    // pitfall) — GetInstanceGeometry returns copies in those same
                    // coords. We never multiply by gi.Transform here.
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    CollectFromGeometry(inner, linkXform, tris, aabb);
                }
                else if (obj is Curve || obj is PolyLine)
                {
                    _curveObjs++; // not occluding geometry — tallied, then skipped
                }
                else
                {
                    _otherObjs++;
                }
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
        /// Diagnostic: describe what the straight-up ray from
        /// <paramref name="origin"/> encounters. Reveals whether the cached
        /// CAD geometry is even spatially aligned with the hanger:
        ///   • "ray crosses 0 bbox" → the hanger's vertical column passes
        ///     through NO import — geometry is offset in XY (transform).
        ///   • crosses N bbox with Z-spans, but "NO triangle hit" → the
        ///     bbox is along the ray but its triangles aren't (finer
        ///     offset, or near surface missing).
        ///   • "closest hit=H" → working; H is the rod length.
        /// Also reports whether the hanger's XY falls inside the overall
        /// CAD footprint at all.
        /// </summary>
        public string ProbeColumn(XYZ origin, XYZ direction)
        {
            // Overall footprint across all cached imports.
            bool any = _imports.Count > 0;
            double oxMin = double.MaxValue, oyMin = double.MaxValue, ozMin = double.MaxValue;
            double oxMax = double.MinValue, oyMax = double.MinValue, ozMax = double.MinValue;
            var spans = new List<double[]>();

            foreach (var ci in _imports)
            {
                if (ci.Min.X < oxMin) oxMin = ci.Min.X;
                if (ci.Min.Y < oyMin) oyMin = ci.Min.Y;
                if (ci.Min.Z < ozMin) ozMin = ci.Min.Z;
                if (ci.Max.X > oxMax) oxMax = ci.Max.X;
                if (ci.Max.Y > oyMax) oyMax = ci.Max.Y;
                if (ci.Max.Z > ozMax) ozMax = ci.Max.Z;

                if (RayHitsAabb(origin, direction, ci.Min, ci.Max, double.PositiveInfinity))
                    spans.Add(new[] { ci.Min.Z - origin.Z, ci.Max.Z - origin.Z });
            }

            spans.Sort((p, q) => p[0].CompareTo(q[0]));
            double? hit = FindClosestHit(origin, direction);

            var sb = new System.Text.StringBuilder();
            sb.Append($"hanger @ ({origin.X:F1},{origin.Y:F1},{origin.Z:F1}) ft. ");

            if (!any)
            {
                sb.Append("No CAD geometry cached.");
                return sb.ToString();
            }

            bool xyInside = origin.X >= oxMin && origin.X <= oxMax
                         && origin.Y >= oyMin && origin.Y <= oyMax;
            sb.Append($"CAD footprint X[{oxMin:F0}…{oxMax:F0}] Y[{oyMin:F0}…{oyMax:F0}] Z[{ozMin:F0}…{ozMax:F0}]; ");
            sb.Append(xyInside ? "hanger XY is INSIDE footprint. " : "hanger XY is OUTSIDE footprint (XY offset!). ");

            sb.Append($"up-ray crosses {spans.Count} bbox");
            if (spans.Count > 0)
            {
                sb.Append("; nearest Z-spans (ft above hanger): ");
                int show = Math.Min(spans.Count, 4);
                for (int i = 0; i < show; i++)
                    sb.Append($"[{spans[i][0]:F1}…{spans[i][1]:F1}]");
                if (spans.Count > show) sb.Append("…");
            }
            sb.Append(hit.HasValue ? $". Closest triangle hit = {hit.Value:F2} ft." : ". NO triangle hit.");
            return sb.ToString();
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
