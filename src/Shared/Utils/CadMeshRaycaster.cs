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
        /// <summary>
        /// When true, also triangulate ImportInstances nested in linked
        /// Revit models. Default false: the user's STEP/IFC DWG is linked
        /// directly into the host project, and pulling CAD from every
        /// linked Revit model dragged in 600+ imports / 60M+ triangles of
        /// irrelevant geometry (each with its own coordinate base + stray
        /// AutoCAD entities at absurd coordinates), which both killed
        /// performance and made the diagnostics unreadable.
        /// </summary>
        public bool IncludeLinkedDocImports { get; set; } = false;

        public void Build()
        {
            _imports.Clear();

            // Host-doc imports — no link transform.
            foreach (var imp in CollectImports(_doc))
                BuildOne(imp, null, "host");

            if (!IncludeLinkedDocImports) return;

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

                string label = "link:" + (rli.Name ?? "?");
                foreach (var imp in CollectImports(linkDoc))
                    BuildOne(imp, linkXform, label);
            }
        }

        private static IEnumerable<ImportInstance> CollectImports(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>();
        }

        private void BuildOne(ImportInstance imp, Transform linkXform, string source)
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
            TryPlacedWorldBox(imp, out XYZ placedMin, out XYZ placedMax);
            _imports.Add(new CadImport
            {
                Triangles = tris,
                Min = aabb.Min,
                Max = aabb.Max,
                Source = source,
                PlacedMin = placedMin,
                PlacedMax = placedMax
            });
        }

        /// <summary>
        /// Geometry option sets tried in order until one yields triangles.
        /// VIEW-BOUND FIRST — view-bound geometry comes back placed (in the
        /// coordinates the import is displayed at). The no-view variants
        /// returned mis-placed geometry (triangles scattered to Z=-4404 ft,
        /// nothing in the hanger's column), so they're now fallbacks only.
        /// </summary>
        private IEnumerable<Options> GeometryOptionVariants()
        {
            // View-bound gives PLACED coords; IncludeNonVisibleObjects=true
            // gives COMPLETE geometry (faces on layers hidden in the
            // raybounce view are otherwise dropped — and the steel right
            // above a hanger may be exactly that). So that combination is
            // tried first.
            if (_view != null)
            {
                yield return new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = true,
                    View = _view
                };
                yield return new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    View = _view
                };
            }
            yield return new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };
            yield return new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };
        }

        /// <summary>
        /// World-space AABB of the import as Revit places it — from
        /// <c>get_BoundingBox</c> with its <c>Transform</c> applied. This is
        /// the ground truth for where the import actually sits; comparing it
        /// to the AABB of the triangles we extracted reveals any
        /// coordinate-space error in our geometry traversal.
        /// </summary>
        private bool TryPlacedWorldBox(ImportInstance imp, out XYZ min, out XYZ max)
        {
            min = null; max = null;
            BoundingBoxXYZ bb = null;
            try { bb = imp.get_BoundingBox(null); } catch { }
            if (bb == null && _view != null) { try { bb = imp.get_BoundingBox(_view); } catch { } }
            if (bb == null) return false;

            Transform t = bb.Transform ?? Transform.Identity;
            double mnx = double.MaxValue, mny = double.MaxValue, mnz = double.MaxValue;
            double mxx = double.MinValue, mxy = double.MinValue, mxz = double.MinValue;
            double[] xs = { bb.Min.X, bb.Max.X };
            double[] ys = { bb.Min.Y, bb.Max.Y };
            double[] zs = { bb.Min.Z, bb.Max.Z };
            foreach (double x in xs)
                foreach (double y in ys)
                    foreach (double z in zs)
                    {
                        XYZ p = t.OfPoint(new XYZ(x, y, z));
                        if (p.X < mnx) mnx = p.X; if (p.X > mxx) mxx = p.X;
                        if (p.Y < mny) mny = p.Y; if (p.Y > mxy) mxy = p.Y;
                        if (p.Z < mnz) mnz = p.Z; if (p.Z > mxz) mxz = p.Z;
                    }
            min = new XYZ(mnx, mny, mnz);
            max = new XYZ(mxx, mxy, mxz);
            return true;
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

            // ── Column scan ──
            // Count triangles whose XY-projection BBOX contains the hanger
            // XY (i.e. roughly directly above/below). This is the decisive
            // number: if it's 0, NO triangle sits in the hanger's vertical
            // column → the geometry is mis-placed (transform / units), not
            // merely "narrowly missed". If it's > 0 but FindClosestHit
            // found nothing, the ray-triangle math or a placement skew is
            // at fault.
            int colTris = 0;
            double colZNear = double.PositiveInfinity, colZFar = double.NegativeInfinity;
            string colSource = null;
            // Nearest triangle to the hanger in PLAN (XY) — tells us whether
            // the steel is a hair off the exact ray or genuinely not above.
            double nearestXY = double.PositiveInfinity;
            double nearestXYZAbove = double.NaN; // Z (from hanger) of that nearest triangle
            foreach (var ci in _imports)
            {
                if (origin.X < ci.Min.X || origin.X > ci.Max.X ||
                    origin.Y < ci.Min.Y || origin.Y > ci.Max.Y) continue;
                foreach (var tri in ci.Triangles)
                {
                    double cx = (tri.A.X + tri.B.X + tri.C.X) / 3.0;
                    double cy = (tri.A.Y + tri.B.Y + tri.C.Y) / 3.0;
                    double dx = cx - origin.X, dy = cy - origin.Y;
                    double dxy = Math.Sqrt(dx * dx + dy * dy);
                    if (dxy < nearestXY)
                    {
                        nearestXY = dxy;
                        nearestXYZAbove = (tri.A.Z + tri.B.Z + tri.C.Z) / 3.0 - origin.Z;
                    }

                    double txmin = Math.Min(tri.A.X, Math.Min(tri.B.X, tri.C.X));
                    double txmax = Math.Max(tri.A.X, Math.Max(tri.B.X, tri.C.X));
                    if (origin.X < txmin || origin.X > txmax) continue;
                    double tymin = Math.Min(tri.A.Y, Math.Min(tri.B.Y, tri.C.Y));
                    double tymax = Math.Max(tri.A.Y, Math.Max(tri.B.Y, tri.C.Y));
                    if (origin.Y < tymin || origin.Y > tymax) continue;

                    colTris++;
                    double zc = (tri.A.Z + tri.B.Z + tri.C.Z) / 3.0 - origin.Z;
                    if (zc < colZNear) colZNear = zc;
                    if (zc > colZFar) colZFar = zc;
                    if (colSource == null) colSource = ci.Source;
                }
            }

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
            sb.Append(xyInside ? "XY inside footprint. " : "XY OUTSIDE footprint! ");

            // Column-scan result is the headline.
            if (colTris == 0)
            {
                sb.Append("NO triangle directly over hanger. ");
                if (!double.IsPositiveInfinity(nearestXY))
                    sb.Append($"Nearest steel is {nearestXY * 12:F1}\" away in plan, at Z {nearestXYZAbove:F2} ft from hanger. ");
                if (nearestXY < 0.25)
                    sb.Append("→ steel is right there but the ray just grazes past it (precision).");
                else
                    sb.Append("→ hanger is NOT under the steel in plan (offset, or hanger point is off).");
            }
            else
            {
                sb.Append($"{colTris} triangle(s) in column [{colSource}], Z range {colZNear:F2}…{colZFar:F2} ft from hanger. ");
                sb.Append(hit.HasValue ? $"Closest hit = {hit.Value:F2} ft." : "but FindClosestHit got NONE (math/skew).");
            }

            // ── Placed-vs-extracted comparison ──
            // For the import whose PLACED bbox column contains the hanger,
            // show where Revit places it vs where our triangles landed. A
            // mismatch is the transform error, and the placed bbox is the
            // truth we should be matching.
            CadImport pick = null;
            foreach (var ci in _imports)
            {
                if (ci.PlacedMin == null || ci.PlacedMax == null) continue;
                if (origin.X >= ci.PlacedMin.X && origin.X <= ci.PlacedMax.X &&
                    origin.Y >= ci.PlacedMin.Y && origin.Y <= ci.PlacedMax.Y)
                { pick = ci; break; }
            }
            if (pick != null)
            {
                sb.Append($" │ import [{pick.Source}] PLACED Z[{pick.PlacedMin.Z:F1}…{pick.PlacedMax.Z:F1}] " +
                          $"vs my-tris Z[{pick.Min.Z:F1}…{pick.Max.Z:F1}], " +
                          $"PLACED X[{pick.PlacedMin.X:F0}…{pick.PlacedMax.X:F0}] " +
                          $"vs my-tris X[{pick.Min.X:F0}…{pick.Max.X:F0}].");
            }
            else
            {
                sb.Append(" │ no import's PLACED bbox is over the hanger either.");
            }
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
            public XYZ Min;        // AABB of the triangles WE extracted
            public XYZ Max;
            public XYZ PlacedMin;  // AABB of where Revit PLACES the import (ground truth)
            public XYZ PlacedMax;
            public string Source;
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
