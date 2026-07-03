using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Unified "what is above this point?" scanner used to set hanger rod
    /// lengths. Fixes three failure classes the plain
    /// <see cref="ReferenceIntersector"/> approach suffered from:
    ///
    ///  1. PHANTOM PROXIMITY (rods stretching, worst under SLOPED decks in
    ///     linked structural files): the intersector's Proximity is sometimes
    ///     an element-extent / wrong-face distance rather than the real
    ///     underside. Every native candidate is therefore RE-MEASURED against
    ///     the hit element's actual triangulated geometry along the hanger's
    ///     own vertical. A candidate whose real geometry never touches that
    ///     vertical is rejected as phantom and the next-closest candidate is
    ///     tried instead.
    ///
    ///  2. FAN SLOPE BIAS: the multi-ray fan (for narrow steel a couple of
    ///     inches off in plan) used to keep the minimum distance across all
    ///     rays, which on a sloped deck is the downhill sample. Fan hits are
    ///     now re-measured along the CENTER line, so all rays that find the
    ///     same sloped element converge to the exact rod length at the
    ///     hanger's plan position. Offset-only hits (steel genuinely beside
    ///     the hanger) win only when they're shorter than the centered hit by
    ///     more than <see cref="OffsetWinMargin"/>.
    ///
    ///  3. LINKED IFC / DIRECTSHAPE PASS-THROUGH: rays have been observed
    ///     passing straight through linked IFC beams (DirectShape faces
    ///     return unreliable references). A triangle index of DirectShapes
    ///     (host + all links, target categories) — plus Generic Model /
    ///     Mass family instances (STEP/SAT imported into a family) when
    ///     <see cref="IncludeGenericCategories"/> is on — is raycast
    ///     manually with the same Möller-Trumbore math as the CAD import
    ///     path.
    ///
    /// Native, mesh-index, and (optional) CAD-import results are merged per
    /// hanger; the closest verified hit wins.
    /// </summary>
    public class StructureRayScanner
    {
        /// <summary>Final answer for one hanger point.</summary>
        public class ScanHit
        {
            /// <summary>Vertical distance (ft) — the rod length.</summary>
            public double Distance { get; set; }
            public BuiltInCategory Category { get; set; } = BuiltInCategory.INVALID;
            public string CategoryLabel { get; set; } = "Other";
            public string ElementName { get; set; } = "";
            public string LinkName { get; set; } = "";
            /// <summary>"native" (ReferenceIntersector), "mesh" (DirectShape/GM index), "cad" (import raycaster).</summary>
            public string Source { get; set; } = "";
            /// <summary>"centered" (verified on the hanger's own vertical), "offset" (fan ray), "proximity" (unverifiable — intersector value).</summary>
            public string Quality { get; set; } = "";
            /// <summary>Proximity minus verified distance (ft) for centered native hits — how far the raw intersector was off.</summary>
            public double CorrectionFt { get; set; }
        }

        private class Candidate
        {
            public double Distance;
            public int Rank; // 0 = centered/verified, 1 = offset, 2 = proximity-only
            public ScanHit Hit;
        }

        private class MeshEntry
        {
            public List<RayTri> Tris;
            public XYZ Min;
            public XYZ Max;
            public BuiltInCategory Category;
            public string Name = "";
            public string LinkName = "";
        }

        // ── Configuration (set before Build) ──
        /// <summary>Shoot offset rings (2"/4") around each hanger to catch narrow steel a hair off in plan.</summary>
        public bool UseFan { get; set; } = true;
        /// <summary>Re-measure native hits against the element's real triangles; reject phantoms.</summary>
        public bool VerifyWithGeometry { get; set; } = true;
        /// <summary>Also accept Generic Models and Masses (IFC leftovers, STEP-in-family) and index their family instances.</summary>
        public bool IncludeGenericCategories { get; set; } = false;
        /// <summary>Build the DirectShape triangle index (host + links). The linked-IFC safety net.</summary>
        public bool IndexDirectShapes { get; set; } = true;
        /// <summary>Optional CAD import raycaster (DWG/DGN/SAT) built by the caller.</summary>
        public CadMeshRaycaster ImportRaycaster { get; set; }
        /// <summary>Hits beyond this vertical distance (ft) are ignored.</summary>
        public double MaxDistance { get; set; } = 120.0;

        /// <summary>An offset (fan) hit only beats a centered hit when shorter by more than this (ft).</summary>
        public const double OffsetWinMargin = 0.5;
        private const int MaxCandidatesPerRay = 8;
        private const double Ring1Radius = 2.0 / 12.0;
        private const double Ring2Radius = 4.0 / 12.0;
        private const double HullMargin = 10.0; // ft of slack around the hanger footprint for the mesh index

        private readonly Document _doc;
        private readonly View3D _view;
        private readonly List<BuiltInCategory> _targetCats;
        private readonly HashSet<int> _allowedCats = new HashSet<int>();

        private ReferenceIntersector _intersector;
        private readonly List<MeshEntry> _meshEntries = new List<MeshEntry>();
        private readonly Dictionary<string, List<RayTri>> _triCache = new Dictionary<string, List<RayTri>>();
        private readonly Dictionary<ElementId, Transform> _linkXforms = new Dictionary<ElementId, Transform>();

        // ── Diagnostics ──
        public int MeshElementCount => _meshEntries.Count;
        public int MeshTriangleCount => _meshEntries.Sum(e => e.Tris.Count);
        /// <summary>Centered native hits whose verified distance differed from raw Proximity by &gt; ~1/8".</summary>
        public int VerifiedCorrections { get; private set; }
        /// <summary>Largest (signed) proximity-minus-verified correction seen, ft. Positive = intersector was long.</summary>
        public double LargestCorrectionFt { get; private set; }
        /// <summary>Native references whose element geometry never touched the ray — the "rod stretches to a phantom" class.</summary>
        public int PhantomRejections { get; private set; }

        public string DiagnosticsSummary =>
            $"mesh index: {MeshElementCount} elements / {MeshTriangleCount} triangles; " +
            $"verified corrections: {VerifiedCorrections}" +
            (VerifiedCorrections > 0 ? $" (largest {LargestCorrectionFt * 12.0:+0.0;-0.0}\")" : "") +
            $"; phantom references rejected: {PhantomRejections}";

        public StructureRayScanner(Document doc, View3D view, IEnumerable<BuiltInCategory> targetCategories)
        {
            _doc = doc;
            _view = view;
            _targetCats = targetCategories.ToList();
        }

        /// <summary>
        /// Prepares the intersector and (optionally) the DirectShape/GM mesh
        /// index. Call once, after setting the configuration properties and
        /// before the per-hanger <see cref="Scan"/> loop. The hanger points
        /// bound the mesh index spatially so huge links stay cheap.
        /// </summary>
        public void Build(ICollection<XYZ> hangerPoints)
        {
            var cats = new List<BuiltInCategory>(_targetCats);
            if (IncludeGenericCategories)
            {
                if (!cats.Contains(BuiltInCategory.OST_GenericModel)) cats.Add(BuiltInCategory.OST_GenericModel);
                if (!cats.Contains(BuiltInCategory.OST_Mass)) cats.Add(BuiltInCategory.OST_Mass);
            }

            _allowedCats.Clear();
            foreach (var c in cats) _allowedCats.Add((int)c);

            // Which side of a link boundary Revit applies the intersector
            // filter to has shifted between versions — OR-ing the link class
            // in and re-checking the RESOLVED element's category ourselves
            // works on all of them (link hits can neither be silently dropped
            // nor slip through unfiltered).
            ElementFilter filter = new LogicalOrFilter(
                new ElementMulticategoryFilter(cats),
                new ElementClassFilter(typeof(RevitLinkInstance)));

            _intersector = new ReferenceIntersector(filter, FindReferenceTarget.Face, _view)
            {
                FindReferencesInRevitLinks = true
            };

            if (IndexDirectShapes && hangerPoints != null && hangerPoints.Count > 0)
                BuildMeshIndex(hangerPoints);
        }

        /// <summary>
        /// Scans straight up from <paramref name="origin"/> and returns the
        /// closest verified structural hit, or null when nothing is found
        /// within <see cref="MaxDistance"/>.
        /// </summary>
        public ScanHit Scan(XYZ origin)
        {
            var candidates = new List<Candidate>();

            // ── Center ray, all three sources ──
            AddNative(candidates, origin, XYZ.Zero);
            AddMesh(candidates, origin, XYZ.Zero);
            AddImports(candidates, origin, XYZ.Zero);

            if (UseFan)
            {
                // Ring 1 always runs, for ALL sources — it's also the deck-
                // joint / nearby-lower-steel guard, and that guard must see
                // IFC/CAD steel (the classes native rays are blind to) just
                // as well as native framing.
                foreach (var off in RingOffsets(Ring1Radius))
                {
                    AddNative(candidates, origin, off);
                    AddMesh(candidates, origin, off);
                    AddImports(candidates, origin, off);
                }

                // Ring 2 only when the center still has nothing verified —
                // it exists purely to rescue total misses.
                if (!candidates.Any(c => c.Rank == 0))
                {
                    foreach (var off in RingOffsets(Ring2Radius))
                    {
                        AddNative(candidates, origin, off);
                        AddMesh(candidates, origin, off);
                        AddImports(candidates, origin, off);
                    }
                }
            }

            return Pick(candidates);
        }

        // ─────────────────────────── selection ───────────────────────────

        private ScanHit Pick(List<Candidate> cands)
        {
            if (cands.Count == 0) return null;

            Candidate centered = cands.Where(c => c.Rank == 0).OrderBy(c => c.Distance).FirstOrDefault();
            Candidate offset   = cands.Where(c => c.Rank == 1).OrderBy(c => c.Distance).FirstOrDefault();
            Candidate prox     = cands.Where(c => c.Rank == 2).OrderBy(c => c.Distance).FirstOrDefault();

            if (centered != null)
            {
                // Offset steel wins only when meaningfully SHORTER — the rod
                // should stop at nearby structure instead of stretching
                // through a deck joint the center ray slipped through. An
                // unverified proximity hit never overrides verified geometry:
                // shorter-but-unverified is exactly the phantom class.
                if (offset != null && offset.Distance < centered.Distance - OffsetWinMargin)
                    return offset.Hit;
                return centered.Hit;
            }

            // No verified centered hit: take the closest of what's left.
            if (offset != null && prox != null)
                return offset.Distance <= prox.Distance ? offset.Hit : prox.Hit;
            return (offset ?? prox)?.Hit;
        }

        // ─────────────────────────── native path ───────────────────────────

        private void AddNative(List<Candidate> list, XYZ center, XYZ off)
        {
            if (_intersector == null) return;

            IList<ReferenceWithContext> refs;
            try { refs = _intersector.Find(center + off, XYZ.BasisZ); }
            catch { return; }
            if (refs == null || refs.Count == 0) return;

            bool isCenter = off.IsZeroLength();
            var seen = new HashSet<string>();
            int examined = 0;

            Candidate bestCentered = null;
            Candidate bestOffset = null;
            Candidate bestProx = null;

            // FindNearest has empirically returned a farther face over a
            // closer one — always Find() and walk the sorted list ourselves.
            // And because Proximity itself is untrusted (it's what produces
            // the phantom distances), we can't stop at the first candidate
            // that verifies: an element whose phantom proximity sorts it
            // LATER can still have the closest REAL geometry. Verify every
            // examined candidate and keep the minimum verified distance.
            foreach (var rwc in refs
                .Where(r => r != null
                    && r.Proximity > RayMeshMath.MIN_HIT_DISTANCE
                    && r.Proximity <= MaxDistance)
                .OrderBy(r => r.Proximity))
            {
                if (examined >= MaxCandidatesPerRay) break;

                Reference r = rwc.GetReference();
                if (r == null) continue;

                if (!ResolveHit(r, out Element elem, out Transform xform, out string linkName, out string ownerKey))
                    continue;

                // Key on the link INSTANCE id — two placed copies of the same
                // link share element ids but have different transforms.
                string key = ownerKey + "|" + elem.Id.IntegerValue;
                if (!seen.Add(key)) continue; // entry+exit faces of the same element

                int catId = elem.Category?.Id.IntegerValue ?? 0;
                if (!_allowedCats.Contains(catId)) continue;

                // Budget counts only genuine structural candidates — on Revit
                // versions where the intersector filter tests the link
                // instance, every linked element streams through here and
                // category rejects must not exhaust the walk before the deck.
                examined++;

                double prox = rwc.Proximity;

                if (VerifyWithGeometry)
                {
                    List<RayTri> tris = GetTriangles(elem, xform, key);
                    if (tris.Count > 0)
                    {
                        // Re-measure along the hanger's OWN vertical — exact on
                        // sloped faces, immune to extent/wrong-face proximity.
                        double? dc = RayMeshMath.ClosestHit(center, XYZ.BasisZ, tris, MaxDistance);
                        if (dc.HasValue)
                        {
                            if (bestCentered == null || dc.Value < bestCentered.Distance)
                            {
                                double corr = isCenter ? prox - dc.Value : 0.0;
                                bestCentered = Make(dc.Value, 0, elem, linkName, "native", "centered", corr);
                            }
                            continue;
                        }

                        if (!isCenter)
                        {
                            double? doff = RayMeshMath.ClosestHit(center + off, XYZ.BasisZ, tris, MaxDistance);
                            if (doff.HasValue)
                            {
                                if (bestOffset == null || doff.Value < bestOffset.Distance)
                                    bestOffset = Make(doff.Value, 1, elem, linkName, "native", "offset", 0.0);
                                continue;
                            }
                        }

                        // The element's real geometry isn't on this ray at all —
                        // phantom reference (extent / plane hit). Next candidate.
                        PhantomRejections++;
                        continue;
                    }
                    // Couldn't triangulate — fall through and trust the reference.
                }

                // Unverified: prefer the reference's actual hit point over
                // Proximity (Proximity has been observed wrong against links).
                double dist = prox;
                XYZ gp = null;
                try { gp = r.GlobalPoint; } catch { }
                if (gp != null)
                {
                    double dz = gp.Z - center.Z; // off is horizontal, so this is the vertical either way
                    if (dz > RayMeshMath.MIN_HIT_DISTANCE && dz <= MaxDistance)
                        dist = dz;
                }

                // Unverifiable hits on offset rays are too risky to keep.
                if (isCenter && (bestProx == null || dist < bestProx.Distance))
                    bestProx = Make(dist, 2, elem, linkName, "native", "proximity", 0.0);

                if (!VerifyWithGeometry)
                    break; // legacy mode: nearest-by-proximity candidate only
            }

            if (bestCentered != null)
            {
                if (isCenter && Math.Abs(bestCentered.Hit.CorrectionFt) > 0.01)
                {
                    VerifiedCorrections++;
                    if (Math.Abs(bestCentered.Hit.CorrectionFt) > Math.Abs(LargestCorrectionFt))
                        LargestCorrectionFt = bestCentered.Hit.CorrectionFt;
                }
                list.Add(bestCentered);
            }
            if (bestOffset != null) list.Add(bestOffset);
            // An unverified center hit is only a last resort — but Pick()
            // handles rank precedence globally, so always surface it.
            if (bestProx != null) list.Add(bestProx);
        }

        private bool ResolveHit(Reference r, out Element elem, out Transform xform,
            out string linkName, out string ownerKey)
        {
            elem = null;
            xform = null;
            linkName = "";
            ownerKey = "host";

            if (r.LinkedElementId != ElementId.InvalidElementId)
            {
                var rli = _doc.GetElement(r.ElementId) as RevitLinkInstance;
                Document linkDoc = rli?.GetLinkDocument();
                if (linkDoc == null) return false;

                elem = linkDoc.GetElement(r.LinkedElementId);
                if (elem == null) return false;

                try { linkName = rli.Name ?? ""; } catch { }
                ownerKey = "rli" + rli.Id.IntegerValue;

                if (!_linkXforms.TryGetValue(rli.Id, out xform))
                {
                    try { xform = rli.GetTotalTransform(); } catch { xform = null; }
                    if (xform != null && xform.IsIdentity) xform = null;
                    _linkXforms[rli.Id] = xform;
                }
                return true;
            }

            elem = _doc.GetElement(r.ElementId);
            if (elem == null || elem is RevitLinkInstance) return false;
            return true;
        }

        private List<RayTri> GetTriangles(Element elem, Transform xform, string key)
        {
            if (_triCache.TryGetValue(key, out var cached)) return cached;
            List<RayTri> tris;
            try { tris = RayMeshMath.TriangulateElement(elem, xform); }
            catch { tris = new List<RayTri>(); }
            _triCache[key] = tris; // cache empties too — don't re-try failures
            return tris;
        }

        // ─────────────────────── mesh-index path ───────────────────────

        private void BuildMeshIndex(ICollection<XYZ> pts)
        {
            double xMin = double.MaxValue, yMin = double.MaxValue, zMin = double.MaxValue;
            double xMax = double.MinValue, yMax = double.MinValue;
            foreach (var p in pts)
            {
                if (p.X < xMin) xMin = p.X;
                if (p.X > xMax) xMax = p.X;
                if (p.Y < yMin) yMin = p.Y;
                if (p.Y > yMax) yMax = p.Y;
                if (p.Z < zMin) zMin = p.Z;
            }
            xMin -= HullMargin; xMax += HullMargin;
            yMin -= HullMargin; yMax += HullMargin;
            double zFloor = zMin - 1.0;

            IndexDoc(_doc, null, "", xMin, xMax, yMin, yMax, zFloor);

            foreach (var rli in new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>())
            {
                Document linkDoc = rli.GetLinkDocument();
                if (linkDoc == null) continue;

                Transform xform = null;
                try { xform = rli.GetTotalTransform(); } catch { }
                if (xform != null && xform.IsIdentity) xform = null;

                string linkName = "";
                try { linkName = rli.Name ?? ""; } catch { }

                IndexDoc(linkDoc, xform, linkName, xMin, xMax, yMin, yMax, zFloor);
            }
        }

        private void IndexDoc(Document d, Transform xform, string linkName,
            double xMin, double xMax, double yMin, double yMax, double zFloor)
        {
            // Honor visibility like the native intersector does: host-doc
            // collectors are bound to the raybounce view (hidden worksets /
            // design options / demolished phases are excluded). A host view
            // can't scope a linked doc's collector, so links get a
            // design-option check per element instead (below).
            bool isHost = d.Equals(_doc);

            FilteredElementCollector NewCollector()
            {
                if (isHost && _view != null)
                {
                    try { return new FilteredElementCollector(d, _view.Id); }
                    catch { }
                }
                return new FilteredElementCollector(d);
            }

            // DirectShapes — how IFC links arrive. ReferenceIntersector face
            // references on them are unreliable (rays observed passing
            // straight through linked IFC beams), so they get manual rays.
            foreach (Element e in NewCollector().OfClass(typeof(DirectShape)))
                TryIndexElement(e, xform, linkName, xMin, xMax, yMin, yMax, zFloor);

            // STEP/SAT imported into a Generic Model / Mass family →
            // FamilyInstance whose imported solid may not raycast natively.
            if (IncludeGenericCategories)
            {
                var gmFilter = new ElementMulticategoryFilter(new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_GenericModel,
                    BuiltInCategory.OST_Mass
                });
                foreach (Element e in NewCollector()
                    .OfClass(typeof(FamilyInstance))
                    .WherePasses(gmFilter))
                {
                    TryIndexElement(e, xform, linkName, xMin, xMax, yMin, yMax, zFloor);
                }
            }
        }

        private void TryIndexElement(Element e, Transform xform, string linkName,
            double xMin, double xMax, double yMin, double yMax, double zFloor)
        {
            int catId = e.Category?.Id.IntegerValue ?? 0;
            if (!_allowedCats.Contains(catId)) return;

            // Skip non-primary design options (matters for linked docs, whose
            // collector can't be scoped to the raybounce view).
            try
            {
                if (e.DesignOption != null && !e.DesignOption.IsPrimary) return;
            }
            catch { }

            // Cheap spatial reject: the element's world bbox must overlap the
            // hangers' XY footprint and not sit entirely below them. All 8
            // corners are transformed — with a rotated bbox/link transform,
            // min/max of two opposite corners is not the true AABB.
            BoundingBoxXYZ bb = null;
            try { bb = e.get_BoundingBox(null); } catch { }
            if (bb == null) return;

            Transform bt = bb.Transform ?? Transform.Identity;
            double bxMin = double.MaxValue, byMin = double.MaxValue;
            double bxMax = double.MinValue, byMax = double.MinValue, bzMax = double.MinValue;
            double[] xs = { bb.Min.X, bb.Max.X };
            double[] ys = { bb.Min.Y, bb.Max.Y };
            double[] zs = { bb.Min.Z, bb.Max.Z };
            foreach (double cx in xs)
                foreach (double cy in ys)
                    foreach (double cz in zs)
                    {
                        XYZ p = bt.OfPoint(new XYZ(cx, cy, cz));
                        if (xform != null) p = xform.OfPoint(p);
                        if (p.X < bxMin) bxMin = p.X;
                        if (p.X > bxMax) bxMax = p.X;
                        if (p.Y < byMin) byMin = p.Y;
                        if (p.Y > byMax) byMax = p.Y;
                        if (p.Z > bzMax) bzMax = p.Z;
                    }

            if (bxMax < xMin || bxMin > xMax || byMax < yMin || byMin > yMax) return;
            if (bzMax < zFloor) return;

            var bounds = new GrowableBounds();
            List<RayTri> tris;
            try { tris = RayMeshMath.TriangulateElement(e, xform, bounds); }
            catch { return; }
            if (tris.Count == 0 || !bounds.HasData) return;

            string name = "";
            try { name = $"{e.Name} #{e.Id.IntegerValue}"; } catch { }

            _meshEntries.Add(new MeshEntry
            {
                Tris = tris,
                Min = bounds.Min,
                Max = bounds.Max,
                Category = (BuiltInCategory)catId,
                Name = name,
                LinkName = linkName
            });
        }

        private void AddMesh(List<Candidate> list, XYZ center, XYZ off)
        {
            if (_meshEntries.Count == 0) return;

            XYZ o = center + off;
            MeshEntry bestEntry = null;
            double bestT = MaxDistance;

            foreach (var e in _meshEntries)
            {
                if (!RayMeshMath.RayHitsAabb(o, XYZ.BasisZ, e.Min, e.Max, bestT)) continue;
                double? t = RayMeshMath.ClosestHit(o, XYZ.BasisZ, e.Tris, bestT);
                if (t.HasValue && t.Value < bestT)
                {
                    bestT = t.Value;
                    bestEntry = e;
                }
            }
            if (bestEntry == null) return;

            int rank = off.IsZeroLength() ? 0 : 1;
            list.Add(new Candidate
            {
                Distance = bestT,
                Rank = rank,
                Hit = new ScanHit
                {
                    Distance = bestT,
                    Category = bestEntry.Category,
                    CategoryLabel = GetCategoryLabel(bestEntry.Category),
                    ElementName = bestEntry.Name,
                    LinkName = bestEntry.LinkName,
                    Source = "mesh",
                    Quality = rank == 0 ? "centered" : "offset"
                }
            });
        }

        // ─────────────────────── import (CAD) path ───────────────────────

        private void AddImports(List<Candidate> list, XYZ center, XYZ off)
        {
            if (ImportRaycaster == null) return;

            double? t = null;
            try { t = ImportRaycaster.FindClosestHit(center + off, XYZ.BasisZ); }
            catch { }
            if (!t.HasValue || t.Value > MaxDistance) return;

            int rank = off.IsZeroLength() ? 0 : 1;
            list.Add(new Candidate
            {
                Distance = t.Value,
                Rank = rank,
                Hit = new ScanHit
                {
                    Distance = t.Value,
                    Category = BuiltInCategory.INVALID,
                    CategoryLabel = "Imported CAD",
                    ElementName = "",
                    LinkName = "",
                    Source = "cad",
                    Quality = rank == 0 ? "centered" : "offset"
                }
            });
        }

        // ─────────────────────────── helpers ───────────────────────────

        private static IEnumerable<XYZ> RingOffsets(double radius)
        {
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4.0;
                yield return new XYZ(Math.Cos(a) * radius, Math.Sin(a) * radius, 0);
            }
        }

        private Candidate Make(double dist, int rank, Element elem, string linkName,
            string source, string quality, double corr)
        {
            int catId = elem.Category?.Id.IntegerValue ?? 0;
            var cat = (BuiltInCategory)catId;
            string name = "";
            try { name = $"{elem.Name} #{elem.Id.IntegerValue}"; } catch { }

            return new Candidate
            {
                Distance = dist,
                Rank = rank,
                Hit = new ScanHit
                {
                    Distance = dist,
                    Category = cat,
                    CategoryLabel = GetCategoryLabel(cat),
                    ElementName = name,
                    LinkName = linkName,
                    Source = source,
                    Quality = quality,
                    CorrectionFt = corr
                }
            };
        }

        public static string GetCategoryLabel(BuiltInCategory cat)
        {
            switch (cat)
            {
                case BuiltInCategory.OST_Floors: return "Floors";
                case BuiltInCategory.OST_Stairs: return "Stairs";
                case BuiltInCategory.OST_Roofs: return "Roofs";
                case BuiltInCategory.OST_StructuralFraming: return "Structural Framing";
                case BuiltInCategory.OST_StructuralColumns: return "Structural Columns";
                case BuiltInCategory.OST_StructuralTruss: return "Structural Trusses";
                case BuiltInCategory.OST_StructuralFoundation: return "Structural Foundations";
                case BuiltInCategory.OST_GenericModel: return "Generic Model";
                case BuiltInCategory.OST_Mass: return "Mass";
                default: return "Other";
            }
        }
    }
}
