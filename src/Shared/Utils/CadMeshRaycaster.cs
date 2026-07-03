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
        private readonly Document _doc;
        private readonly View3D _view;
        private readonly List<CadImport> _imports = new List<CadImport>();

        // ── Diagnostics (populated during Build) ──
        private int _importsSeen;       // total ImportInstance elements enumerated
        private int _importsWithGeom;   // imports that yielded ≥1 triangle
        private readonly GeomCounters _counters = new GeomCounters(); // per-kind geometry tallies

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
            $"objects → Solid={_counters.Solids}, Mesh={_counters.Meshes}, " +
            $"GeomInstance={_counters.Instances}, Curve/PolyLine={_counters.Curves}, other={_counters.Others}; " +
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

            // PLACED bbox is ground truth — where Revit actually shows the
            // import. We use it to pick the geometry variant that is both
            // correctly placed AND most complete.
            TryPlacedWorldBox(imp, out XYZ placedMin, out XYZ placedMax);

            List<RayTri> bestTris = null;
            GrowableBounds bestAabb = null;

            // Try each option set; keep the FIRST that is correctly placed
            // (its triangle bbox center sits within the placed bbox). If
            // none verifies as placed, keep whichever produced the most
            // triangles as a fallback.
            foreach (var opts in GeometryOptionVariants())
            {
                var tris = new List<RayTri>();
                var aabb = new GrowableBounds();

                GeometryElement geom;
                try { geom = imp.get_Geometry(opts); }
                catch { continue; }
                if (geom == null) continue;

                RayMeshMath.CollectTriangles(geom, linkXform, tris, aabb, _counters);
                if (tris.Count == 0) continue;

                if (PlacedRoughlyMatches(aabb, placedMin, placedMax))
                {
                    bestTris = tris;
                    bestAabb = aabb;
                    break; // placed + (variants ordered most-complete-first) → use it
                }

                // Not placed-correct (e.g. symbol-space coords) — remember
                // only if richer than the current fallback.
                if (bestTris == null || tris.Count > bestTris.Count)
                {
                    bestTris = tris;
                    bestAabb = aabb;
                }
            }

            if (bestTris == null || bestTris.Count == 0) return;

            _importsWithGeom++;
            _imports.Add(new CadImport
            {
                Triangles = bestTris,
                Min = bestAabb.Min,
                Max = bestAabb.Max,
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
            // No-view + IncludeNonVisibleObjects gives the MOST COMPLETE
            // geometry (no view clipping, hidden-layer faces included). It
            // sometimes comes back in symbol (un-placed) coordinates, but
            // BuildOne verifies each variant against the PLACED bbox and
            // falls through to the view-bound variants (which are reliably
            // placed) if this one is mis-placed.
            yield return new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };
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
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };
        }

        /// <summary>
        /// True if the triangle AABB sits where Revit places the import —
        /// its center is within one bbox-diagonal of the placed-box center.
        /// Detects gross mis-placement (symbol-space geometry scattered to
        /// Z=-4404 ft) while tolerating partial captures. Returns true when
        /// no placed box is available (nothing to check against).
        /// </summary>
        private static bool PlacedRoughlyMatches(GrowableBounds tri, XYZ placedMin, XYZ placedMax)
        {
            if (placedMin == null || placedMax == null) return true;
            XYZ tMin = tri.Min, tMax = tri.Max;
            double tcx = (tMin.X + tMax.X) * 0.5, tcy = (tMin.Y + tMax.Y) * 0.5, tcz = (tMin.Z + tMax.Z) * 0.5;
            double pcx = (placedMin.X + placedMax.X) * 0.5, pcy = (placedMin.Y + placedMax.Y) * 0.5, pcz = (placedMin.Z + placedMax.Z) * 0.5;
            double dx = tcx - pcx, dy = tcy - pcy, dz = tcz - pcz;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double diag = placedMax.DistanceTo(placedMin);
            return dist <= Math.Max(diag, 50.0);
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
                if (!RayMeshMath.RayHitsAabb(origin, direction, ci.Min, ci.Max, bestT))
                    continue;

                double? t = RayMeshMath.ClosestHit(origin, direction, ci.Triangles, bestT);
                if (t.HasValue && t.Value < bestT)
                    bestT = t.Value;
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

                if (RayMeshMath.RayHitsAabb(origin, direction, ci.Min, ci.Max, double.PositiveInfinity))
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
                sb.Append(hit.HasValue ? $"Closest hit = {hit.Value:F2} ft. " : "but FindClosestHit got NONE (math/skew). ");
                if (!double.IsPositiveInfinity(nearestXY))
                    sb.Append($"(nearest steel in plan {nearestXY * 12:F1}\" away @ Z {nearestXYZAbove:F2} ft)");
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

        private class CadImport
        {
            public List<RayTri> Triangles;
            public XYZ Min;        // AABB of the triangles WE extracted
            public XYZ Max;
            public XYZ PlacedMin;  // AABB of where Revit PLACES the import (ground truth)
            public XYZ PlacedMax;
            public string Source;
        }
    }
}
