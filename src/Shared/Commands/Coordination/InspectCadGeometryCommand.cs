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
            sb.AppendLine($"  solids with volume: {s.SolidsWithVolume}   total triangles (meshes): {s.Triangles}");
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
                    if (vol > 1e-9) s.SolidsWithVolume++;

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

        private class RefEq : IEqualityComparer<Face>
        {
            public bool Equals(Face a, Face b) => ReferenceEquals(a, b);
            public int GetHashCode(Face f) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(f);
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

            // ── face-adjacency connected components ──
            var comps = new List<List<Face>>();
            int danglingEdges = 0;
            foreach (Solid s in solids)
            {
                var idx = new Dictionary<Face, int>(new RefEq());
                var faces = new List<Face>();
                foreach (Face f in s.Faces) { idx[f] = faces.Count; faces.Add(f); }

                int[] parent = new int[faces.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;
                Func<int, int> Find = null;
                Find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };

                foreach (Edge e in s.Edges)
                {
                    try
                    {
                        Face fa = e.GetFace(0), fb = e.GetFace(1);
                        int ia, ib;
                        if (fa == null || fb == null || !idx.TryGetValue(fa, out ia) || !idx.TryGetValue(fb, out ib))
                        { danglingEdges++; continue; }
                        int ra = Find(ia), rb = Find(ib);
                        if (ra != rb) parent[ra] = rb;
                    }
                    catch { danglingEdges++; }
                }

                var groups = new Dictionary<int, List<Face>>();
                for (int i = 0; i < faces.Count; i++)
                {
                    int r = Find(i);
                    List<Face> g;
                    if (!groups.TryGetValue(r, out g)) { g = new List<Face>(); groups[r] = g; }
                    g.Add(faces[i]);
                }
                comps.AddRange(groups.Values);
            }

            sb.AppendLine();
            sb.AppendLine($"  face-adjacency components: {comps.Count}   (edges that didn't resolve to 2 known faces: {danglingEdges})");
            var bySize = comps.GroupBy(c => Bucket(c.Count)).OrderBy(g => g.Key).ToList();
            sb.AppendLine("  component size histogram (faces per component):");
            foreach (var g in bySize) sb.AppendLine($"     {g.Key,-14} x{g.Count()}");

            // ── fit the components and see whether they land on real pipe sizes ──
            var fits = new List<Cyl>();
            int fitFail = 0;
            foreach (var c in comps.OrderByDescending(c => c.Count).Take(600))
            {
                Cyl cy = FitComponent(c);
                if (cy == null) fitFail++; else fits.Add(cy);
            }

            sb.AppendLine();
            sb.AppendLine($"  --- CYLINDER FIT on {Math.Min(600, comps.Count)} largest components " +
                          $"({fits.Count} fit, {fitFail} rejected) ---");
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

                sb.AppendLine("  sample (first 25):    OD(in)  len(ft)  slope(in/10ft)  faces");
                foreach (var f in fits.Take(25))
                    sb.AppendLine($"      {f.RadiusIn * 2,10:0.###}  {f.LengthFt,7:0.##}  {f.SlopeIn10,13:0.####}  {f.FaceCount,6}");
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
        private static Cyl FitComponent(List<Face> faces)
        {
            try
            {
                // Axis: minimize sum (a . n)^2 over area-weighted face normals.
                double[,] m = new double[3, 3];
                var normals = new List<KeyValuePair<XYZ, double>>();
                foreach (Face f in faces)
                {
                    var pf = f as PlanarFace;
                    if (pf == null) continue;
                    double a = 0;
                    try { a = f.Area; } catch { }
                    if (a <= 1e-12) continue;
                    XYZ n = pf.FaceNormal;
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

                // Radius: circle fit on side-facet vertices only (cap fans bias it low).
                XYZ tmp = Math.Abs(axis.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
                XYZ u = axis.CrossProduct(tmp).Normalize();
                XYZ v = axis.CrossProduct(u).Normalize();
                double Sx = 0, Sy = 0, Sxx = 0, Syy = 0, Sxy = 0, Sxz = 0, Syz = 0, Sz = 0;
                double lo = double.MaxValue, hi = double.MinValue;
                int N = 0;
                foreach (Face f in faces)
                {
                    var pf = f as PlanarFace;
                    if (pf == null || Math.Abs(pf.FaceNormal.DotProduct(axis)) > 0.35) continue;
                    Mesh msh = f.Triangulate();
                    if (msh == null) continue;
                    for (int i = 0; i < msh.Vertices.Count; i++)
                    {
                        XYZ p = msh.Vertices[i];
                        double x = p.DotProduct(u), y = p.DotProduct(v), d = p.DotProduct(axis);
                        double z = x * x + y * y;
                        Sx += x; Sy += y; Sxx += x * x; Syy += y * y; Sxy += x * y;
                        Sxz += x * z; Syz += y * z; Sz += z; N++;
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

                XYZ ax = axis.Z < 0 ? axis.Negate() : axis;
                double run = Math.Sqrt(ax.X * ax.X + ax.Y * ax.Y);
                return new Cyl
                {
                    RadiusIn = rad * 12.0,
                    LengthFt = hi > lo ? hi - lo : 0.0,
                    Axis = ax,
                    SlopeIn10 = run < 1e-9 ? 0.0 : ax.Z / run * 120.0,
                    FaceCount = faces.Count
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
        }

        private class Stats
        {
            public int Solids, SolidsWithVolume, Meshes, Curves, Instances, Other, Triangles;
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
                Solids += o.Solids; SolidsWithVolume += o.SolidsWithVolume; Meshes += o.Meshes;
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
