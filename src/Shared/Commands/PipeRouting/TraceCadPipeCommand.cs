using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>
    /// Traces the pipe in a linked/imported CAD (typically a Navisworks coordination model
    /// round-tripped NWC -> FBX -> 3ds Max -> DWG) and builds real Revit <see cref="Pipe"/>
    /// from it.
    ///
    /// HOW THE SOURCE GEOMETRY IS SHAPED (measured on a real 1641 x 801 ft warehouse):
    ///   • the PIPE arrives as a Mesh — coarse triangulated tubes, one connected component
    ///     per straight run (97 runs, 5,480 linear ft, 6"/8"/10")
    ///   • the FITTINGS arrive as separate Solids — compact ~14 in bodies. Every one of them
    ///     sat within 0.8 ft of a pipe-run end, so they mark the junctions.
    /// Only the mesh is traced; the solids are left alone (they are where fittings go, and
    /// Revit will make its own when the pipes are connected).
    ///
    /// PER RUN: weld vertices, take connected components of the triangle graph, then the
    /// component's principal axis (largest eigenvector of the vertex covariance) is the pipe
    /// axis, the extent along it gives the endpoints, and the MEDIAN radial distance gives
    /// the radius. Median rather than mean because a coarse tube's vertices sit on the
    /// circumscribed polygon and stray triangles at the ends would drag a mean.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TraceCadPipeCommand : IExternalCommand
    {
        private const double WeldFt = 1e-4;    // vertex weld tolerance

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var imports = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance))
                                  .Cast<Element>().ToList();
                if (imports.Count == 0)
                {
                    TaskDialog.Show("Trace CAD Pipe",
                        "No CAD imports/links found.\n\nLink the DWG first (Insert > Link CAD).");
                    return Result.Cancelled;
                }

                // ── harvest candidate runs before showing the dialog, so it can report counts ──
                var runs = new List<Run>();
                foreach (Element imp in imports)
                    CollectRuns(doc, imp, runs);

                if (runs.Count == 0)
                {
                    TaskDialog.Show("Trace CAD Pipe",
                        "No mesh tube runs found in the linked CAD.\n\n" +
                        "Run Coordination > Inspect CAD Geom to see what the link actually contains — " +
                        "if it reports Mesh=0 the piping did not survive the export.");
                    return Result.Cancelled;
                }

                var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType))
                    .Cast<PipeType>().OrderBy(t => t.Name)
                    .Select(t => (t.Id.IntegerValue, t.Name)).ToList();
                var systems = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType))
                    .Cast<PipingSystemType>().OrderBy(t => t.Name)
                    .Select(t => (t.Id.IntegerValue, t.Name)).ToList();
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (pipeTypes.Count == 0 || systems.Count == 0 || levels.Count == 0)
                {
                    TaskDialog.Show("Trace CAD Pipe", "This document needs at least one pipe type, piping system and level.");
                    return Result.Cancelled;
                }

                using (var dlg = new TraceCadPipeDialog(pipeTypes, systems,
                                     levels.Select(l => l.Name).ToList(), runs.Count,
                                     runs.Sum(r => r.LengthFt), SizeSummary(runs)))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;

                    Level level = levels.FirstOrDefault(l => l.Name == dlg.LevelName) ?? levels[0];
                    var keep = runs.Where(r => r.LengthFt >= dlg.MinLengthFt).ToList();
                    if (keep.Count == 0)
                    {
                        TaskDialog.Show("Trace CAD Pipe", $"No runs are at least {dlg.MinLengthFt:0.##} ft long.");
                        return Result.Cancelled;
                    }

                    var rpt = new Report { Considered = runs.Count, Skipped = runs.Count - keep.Count };
                    using (var tx = new Transaction(doc, "Trace CAD Pipe"))
                    {
                        tx.Start();
                        foreach (Run r in keep)
                        {
                            double dia = dlg.ForceSizeIn > 0 ? dlg.ForceSizeIn / 12.0
                                       : dlg.SnapToNominal ? Nominal(r.DiameterIn) / 12.0
                                       : r.DiameterIn / 12.0;

                            XYZ a = r.Start, b = r.End;
                            if (dlg.FlattenSlope)
                            {
                                double z = (a.Z + b.Z) / 2.0;
                                a = new XYZ(a.X, a.Y, z); b = new XYZ(b.X, b.Y, z);
                            }
                            if (a.DistanceTo(b) <= 0.01) { rpt.Fail++; continue; }

                            Pipe p = null;
                            try { p = Pipe.Create(doc, new ElementId(dlg.SystemTypeId), new ElementId(dlg.PipeTypeId), level.Id, a, b); }
                            catch { }
                            if (p == null) { rpt.Fail++; continue; }

                            SetDiameter(p, dia);
                            rpt.Placed++;
                            rpt.LengthFt += r.LengthFt;
                            rpt.Sizes[Nominal(dia * 12.0)] = rpt.Sizes.TryGetValue(Nominal(dia * 12.0), out int n) ? n + 1 : 1;
                        }
                        tx.Commit();
                    }

                    TaskDialog.Show("Trace CAD Pipe", rpt.ToString());
                    return Result.Succeeded;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        // ── geometry ────────────────────────────────────────────────────────────────

        private class Run
        {
            public XYZ Start, End, Axis;
            public double LengthFt, DiameterIn;
            public int Triangles;
            public double SlopeIn10;
        }

        private static void CollectMeshes(GeometryElement geom, List<Mesh> outp, int depth)
        {
            if (geom == null || depth > 8) return;
            foreach (GeometryObject obj in geom)
            {
                var inst = obj as GeometryInstance;
                if (inst != null) { CollectMeshes(inst.GetInstanceGeometry(), outp, depth + 1); continue; }
                var m = obj as Mesh;
                if (m != null && m.NumTriangles > 0) outp.Add(m);
            }
        }

        private static void CollectRuns(Document doc, Element imp, List<Run> runs)
        {
            var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geom = null;
            try { geom = imp.get_Geometry(opt); } catch { }
            if (geom == null) return;

            var meshes = new List<Mesh>();
            CollectMeshes(geom, meshes, 0);

            foreach (Mesh m in meshes)
            {
                int nt = m.NumTriangles;
                var tri = new int[nt][];
                var pts = new List<XYZ>();
                var weld = new Dictionary<long, int>();

                for (int t = 0; t < nt; t++)
                {
                    MeshTriangle mt;
                    try { mt = m.get_Triangle(t); } catch { tri[t] = null; continue; }
                    var ids = new int[3];
                    for (int k = 0; k < 3; k++)
                    {
                        XYZ v = mt.get_Vertex(k);
                        long key = WeldKey(v);
                        int id;
                        if (!weld.TryGetValue(key, out id)) { id = pts.Count; weld[key] = id; pts.Add(v); }
                        ids[k] = id;
                    }
                    tri[t] = ids;
                }

                // union-find over triangles sharing a welded vertex
                var parent = new int[nt];
                for (int i = 0; i < nt; i++) parent[i] = i;
                Func<int, int> find = null;
                find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
                var firstTriAt = new Dictionary<int, int>();
                for (int t = 0; t < nt; t++)
                {
                    if (tri[t] == null) continue;
                    foreach (int v in tri[t])
                    {
                        int other;
                        if (firstTriAt.TryGetValue(v, out other))
                        { int ra = find(t), rb = find(other); if (ra != rb) parent[ra] = rb; }
                        else firstTriAt[v] = t;
                    }
                }

                var groups = new Dictionary<int, List<int>>();
                for (int t = 0; t < nt; t++)
                {
                    if (tri[t] == null) continue;
                    int r = find(t);
                    List<int> g;
                    if (!groups.TryGetValue(r, out g)) { g = new List<int>(); groups[r] = g; }
                    g.Add(t);
                }

                foreach (var g in groups.Values)
                {
                    Run run = FitRun(g, tri, pts);
                    if (run != null) runs.Add(run);
                }
            }
        }

        private static long WeldKey(XYZ v)
        {
            unchecked
            {
                long h = 17;
                h = h * 1000003 + (long)Math.Round(v.X / WeldFt);
                h = h * 1000003 + (long)Math.Round(v.Y / WeldFt);
                h = h * 1000003 + (long)Math.Round(v.Z / WeldFt);
                return h;
            }
        }

        /// <summary>Principal axis of the component's vertices is the pipe axis; the extent
        /// along it gives the endpoints; the MEDIAN radial distance gives the radius.</summary>
        private static Run FitRun(List<int> group, int[][] tri, List<XYZ> pts)
        {
            var vs = new List<XYZ>();
            var seen = new HashSet<int>();
            foreach (int t in group)
                foreach (int v in tri[t])
                    if (seen.Add(v)) vs.Add(pts[v]);
            if (vs.Count < 6) return null;

            double cx = vs.Average(p => p.X), cy = vs.Average(p => p.Y), cz = vs.Average(p => p.Z);
            var c = new XYZ(cx, cy, cz);

            double[,] M = new double[3, 3];
            foreach (XYZ p in vs)
            {
                double[] d = { p.X - cx, p.Y - cy, p.Z - cz };
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) M[i, j] += d[i] * d[j];
            }
            double[] val; double[][] vec;
            Jacobi(M, out val, out vec);
            int k0 = 0;
            for (int i = 1; i < 3; i++) if (val[i] > val[k0]) k0 = i;   // LARGEST = along the run
            XYZ axis = new XYZ(vec[0][k0], vec[1][k0], vec[2][k0]).Normalize();

            double lo = double.MaxValue, hi = double.MinValue;
            var radii = new List<double>();
            foreach (XYZ p in vs)
            {
                XYZ d = p - c;
                double a = d.DotProduct(axis);
                if (a < lo) lo = a;
                if (a > hi) hi = a;
                radii.Add((d - axis.Multiply(a)).GetLength());
            }
            double len = hi - lo;
            if (len < 1.0) return null;
            radii.Sort();

            XYZ s = c + axis.Multiply(lo), e = c + axis.Multiply(hi);
            if (s.Z > e.Z) { XYZ t2 = s; s = e; e = t2; }
            double run = Math.Sqrt((e.X - s.X) * (e.X - s.X) + (e.Y - s.Y) * (e.Y - s.Y));

            return new Run
            {
                Start = s,
                End = e,
                Axis = axis,
                LengthFt = len,
                DiameterIn = radii[radii.Count / 2] * 24.0,
                Triangles = group.Count,
                SlopeIn10 = run < 1e-9 ? 0.0 : (e.Z - s.Z) / run * 120.0
            };
        }

        private static void Jacobi(double[,] mIn, out double[] val, out double[][] vec)
        {
            double[,] a = (double[,])mIn.Clone();
            double[][] v = new double[3][];
            for (int i = 0; i < 3; i++) { v[i] = new double[3]; v[i][i] = 1; }
            for (int sweep = 0; sweep < 100; sweep++)
            {
                if (a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2] < 1e-30) break;
                for (int p = 0; p < 2; p++)
                    for (int q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-300) continue;
                        double th = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        double t = (th >= 0 ? 1.0 : -1.0) / (Math.Abs(th) + Math.Sqrt(th * th + 1));
                        double cc = 1 / Math.Sqrt(t * t + 1), ss = t * cc;
                        for (int k = 0; k < 3; k++) { double akp = a[k, p], akq = a[k, q]; a[k, p] = cc * akp - ss * akq; a[k, q] = ss * akp + cc * akq; }
                        for (int k = 0; k < 3; k++) { double apk = a[p, k], aqk = a[q, k]; a[p, k] = cc * apk - ss * aqk; a[q, k] = ss * apk + cc * aqk; }
                        for (int k = 0; k < 3; k++) { double vkp = v[k][p], vkq = v[k][q]; v[k][p] = cc * vkp - ss * vkq; v[k][q] = ss * vkp + cc * vkq; }
                    }
            }
            val = new double[] { a[0, 0], a[1, 1], a[2, 2] };
            vec = v;
        }

        // ── nominal sizes ───────────────────────────────────────────────────────────

        private static readonly double[] NomOd =
            { 1.315, 1.660, 1.900, 2.375, 2.875, 3.500, 4.000, 4.500, 5.563, 6.625, 8.625, 10.750, 12.750 };
        private static readonly string[] NomName =
            { "1\"", "1-1/4\"", "1-1/2\"", "2\"", "2-1/2\"", "3\"", "3-1/2\"", "4\"", "5\"", "6\"", "8\"", "10\"", "12\"" };

        private static double Nominal(double odIn)
        {
            int best = 0; double be = double.MaxValue;
            for (int i = 0; i < NomOd.Length; i++)
            { double e = Math.Abs(NomOd[i] - odIn); if (e < be) { be = e; best = i; } }
            return NomOd[best];
        }

        private static string NominalName(double odIn)
        {
            int best = 0; double be = double.MaxValue;
            for (int i = 0; i < NomOd.Length; i++)
            { double e = Math.Abs(NomOd[i] - odIn); if (e < be) { be = e; best = i; } }
            return NomName[best];
        }

        private static string SizeSummary(List<Run> runs)
        {
            var by = new Dictionary<string, int>();
            foreach (Run r in runs)
            {
                string k = NominalName(r.DiameterIn);
                by[k] = by.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            return string.Join(",  ", by.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " x" + kv.Value));
        }

        private static void SetDiameter(MEPCurve curve, double sizeFt)
        {
            if (curve == null || sizeFt <= 0) return;
            try
            {
                var d = curve.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (d != null && !d.IsReadOnly) d.Set(sizeFt);
            }
            catch { }
        }

        private class Report
        {
            public int Considered, Placed, Skipped, Fail;
            public double LengthFt;
            public readonly Dictionary<double, int> Sizes = new Dictionary<double, int>();

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine("Trace CAD Pipe");
                sb.AppendLine();
                sb.AppendLine($"Runs found in the link: {Considered}");
                sb.AppendLine($"Pipes placed: {Placed}   ({LengthFt:0} linear ft)");
                if (Skipped > 0) sb.AppendLine($"Skipped (under the minimum length): {Skipped}");
                if (Fail > 0) sb.AppendLine($"Failed to create: {Fail}");
                if (Sizes.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Sizes placed:");
                    foreach (var kv in Sizes.OrderByDescending(kv => kv.Value))
                        sb.AppendLine($"   {NominalName(kv.Key)}  x{kv.Value}");
                }
                sb.AppendLine();
                sb.AppendLine("The pipes are placed but NOT connected — fittings are not inserted. " +
                              "Check the alignment against the link before doing anything else with them.");
                return sb.ToString();
            }
        }
    }
}
