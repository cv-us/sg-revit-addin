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

                // Best-fitting built-in catalog, for the dialog's up-front preview.
                var cats = new (string name, SizeEntry[] table)[]
                { ("Steel / IPS", Steel), ("Ductile iron", DuctileIron), ("PVC C900", PvcC900) };
                var bestCat = cats.OrderBy(c => TableError(c.table, runs)).First();

                using (var dlg = new TraceCadPipeDialog(pipeTypes, systems,
                                     levels.Select(l => l.Name).ToList(), runs.Count,
                                     runs.Sum(r => r.LengthFt),
                                     SizeSummary(bestCat.table, runs),
                                     bestCat.name, TableError(bestCat.table, runs) * 1000.0,
                                     cats.Select(c => c.name + $"  (fit {TableError(c.table, runs) * 1000:0} mil)").ToList()))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;

                    Level level = levels.FirstOrDefault(l => l.Name == dlg.LevelName) ?? levels[0];
                    var keep = runs.Where(r => r.LengthFt >= dlg.MinLengthFt).ToList();
                    if (keep.Count == 0)
                    {
                        TaskDialog.Show("Trace CAD Pipe", $"No runs are at least {dlg.MinLengthFt:0.##} ft long.");
                        return Result.Cancelled;
                    }

                    // Which size table to resolve OD -> nominal against.
                    SizeEntry[] table = null;
                    string tableName;
                    if (dlg.CatalogIndex < 0)   // "from the pipe type"
                    {
                        table = TableFromPipeType(doc, new ElementId(dlg.PipeTypeId));
                        tableName = table != null ? "the pipe type's own size table" : null;
                        if (table == null)
                        { table = bestCat.table; tableName = bestCat.name + " (the pipe type carries no sizes)"; }
                    }
                    else
                    { table = cats[dlg.CatalogIndex].table; tableName = cats[dlg.CatalogIndex].name; }

                    var rpt = new Report
                    {
                        Considered = runs.Count,
                        Skipped = runs.Count - keep.Count,
                        TableName = tableName,
                        FitMil = TableError(table, keep) * 1000.0
                    };

                    using (var tx = new Transaction(doc, "Trace CAD Pipe"))
                    {
                        tx.Start();
                        var placed = new List<Pipe>();
                        foreach (Run r in keep)
                        {
                            // NOMINAL, not OD — that distinction is the whole bug behind "10-3/4 in".
                            double nomIn = dlg.ForceSizeIn > 0 ? dlg.ForceSizeIn
                                         : dlg.UseMeasured ? r.DiameterIn
                                         : Match(table, r.DiameterIn).NominalIn;

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

                            SetDiameter(p, nomIn / 12.0);
                            placed.Add(p);
                            rpt.Placed++;
                            rpt.LengthFt += r.LengthFt;

                            // report the size Revit ACTUALLY took, not the one we asked for —
                            // the pipe type's size list can refuse a value.
                            double actual = nomIn;
                            try
                            {
                                var prm = p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                                if (prm != null) actual = prm.AsDouble() * 12.0;
                            }
                            catch { }
                            string lbl = Frac(actual) + "\"";
                            rpt.Sizes[lbl] = rpt.Sizes.TryGetValue(lbl, out int nn) ? nn + 1 : 1;
                            if (Math.Abs(actual - nomIn) > 0.02) rpt.SizeRefused++;
                        }

                        if (dlg.ConnectEnds) ConnectRuns(doc, placed, rpt);
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

        // ── size tables ─────────────────────────────────────────────────────────────
        //
        // What we MEASURE off the mesh is the OUTSIDE diameter. What Revit's diameter
        // parameter wants is the NOMINAL size. Those are not the same number, and
        // conflating them is what produced "10-3/4 in" pipe: 10.750 is the OD of 10 in
        // steel, handed to Revit as though it were the nominal.
        //
        // The OD for a given nominal also depends on the MATERIAL, so matching against the
        // wrong catalog mislabels everything. Measured against a real underground model:
        //     steel / IPS   mean error 375 mil
        //     ductile iron  mean error  36 mil   <- 6.90 -> DI 6", 11.10 -> DI 10", exact
        //     PVC C900      mean error  36 mil   (same ODs as DI in these sizes)
        //
        // So the table is read from the chosen PIPE TYPE's own segments where possible —
        // that is self-consistent with whatever material the user picked — and falls back
        // to these catalogs only if the type carries no sizes.

        private class SizeEntry
        {
            public double NominalIn;
            public double OdIn;
            public string Label;
        }

        private static readonly SizeEntry[] Steel = Build(
            new[] { 1.0, 1.25, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 12.0 },
            new[] { 1.315, 1.660, 1.900, 2.375, 2.875, 3.500, 4.500, 5.563, 6.625, 8.625, 10.750, 12.750 });

        private static readonly SizeEntry[] DuctileIron = Build(          // AWWA C151
            new[] { 3.0, 4.0, 6.0, 8.0, 10.0, 12.0, 14.0, 16.0, 18.0, 20.0, 24.0 },
            new[] { 3.96, 4.80, 6.90, 9.05, 11.10, 13.20, 15.30, 17.40, 19.50, 21.60, 25.80 });

        private static readonly SizeEntry[] PvcC900 = Build(             // cast-iron OD
            new[] { 4.0, 6.0, 8.0, 10.0, 12.0, 14.0, 16.0 },
            new[] { 4.80, 6.90, 9.05, 11.10, 13.20, 15.30, 17.40 });

        private static SizeEntry[] Build(double[] nom, double[] od)
        {
            var list = new SizeEntry[nom.Length];
            for (int i = 0; i < nom.Length; i++)
                list[i] = new SizeEntry { NominalIn = nom[i], OdIn = od[i], Label = Frac(nom[i]) + "\"" };
            return list;
        }

        private static string Frac(double inches)
        {
            int whole = (int)Math.Floor(inches + 1e-6);
            double f = inches - whole;
            string fr = f < 0.0625 ? "" : f < 0.3125 ? "-1/4" : f < 0.4375 ? "-1/3"
                      : f < 0.5625 ? "-1/2" : f < 0.8125 ? "-3/4" : "";
            if (fr == "" && f >= 0.5625) { whole += 1; }
            return whole + fr;
        }

        /// <summary>The size table of the chosen pipe type, read from its routing-preference
        /// segments. Both nominal and outside diameter come straight from Revit, so a ductile
        /// iron or PVC type resolves correctly without needing to know which it is.</summary>
        private static SizeEntry[] TableFromPipeType(Document doc, ElementId pipeTypeId)
        {
            try
            {
                var pt = doc.GetElement(pipeTypeId) as PipeType;
                if (pt == null) return null;
                RoutingPreferenceManager rpm = pt.RoutingPreferenceManager;
                if (rpm == null) return null;

                var found = new Dictionary<double, SizeEntry>();
                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                for (int i = 0; i < n; i++)
                {
                    RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                    if (rule == null) continue;
                    var seg = doc.GetElement(rule.MEPPartId) as Segment;
                    if (seg == null) continue;
                    foreach (MEPSize s in seg.GetSizes())
                    {
                        double nomIn = s.NominalDiameter * 12.0;
                        double odIn = s.OuterDiameter * 12.0;
                        if (odIn <= 0.01) continue;
                        if (!found.ContainsKey(nomIn))
                            found[nomIn] = new SizeEntry { NominalIn = nomIn, OdIn = odIn, Label = Frac(nomIn) + "\"" };
                    }
                }
                return found.Count > 0 ? found.Values.OrderBy(e => e.NominalIn).ToArray() : null;
            }
            catch { return null; }
        }

        private static SizeEntry Match(SizeEntry[] table, double measuredOdIn)
        {
            SizeEntry best = table[0]; double be = double.MaxValue;
            foreach (SizeEntry e in table)
            { double err = Math.Abs(e.OdIn - measuredOdIn); if (err < be) { be = err; best = e; } }
            return best;
        }

        /// <summary>Mean |OD error| of a table against the measured runs — used to pick the
        /// best-fitting catalog automatically and to warn when nothing fits well.</summary>
        private static double TableError(SizeEntry[] table, List<Run> runs)
        {
            if (table == null || table.Length == 0 || runs.Count == 0) return double.MaxValue;
            double tot = 0;
            foreach (Run r in runs) tot += Math.Abs(Match(table, r.DiameterIn).OdIn - r.DiameterIn);
            return tot / runs.Count;
        }

        private static string SizeSummary(SizeEntry[] table, List<Run> runs)
        {
            var by = new Dictionary<string, int>();
            foreach (Run r in runs)
            {
                string k = Match(table, r.DiameterIn).Label;
                by[k] = by.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            return string.Join(",  ", by.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " x" + kv.Value));
        }

        // ── fittings ────────────────────────────────────────────────────────────────
        //
        // Traced runs stop SHORT of each other, because in the source model the fitting body
        // occupies the gap (every one of the 87 solids sat within 0.8 ft of a run end).
        //
        // ONLY genuine angled ELBOWS are built, and only when the corner is provably local.
        // Everything else is left EXACTLY as placed — no pipe is moved or deleted:
        //   • inline / reducer joins (two near-collinear ends) — left alone. This is the fix
        //     for "pipe beyond a reducer vanished": on the real model those near-parallel
        //     axes computed a crossing point up to 8.7 ft away, and extending the small
        //     continuation pipe to it dragged it off into nothing. A reducer also wants a
        //     transition fitting, not an elbow.
        //   • tees / crosses (3+ ends at one point) — left alone; we don't fabricate a
        //     two-way elbow where a branch belongs.
        //   • any corner whose crossing is not close to BOTH ends — left alone.
        // Measured on the real runs: genuine elbows cross within 1.1 ft of both ends with a
        // sub-0.1 ft closest approach; the bounds below keep all of those and drop the rest.

        private const double ClusterFt = 2.5;      // ends this close are treated as one junction
        private const double CornerBoundFt = 2.0;  // the crossing must be within this of both ends
        private const double ApproachGapFt = 0.5;  // the two axis lines must pass this close
        private const double CollinearDot = 0.90;  // |axis . axis| above this = inline, not an elbow

        private class PEnd { public Pipe Pipe; public int Run; public XYZ Pt; public XYZ Axis; }

        private static Connector EndNear(Pipe p, XYZ pt)
        {
            try
            {
                Connector best = null; double bd = double.MaxValue;
                foreach (Connector c in p.ConnectorManager.Connectors)
                {
                    if (c.ConnectorType != ConnectorType.End || c.IsConnected) continue;
                    double d = c.Origin.DistanceTo(pt);
                    if (d < bd) { bd = d; best = c; }
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>Closest approach of two infinite lines. Returns the midpoint of the closest
        /// approach and the gap between the lines there; false if too near-parallel to trust.</summary>
        private static bool ClosestApproach(XYZ p1, XYZ d1, XYZ p2, XYZ d2, out XYZ meet, out double gap)
        {
            meet = null; gap = double.MaxValue;
            XYZ w = p1 - p2;
            double a = d1.DotProduct(d1), b = d1.DotProduct(d2), c = d2.DotProduct(d2);
            double d = d1.DotProduct(w), e = d2.DotProduct(w);
            double den = a * c - b * b;
            if (Math.Abs(den) < 0.02) return false;          // near-parallel: crossing is unstable
            double s = (b * e - c * d) / den, t = (a * e - b * d) / den;
            XYZ q1 = p1 + d1.Multiply(s), q2 = p2 + d2.Multiply(t);
            gap = q1.DistanceTo(q2);
            meet = (q1 + q2).Multiply(0.5);
            return true;
        }

        private static void ConnectRuns(Document doc, List<Pipe> pipes, Report rpt)
        {
            // every free end, with the pipe's own axis
            var ends = new List<PEnd>();
            for (int r = 0; r < pipes.Count; r++)
            {
                var lc = pipes[r].Location as LocationCurve;
                if (lc == null) continue;
                Curve cv = lc.Curve;
                XYZ a = cv.GetEndPoint(0), b = cv.GetEndPoint(1);
                XYZ axis = (b - a).Normalize();
                ends.Add(new PEnd { Pipe = pipes[r], Run = r, Pt = a, Axis = axis });
                ends.Add(new PEnd { Pipe = pipes[r], Run = r, Pt = b, Axis = axis });
            }

            // cluster ends by proximity (union-find)
            int n = ends.Count;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            double c2 = ClusterFt * ClusterFt;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    XYZ dv = ends[i].Pt - ends[j].Pt;
                    if (dv.DotProduct(dv) <= c2) { int ra = find(i), rb = find(j); if (ra != rb) parent[ra] = rb; }
                }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                List<int> g;
                if (!groups.TryGetValue(r, out g)) { g = new List<int>(); groups[r] = g; }
                g.Add(i);
            }

            foreach (var g in groups.Values)
            {
                if (g.Count == 1) continue;                       // open / dead end
                if (g.Count >= 3) { rpt.LeftMultiway++; continue; } // tee / cross — don't fabricate

                PEnd e0 = ends[g[0]], e1 = ends[g[1]];
                if (e0.Run == e1.Run) continue;                   // both ends of one tiny run

                double axisDot = Math.Abs(e0.Axis.DotProduct(e1.Axis));
                if (axisDot > CollinearDot) { rpt.LeftInline++; continue; }   // reducer / straight — leave alone

                XYZ meet; double gap;
                if (!ClosestApproach(e0.Pt, e0.Axis, e1.Pt, e1.Axis, out meet, out gap)
                    || gap > ApproachGapFt
                    || meet.DistanceTo(e0.Pt) > CornerBoundFt
                    || meet.DistanceTo(e1.Pt) > CornerBoundFt)
                { rpt.CornerSkipped++; continue; }

                // Both extends are bounded and non-flipping; if either refuses, nothing moved.
                if (!ExtendTo(e0.Pipe, e0.Pt, meet) || !ExtendTo(e1.Pipe, e1.Pt, meet))
                { rpt.CornerSkipped++; continue; }

                doc.Regenerate();
                Connector ca = EndNear(e0.Pipe, meet), cb = EndNear(e1.Pipe, meet);
                if (ca == null || cb == null) { rpt.CornerSkipped++; continue; }

                // The pipes already meet at the corner; if the fitting can't be made, leave
                // them touching (still correct geometry) rather than force a bad connect.
                try { doc.Create.NewElbowFitting(ca, cb); rpt.Elbows++; }
                catch { rpt.CornerSkipped++; }
            }
        }

        /// <summary>Move the pipe endpoint nearest <paramref name="from"/> to <paramref name="to"/>,
        /// but only if the move is small and doesn't flip or collapse the pipe. Returns false —
        /// changing nothing — otherwise. This is what makes the join pass unable to lose pipe.</summary>
        private static bool ExtendTo(Pipe p, XYZ from, XYZ to)
        {
            try
            {
                var lc = p.Location as LocationCurve;
                if (lc == null) return false;
                Curve cv = lc.Curve;
                XYZ a = cv.GetEndPoint(0), b = cv.GetEndPoint(1);
                bool movingA = a.DistanceTo(from) <= b.DistanceTo(from);
                XYZ moving = movingA ? a : b, fixedEnd = movingA ? b : a;

                if (moving.DistanceTo(to) > CornerBoundFt) return false;       // never drag far
                double oldLen = a.DistanceTo(b), newLen = fixedEnd.DistanceTo(to);
                if (newLen < 0.05 || newLen < 0.3 * oldLen || newLen > 3.0 * oldLen) return false;
                // no flip: the pipe must still point the same general way
                if ((to - fixedEnd).Normalize().DotProduct((moving - fixedEnd).Normalize()) < 0.5) return false;

                XYZ na = movingA ? to : a, nb = movingA ? b : to;
                lc.Curve = Line.CreateBound(na, nb);
                return true;
            }
            catch { return false; }
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
            public int Considered, Placed, Skipped, Fail, SizeRefused;
            public int Elbows, LeftInline, LeftMultiway, CornerSkipped;
            public double LengthFt, FitMil;
            public string TableName;
            public readonly Dictionary<string, int> Sizes = new Dictionary<string, int>();

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
                    sb.AppendLine($"Sizes (matched against {TableName}, mean fit {FitMil:0} mil):");
                    foreach (var kv in Sizes.OrderByDescending(kv => kv.Value))
                        sb.AppendLine($"   {kv.Key}  x{kv.Value}");
                    if (FitMil > 150)
                        sb.AppendLine("   NOTE: that is a loose fit — the pipe may be a different material " +
                                      "than the table assumes. Try another catalog in the dialog.");
                    if (SizeRefused > 0)
                        sb.AppendLine($"   {SizeRefused} pipe(s) came out a different size than asked — " +
                                      "the pipe type's size list does not carry that size.");
                }

                if (Elbows + LeftInline + LeftMultiway + CornerSkipped > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Elbows inserted at angled corners: {Elbows}");
                    if (LeftInline > 0)
                        sb.AppendLine($"Inline joins (reducers / straight runs) left as-is: {LeftInline} " +
                                      "— these need a reducer, not an elbow, so the pipe is left untouched.");
                    if (LeftMultiway > 0)
                        sb.AppendLine($"Tees / crosses left as-is: {LeftMultiway} — a branch fitting isn't fabricated.");
                    if (CornerSkipped > 0)
                        sb.AppendLine($"Corners skipped (crossing not local enough to trust): {CornerSkipped}");
                    sb.AppendLine("Nothing but the angled elbows was moved — every other run is exactly where it was placed.");
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine("Pipes are NOT connected — no fittings inserted.");
                }

                sb.AppendLine();
                sb.AppendLine("Check the alignment against the link before doing anything else with these.");
                return sb.ToString();
            }
        }
    }
}
