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
                // ── Gather heads + branch from selection (or pick) ──
                var sel = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
                var heads = sel.OfType<FamilyInstance>()
                    .Where(fi => fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers)
                    .ToList();
                Pipe branch = sel.OfType<Pipe>().FirstOrDefault();

                if (heads.Count == 0)
                {
                    try
                    {
                        var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                            new CategoryFilter(BuiltInCategory.OST_Sprinklers),
                            "Select pendent sprinkler heads to drop to, then Finish.");
                        heads = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
                if (heads.Count == 0)
                {
                    TaskDialog.Show("Sprinkler Drops", "No sprinkler heads selected.");
                    return Result.Cancelled;
                }

                if (branch == null)
                {
                    try
                    {
                        var r = uidoc.Selection.PickObject(ObjectType.Element,
                            new CategoryFilter(BuiltInCategory.OST_PipeCurves),
                            "Select the BRANCH LINE pipe to connect the drops to.");
                        branch = doc.GetElement(r) as Pipe;
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
                if (branch == null)
                {
                    TaskDialog.Show("Sprinkler Drops", "No branch-line pipe selected.");
                    return Result.Cancelled;
                }

                // ── Dialog ──
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

                int dropTypeId, armTypeId, flexTypeId;
                double riseFt, termFt, stubFt, maxFlexFt;
                bool swallow;
                using (var dlg = new SprinklerDropDialog(pipeTypes, flexTypes))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;
                    dropTypeId = dlg.DropPipeTypeId; armTypeId = dlg.ArmPipeTypeId; flexTypeId = dlg.FlexTypeId;
                    riseFt = dlg.RiseInches / 12.0; termFt = dlg.TermHeightInches / 12.0;
                    stubFt = dlg.StubInches / 12.0; maxFlexFt = dlg.MaxFlexInches / 12.0;
                    swallow = dlg.SwallowWarnings;
                }

                // ── Resolve system + level from the branch ──
                ElementId sysTypeId = branch.MEPSystem?.GetTypeId() ?? FirstPipingSystemTypeId(doc);
                ElementId levelId = branch.ReferenceLevel?.Id ?? FirstLevelId(doc);
                if (sysTypeId == ElementId.InvalidElementId || levelId == ElementId.InvalidElementId)
                {
                    TaskDialog.Show("Sprinkler Drops", "Could not resolve a piping system type or level from the branch.");
                    return Result.Failed;
                }

                Line branchLine = (branch.Location as LocationCurve)?.Curve as Line;
                if (branchLine == null)
                {
                    TaskDialog.Show("Sprinkler Drops", "The selected branch is not a straight pipe.");
                    return Result.Cancelled;
                }

                int done = 0, failed = 0;
                var failReasons = new List<string>();

                using (var tx = new Transaction(doc, "Place Sprinkler Drops"))
                {
                    tx.Start();
                    if (swallow)
                    {
                        var fho = tx.GetFailureHandlingOptions();
                        fho.SetFailuresPreprocessor(new WarningSwallower());
                        fho.SetClearAfterRollback(true);
                        tx.SetFailureHandlingOptions(fho);
                    }

                    foreach (var head in heads)
                    {
                        try
                        {
                            string err = PlaceOne(doc, head, branch, branchLine, sysTypeId, levelId,
                                new ElementId(dropTypeId), new ElementId(armTypeId), new ElementId(flexTypeId),
                                riseFt, termFt, stubFt, maxFlexFt);
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

                var lines = new List<string>
                {
                    $"Sprinkler Drops — {heads.Count} head(s).",
                    $"Placed: {done}",
                    $"Failed/skipped: {failed}"
                };
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

        /// <summary>Places one drop. Returns null on success, or a short reason string on failure.</summary>
        private string PlaceOne(Document doc, FamilyInstance head, Pipe branch, Line branchLine,
            ElementId sysTypeId, ElementId levelId, ElementId dropTypeId, ElementId armTypeId, ElementId flexTypeId,
            double riseFt, double termFt, double stubFt, double maxFlexFt)
        {
            const double MinSeg = 0.01;   // ~1/8"
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
            XYZ aim = armXY.GetLength() > MinArm
                ? armXY.Normalize()
                : Perp(branchLine);
            XYZ R4 = R3 + aim * stubFt;                            // stub end — flex starts here

            // Optional flex-length sanity check.
            if (maxFlexFt > 1e-6)
            {
                double flexNeed = R4.DistanceTo(H);
                if (flexNeed > maxFlexFt)
                    return $"flex would need {flexNeed * 12:F1}\" > max {maxFlexFt * 12:F0}\"";
            }

            bool haveRise = (R1.Z - T.Z) > MinArm;
            bool haveArm = armXY.GetLength() > MinArm;

            // ── Build the hard-pipe run. First segment is a free XYZ pipe;
            //    subsequent segments use the connector overload so the
            //    routing-preference elbow inserts at creation. ──
            Pipe first = null;     // segment whose start connector taps the branch (at T)
            Connector flowEnd;     // free connector that feeds the next segment

            if (haveRise)
            {
                first = Pipe.Create(doc, sysTypeId, armTypeId, levelId, T, R1);
                flowEnd = EndConnectorNear(first, R1);
            }
            else
            {
                // No rise: arm starts at branch height directly to over-head.
                first = Pipe.Create(doc, sysTypeId, armTypeId, levelId, T, R2);
                flowEnd = EndConnectorNear(first, R2);
            }
            if (first == null || flowEnd == null) return "failed to create first segment";

            if (haveRise && haveArm)
            {
                Pipe arm = Pipe.Create(doc, armTypeId, levelId, flowEnd, R2); // elbow at R1
                flowEnd = EndConnectorNear(arm, R2);
            }

            // Drop (vertical) from R2 to R3 — elbow at R2.
            Pipe drop = Pipe.Create(doc, dropTypeId, levelId, flowEnd, R3);
            Connector dropEnd = EndConnectorNear(drop, R3);
            if (dropEnd == null) return "failed to create drop";

            // Stub (horizontal) from R3 to R4 — REAL 90° elbow at R3 (drop base).
            Pipe stub = Pipe.Create(doc, dropTypeId, levelId, dropEnd, R4);
            Connector stubEnd = EndConnectorNear(stub, R4);
            if (stubEnd == null) return "failed to create stub";

            // ── Tee onto the branch at T. ──
            try
            {
                Connector tapConn = EndConnectorNear(first, T);
                if (tapConn != null && !tapConn.IsConnected)
                {
                    ElementId newBranchId = PlumbingUtils.BreakCurve(doc, branch.Id, T);
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

            // ── Flex: stub open end (R4) → head inlet (H). ──
            try
            {
                var pts = new List<XYZ> { R4, H };
                FlexPipe flex = FlexPipe.Create(doc, sysTypeId, flexTypeId, levelId, pts);
                if (flex != null)
                {
                    var fc = flex.ConnectorManager.Connectors.Cast<Connector>()
                        .Where(c => c.ConnectorType == ConnectorType.End).ToList();
                    Connector flexAtStub = fc.OrderBy(c => c.Origin.DistanceTo(R4)).FirstOrDefault();
                    Connector flexAtHead = fc.OrderBy(c => c.Origin.DistanceTo(H)).FirstOrDefault();
                    if (flexAtStub != null && !flexAtStub.IsConnected) { try { flexAtStub.ConnectTo(stubEnd); } catch { } }
                    if (flexAtHead != null && !flexAtHead.IsConnected) { try { flexAtHead.ConnectTo(inlet); } catch { } }
                }
                else return "drop placed but flex create returned null";
            }
            catch (Exception ex)
            {
                return "drop placed but flex failed: " + ex.Message;
            }

            return null;
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
