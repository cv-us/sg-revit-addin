using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WinForms = System.Windows.Forms;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Read-only diagnostic for the "trace a linked coordination model into real pipe"
    /// question. Point it at a linked/imported CAD (e.g. an NWC round-tripped through
    /// FBX -> 3ds Max -> DWG) and it reports what Revit ACTUALLY hands the API, which
    /// decides how hard the conversion is:
    ///
    ///   • GEOMETRY KIND — Solid vs Mesh. Solids with <see cref="CylindricalFace"/>
    ///     are the jackpot: exact axis + radius, no cylinder fitting at all. A pile of
    ///     <see cref="Mesh"/> means fitting cylinders to triangle soup.
    ///   • SEGMENTATION — how many separate solids/instances. If every pipe arrives as
    ///     its own solid, clustering is free; one merged blob means segmenting first.
    ///   • SIZES — cylindrical-face radii clustered and matched to nominal steel pipe OD,
    ///     so we can see whether real pipe sizes are recoverable.
    ///   • SLOPE — the pitch of every cylinder axis, in in/10 ft.
    ///   • COORDINATE MAGNITUDE — far-from-origin geometry (state-plane) is the
    ///     precision trap: float32 in the FBX leg turns into vertex jitter, and slope is
    ///     the first casualty (a level pipe reads as pitched) well before diameter is.
    ///   • LAYERS — GraphicsStyle names that survived the round trip (the only metadata
    ///     left once FBX has thrown away sizes and systems).
    ///
    /// Nothing is modified. Output is a copyable text report.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class InspectCadGeometryCommand : IExternalCommand
    {
        // Sample this many cylindrical faces for the detail table (the histograms use all).
        private const int DetailSample = 40;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var imports = PickImports(uidoc, doc);
                if (imports == null) return Result.Cancelled;
                if (imports.Count == 0)
                {
                    TaskDialog.Show("Inspect CAD Geometry",
                        "No CAD imports/links found in this document.\n\n" +
                        "Link the DWG first (Insert > Link CAD), then run this again.");
                    return Result.Cancelled;
                }

                var sb = new StringBuilder();
                sb.AppendLine("INSPECT CAD GEOMETRY");
                sb.AppendLine($"Document: {doc.Title}");
                sb.AppendLine($"Imports inspected: {imports.Count}");
                sb.AppendLine(new string('=', 78));

                var all = new Stats();
                foreach (Element imp in imports)
                    DumpImport(doc, imp, all, sb);

                sb.AppendLine();
                sb.AppendLine(new string('=', 78));
                sb.AppendLine("VERDICT");
                sb.AppendLine(new string('=', 78));
                Verdict(all, sb);

                ShowReport(sb.ToString());
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        /// <summary>Current selection if it holds imports, else every ImportInstance in the doc.</summary>
        private static List<Element> PickImports(UIDocument uidoc, Document doc)
        {
            var sel = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id))
                          .OfType<ImportInstance>().Cast<Element>().ToList();
            if (sel.Count > 0) return sel;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<Element>()
                .ToList();
        }

        private static void DumpImport(Document doc, Element imp, Stats all, StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine($"IMPORT: {SafeName(imp)}   (id {imp.Id.IntegerValue})");

            var ii = imp as ImportInstance;
            if (ii != null)
                sb.AppendLine($"  pinned={ii.Pinned}   category={imp.Category?.Name ?? "(none)"}");

            var opt = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geom = null;
            try { geom = imp.get_Geometry(opt); } catch (Exception ex) { sb.AppendLine($"  !! get_Geometry threw: {ex.Message}"); }
            if (geom == null) { sb.AppendLine("  !! no geometry returned"); return; }

            var s = new Stats();
            Walk(doc, geom, Transform.Identity, s, 0);

            sb.AppendLine($"  geometry objects: Solid={s.Solids}  Mesh={s.Meshes}  " +
                          $"Curve={s.Curves}  GeometryInstance={s.Instances}  other={s.Other}");
            sb.AppendLine($"  solids with volume: {s.SolidsWithVolume} (of which NEGATIVE/inverted: {s.NegativeVolume})" +
                          $"   total triangles (meshes): {s.Triangles}");
            sb.AppendLine($"  faces: total={s.Faces}  CYLINDRICAL={s.CylFaces}  planar={s.PlanarFaces}  " +
                          $"conical={s.ConicalFaces}  revolved={s.RevolvedFaces}  other={s.OtherFaces}");
            if (s.Layers.Count > 0)
                sb.AppendLine($"  layers/styles seen ({s.Layers.Count}): " +
                              string.Join(", ", s.Layers.OrderBy(x => x).Take(25)) +
                              (s.Layers.Count > 25 ? ", ..." : ""));

            if (s.Cylinders.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  --- CYLINDER RADII -> nominal pipe size ({s.Cylinders.Count} cylindrical faces) ---");
                foreach (var grp in s.Cylinders.GroupBy(c => Math.Round(c.RadiusIn, 3))
                                               .OrderByDescending(g => g.Count()))
                {
                    double odIn = grp.Key * 2;
                    string nom = Nominal(odIn, out double offMil);
                    sb.AppendLine($"    r={grp.Key,7:0.###}in  OD={odIn,7:0.###}in  x{grp.Count(),-5} " +
                                  $"-> {nom} (off {offMil:0.#} mil)");
                }

                sb.AppendLine();
                sb.AppendLine($"  --- CYLINDER AXES (first {Math.Min(DetailSample, s.Cylinders.Count)}) ---");
                sb.AppendLine("      OD(in)   len(ft)   slope(in/10ft)  axis(x,y,z)");
                foreach (var c in s.Cylinders.Take(DetailSample))
                    sb.AppendLine($"      {c.RadiusIn * 2,6:0.###}  {c.LengthFt,8:0.##}   {c.SlopeIn10,12:0.####}   " +
                                  $"({c.Axis.X:0.###}, {c.Axis.Y:0.###}, {c.Axis.Z:0.###})");

                int level = s.Cylinders.Count(c => Math.Abs(c.SlopeIn10) < 0.01);
                int vert = s.Cylinders.Count(c => Math.Abs(c.Axis.Z) > 0.99);
                int sloped = s.Cylinders.Count - level - vert;
                sb.AppendLine();
                sb.AppendLine($"  slope breakdown: level={level}  sloped={sloped}  vertical={vert}");
                if (sloped > 0)
                {
                    var pitches = s.Cylinders.Where(c => Math.Abs(c.SlopeIn10) >= 0.01 && Math.Abs(c.Axis.Z) <= 0.99)
                                             .Select(c => Math.Abs(c.SlopeIn10)).OrderBy(x => x).ToList();
                    sb.AppendLine($"  sloped pitches: min={pitches.First():0.###}  " +
                                  $"median={pitches[pitches.Count / 2]:0.###}  max={pitches.Last():0.###} in/10ft");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"  coordinate extent: X [{s.Min.X:0.#} .. {s.Max.X:0.#}]  " +
                          $"Y [{s.Min.Y:0.#} .. {s.Max.Y:0.#}]  Z [{s.Min.Z:0.#} .. {s.Max.Z:0.#}]  (ft)");
            double far = Math.Max(Math.Max(Math.Abs(s.Min.X), Math.Abs(s.Max.X)),
                                  Math.Max(Math.Abs(s.Min.Y), Math.Abs(s.Max.Y)));
            sb.AppendLine($"  farthest coordinate from origin: {far:0.#} ft  " +
                          $"-> float32 ulp there ~= {(far * Math.Pow(2, -23)) * 12000:0.#} mil");

            try { SegmentationProbe(doc, geom, sb); }
            catch (Exception ex) { sb.AppendLine($"  !! segmentation probe threw: {ex.Message}"); }

            all.Merge(s);
        }

        /// <summary>Recursively walk geometry, tallying kinds and harvesting cylindrical faces.</summary>
        private static void Walk(Document doc, GeometryElement geom, Transform xf, Stats s, int depth)
        {
            if (geom == null || depth > 8) return;

            foreach (GeometryObject obj in geom)
            {
                if (obj == null) continue;

                try
                {
                    var gs = doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
                    if (gs != null && !string.IsNullOrEmpty(gs.Name)) s.Layers.Add(gs.Name);
                }
                catch { }

                var inst = obj as GeometryInstance;
                if (inst != null)
                {
                    s.Instances++;
                    // GetInstanceGeometry() returns owning-doc coordinates with placement baked in,
                    // matching how CadMeshRaycaster handles host-doc imports.
                    Walk(doc, inst.GetInstanceGeometry(), xf, s, depth + 1);
                    continue;
                }

                var solid = obj as Solid;
                if (solid != null)
                {
                    s.Solids++;
                    double vol = 0;
                    try { vol = solid.Volume; } catch { }
                    // NOTE: use Abs. Imported CAD often comes back with INVERTED face
                    // orientation, which Revit reports as a NEGATIVE volume — that is a
                    // closed solid with flipped normals, not an empty one. (v1 tested
                    // vol > 0 and so reported "solids with volume: 0" on a solid whose
                    // volume was -131.8 cu ft, which read as "open shells". It wasn't.)
                    if (Math.Abs(vol) > 1e-9) s.SolidsWithVolume++;
                    if (vol < -1e-9) s.NegativeVolume++;

                    foreach (Face f in solid.Faces)
                    {
                        s.Faces++;
                        var cyl = f as CylindricalFace;
                        if (cyl != null)
                        {
                            s.CylFaces++;
                            RecordCylinder(cyl, xf, s);
                        }
                        else if (f is PlanarFace) s.PlanarFaces++;
                        else if (f is ConicalFace) s.ConicalFaces++;
                        else if (f is RevolvedFace) s.RevolvedFaces++;
                        else s.OtherFaces++;
                    }

                    // Bounds from the solid's tessellation.
                    try
                    {
                        foreach (Face f in solid.Faces)
                        {
                            Mesh m = f.Triangulate();
                            if (m == null) continue;
                            for (int i = 0; i < m.Vertices.Count; i++) s.Grow(xf.OfPoint(m.Vertices[i]));
                        }
                    }
                    catch { }
                    continue;
                }

                var mesh = obj as Mesh;
                if (mesh != null)
                {
                    s.Meshes++;
                    s.Triangles += mesh.NumTriangles;
                    for (int i = 0; i < mesh.Vertices.Count; i++) s.Grow(xf.OfPoint(mesh.Vertices[i]));
                    continue;
                }

                if (obj is Curve || obj is PolyLine) { s.Curves++; continue; }
                s.Other++;
            }
        }

        private static void RecordCylinder(CylindricalFace cyl, Transform xf, Stats s)
        {
            try
            {
                XYZ axis = xf.OfVector(cyl.Axis).Normalize();
                // CylindricalFace exposes its radius as a VECTOR (indexed); its length is the radius.
                double radFt = 0;
                try { radFt = xf.OfVector(cyl.get_Radius(0)).GetLength(); } catch { }
                if (radFt <= 1e-9) return;

                // Axial extent from the face's own tessellation.
                double lo = double.MaxValue, hi = double.MinValue;
                Mesh m = cyl.Triangulate();
                if (m != null)
                    for (int i = 0; i < m.Vertices.Count; i++)
                    {
                        double d = xf.OfPoint(m.Vertices[i]).DotProduct(axis);
                        if (d < lo) lo = d;
                        if (d > hi) hi = d;
                    }

                XYZ a = axis.Z < 0 ? axis.Negate() : axis;
                double run = Math.Sqrt(a.X * a.X + a.Y * a.Y);
                double slope = run < 1e-9 ? 0.0 : a.Z / run * 120.0;   // in per 10 ft

                s.Cylinders.Add(new Cyl
                {
                    RadiusIn = radFt * 12.0,
                    LengthFt = (hi > lo) ? hi - lo : 0.0,
                    Axis = a,
                    SlopeIn10 = slope
                });
            }
            catch { }
        }

        // ── Segmentation probe ───────────────────────────────────────────────────────
        // The v1 report answered "is the geometry there" (yes) and "is it analytic" (no,
        // faceted). What decides the project's size is whether the merged solids can be
        // split back into individual pipes. Two routes, cheapest first:
        //   1. SolidUtils.SplitVolumes — free if the solids are valid closed volumes.
        //   2. Face adjacency via Edge.GetFace(0/1) — TOPOLOGICAL, so no tolerance to tune;
        //      connected components of that graph are the separable objects.
        // Then we actually fit each component and see whether it lands on a real pipe size.

        private static void CollectSolids(GeometryElement geom, List<Solid> outp, int depth)
        {
            if (geom == null || depth > 8) return;
            foreach (GeometryObject obj in geom)
            {
                var inst = obj as GeometryInstance;
                if (inst != null) { CollectSolids(inst.GetInstanceGeometry(), outp, depth + 1); continue; }
                var sol = obj as Solid;
                if (sol != null && sol.Faces.Size > 0) outp.Add(sol);
            }
        }

        /// <summary>Quantised endpoint-pair key for an edge. Revit hands back a FRESH Face/Edge
        /// wrapper on every call, so reference equality can't be used to match an Edge's faces
        /// to the solid's face list (v1 of this probe tried that: all 42,089 edges failed to
        /// resolve and every face came back as its own component). Keying on geometry instead
        /// sidesteps object identity entirely.</summary>
        private static long EdgeKey(XYZ a, XYZ b)
        {
            // Order-independent so both faces on an edge produce the same key.
            if (a.X > b.X || (a.X == b.X && (a.Y > b.Y || (a.Y == b.Y && a.Z > b.Z))))
            { XYZ t = a; a = b; b = t; }
            const double Q = 1.0 / 512.0;   // ~1/40 in — finer than any real joint, coarser than round-off
            unchecked
            {
                long h = 17;
                h = h * 31 + (long)Math.Round(a.X / Q);
                h = h * 31 + (long)Math.Round(a.Y / Q);
                h = h * 31 + (long)Math.Round(a.Z / Q);
                h = h * 31 + (long)Math.Round(b.X / Q);
                h = h * 31 + (long)Math.Round(b.Y / Q);
                h = h * 31 + (long)Math.Round(b.Z / Q);
                return h;
            }
        }

        /// <summary>Faces of a solid with their normals, areas and shared-edge adjacency.</summary>
        private class FaceGraph
        {
            public readonly List<Face> F = new List<Face>();
            public readonly List<XYZ> N = new List<XYZ>();       // null for non-planar
            public readonly List<double> A = new List<double>();
            public readonly List<List<int>> Adj = new List<List<int>>();
        }

        private static FaceGraph BuildGraph(Solid s, out int edgeUses, out int matched)
        {
            var g = new FaceGraph();
            foreach (Face f in s.Faces)
            {
                var pf = f as PlanarFace;
                g.F.Add(f);
                g.N.Add(pf != null ? pf.FaceNormal : null);
                double a = 0; try { a = f.Area; } catch { }
                g.A.Add(a);
                g.Adj.Add(new List<int>());
            }

            var byKey = new Dictionary<long, int>();
            edgeUses = 0; matched = 0;
            for (int i = 0; i < g.F.Count; i++)
            {
                EdgeArrayArray loops;
                try { loops = g.F[i].EdgeLoops; } catch { continue; }
                if (loops == null) continue;
                foreach (EdgeArray loop in loops)
                    foreach (Edge e in loop)
                    {
                        Curve c;
                        try { c = e.AsCurve(); } catch { continue; }
                        if (c == null) continue;
                        long k = EdgeKey(c.GetEndPoint(0), c.GetEndPoint(1));
                        edgeUses++;
                        int other;
                        if (byKey.TryGetValue(k, out other))
                        { matched++; g.Adj[i].Add(other); g.Adj[other].Add(i); }
                        else byKey[k] = i;
                    }
            }
            return g;
        }

        /// <summary>Connected components over the shared-edge adjacency.</summary>
        private static List<List<int>> Components(FaceGraph g)
        {
            var comps = new List<List<int>>();
            var seen = new bool[g.F.Count];
            for (int i = 0; i < g.F.Count; i++)
            {
                if (seen[i]) continue;
                var comp = new List<int>();
                var q = new Queue<int>();
                q.Enqueue(i); seen[i] = true;
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    comp.Add(cur);
                    foreach (int nb in g.Adj[cur]) if (!seen[nb]) { seen[nb] = true; q.Enqueue(nb); }
                }
                comps.Add(comp);
            }
            return comps;
        }

        /// <summary>Split one welded run into individual straight pipes by REGION GROWING on
        /// an exact axis.
        ///
        /// The v1 heuristic ("a side facet's longest edge is parallel to the axis") is wrong
        /// here: this geometry is TRIANGULATED (2E/F = 3.21), so the longest edge of a split
        /// facet is the DIAGONAL — 5.2 deg off-axis, tilting opposite ways on alternating
        /// triangles. Binning on that, plus on perpendicular offset, shattered every cylinder
        /// into 1-4 face fragments (26,217 faces -> 22,270 "clusters").
        ///
        /// Instead: for two ADJACENT faces on the same cylinder the normals are both
        /// perpendicular to the axis, so n1 x n2 IS the axis — exactly, whatever the
        /// triangulation. Seed on such a pair, then flood outward taking every face whose
        /// normal is perpendicular to that axis. Growth stops naturally at a joint, where the
        /// faces belong to a different axis.</summary>
        private static bool Solve3(double[,] M, double[] b, double[] outp)
        {
            double[,] A = (double[,])M.Clone(); double[] r = (double[])b.Clone();
            for (int c = 0; c < 3; c++)
            {
                int piv = c;
                for (int q = c + 1; q < 3; q++) if (Math.Abs(A[q, c]) > Math.Abs(A[piv, c])) piv = q;
                if (Math.Abs(A[piv, c]) < 1e-12) return false;
                if (piv != c)
                {
                    for (int j = 0; j < 3; j++) { double tv = A[c, j]; A[c, j] = A[piv, j]; A[piv, j] = tv; }
                    double tb = r[c]; r[c] = r[piv]; r[piv] = tb;
                }
                for (int q = c + 1; q < 3; q++)
                {
                    double f = A[q, c] / A[c, c];
                    for (int j = c; j < 3; j++) A[q, j] -= f * A[c, j];
                    r[q] -= f * r[c];
                }
            }
            for (int i = 2; i >= 0; i--)
            {
                double s2 = r[i];
                for (int j = i + 1; j < 3; j++) s2 -= A[i, j] * outp[j];
                outp[i] = s2 / A[i, i];
            }
            return true;
        }

        /// <summary>Pin the axis LINE, not just its direction. Every side facet of ONE
        /// cylinder satisfies (p - c).n = d for a constant d (axis-to-chord-plane distance),
        /// which is linear in (c_u, c_v, d). Without this constraint, growth leaks through a
        /// welded tee into a neighbouring pipe and the circle fit spans both — measured on
        /// the real model as ~2x inflated diameters and, tellingly, ZERO level runs.
        ///
        /// Solved by RANSAC rather than least squares: the bootstrap patch can itself be
        /// contaminated (the seed's own neighbour may already be on the wrong pipe), and one
        /// foreign face drags a least-squares solve off the true line, after which the second
        /// pass rejects nearly everything.</summary>
        private static bool SolveAxisLine(FaceGraph g, List<int> idx, XYZ axis, XYZ u, XYZ v,
                                          out double cu, out double cv, out double d)
        {
            cu = cv = d = 0;
            var NU = new List<double>(); var NV = new List<double>(); var R = new List<double>();
            foreach (int i in idx)
            {
                XYZ nn = g.N[i];
                if (nn == null || Math.Abs(nn.DotProduct(axis)) > 0.35) continue;
                double nu = nn.DotProduct(u), nv = nn.DotProduct(v);
                double len = Math.Sqrt(nu * nu + nv * nv);
                if (len < 1e-9) continue;
                nu /= len; nv /= len;
                XYZ p;
                try { p = g.F[i].Evaluate(new UV(0.5, 0.5)); } catch { continue; }
                NU.Add(nu); NV.Add(nv); R.Add(p.DotProduct(u) * nu + p.DotProduct(v) * nv);
            }
            int n = NU.Count;
            if (n < 3) return false;

            const double Tol = 0.02;   // 1/4 in
            int bestIn = 0; var sol = new double[3]; var outp = new double[3];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    for (int k = j + 1; k < n; k++)
                    {
                        double[,] M = { { NU[i], NV[i], 1 }, { NU[j], NV[j], 1 }, { NU[k], NV[k], 1 } };
                        double[] b = { R[i], R[j], R[k] };
                        if (!Solve3(M, b, outp)) continue;
                        int inl = 0;
                        for (int m = 0; m < n; m++)
                            if (Math.Abs(NU[m] * outp[0] + NV[m] * outp[1] + outp[2] - R[m]) < Tol) inl++;
                        if (inl > bestIn) { bestIn = inl; sol[0] = outp[0]; sol[1] = outp[1]; sol[2] = outp[2]; }
                    }
            if (bestIn < 3) return false;
            cu = sol[0]; cv = sol[1]; d = sol[2];
            return true;
        }

        private static double LineResid(FaceGraph g, int i, XYZ axis, XYZ u, XYZ v,
                                        double cu, double cv, double d)
        {
            XYZ nn = g.N[i];
            if (nn == null) return double.MaxValue;
            double nu = nn.DotProduct(u), nv = nn.DotProduct(v);
            double len = Math.Sqrt(nu * nu + nv * nv);
            if (len < 1e-9) return double.MaxValue;
            nu /= len; nv /= len;
            XYZ p;
            try { p = g.F[i].Evaluate(new UV(0.5, 0.5)); } catch { return double.MaxValue; }
            return Math.Abs(p.DotProduct(u) * nu + p.DotProduct(v) * nv - (cu * nu + cv * nv) - d);
        }

        /// <summary>Axis seed from the smallest eigenvector of a LOCAL PATCH's area-weighted
        /// normal covariance.
        ///
        /// Deriving the axis from a single adjacent pair via n1 x n2 is fragile. Picking the
        /// largest |dot| below the coplanar cutoff (which was needed to avoid seeding on an
        /// end cap) preferentially selects the two NEARLY-coplanar triangles of one quad — and
        /// the cross product of two near-parallel normals is numerical noise that can point
        /// anywhere, including 90 deg off the true axis. Real FBX / 3ds Max output has exactly
        /// that near-coplanarity, and the signature is unmistakable: the reported "length"
        /// collapses to the pipe DIAMETER and the reported "OD" becomes
        /// sqrt(segmentLength^2 + diameter^2). On the real model that read as 8-13.7 in ODs
        /// with 0.30-0.45 ft lengths and not one level run.
        ///
        /// A ~24-face patch averages the noise away, and the eigenvalue ratio doubles as a
        /// tube test: a genuine cylinder has one near-zero eigenvalue, a plate or corner
        /// does not.</summary>
        private static XYZ SeedAxisCov(FaceGraph g, int seed, HashSet<int> used)
        {
            var patch = new List<int>();
            var q = new Queue<int>();
            var vis = new HashSet<int> { seed };
            q.Enqueue(seed);
            while (q.Count > 0 && patch.Count < 24)
            {
                int cur = q.Dequeue();
                if (used.Contains(cur) || g.N[cur] == null) continue;
                patch.Add(cur);
                foreach (int nb in g.Adj[cur]) if (vis.Add(nb)) q.Enqueue(nb);
            }
            if (patch.Count < 6) return null;

            double[,] m = new double[3, 3];
            foreach (int i in patch)
            {
                XYZ n = g.N[i];
                double a = g.A[i];
                if (n == null || a <= 1e-12) continue;
                double[] e = { n.X, n.Y, n.Z };
                for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) m[r, c] += a * e[r] * e[c];
            }
            double[] val; double[][] vec;
            Jacobi(m, out val, out vec);
            int k = 0;
            for (int i = 1; i < 3; i++) if (val[i] < val[k]) k = i;
            var sorted = val.OrderBy(x => x).ToArray();
            if (sorted[1] < 1e-15 || sorted[0] / sorted[1] > 0.05) return null;   // not a tube
            return new XYZ(vec[0][k], vec[1][k], vec[2][k]).Normalize();
        }

        private static List<List<int>> Cylinders(FaceGraph g, List<int> comp)
        {
            const double PerpTol = 0.06;      // |n . axis| for "this face wraps that axis" (~3.4 deg)
            const double LineTol = 0.02;      // 1/4 in from the fitted axis line

            var result = new List<List<int>>();
            var used = new HashSet<int>();

            foreach (int seed in comp)
            {
                if (used.Contains(seed) || g.N[seed] == null) continue;

                XYZ axis = SeedAxisCov(g, seed, used);
                if (axis == null) continue;   // flat plate region, not a tube

                XYZ tmp = Math.Abs(axis.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
                XYZ uu = axis.CrossProduct(tmp).Normalize();
                XYZ vv = axis.CrossProduct(uu).Normalize();

                // PASS 1: a small local patch on perpendicularity alone, to pin the axis line.
                var patch = new List<int>();
                {
                    var q0 = new Queue<int>();
                    var v0 = new HashSet<int> { seed };
                    q0.Enqueue(seed);
                    while (q0.Count > 0 && patch.Count < 12)
                    {
                        int cur = q0.Dequeue();
                        if (used.Contains(cur) || g.N[cur] == null) continue;
                        if (Math.Abs(g.N[cur].DotProduct(axis)) > PerpTol) continue;
                        patch.Add(cur);
                        foreach (int nb in g.Adj[cur]) if (v0.Add(nb)) q0.Enqueue(nb);
                    }
                }
                double cu, cv, dd;
                bool haveLine = SolveAxisLine(g, patch, axis, uu, vv, out cu, out cv, out dd);

                // PASS 2: full growth, now also requiring the face to hug THAT axis line.
                var region = new List<int>();
                var q = new Queue<int>();
                var visited = new HashSet<int> { seed };
                q.Enqueue(seed);
                while (q.Count > 0)
                {
                    int cur = q.Dequeue();
                    if (used.Contains(cur) || g.N[cur] == null) continue;
                    if (Math.Abs(g.N[cur].DotProduct(axis)) > PerpTol) continue;   // different axis
                    if (haveLine && LineResid(g, cur, axis, uu, vv, cu, cv, dd) > LineTol) continue;
                    region.Add(cur); used.Add(cur);
                    foreach (int nb in g.Adj[cur]) if (visited.Add(nb)) q.Enqueue(nb);
                }
                if (region.Count >= 3) result.Add(region);
            }
            return result;
        }

        private static void SegmentationProbe(Document doc, GeometryElement geom, StringBuilder sb)
        {
            var solids = new List<Solid>();
            CollectSolids(geom, solids, 0);
            if (solids.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine("  ================ SEGMENTATION PROBE ================");

            // ── per-solid detail ──
            sb.AppendLine("   #   faces   edges      volume    surfArea  validTess  splitVolumes  layer");
            int totalSplit = 0; bool splitWorked = false;
            for (int i = 0; i < solids.Count; i++)
            {
                Solid s = solids[i];
                double vol = 0, area = 0; bool valid = false;
                try { vol = s.Volume; } catch { }
                try { area = s.SurfaceArea; } catch { }
                try { valid = SolidUtils.IsValidForTessellation(s); } catch { }

                string split = "-";
                try
                {
                    IList<Solid> parts = SolidUtils.SplitVolumes(s);
                    if (parts != null) { split = parts.Count.ToString(); totalSplit += parts.Count; if (parts.Count > 1) splitWorked = true; }
                }
                catch (Exception ex) { split = "ERR:" + ex.GetType().Name; }

                string layer = "(none)";
                try
                {
                    var gs = doc.GetElement(s.GraphicsStyleId) as GraphicsStyle;
                    if (gs != null && !string.IsNullOrEmpty(gs.Name)) layer = gs.Name;
                }
                catch { }

                sb.AppendLine($"  {i,2}  {s.Faces.Size,6}  {s.Edges.Size,6}  {vol,10:0.###}  {area,10:0.#}  {valid,9}  {split,12}  {layer}");
            }
            sb.AppendLine($"  SplitVolumes total pieces: {totalSplit}   (useful: {splitWorked})");

            // BULK CROSS-CHECK — independent of any fitting, and the thing that caught the
            // fit being wrong. For a cylinder V = pi r^2 L and lateral A = 2 pi r L, so
            // r = 2V/A and L = A/(2 pi r) straight off the solid's own volume and area.
            // If the fitted diameters disagree with this, the segmentation is leaking.
            double totVol = 0, totArea = 0;
            foreach (Solid s2 in solids)
            {
                try { totVol += Math.Abs(s2.Volume); } catch { }
                try { totArea += s2.SurfaceArea; } catch { }
            }
            if (totVol > 1e-9 && totArea > 1e-9)
            {
                double rAvg = 2.0 * totVol / totArea;
                double lTot = totArea / (2.0 * Math.PI * rAvg);
                sb.AppendLine();
                sb.AppendLine($"  BULK CROSS-CHECK (from volume+area alone, no fitting):");
                sb.AppendLine($"     average DIAMETER 2*(2V/A) = {rAvg * 24.0:0.##} in");
                sb.AppendLine($"     total LENGTH  A/(2*pi*r)  = {lTot:0} linear ft");
                sb.AppendLine("     -> fitted sizes should straddle that diameter. If they run well above it,");
                sb.AppendLine("        regions are spanning more than one pipe and the radii are inflated.");
            }

            // ── face-adjacency components, then region-grown cylinders ──
            var fits = new List<Cyl>();
            int edgeUses = 0, edgesShared = 0, compCount = 0, cylCount = 0, fitFail = 0;
            var compSizes = new List<int>();
            var cylSizes = new List<int>();

            foreach (Solid s in solids)
            {
                int eu, sh;
                FaceGraph g = BuildGraph(s, out eu, out sh);
                edgeUses += eu; edgesShared += sh;

                var comps = Components(g);
                compCount += comps.Count;
                compSizes.AddRange(comps.Select(c => c.Count));

                foreach (var c in comps.OrderByDescending(c => c.Count))
                {
                    foreach (var cyl in Cylinders(g, c))
                    {
                        cylCount++;
                        cylSizes.Add(cyl.Count);
                        if (fits.Count + fitFail >= 4000) continue;   // cap the detail work
                        Cyl cy = FitComponent(g, cyl);
                        if (cy == null) fitFail++; else fits.Add(cy);
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine($"  face-adjacency components: {compCount}   (edge uses {edgeUses}, matched pairs {edgesShared})");
            if (edgesShared == 0)
                sb.AppendLine("  !! no shared edges matched — faces are not welded; use the SplitVolumes pieces instead.");
            sb.AppendLine("  component size histogram (faces per component):");
            foreach (var g in compSizes.GroupBy(Bucket).OrderBy(g => g.Key))
                sb.AppendLine($"     {g.Key,-14} x{g.Count()}");

            sb.AppendLine();
            sb.AppendLine($"  region-grown cylinders: {cylCount}");
            sb.AppendLine("  cylinder size histogram (faces per cylinder):");
            foreach (var g in cylSizes.GroupBy(Bucket).OrderBy(g => g.Key))
                sb.AppendLine($"     {g.Key,-14} x{g.Count()}");

            sb.AppendLine();
            sb.AppendLine($"  --- CYLINDER FIT ({fits.Count} fit, {fitFail} rejected) ---");
            var solid360 = fits.Where(f => f.CoverageDeg >= 270 && f.RmsMil < 50).ToList();
            sb.AppendLine($"  well-conditioned (>=270 deg wrap AND <50 mil RMS): {solid360.Count}/{fits.Count}");
            sb.AppendLine("  NOTE: a fit on a partial arc over-reads the radius; trust the well-conditioned ones.");
            if (solid360.Count > 0) fits = solid360;
            if (fits.Count > 0)
            {
                sb.AppendLine("  fitted OD -> nominal size:");
                foreach (var g in fits.GroupBy(f => Math.Round(f.RadiusIn * 2, 2)).OrderByDescending(g => g.Count()).Take(25))
                {
                    double offMil; string nom = Nominal(g.Key, out offMil);
                    sb.AppendLine($"     OD {g.Key,7:0.###}in  x{g.Count(),-5} -> {nom} (off {offMil:0.#} mil)");
                }
                int onCat = fits.Count(f => { double o; Nominal(f.RadiusIn * 2, out o); return o < 60; });
                sb.AppendLine($"  within 60 mil of a nominal steel OD: {onCat}/{fits.Count}");

                int level = fits.Count(f => Math.Abs(f.SlopeIn10) < 0.01);
                int vert = fits.Count(f => Math.Abs(f.Axis.Z) > 0.99);
                sb.AppendLine($"  slope: level={level}  sloped={fits.Count - level - vert}  vertical={vert}");
                var pitched = fits.Where(f => Math.Abs(f.SlopeIn10) >= 0.01 && Math.Abs(f.Axis.Z) <= 0.99)
                                  .Select(f => Math.Abs(f.SlopeIn10)).OrderBy(x => x).ToList();
                if (pitched.Count > 0)
                    sb.AppendLine($"  sloped pitches: min={pitched.First():0.###}  median={pitched[pitched.Count / 2]:0.###}  " +
                                  $"max={pitched.Last():0.###} in/10ft");

                sb.AppendLine("  sample (first 25):    OD(in)  len(ft)  slope(in/10ft)  faces   wrap(deg)  rms(mil)");
                foreach (var f in fits.Take(25))
                    sb.AppendLine($"      {f.RadiusIn * 2,10:0.###}  {f.LengthFt,7:0.##}  {f.SlopeIn10,13:0.####}  {f.FaceCount,6}  {f.CoverageDeg,9:0}  {f.RmsMil,8:0.#}");
            }
        }

        private static string Bucket(int n)
        {
            if (n <= 4) return "1-4";
            if (n <= 8) return "5-8";
            if (n <= 16) return "9-16";
            if (n <= 32) return "17-32";
            if (n <= 64) return "33-64";
            if (n <= 256) return "65-256";
            if (n <= 1024) return "257-1024";
            return ">1024";
        }

        /// <summary>Fit one connected face group as a cylinder: axis from the area-weighted
        /// normal covariance (smallest eigenvector), radius from a circle fit on the
        /// perpendicular plane with cap facets excluded. Returns null if it isn't cylinder-like.</summary>
        private static Cyl FitComponent(FaceGraph g, List<int> idxs)
        {
            try
            {
                // Axis: minimize sum (a . n)^2 over area-weighted face normals.
                double[,] m = new double[3, 3];
                var normals = new List<KeyValuePair<XYZ, double>>();
                foreach (int i0 in idxs)
                {
                    XYZ n = g.N[i0];
                    double a = g.A[i0];
                    if (n == null || a <= 1e-12) continue;
                    normals.Add(new KeyValuePair<XYZ, double>(n, a));
                    double[] e = { n.X, n.Y, n.Z };
                    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] += a * e[i] * e[j];
                }
                if (normals.Count < 3) return null;

                double[] val; double[][] vec;
                Jacobi(m, out val, out vec);
                int k = 0;
                for (int i = 1; i < 3; i++) if (val[i] < val[k]) k = i;
                XYZ axis = new XYZ(vec[0][k], vec[1][k], vec[2][k]).Normalize();

                // Cylinder-likeness: the side facets should hug the axis-perpendicular plane.
                double sideArea = normals.Where(p => Math.Abs(p.Key.DotProduct(axis)) < 0.35).Sum(p => p.Value);
                double totArea = normals.Sum(p => p.Value);
                if (totArea <= 0 || sideArea / totArea < 0.5) return null;   // not a tube

                XYZ tmp = Math.Abs(axis.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
                XYZ u = axis.CrossProduct(tmp).Normalize();
                XYZ v = axis.CrossProduct(u).Normalize();

                // ARC COVERAGE: how much of the circumference do these facets actually wrap?
                // A circle fitted to a partial arc is wildly optimistic about its radius — that
                // is exactly how 4-face fragments of real pipe came back as "9-13 in OD".
                //
                // Measured as 360 minus the LARGEST ANGULAR GAP between consecutive facet
                // normals. Counting filled bins does not work: an n-facet cylinder only has n
                // distinct normals, so a full 16-facet ring could never fill more than 16 of 36
                // bins (160 deg) and every real pipe failed a ">= 300 deg" test. By the gap
                // measure a full 16-facet ring scores 337.5 deg and a 90 deg arc scores 90.
                var angs = new List<double>();
                foreach (var p in normals)
                {
                    if (Math.Abs(p.Key.DotProduct(axis)) > 0.35) continue;
                    angs.Add(Math.Atan2(p.Key.DotProduct(v), p.Key.DotProduct(u)));
                }
                double coverageDeg = 0;
                if (angs.Count >= 2)
                {
                    angs.Sort();
                    double maxGap = angs[0] + 2 * Math.PI - angs[angs.Count - 1];
                    for (int i = 1; i < angs.Count; i++) maxGap = Math.Max(maxGap, angs[i] - angs[i - 1]);
                    coverageDeg = 360.0 - maxGap * 180.0 / Math.PI;
                }

                // Radius: circle fit on side-facet vertices only (cap fans bias it low).
                double Sx = 0, Sy = 0, Sxx = 0, Syy = 0, Sxy = 0, Sxz = 0, Syz = 0, Sz = 0;
                double lo = double.MaxValue, hi = double.MinValue;
                int N = 0;
                var px = new List<double>(); var py = new List<double>();
                foreach (int i0 in idxs)
                {
                    if (g.N[i0] == null || Math.Abs(g.N[i0].DotProduct(axis)) > 0.35) continue;
                    Mesh msh = g.F[i0].Triangulate();
                    if (msh == null) continue;
                    for (int i = 0; i < msh.Vertices.Count; i++)
                    {
                        XYZ p = msh.Vertices[i];
                        double x = p.DotProduct(u), y = p.DotProduct(v), d = p.DotProduct(axis);
                        double z = x * x + y * y;
                        Sx += x; Sy += y; Sxx += x * x; Syy += y * y; Sxy += x * y;
                        Sxz += x * z; Syz += y * z; Sz += z; N++;
                        px.Add(x); py.Add(y);
                        if (d < lo) lo = d;
                        if (d > hi) hi = d;
                    }
                }
                if (N < 6) return null;

                double[,] A = { { Sxx, Sxy, Sx }, { Sxy, Syy, Sy }, { Sx, Sy, N } };
                double[] b = { -Sxz, -Syz, -Sz };
                for (int c = 0; c < 3; c++)
                {
                    int piv = c;
                    for (int r = c + 1; r < 3; r++) if (Math.Abs(A[r, c]) > Math.Abs(A[piv, c])) piv = r;
                    if (Math.Abs(A[piv, c]) < 1e-14) return null;
                    if (piv != c)
                    {
                        for (int j = 0; j < 3; j++) { double t = A[c, j]; A[c, j] = A[piv, j]; A[piv, j] = t; }
                        double tb = b[c]; b[c] = b[piv]; b[piv] = tb;
                    }
                    for (int r = c + 1; r < 3; r++)
                    {
                        double fr = A[r, c] / A[c, c];
                        for (int j = c; j < 3; j++) A[r, j] -= fr * A[c, j];
                        b[r] -= fr * b[c];
                    }
                }
                double[] sol = new double[3];
                for (int i = 2; i >= 0; i--)
                {
                    double s2 = b[i];
                    for (int j = i + 1; j < 3; j++) s2 -= A[i, j] * sol[j];
                    sol[i] = s2 / A[i, i];
                }
                double cx = -sol[0] / 2, cy = -sol[1] / 2;
                double rad = Math.Sqrt(Math.Max(0, cx * cx + cy * cy - sol[2]));
                if (rad <= 1e-6 || rad > 3.0) return null;   // >72in dia: not pipe

                // Residual: how well the vertices actually sit on that circle. A partial-arc
                // or non-circular cluster shows up here even when the fit "succeeded".
                double sse = 0;
                for (int i = 0; i < px.Count; i++)
                {
                    double dr = Math.Sqrt((px[i] - cx) * (px[i] - cx) + (py[i] - cy) * (py[i] - cy)) - rad;
                    sse += dr * dr;
                }
                double rms = Math.Sqrt(sse / px.Count);

                XYZ ax = axis.Z < 0 ? axis.Negate() : axis;
                double run = Math.Sqrt(ax.X * ax.X + ax.Y * ax.Y);
                return new Cyl
                {
                    RadiusIn = rad * 12.0,
                    LengthFt = hi > lo ? hi - lo : 0.0,
                    Axis = ax,
                    SlopeIn10 = run < 1e-9 ? 0.0 : ax.Z / run * 120.0,
                    FaceCount = idxs.Count,
                    CoverageDeg = coverageDeg,
                    RmsMil = rms * 12000.0
                };
            }
            catch { return null; }
        }

        private static void Jacobi(double[,] mIn, out double[] val, out double[][] vec)
        {
            double[,] a = (double[,])mIn.Clone();
            double[][] v = new double[3][];
            for (int i = 0; i < 3; i++) { v[i] = new double[3]; v[i][i] = 1; }
            for (int sweep = 0; sweep < 100; sweep++)
            {
                double off = a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2];
                if (off < 1e-32) break;
                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;
                        double th = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        double t = (th >= 0 ? 1.0 : -1.0) / (Math.Abs(th) + Math.Sqrt(th * th + 1));
                        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                        for (int k = 0; k < 3; k++) { double akp = a[k, p], akq = a[k, q]; a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq; }
                        for (int k = 0; k < 3; k++) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk; }
                        for (int k = 0; k < 3; k++) { double vkp = v[k][p], vkq = v[k][q]; v[k][p] = c * vkp - s * vkq; v[k][q] = s * vkp + c * vkq; }
                    }
            }
            val = new double[] { a[0, 0], a[1, 1], a[2, 2] };
            vec = v;
        }

        private static void Verdict(Stats s, StringBuilder sb)
        {
            if (s.CylFaces > 0)
            {
                sb.AppendLine($"GEOMETRY: ACIS solids WITH analytic cylindrical faces ({s.CylFaces}).");
                sb.AppendLine("  -> BEST CASE. Axis, radius and slope are read directly off the face.");
                sb.AppendLine("     No cylinder fitting, no tessellation error, no cap-facet bias.");
            }
            else if (s.Solids > 0)
            {
                sb.AppendLine($"GEOMETRY: solids ({s.Solids}) but NO cylindrical faces.");
                sb.AppendLine("  -> Pipe arrived faceted (planar sides). Fitting works: axis = smallest");
                sb.AppendLine("     eigenvector of the area-weighted normal covariance; radius = circle fit");
                sb.AppendLine("     on the perpendicular plane. MUST exclude end-cap facets or the diameter");
                sb.AppendLine("     reads low (measured 4.500in -> 4.108in: a 2-1/2in pipe called 2in).");
            }
            else if (s.Meshes > 0)
            {
                sb.AppendLine($"GEOMETRY: meshes only ({s.Meshes} meshes, {s.Triangles} triangles).");
                sb.AppendLine("  -> Triangle soup. Same fitting as above, plus segmentation first.");
            }
            else
            {
                sb.AppendLine("GEOMETRY: no solids or meshes found — nothing to trace.");
                sb.AppendLine("  -> Check the link is 3D (not a 2D DWG) and the view detail level.");
            }

            sb.AppendLine();
            int units = s.Solids + s.Meshes;
            sb.AppendLine($"SEGMENTATION: {units} geometry unit(s) across {s.Instances} instance(s).");
            if (units > 20)
            {
                sb.AppendLine("  -> Many separate units: pipes are likely pre-separated. Clustering is nearly free.");
            }
            else if (units > 0)
            {
                sb.AppendLine("  -> Few units: geometry may be merged into one blob. Expect to segment");
                sb.AppendLine("     (connected components, then per-component cylinder fitting) first.");
            }

            sb.AppendLine();
            double far = Math.Max(Math.Max(Math.Abs(s.Min.X), Math.Abs(s.Max.X)),
                                  Math.Max(Math.Abs(s.Min.Y), Math.Abs(s.Max.Y)));
            double ulpMil = far * Math.Pow(2, -23) * 12000;
            sb.AppendLine($"PRECISION: farthest coordinate {far:0.#} ft -> float32 ulp ~{ulpMil:0.#} mil.");
            if (ulpMil > 300)
            {
                sb.AppendLine("  -> DANGER. At this distance a float32 FBX leg injects enough jitter to");
                sb.AppendLine("     fake slope on level pipe (measured: 500 mil -> 0.17 in/10ft phantom");
                sb.AppendLine("     pitch) and, past ~2900 mil, to misread a 4in pipe as 6in.");
                sb.AppendLine("     Re-export near the origin, not on state-plane coordinates.");
            }
            else if (ulpMil > 30)
            {
                sb.AppendLine("  -> Marginal. Diameters should survive; verify slope against a run you");
                sb.AppendLine("     know is dead level before trusting any pitch you read.");
            }
            else
            {
                sb.AppendLine("  -> Fine. Close enough to the origin that float32 jitter is negligible.");
            }

            if (s.Cylinders.Count > 0)
            {
                sb.AppendLine();
                int matched = s.Cylinders.Count(c => { Nominal(c.RadiusIn * 2, out double off); return off < 60; });
                sb.AppendLine($"SIZES: {matched}/{s.Cylinders.Count} cylinders land within 60 mil of a nominal steel OD.");
                if (matched < s.Cylinders.Count * 0.8)
                    sb.AppendLine("  -> Many off-catalog radii. Could be insulation, fittings, or a scale problem.");
            }
        }

        // Standard steel pipe OD (inches).
        private static readonly string[] NomName =
            { "1/2\"", "3/4\"", "1\"", "1-1/4\"", "1-1/2\"", "2\"", "2-1/2\"", "3\"", "3-1/2\"", "4\"", "5\"", "6\"", "8\"", "10\"", "12\"" };
        private static readonly double[] NomOd =
            { 0.840, 1.050, 1.315, 1.660, 1.900, 2.375, 2.875, 3.500, 4.000, 4.500, 5.563, 6.625, 8.625, 10.750, 12.750 };

        private static string Nominal(double odIn, out double offMil)
        {
            int best = 0; double be = double.MaxValue;
            for (int i = 0; i < NomOd.Length; i++)
            {
                double e = Math.Abs(NomOd[i] - odIn);
                if (e < be) { be = e; best = i; }
            }
            offMil = be * 1000.0;
            return NomName[best];
        }

        private class Cyl
        {
            public double RadiusIn;
            public double LengthFt;
            public XYZ Axis;
            public double SlopeIn10;
            public int FaceCount;
            public double CoverageDeg;   // how much of the circumference the facets wrap
            public double RmsMil;        // circle-fit residual
        }

        private class Stats
        {
            public int Solids, SolidsWithVolume, NegativeVolume, Meshes, Curves, Instances, Other, Triangles;
            public int Faces, CylFaces, PlanarFaces, ConicalFaces, RevolvedFaces, OtherFaces;
            public readonly HashSet<string> Layers = new HashSet<string>();
            public readonly List<Cyl> Cylinders = new List<Cyl>();
            public XYZ Min = new XYZ(double.MaxValue, double.MaxValue, double.MaxValue);
            public XYZ Max = new XYZ(double.MinValue, double.MinValue, double.MinValue);

            public void Grow(XYZ p)
            {
                Min = new XYZ(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z));
                Max = new XYZ(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z));
            }

            public void Merge(Stats o)
            {
                Solids += o.Solids; SolidsWithVolume += o.SolidsWithVolume; NegativeVolume += o.NegativeVolume; Meshes += o.Meshes;
                Curves += o.Curves; Instances += o.Instances; Other += o.Other; Triangles += o.Triangles;
                Faces += o.Faces; CylFaces += o.CylFaces; PlanarFaces += o.PlanarFaces;
                ConicalFaces += o.ConicalFaces; RevolvedFaces += o.RevolvedFaces; OtherFaces += o.OtherFaces;
                foreach (var l in o.Layers) Layers.Add(l);
                Cylinders.AddRange(o.Cylinders);
                if (o.Min.X <= o.Max.X) { Grow(o.Min); Grow(o.Max); }
            }
        }

        private static string SafeName(Element e)
        {
            try { return string.IsNullOrEmpty(e.Name) ? "(unnamed)" : e.Name; } catch { return "(unnamed)"; }
        }

        private static void ShowReport(string text)
        {
            using (var f = new DpiAwareForm())
            {
                f.Text = "Inspect CAD Geometry — copy this and paste it back";
                f.StartPosition = WinForms.FormStartPosition.CenterScreen;
                f.ClientSize = new System.Drawing.Size(860, 640);
                var tb = new WinForms.TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = WinForms.ScrollBars.Both,
                    WordWrap = false,
                    Dock = WinForms.DockStyle.Fill,
                    Font = new System.Drawing.Font("Consolas", 9f),
                    Text = text
                };
                var panel = new WinForms.Panel { Dock = WinForms.DockStyle.Bottom, Height = 44 };
                var btnCopy = new WinForms.Button { Text = "Copy to clipboard", Location = new System.Drawing.Point(10, 8), Size = new System.Drawing.Size(150, 28) };
                btnCopy.Click += (s, ev) => { try { WinForms.Clipboard.SetText(text); } catch { } };
                var btnClose = new WinForms.Button { Text = "Close", DialogResult = WinForms.DialogResult.OK, Location = new System.Drawing.Point(170, 8), Size = new System.Drawing.Size(90, 28) };
                panel.Controls.Add(btnCopy);
                panel.Controls.Add(btnClose);
                f.Controls.Add(tb);
                f.Controls.Add(panel);
                f.AcceptButton = btnClose;
                f.ShowDialog();
            }
        }
    }
}
