using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>
    /// Places hard-pipe sprinkler drops to pendent heads, ending in a REAL
    /// threaded elbow at the drop base, with a flexible sprinkler hose from
    /// the elbow to the head.
    ///
    /// WHY AN ELBOW, NOT A UNION: HydraCAD's native drop runs the flex
    /// collinear with the hard pipe, so Revit resolves a union/coupling. We
    /// instead build a genuine angular turn — a short horizontal STUB off the
    /// bottom of the vertical drop — so the fitting between the drop and the
    /// stub is a real 90° elbow (resolved from the drop pipe type's routing
    /// preferences). The elbow can be rotated to aim the flex in any
    /// direction, and the BOM lists an elbow. The flex then runs from the
    /// stub's open end to the head.
    ///
    /// GEOMETRY (up-over-down return bend), per head:
    ///   T  = tap point on the branch line (closest point to the head)
    ///   R1 = T + rise            (riser, vertical)        — armover type
    ///   R2 = (head.XY, R1.Z)     (arm, horizontal)        — armover type
    ///   R3 = (head.XY, head.Z + termHeight)  (drop, vertical) — drop type
    ///   R4 = R3 + aim*stub       (stub, horizontal)       — drop type  ← elbow at R3
    ///   flex: R4 → head inlet
    /// Elbows auto-resolve at R1, R2, R3 because each is a 90° turn. The
    /// branch tee is inserted by breaking the branch at T and calling
    /// NewTeeFitting.
    ///
    /// RELIABILITY: pipes after the first are created with the connector
    /// overload of Pipe.Create (start = previous pipe's free end connector),
    /// which inserts the routing-preference fitting AT CREATION — more
    /// dependable than a post-hoc Connector.ConnectTo. Warnings are swallowed
    /// via an IFailuresPreprocessor so one recoverable warning doesn't abort
    /// the batch; every head is wrapped in try/catch and reported.
    ///
    /// ⚠ This is a first implementation of a hard Revit MEP routing flow and
    /// will likely need field iteration (elbow resolution, branch tee, flex
    /// join are the touchy steps).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SprinklerDropCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Collect types + sensible default (hcad3 threaded) ──
                var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
                    .OrderBy(p => p.Name).Select(p => (id: p.Id.IntegerValue, name: p.Name)).ToList();
                var flexTypes = new FilteredElementCollector(doc).OfClass(typeof(FlexPipeType)).Cast<FlexPipeType>()
                    .OrderBy(p => p.Name)
                    .Select(p => (id: p.Id.IntegerValue, name: $"{p.FamilyName} : {p.Name}")).ToList();
                if (pipeTypes.Count == 0 || flexTypes.Count == 0)
                {
                    TaskDialog.Show("Sprinkler Drops",
                        "Need at least one Pipe Type and one Flex Pipe Type loaded in the project.");
                    return Result.Cancelled;
                }
                int defaultPipeTypeId = DefaultThreadedPipeType(pipeTypes);

                // ── Dialog (settings only; picking happens after, per mode) ──
                Cfg cfg;
                SprinklerDropDialog.ConnectionMode mode;
                using (var dlg = new SprinklerDropDialog(pipeTypes, flexTypes, defaultPipeTypeId))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;
                    mode = dlg.Mode;
                    cfg = new Cfg
                    {
                        DropType = new ElementId(dlg.DropPipeTypeId),
                        ArmType = new ElementId(dlg.ArmPipeTypeId),
                        FlexType = new ElementId(dlg.FlexTypeId),
                        SizeFt = dlg.SizeInches / 12.0,
                        FlexSizeFt = dlg.FlexSizeInches / 12.0,
                        RiseFt = dlg.RiseInches / 12.0,
                        TermFt = dlg.TermHeightInches / 12.0,
                        StubFt = dlg.StubInches / 12.0,
                        MaxFlexFt = dlg.MaxFlexInches / 12.0,
                        Swallow = dlg.SwallowWarnings
                    };
                }

                int done = 0, failed = 0;
                var failReasons = new List<string>();

                if (mode == SprinklerDropDialog.ConnectionMode.Continuous)
                {
                    // Click a head, then its pipe; repeat until Esc.
                    while (true)
                    {
                        FamilyInstance head; Pipe pipe;
                        try
                        {
                            var hRef = uidoc.Selection.PickObject(ObjectType.Element,
                                new CategoryFilter(BuiltInCategory.OST_Sprinklers),
                                "Pick a sprinkler head (Esc to finish).");
                            head = doc.GetElement(hRef) as FamilyInstance;
                            var pRef = uidoc.Selection.PickObject(ObjectType.Element,
                                new CategoryFilter(BuiltInCategory.OST_PipeCurves),
                                "Pick the pipe to connect it to (Esc to finish).");
                            pipe = doc.GetElement(pRef) as Pipe;
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                        if (head == null || pipe == null) continue;

                        using (var tx = new Transaction(doc, "Sprinkler Drop"))
                        {
                            tx.Start();
                            ApplySwallow(tx, cfg.Swallow);
                            string err = ConnectForHead(doc, head, pipe, cfg, out _);
                            tx.Commit();
                            if (err == null) done++;
                            else { failed++; failReasons.Add($"  {head.Id}: {err}"); }
                        }
                    }
                }
                else
                {
                    // Batch: heads from selection (or pick), then ONE pipe.
                    var heads = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id))
                        .OfType<FamilyInstance>()
                        .Where(fi => fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers)
                        .ToList();
                    if (heads.Count == 0)
                    {
                        try
                        {
                            var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                                new CategoryFilter(BuiltInCategory.OST_Sprinklers),
                                "Select sprinkler heads, then Finish.");
                            heads = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                    }
                    if (heads.Count == 0) { TaskDialog.Show("Sprinkler Drops", "No heads selected."); return Result.Cancelled; }

                    Pipe pipe;
                    try
                    {
                        var pRef = uidoc.Selection.PickObject(ObjectType.Element,
                            new CategoryFilter(BuiltInCategory.OST_PipeCurves),
                            "Pick the one pipe to connect all selected heads to.");
                        pipe = doc.GetElement(pRef) as Pipe;
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                    if (pipe == null) { TaskDialog.Show("Sprinkler Drops", "No pipe selected."); return Result.Cancelled; }

                    using (var tx = new Transaction(doc, "Place Sprinkler Drops"))
                    {
                        tx.Start();
                        ApplySwallow(tx, cfg.Swallow);

                        // Teeing onto the pipe splits it; track the pieces so each
                        // head ties into whichever piece sits under its location.
                        var pieceIds = new List<ElementId> { pipe.Id };
                        foreach (var head in heads)
                        {
                            try
                            {
                                Connector inlet = InletConnector(head);
                                if (inlet == null) { failed++; failReasons.Add($"  {head.Id}: no open inlet"); continue; }
                                XYZ H = inlet.Origin;

                                Pipe target = null;
                                foreach (var pid in pieceIds)
                                {
                                    var pp = doc.GetElement(pid) as Pipe;
                                    var ln = (pp?.Location as LocationCurve)?.Curve as Line;
                                    if (ln != null && FootWithinSegment(ln, H)) { target = pp; break; }
                                }
                                if (target == null)
                                {
                                    failed++;
                                    failReasons.Add($"  {head.Id}: not in-line/perpendicular to the pipe run");
                                    continue;
                                }

                                string err = ConnectForHead(doc, head, target, cfg, out ElementId newPiece);
                                if (newPiece != ElementId.InvalidElementId) pieceIds.Add(newPiece);
                                if (err == null) done++;
                                else { failed++; failReasons.Add($"  {head.Id}: {err}"); }
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                failReasons.Add($"  {head.Id}: {ex.Message}");
                            }
                        }

                        tx.Commit();
                    }
                }

                var lines = new List<string> { $"Sprinkler Drops — placed: {done}, failed/skipped: {failed}." };
                if (failReasons.Count > 0)
                {
                    lines.Add("");
                    lines.Add("Issues:");
                    lines.AddRange(failReasons.Take(15));
                    if (failReasons.Count > 15) lines.Add($"  …and {failReasons.Count - 15} more.");
                }
                TaskDialog.Show("Sprinkler Drops", string.Join("\n", lines));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Connects one head to the given pipe: resolves system/level, then
        /// places the drop. Returns null on success or a short reason string.
        /// <paramref name="newPiece"/> receives the new pipe element the branch
        /// split created (for batch piece tracking), else Invalid.
        /// </summary>
        private string ConnectForHead(Document doc, FamilyInstance head, Pipe pipe, Cfg cfg, out ElementId newPiece)
        {
            newPiece = ElementId.InvalidElementId;
            var line = (pipe.Location as LocationCurve)?.Curve as Line;
            if (line == null) return "pipe is not straight";
            ElementId sysId = pipe.MEPSystem?.GetTypeId() ?? FirstPipingSystemTypeId(doc);
            ElementId lvlId = pipe.ReferenceLevel?.Id ?? FirstLevelId(doc);
            if (sysId == ElementId.InvalidElementId || lvlId == ElementId.InvalidElementId)
                return "no system/level on the pipe";
            return PlaceOne(doc, head, pipe, line, sysId, lvlId,
                cfg.DropType, cfg.ArmType, cfg.FlexType,
                cfg.SizeFt, cfg.FlexSizeFt, cfg.RiseFt, cfg.TermFt, cfg.StubFt, cfg.MaxFlexFt, out newPiece);
        }

        /// <summary>Places one drop. Returns null on success, or a short reason string on failure.</summary>
        private string PlaceOne(Document doc, FamilyInstance head, Pipe branch, Line branchLine,
            ElementId sysTypeId, ElementId levelId, ElementId dropTypeId, ElementId armTypeId, ElementId flexTypeId,
            double sizeFt, double flexSizeFt, double riseFt, double termFt, double stubFt, double maxFlexFt,
            out ElementId newBranchPiece)
        {
            newBranchPiece = ElementId.InvalidElementId;
            const double MinSeg = 0.04;   // ~1/2" — below this a segment is dropped (collinear/degenerate)
            const double MinArm = 0.08;   // ~1" — below this the up-over has no real horizontal turn

            Connector inlet = InletConnector(head);
            if (inlet == null) return "no open piping inlet on head (already dropped?)";

            XYZ H = inlet.Origin;
            XYZ T = branchLine.Project(H)?.XYZPoint;
            if (T == null) return "could not project head onto branch";

            double termZ = H.Z + termFt;
            if (termZ <= H.Z + 1e-6) termZ = H.Z + (1.0 / 12.0); // guard: keep hard pipe above head

            // Route points.
            XYZ R1 = new XYZ(T.X, T.Y, T.Z + riseFt);              // top of riser
            XYZ R2 = new XYZ(H.X, H.Y, R1.Z);                      // over the head
            XYZ R3 = new XYZ(H.X, H.Y, termZ);                     // drop base
            if (R2.Z - R3.Z < MinSeg) return "drop too short — termination height too high vs rise";

            // Horizontal aim for the stub (and thus the elbow rotation): along
            // the arm if there is one, else perpendicular to the branch.
            XYZ armXY = new XYZ(R2.X - R1.X, R2.Y - R1.Y, 0);
            bool haveRise = (R1.Z - T.Z) > MinArm;
            bool haveArm = armXY.GetLength() > MinArm;
            XYZ aim = haveArm ? armXY.Normalize() : Perp(branchLine);
            XYZ R4 = R3 + aim * stubFt;                            // stub end — flex starts here

            // Optional flex-length sanity check.
            if (maxFlexFt > 1e-6)
            {
                double flexNeed = R4.DistanceTo(H);
                if (flexNeed > maxFlexFt)
                    return $"flex would need {flexNeed * 12:F1}\" > max {maxFlexFt * 12:F0}\"";
            }

            // ── Build the hard-pipe run as a polyline of FREE pipes that share
            //    endpoints, then insert a REAL elbow at every interior joint.
            //    (The connector overload of Pipe.Create connected the segments
            //    but did not insert BOM elbows; explicit NewElbowFitting does.)
            //
            //    Points, branch → stub end:  T → [R1] → [R2] → R3 → R4
            //      • R1 only when there's a rise
            //      • R2 only when the head is offset from the branch (an arm)
            //    Segments ending at or starting from R3 are the DROP type; the
            //    rest are the ARMOVER type.
            var pts = new List<XYZ> { T };
            if (haveRise) pts.Add(R1);
            if (haveArm) pts.Add(R2);
            pts.Add(R3);
            pts.Add(R4);
            DedupeConsecutive(pts, MinSeg);
            if (pts.Count < 2) return "degenerate geometry (no segments to build)";

            var segPipes = new List<Pipe>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                XYZ a = pts[i], b = pts[i + 1];
                // Drop type for the vertical drop into R3 and the stub off R3.
                bool isDropOrStub = a.IsAlmostEqualTo(R3) || b.IsAlmostEqualTo(R3);
                ElementId segType = isDropOrStub ? dropTypeId : armTypeId;
                Pipe p = Pipe.Create(doc, sysTypeId, segType, levelId, a, b);
                if (p == null) return $"failed to create pipe segment {i + 1}";
                SetDiameter(p, sizeFt);
                segPipes.Add(p);
            }

            Pipe first = segPipes[0];                       // taps the branch at T
            Pipe stub = segPipes[segPipes.Count - 1];      // open end at R4 feeds the flex
            Connector stubEnd = EndConnectorNear(stub, R4);
            if (stubEnd == null) return "failed to resolve stub end connector";

            // Interior joints: elbow between each consecutive pair (fallback to a
            // bare connection if an elbow can't resolve, e.g. collinear).
            for (int i = 0; i < segPipes.Count - 1; i++)
            {
                XYZ joint = pts[i + 1];
                Connector c1 = EndConnectorNear(segPipes[i], joint);
                Connector c2 = EndConnectorNear(segPipes[i + 1], joint);
                JoinWithElbow(doc, c1, c2);
            }

            // ── Tee onto the branch at T. ──
            try
            {
                Connector tapConn = EndConnectorNear(first, T);
                if (tapConn != null && !tapConn.IsConnected)
                {
                    ElementId newBranchId = PlumbingUtils.BreakCurve(doc, branch.Id, T);
                    newBranchPiece = newBranchId;
                    var branchPiece2 = doc.GetElement(newBranchId) as Pipe;
                    Connector b1 = EndConnectorNear(branch, T);
                    Connector b2 = branchPiece2 != null ? EndConnectorNear(branchPiece2, T) : null;
                    if (b1 != null && b2 != null)
                        doc.Create.NewTeeFitting(b1, b2, tapConn);
                    else
                        tapConn.ConnectTo(b1 ?? b2);
                }
            }
            catch (Exception ex)
            {
                // Hard pipe is placed; just the branch tap failed. Report but
                // don't abort — user can connect the riser manually.
                return "drop placed but branch tee failed: " + ex.Message;
            }

            // ── Flex: head inlet (H) → stub open end (R4). Points are ordered
            //    HEAD-FIRST on purpose: the flex family's first connector carries
            //    the reducing nipple + bracket, which belongs at the SPRINKLER
            //    HEAD; the plain threaded end then lands on the hard pipe. ──
            try
            {
                var fpts = new List<XYZ> { H, R4 };
                FlexPipe flex = FlexPipe.Create(doc, sysTypeId, flexTypeId, levelId, fpts);
                if (flex != null)
                {
                    var fc = flex.ConnectorManager.Connectors.Cast<Connector>()
                        .Where(c => c.ConnectorType == ConnectorType.End).ToList();
                    Connector flexAtHead = fc.OrderBy(c => c.Origin.DistanceTo(H)).FirstOrDefault();
                    Connector flexAtStub = fc.OrderBy(c => c.Origin.DistanceTo(R4)).FirstOrDefault();
                    if (flexAtHead != null && !flexAtHead.IsConnected) { try { flexAtHead.ConnectTo(inlet); } catch { } }
                    if (flexAtStub != null && !flexAtStub.IsConnected) { try { flexAtStub.ConnectTo(stubEnd); } catch { } }
                    // Set flex size LAST so a ConnectTo auto-resize doesn't override it.
                    SetDiameter(flex, flexSizeFt);
                }
                else return "drop placed but flex create returned null";
            }
            catch (Exception ex)
            {
                return "drop placed but flex failed: " + ex.Message;
            }

            return null;
        }

        /// <summary>
        /// Inserts a real elbow fitting between two free, coincident-origin end
        /// connectors. Falls back to a bare ConnectTo if an elbow can't resolve
        /// (e.g. the two runs are collinear, or the type has no elbow rule).
        /// </summary>
        private static void JoinWithElbow(Document doc, Connector c1, Connector c2)
        {
            if (c1 == null || c2 == null) return;
            if (c1.IsConnected || c2.IsConnected) return;
            try { doc.Create.NewElbowFitting(c1, c2); }
            catch { try { c1.ConnectTo(c2); } catch { } }
        }

        /// <summary>Removes consecutive points closer than <paramref name="minFt"/> (in place).</summary>
        private static void DedupeConsecutive(List<XYZ> pts, double minFt)
        {
            for (int i = pts.Count - 1; i > 0; i--)
                if (pts[i].DistanceTo(pts[i - 1]) < minFt) pts.RemoveAt(i);
        }

        /// <summary>Bundle of dialog-chosen settings passed to placement.</summary>
        private class Cfg
        {
            public ElementId DropType, ArmType, FlexType;
            public double SizeFt, FlexSizeFt, RiseFt, TermFt, StubFt, MaxFlexFt;
            public bool Swallow;
        }

        private static void ApplySwallow(Transaction tx, bool swallow)
        {
            if (!swallow) return;
            var fho = tx.GetFailureHandlingOptions();
            fho.SetFailuresPreprocessor(new WarningSwallower());
            fho.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(fho);
        }

        /// <summary>Pick a sensible default drop/armover pipe type: hcad3 + threaded, else any threaded, else first.</summary>
        private static int DefaultThreadedPipeType(List<(int id, string name)> types)
        {
            foreach (var t in types)
                if (t.name.IndexOf("hcad3", StringComparison.OrdinalIgnoreCase) >= 0
                    && t.name.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0)
                    return t.id;
            foreach (var t in types)
                if (t.name.IndexOf("thread", StringComparison.OrdinalIgnoreCase) >= 0)
                    return t.id;
            return types.Count > 0 ? types[0].id : -1;
        }

        /// <summary>True if <paramref name="pt"/>'s projection lands within the pipe segment (±6").</summary>
        private static bool FootWithinSegment(Line line, XYZ pt)
        {
            XYZ p0 = line.GetEndPoint(0), p1 = line.GetEndPoint(1);
            XYZ dir = p1 - p0;
            double len = dir.GetLength();
            if (len < 1e-9) return false;
            dir = dir.Normalize();
            double t = (pt - p0).DotProduct(dir);
            return t >= -0.5 && t <= len + 0.5;
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

        /// <summary>Horizontal unit vector perpendicular to the branch line.</summary>
        private static XYZ Perp(Line branchLine)
        {
            XYZ d = (branchLine.GetEndPoint(1) - branchLine.GetEndPoint(0));
            XYZ dxy = new XYZ(d.X, d.Y, 0);
            if (dxy.GetLength() < 1e-9) return XYZ.BasisX;
            dxy = dxy.Normalize();
            return new XYZ(-dxy.Y, dxy.X, 0); // rotate 90° in plan
        }

        // ── Connector / geometry helpers ──

        private static Connector InletConnector(FamilyInstance head)
        {
            var cm = head.MEPModel?.ConnectorManager;
            if (cm == null) return null;
            Connector best = null;
            foreach (Connector c in cm.Connectors)
            {
                if (c.Domain != Domain.DomainPiping) continue;
                if (c.IsConnected) continue;
                if (c.ConnectorType == ConnectorType.End) return c;
                best = best ?? c;
            }
            return best;
        }

        private static Connector EndConnectorNear(MEPCurve curve, XYZ pt)
        {
            Connector best = null; double bestD = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                double d = c.Origin.DistanceTo(pt);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private static ElementId FirstPipingSystemTypeId(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).FirstOrDefault()?.Id
               ?? ElementId.InvalidElementId;

        private static ElementId FirstLevelId(Document doc)
            => new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstOrDefault()?.Id
               ?? ElementId.InvalidElementId;

        private class CategoryFilter : ISelectionFilter
        {
            private readonly int _cat;
            public CategoryFilter(BuiltInCategory c) { _cat = (int)c; }
            public bool AllowElement(Element e) => e.Category?.Id.IntegerValue == _cat;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        /// <summary>Swallows recoverable warnings so batch placement isn't interrupted.</summary>
        private class WarningSwallower : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (FailureMessageAccessor m in a.GetFailureMessages())
                {
                    if (m.GetSeverity() == FailureSeverity.Warning)
                        a.DeleteWarning(m);
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}
