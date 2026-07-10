using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.SprinklerLayout
{
    /// <summary>
    /// "Layout" — lays out branch lines with sprinklers at VARIABLE spacings that TILE
    /// to fill a picked area. Line spacings live in numbered slots, head spacings in
    /// lettered slots; the sequence strings cycle to span the area ("112112" tiles the
    /// line pattern across the width, "AABA" tiles heads along each line's length).
    ///
    /// Two pick modes:
    ///  • Fill area — pick two opposite corners; lines fill the rectangle.
    ///  • Area + central main — pick two corners and a main line; branches slope DOWN
    ///    toward the main from both sides (draining dry/pre-action systems) and tie into
    ///    the main with a riser nipple + tee, while the main slopes toward its riser end.
    ///
    /// Branch ends can be capped (cap fitting from the pipe type's routing preferences)
    /// a settable distance past the last head. Heads connect directly at outlets or on
    /// vertical sprigs; sprigs can terminate at a common elevation (lengths adapt to the
    /// slope) or a fixed length.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LayoutCommand : IExternalCommand
    {
        private const double SprigMinFt = 0.05;   // a sprig shorter than this becomes a line outlet
        private const double HeadMarginFt = 1.0;  // keep heads at least this far from the main crossing
        private const double MainStubFt = 0.5;    // 6" the main continues past the last high-end riser
        private const double CrossFlatFt = 1.0;   // short horizontal run at the crossing so the tee is collinear

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            Document doc = uidoc.Document;

            // ── Dropdown data ──
            var pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
                .OrderBy(p => p.Name).Select(p => (id: p.Id.IntegerValue, name: p.Name)).ToList();
            if (pipeTypes.Count == 0)
            { TaskDialog.Show("Layout", "No pipe types in this project."); return Result.Cancelled; }

            var systemTypes = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>()
                .OrderBy(s => s.Name).Select(s => (id: s.Id.IntegerValue, name: s.Name)).ToList();
            if (systemTypes.Count == 0)
            { TaskDialog.Show("Layout", "No piping system types in this project."); return Result.Cancelled; }

            var headSyms = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers)
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .Select(fs => (id: fs.Id.IntegerValue, name: $"{fs.FamilyName} : {fs.Name}")).ToList();

            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0)
            { TaskDialog.Show("Layout", "No levels in this project."); return Result.Cancelled; }
            string defaultLevel = doc.ActiveView?.GenLevel?.Name ?? levels[0].Name;

            var fittingSyms = FittingSymbols(doc);
            var fittings = fittingSyms
                .OrderBy(fs => fs.FamilyName).ThenBy(fs => fs.Name)
                .Select(fs => (id: fs.Id.IntegerValue, name: $"{fs.FamilyName} : {fs.Name}")).ToList();

            LayoutDialog dlg;
            using (dlg = new LayoutDialog(pipeTypes, systemTypes, headSyms,
                       levels.Select(l => l.Name).ToList(), defaultLevel,
                       fittings, DefaultOutletName(fittingSyms), DefaultRiserTeeName(fittingSyms)))
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;

            Level level = levels.FirstOrDefault(l => l.Name == dlg.LevelName);
            if (level == null)
            { TaskDialog.Show("Layout", $"Level \"{dlg.LevelName}\" not found."); return Result.Cancelled; }

            bool wantHeads = dlg.HeadSequence.Length > 0;
            FamilySymbol headSym = wantHeads ? doc.GetElement(new ElementId(dlg.HeadSymbolId)) as FamilySymbol : null;
            if (wantHeads && headSym == null)
            { TaskDialog.Show("Layout", "The selected sprinkler type was not found."); return Result.Cancelled; }

            var pipeType = doc.GetElement(new ElementId(dlg.PipeTypeId)) as PipeType;

            // ── Pick points ──
            bool mainMode = dlg.PickMode == LayoutDialog.PickModeKind.AreaMain;
            XYZ c1, c2, cm = null;
            try
            {
                c1 = uidoc.Selection.PickPoint("Layout: pick the FIRST corner of the area");
                c2 = uidoc.Selection.PickPoint("Layout: pick the OPPOSITE corner");
                if (mainMode)
                    cm = uidoc.Selection.PickPoint("Layout: pick where the MAIN runs (branches slope toward it)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                TaskDialog.Show("Layout",
                    "Point picking needs a plan view (or a view with an active work plane). " +
                    "Open a floor plan and run Layout again.");
                return Result.Cancelled;
            }

            var ctx = new Ctx
            {
                Doc = doc,
                Level = level,
                HeadSym = headSym,
                CapSym = dlg.CapEnds ? ResolveCapSymbol(doc, pipeType) : null,
                SysId = new ElementId(dlg.SystemTypeId),
                PipeTypeId = new ElementId(dlg.PipeTypeId),
                PipeType = pipeType,
                OutletSymId = dlg.OutletFittingId > 0 ? new ElementId(dlg.OutletFittingId) : ElementId.InvalidElementId,
                RiserTeeSymId = dlg.RiserTeeFittingId > 0 ? new ElementId(dlg.RiserTeeFittingId) : ElementId.InvalidElementId,
                LinesAlongX = dlg.LinesAlongX,
                LineSizeFt = dlg.LineSizeIn / 12.0,
                SprigSizeFt = dlg.SprigSizeIn / 12.0,
                MainSizeFt = dlg.MainSizeIn / 12.0,
                RiserSizeFt = dlg.RiserSizeIn / 12.0,
                UseSprigs = dlg.UseSprigs,
                CommonElev = dlg.SprigsToCommonElev,
                TermZ = level.Elevation + dlg.TermElevFt,
                SprigLenFt = dlg.SprigLenFt,
                CapEnds = dlg.CapEnds,
                ExtendToCapFt = dlg.ExtendToCapFt
            };

            double[] lineGaps = dlg.LineSequence.Select(c => dlg.LineSlotFt[c - '1']).ToArray();
            double[] headGaps = dlg.HeadSequence.Select(c => dlg.HeadSlotFt[c - 'A']).ToArray();

            var rpt = new Report();
            Result result = mainMode
                ? BuildMainMode(ctx, dlg, c1, c2, cm, lineGaps, headGaps, rpt: rpt)
                : BuildFillArea(ctx, dlg, c1, c2, lineGaps, headGaps, rpt);

            if (result != Result.Succeeded) return result;

            TaskDialog.Show("Layout", rpt.ToString(mainMode, ctx.CapEnds && ctx.CapSym == null));
            return Result.Succeeded;
        }

        // ── Shared per-run context + report ──

        private class Ctx
        {
            public Document Doc;
            public Level Level;
            public FamilySymbol HeadSym;
            public FamilySymbol CapSym;
            public ElementId SysId, PipeTypeId;
            public PipeType PipeType;
            public ElementId OutletSymId, RiserTeeSymId;
            public bool LinesAlongX;
            public double LineSizeFt, SprigSizeFt, MainSizeFt, RiserSizeFt;
            public bool UseSprigs, CommonElev;
            public double TermZ, SprigLenFt;
            public bool CapEnds;
            public double ExtendToCapFt;

            /// <summary>Map (along-u, along-m, z) into world XY per the branch direction.</summary>
            public XYZ Pt(double u, double m, double z)
                => LinesAlongX ? new XYZ(u, m, z) : new XYZ(m, u, z);
        }

        private class Report
        {
            public int Lines, Segs, Heads, Sprigs, Nipples, Tees, Elbows, Caps,
                       HeadConn, PlainConn, Outlet, ShortNipple, Fail, ChosenFit, DefaultFit, Joints;

            public string ToString(bool mainMode, bool capMissing)
            {
                string r = "Layout\n\n" +
                           $"{(mainMode ? "Branch lines" : "Lines")}: {Lines}   (pipe segments: {Segs})\n" +
                           $"Heads placed: {Heads}\n";
                if (Sprigs > 0) r += $"Sprigs: {Sprigs}\n";
                if (mainMode) r += $"Riser nipples: {Nipples}\n";
                r += $"Tees: {Tees}   Elbows: {Elbows}";
                if (Caps > 0) r += $"   Caps: {Caps}";
                r += $"\nHeads connected: {HeadConn}\n";
                if (mainMode && (ChosenFit > 0 || DefaultFit > 0))
                    r += $"Junction fittings: {ChosenFit} chosen (GOL / Firelock)" +
                         (DefaultFit > 0 ? $", {DefaultFit} fell back to the routing-preference default" : "") + "\n";
                if (Joints > 0) r += $"Slope-transition couplings: {Joints}\n";
                if (PlainConn > 0) r += $"Joined without a fitting (tee/elbow failed — check routing preferences): {PlainConn}\n";
                if (Outlet > 0) r += $"Heads placed as line outlets (sprig omitted): {Outlet}\n";
                if (ShortNipple > 0) r += $"Crossings with the main at/above the branch (nipple skipped — check the main elevation): {ShortNipple}\n";
                if (capMissing) r += "Caps requested but the pipe type has no cap in its routing preferences (and none loaded) — ends left open.\n";
                if (Fail > 0) r += $"Failures: {Fail}\n";
                return r;
            }
        }

        // ── Fill-area mode (two corners) ──

        private Result BuildFillArea(Ctx ctx, LayoutDialog dlg, XYZ c1, XYZ c2,
                                     double[] lineGaps, double[] headGaps, Report rpt)
        {
            Document doc = ctx.Doc;
            double minX = Math.Min(c1.X, c2.X), maxX = Math.Max(c1.X, c2.X);
            double minY = Math.Min(c1.Y, c2.Y), maxY = Math.Max(c1.Y, c2.Y);

            double uMin, uMax, mMin, mMax;
            if (ctx.LinesAlongX) { uMin = minX; uMax = maxX; mMin = minY; mMax = maxY; }
            else { uMin = minY; uMax = maxY; mMin = minX; mMax = maxX; }
            double lengthSpan = uMax - uMin, widthSpan = mMax - mMin;
            if (lengthSpan < 0.5 || widthSpan < 0.5)
            { TaskDialog.Show("Layout", "The two corners are too close together to lay anything out."); return Result.Cancelled; }

            List<double> lineOffsets = Tile(lineGaps, widthSpan);
            List<double> stations = Tile(headGaps, lengthSpan);
            if (lineOffsets.Count == 0)
            { TaskDialog.Show("Layout", "The first line spacing is wider than the picked area — nothing to place."); return Result.Cancelled; }

            // Segment cut list: near edge (0), each head, then extend past the last head
            // to the cap (or out to the far edge if no extension and no heads).
            var cuts = new List<double> { 0 };
            cuts.AddRange(stations);
            double lastStation = stations.Count > 0 ? stations[stations.Count - 1] : 0.0;
            double farEnd = stations.Count == 0 ? lengthSpan : lastStation + ctx.ExtendToCapFt;
            bool capFar = ctx.CapEnds && ctx.CapSym != null && stations.Count > 0 && farEnd - lastStation > 0.02;
            if (farEnd - lastStation > 0.02) cuts.Add(farEnd);

            double slope = dlg.SlopeFtPerFt;
            double zBase = ctx.Level.Elevation + dlg.StartElevFt;

            if (!Confirmed(lineOffsets.Count, stations.Count)) return Result.Cancelled;

            using (var tx = new Transaction(doc, "Layout"))
            {
                ApplySwallow(tx);
                tx.Start();
                Activate(ctx);

                var lines = new List<LineWork>();
                foreach (double lineOff in lineOffsets)
                {
                    var work = new LineWork();
                    // start point of this line at along-u = 0
                    Func<double, XYZ> at = d => ctx.Pt(uMin + d, mMin + lineOff, zBase + slope * d);

                    for (int k = 0; k + 1 < cuts.Count; k++)
                        work.Segs.Add(CreatePipe(ctx, at(cuts[k]), at(cuts[k + 1]), ctx.LineSizeFt, rpt));

                    // node 0 = near edge (open); node j+1 = head station j
                    for (int j = 0; j < stations.Count; j++)
                    {
                        XYZ T = at(stations[j]);
                        (bool useSprig, XYZ target) = SprigDecision(ctx, T, rpt);
                        work.Stations.Add(new Station { T = T, Target = target, UseSprig = useSprig, Kind = StationKind.Head, NodeIndex = j + 1 });
                    }
                    lines.Add(work);
                    rpt.Lines++;
                }

                PlaceHeads(ctx, lines, rpt);
                doc.Regenerate();
                RepinHeads(ctx, lines);
                doc.Regenerate();

                foreach (var work in lines)
                {
                    ConnectLine(ctx, work, rpt);
                    // Cap the far end (the last segment's still-open outer end).
                    if (capFar && work.Segs.Count > 0)
                        CapEnd(ctx, work.Segs[work.Segs.Count - 1], null, rpt);
                }

                tx.Commit();
            }
            return Result.Succeeded;
        }

        // ── Area + central main mode (three points) ──

        private Result BuildMainMode(Ctx ctx, LayoutDialog dlg, XYZ c1, XYZ c2, XYZ cm,
                                     double[] lineGaps, double[] headGaps, Report rpt)
        {
            Document doc = ctx.Doc;
            double minX = Math.Min(c1.X, c2.X), maxX = Math.Max(c1.X, c2.X);
            double minY = Math.Min(c1.Y, c2.Y), maxY = Math.Max(c1.Y, c2.Y);

            double uMin, uMax, mMin, mMax, uMain;
            if (ctx.LinesAlongX)
            { uMin = minX; uMax = maxX; mMin = minY; mMax = maxY; uMain = Clamp(cm.X, uMin, uMax); }
            else
            { uMin = minY; uMax = maxY; mMin = minX; mMax = maxX; uMain = Clamp(cm.Y, uMin, uMax); }
            double widthSpan = mMax - mMin;
            if (uMax - uMin < 1.0 || widthSpan < 0.5)
            { TaskDialog.Show("Layout", "The picked area is too small to lay out a main + branches."); return Result.Cancelled; }

            List<double> mOffsets = Tile(lineGaps, widthSpan);      // branch positions along the main
            if (mOffsets.Count == 0)
            { TaskDialog.Show("Layout", "The first line spacing is wider than the picked area — nothing to place."); return Result.Cancelled; }
            var branchM = mOffsets.Select(o => mMin + o).ToList();

            double branchSlope = Math.Abs(dlg.SlopeFtPerFt);        // magnitude; both sides fall to the main
            double branchLowZ = ctx.Level.Elevation + dlg.StartElevFt;
            double mainElev = ctx.Level.Elevation + dlg.MainElevFt;
            double mainSlope = Math.Abs(dlg.MainSlopeFtPerFt);
            // main falls toward the riser end; "reversed" puts the riser at the far (mMax) end
            Func<double, double> mainZ = m =>
            {
                double distFromHigh = dlg.MainSlopeReversed ? (m - mMin) : (mMax - m);
                return mainElev - mainSlope * distFromHigh;
            };

            if (!Confirmed(mOffsets.Count, headGaps.Length == 0 ? 0 : (int)((uMax - uMin) / Math.Max(0.5, headGaps.Min())))) return Result.Cancelled;

            using (var tx = new Transaction(doc, "Layout"))
            {
                ApplySwallow(tx);
                tx.Start();
                Activate(ctx);

                // Branch V nodes per line.
                var branches = new List<LineWork>();
                foreach (double m in branchM)
                {
                    var work = BuildBranchV(ctx, uMin, uMax, uMain, m, headGaps,
                                            branchLowZ, branchSlope, rpt);
                    branches.Add(work);
                    rpt.Lines++;
                }

                // Segmented cross-main: it BREAKS at each riser crossing, with the outlet
                // (GOL) fitting between the two pieces. Past the last riser at the high
                // (top-of-slope) end it continues a 6" stub that gets capped; the low/riser
                // end is left open to tie into the system riser.
                var mainSegs = new List<Pipe>();
                for (int i = 0; i + 1 < branchM.Count; i++)
                    mainSegs.Add(CreatePipe(ctx,
                        ctx.Pt(uMain, branchM[i], mainZ(branchM[i])),
                        ctx.Pt(uMain, branchM[i + 1], mainZ(branchM[i + 1])), ctx.MainSizeFt, rpt));

                int hiIndex = dlg.MainSlopeReversed ? 0 : branchM.Count - 1;
                double stubEndM = branchM[hiIndex] + (dlg.MainSlopeReversed ? -1 : 1) * MainStubFt;
                XYZ mainStubEndPt = ctx.Pt(uMain, stubEndM, mainZ(stubEndM));
                Pipe mainStub = CreatePipe(ctx,
                    ctx.Pt(uMain, branchM[hiIndex], mainZ(branchM[hiIndex])), mainStubEndPt, ctx.MainSizeFt, rpt);

                PlaceHeads(ctx, branches, rpt);
                doc.Regenerate();
                RepinHeads(ctx, branches);
                doc.Regenerate();

                // Connect each branch's heads/sprigs + build the crossing nipple.
                for (int i = 0; i < branches.Count; i++)
                {
                    LineWork work = branches[i];
                    ConnectLine(ctx, work, rpt);
                    Station cross = work.Stations.FirstOrDefault(s => s.Kind == StationKind.Crossing);

                    // Riser nipple down to the main + tee on the main.
                    double mZ = mainZ(branchM[i]);
                    if (cross != null && branchLowZ - mZ > SprigMinFt)
                    {
                        XYZ topPt = ctx.Pt(uMain, branchM[i], branchLowZ);
                        XYZ botPt = ctx.Pt(uMain, branchM[i], mZ);
                        Pipe nipple = CreatePipe(ctx, topPt, botPt, ctx.RiserSizeFt, rpt);
                        if (nipple != null)
                        {
                            rpt.Nipples++;
                            // Break the main at the crossing: the two collinear main pieces
                            // + the nipple bottom become the outlet (GOL) tee.
                            Connector nBot = FreeEndNear(nipple, botPt);
                            Pipe mBefore = i - 1 >= 0 && i - 1 < mainSegs.Count ? mainSegs[i - 1] : null;
                            Pipe mAfter = i < mainSegs.Count ? mainSegs[i] : null;
                            if (i == hiIndex && mainStub != null)
                            { if (dlg.MainSlopeReversed) mBefore = mainStub; else mAfter = mainStub; }
                            Connector mA = mBefore != null ? FreeEndNear(mBefore, botPt) : null;
                            Connector mB = mAfter != null ? FreeEndNear(mAfter, botPt) : null;
                            JoinRun(ctx, mA, mB, nBot, rpt);

                            // Riser-top Firelock tee: the two branch halves + the nipple top.
                            Connector nTop = FreeEndNear(nipple, topPt);
                            if (nTop != null && (cross.RunA != null || cross.RunB != null))
                                JoinBranch(ctx, cross.RunA, cross.RunB, nTop, rpt);
                            SetDiameter(nipple, ctx.RiserSizeFt);
                        }
                    }
                    else if (cross != null)
                    {
                        // Main is at/above the branch — no nipple; keep the branch's two
                        // halves connected to each other so it isn't hydraulically split.
                        rpt.ShortNipple++;
                        if (cross.RunA != null && cross.RunB != null)
                        { try { cross.RunA.ConnectTo(cross.RunB); rpt.PlainConn++; } catch { rpt.Fail++; } }
                    }

                    // Cap the true outer branch ends (never the main crossing).
                    if (ctx.CapEnds && ctx.CapSym != null)
                    {
                        if (work.HasLeftCap && work.Segs.Count > 0) CapEnd(ctx, work.Segs[0], work.LeftCapPt, rpt);
                        if (work.HasRightCap && work.Segs.Count > 0) CapEnd(ctx, work.Segs[work.Segs.Count - 1], work.RightCapPt, rpt);
                    }
                    continue;
                }

                // Cap the far (high) end of the main's 6" stub; the riser end stays open.
                if (mainStub != null && ctx.CapEnds && ctx.CapSym != null)
                {
                    doc.Regenerate();   // settle the tees before reading the stub's open end
                    CapEnd(ctx, mainStub, mainStubEndPt, rpt);
                }

                tx.Commit();
            }
            return Result.Succeeded;
        }

        /// <summary>Build one branch as a shallow V (low at the main crossing, rising to
        /// both outer edges), with head nodes tiled from each outer edge inward and a
        /// crossing node at the main. Returns segments + stations (heads + the crossing).</summary>
        private LineWork BuildBranchV(Ctx ctx, double uMin, double uMax, double uMain, double m,
                                      double[] headGaps, double branchLowZ, double branchSlope, Report rpt)
        {
            // Head u-positions on each half, measured inward from the outer edge.
            var leftHeads = new List<double>();
            var rightHeads = new List<double>();
            if (headGaps.Length > 0)
            {
                // left half: uMin + cumulative, inward toward uMain
                double c = 0;
                for (int i = 0, g = 0; g < 100000; i++, g++)
                {
                    double gap = headGaps[i % headGaps.Length]; if (gap <= 1e-6) break;
                    c += gap; double up = uMin + c;
                    if (up > uMain - HeadMarginFt) break;
                    leftHeads.Add(up);
                }
                // right half: uMax - cumulative, inward toward uMain
                c = 0;
                for (int i = 0, g = 0; g < 100000; i++, g++)
                {
                    double gap = headGaps[i % headGaps.Length]; if (gap <= 1e-6) break;
                    c += gap; double up = uMax - c;
                    if (up < uMain + HeadMarginFt) break;
                    rightHeads.Add(up);
                }
                rightHeads.Sort();
            }

            // A short horizontal run centered on the crossing keeps the riser-top tee's
            // two run connectors collinear (a sloped V would kink and defeat NewTeeFitting).
            // The pipe is flat within flatHalf of the crossing, then slopes up to the edges.
            double flatHalf = branchSlope > 1e-9 ? CrossFlatFt / 2.0 : 0.0;
            Func<double, double> bz = u => branchLowZ + branchSlope * Math.Max(0.0, Math.Abs(u - uMain) - flatHalf);
            Func<double, XYZ> at = u => ctx.Pt(u, m, bz(u));

            // Node u-list (ascending): [leftCap] leftHeads uMain rightHeads [rightCap].
            // A cap stub is only added where that side has heads AND there is a real
            // extend-to-cap distance (else the outermost head IS the branch end — no
            // degenerate zero-length segment, and nothing to cap there).
            bool addLeftCap = leftHeads.Count > 0 && ctx.CapEnds && ctx.ExtendToCapFt > 0.02;
            bool addRightCap = rightHeads.Count > 0 && ctx.CapEnds && ctx.ExtendToCapFt > 0.02;

            var work = new LineWork();
            var nodeU = new List<double>();
            var nodeKind = new List<StationKind?>();   // null = cap
            if (addLeftCap) { nodeU.Add(leftHeads[0] - ctx.ExtendToCapFt); nodeKind.Add(null); work.HasLeftCap = true; work.LeftCapPt = at(nodeU[0]); }
            foreach (var u in leftHeads) { nodeU.Add(u); nodeKind.Add(StationKind.Head); }
            if (flatHalf > 0) { nodeU.Add(uMain - flatHalf); nodeKind.Add(StationKind.Joint); }
            nodeU.Add(uMain); nodeKind.Add(StationKind.Crossing);
            if (flatHalf > 0) { nodeU.Add(uMain + flatHalf); nodeKind.Add(StationKind.Joint); }
            foreach (var u in rightHeads) { nodeU.Add(u); nodeKind.Add(StationKind.Head); }
            if (addRightCap) { double rc = rightHeads[rightHeads.Count - 1] + ctx.ExtendToCapFt; nodeU.Add(rc); nodeKind.Add(null); work.HasRightCap = true; work.RightCapPt = at(rc); }

            for (int k = 0; k + 1 < nodeU.Count; k++)
                work.Segs.Add(CreatePipe(ctx, at(nodeU[k]), at(nodeU[k + 1]), ctx.LineSizeFt, rpt));

            for (int k = 0; k < nodeU.Count; k++)
            {
                if (nodeKind[k] == null) continue;
                XYZ T = at(nodeU[k]);
                var st = new Station { T = T, NodeIndex = k, Kind = nodeKind[k].Value };
                if (nodeKind[k] == StationKind.Head)
                {
                    (bool useSprig, XYZ target) = SprigDecision(ctx, T, rpt);
                    st.UseSprig = useSprig; st.Target = target;
                }
                else { st.Target = T; }   // crossing: nipple handled after connect
                work.Stations.Add(st);
            }
            return work;
        }

        // ── Shared placement / connection ──

        private void PlaceHeads(Ctx ctx, List<LineWork> lines, Report rpt)
        {
            if (ctx.HeadSym == null) return;
            foreach (var work in lines)
                foreach (var st in work.Stations)
                {
                    if (st.Kind != StationKind.Head) continue;
                    try { st.Head = ctx.Doc.Create.NewFamilyInstance(st.Target, ctx.HeadSym, ctx.Level, StructuralType.NonStructural); }
                    catch { }
                    if (st.Head != null) rpt.Heads++; else rpt.Fail++;
                }
        }

        private void RepinHeads(Ctx ctx, List<LineWork> lines)
        {
            foreach (var work in lines)
                foreach (var st in work.Stations)
                {
                    if (st.Head == null) continue;
                    Connector inlet = InletConnector(st.Head);
                    if (inlet == null) continue;
                    XYZ d = st.Target - inlet.Origin;
                    if (d.GetLength() > 1e-6 && st.Head.Location is LocationPoint lp)
                    { try { lp.Point = lp.Point + d; } catch { } }
                }
        }

        /// <summary>Connect a line's head/sprig stations to its collinear segments (tee
        /// interior, elbow at the last). Records the crossing station's run/branch
        /// connectors so the caller can add the riser nipple.</summary>
        private void ConnectLine(Ctx ctx, LineWork work, Report rpt)
        {
            Document doc = ctx.Doc;
            foreach (var st in work.Stations)
            {
                // Uniform node indexing: node k sits between Segs[k-1] and Segs[k].
                int k = st.NodeIndex;
                Pipe segBefore = k - 1 >= 0 && k - 1 < work.Segs.Count ? work.Segs[k - 1] : null;
                Pipe segAfter = k >= 0 && k < work.Segs.Count ? work.Segs[k] : null;

                Connector cA = segBefore != null ? FreeEndNear(segBefore, st.T) : null;
                Connector cB = segAfter != null ? FreeEndNear(segAfter, st.T) : null;

                if (st.Kind == StationKind.Joint)
                {
                    // Slope transition (flat run ↔ sloped half): a plain grooved coupling.
                    if (cA != null && cB != null) { try { cA.ConnectTo(cB); rpt.Joints++; } catch { rpt.Fail++; } }
                    continue;
                }

                if (st.Kind == StationKind.Crossing)
                {
                    // The branch runs continuously through; the nipple (added later) is
                    // the tee branch. Remember the two run ends for the caller.
                    st.RunA = cA; st.RunB = cB;
                    continue;
                }

                Connector branchConn = null;
                if (st.UseSprig)
                {
                    Pipe sprig = CreatePipe(ctx, st.T, new XYZ(st.T.X, st.T.Y, st.Target.Z), ctx.SprigSizeFt, rpt);
                    if (sprig != null) { st.Sprig = sprig; rpt.Sprigs++; branchConn = FreeEndNear(sprig, st.T); }
                }
                else if (st.Head != null)
                {
                    branchConn = InletConnector(st.Head);
                }

                if (cA != null && cB != null && branchConn != null)
                {
                    try { doc.Create.NewTeeFitting(cA, cB, branchConn); rpt.Tees++; }
                    catch { try { cA.ConnectTo(cB); branchConn.ConnectTo(cA); rpt.PlainConn++; } catch { rpt.Fail++; } }
                }
                else if ((cA != null || cB != null) && branchConn != null)
                {
                    // end station — elbow with whichever single segment is present
                    Connector one = cA ?? cB;
                    try { doc.Create.NewElbowFitting(one, branchConn); rpt.Elbows++; }
                    catch { try { one.ConnectTo(branchConn); rpt.PlainConn++; } catch { rpt.Fail++; } }
                }
                else if (cA != null && cB != null)
                {
                    try { cA.ConnectTo(cB); rpt.PlainConn++; } catch { rpt.Fail++; }
                }

                if (st.Sprig != null && st.Head != null)
                {
                    Connector top = FreeEndNear(st.Sprig, new XYZ(st.T.X, st.T.Y, st.Target.Z));
                    Connector inlet = InletConnector(st.Head);
                    if (top != null && inlet != null && top.Origin.DistanceTo(inlet.Origin) < 0.1)
                    { try { top.ConnectTo(inlet); rpt.HeadConn++; } catch { rpt.Fail++; } }
                }
                else if (!st.UseSprig && st.Head != null && branchConn != null && branchConn.IsConnected)
                { rpt.HeadConn++; }
            }

            foreach (var st in work.Stations)
                if (st.Sprig != null) SetDiameter(st.Sprig, ctx.SprigSizeFt);
        }

        /// <summary>Break the main into its two collinear pieces + the nipple bottom as a
        /// tee (the outlet / GOL), forcing the chosen outlet family; elbow at a main end.</summary>
        private void JoinRun(Ctx ctx, Connector mainA, Connector mainB, Connector branch, Report rpt)
        {
            Document doc = ctx.Doc;
            if (branch == null) { rpt.Fail++; return; }
            if (mainA != null && mainB != null)
            {
                FamilySymbol chosen = Resolve(doc, ctx.OutletSymId);
                try
                {
                    if (chosen != null)
                    {
                        if (!chosen.IsActive) { chosen.Activate(); doc.Regenerate(); }
                        WithForcedJunctionFitting(ctx.PipeType, chosen.Id, () => doc.Create.NewTeeFitting(mainA, mainB, branch));
                        rpt.Tees++; rpt.ChosenFit++;
                    }
                    else { doc.Create.NewTeeFitting(mainA, mainB, branch); rpt.Tees++; rpt.DefaultFit++; }
                }
                catch { try { mainA.ConnectTo(mainB); branch.ConnectTo(mainA); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
            else if (mainA != null || mainB != null)
            {
                Connector m = mainA ?? mainB;
                try { doc.Create.NewElbowFitting(m, branch); rpt.Elbows++; }
                catch { try { m.ConnectTo(branch); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
        }

        /// <summary>Tee the branch's two through halves + the nipple top at the crossing
        /// (a Firelock tee — force the chosen tee family; elbow if one half is missing).</summary>
        private void JoinBranch(Ctx ctx, Connector runA, Connector runB, Connector nipTop, Report rpt)
        {
            Document doc = ctx.Doc;
            if (nipTop == null) { rpt.Fail++; return; }
            if (runA != null && runB != null)
            {
                FamilySymbol chosen = Resolve(doc, ctx.RiserTeeSymId);
                try
                {
                    if (chosen != null)
                    {
                        if (!chosen.IsActive) { chosen.Activate(); doc.Regenerate(); }
                        WithForcedJunctionFitting(ctx.PipeType, chosen.Id, () => doc.Create.NewTeeFitting(runA, runB, nipTop));
                        rpt.Tees++; rpt.ChosenFit++;
                    }
                    else { doc.Create.NewTeeFitting(runA, runB, nipTop); rpt.Tees++; rpt.DefaultFit++; }
                }
                catch { try { runA.ConnectTo(runB); nipTop.ConnectTo(runA); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
            else if (runA != null || runB != null)
            {
                Connector r = runA ?? runB;
                try { doc.Create.NewElbowFitting(r, nipTop); rpt.Elbows++; }
                catch { try { r.ConnectTo(nipTop); rpt.PlainConn++; } catch { rpt.Fail++; } }
            }
        }

        // ── Fitting selection (force a chosen family via a temporary Junctions rule) ──

        private static readonly StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        private static List<FamilySymbol> FittingSymbols(Document doc) =>
            new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>().ToList();

        private static PartType GetPartType(FamilySymbol fs)
        {
            Parameter p = fs?.Family?.get_Parameter(BuiltInParameter.FAMILY_CONTENT_PART_TYPE);
            return p != null ? (PartType)p.AsInteger() : PartType.Undefined;
        }

        private static string DefaultOutletName(List<FamilySymbol> fittings)
        {
            var fs = fittings.FirstOrDefault(f =>
            {
                string fam = f.FamilyName ?? "";
                PartType pt = GetPartType(f);
                bool tap = pt == PartType.TapAdjustable || pt == PartType.TapPerpendicular || pt == PartType.Tee;
                bool named = fam.IndexOf("GOL", OIC) >= 0 || fam.IndexOf("grooved outlet", OIC) >= 0;
                return tap && named;
            });
            return fs != null ? $"{fs.FamilyName} : {fs.Name}" : null;
        }

        private static string DefaultRiserTeeName(List<FamilySymbol> fittings)
        {
            var fs = fittings.FirstOrDefault(f =>
            {
                string fam = f.FamilyName ?? "";
                return GetPartType(f) == PartType.Tee
                    && fam.IndexOf("firelock", OIC) >= 0 && fam.IndexOf("tee", OIC) >= 0;
            });
            return fs != null ? $"{fs.FamilyName} : {fs.Name}" : null;
        }

        private static FamilySymbol Resolve(Document doc, ElementId id)
            => id != null && id != ElementId.InvalidElementId && doc.GetElement(id) is FamilySymbol fs ? fs : null;

        /// <summary>Run <paramref name="create"/> with a top-priority Junctions rule that
        /// forces <paramref name="fittingSymId"/>, then remove it (leaving the shared
        /// pipe type byte-identical). No-op wrapper if the type has no routing manager.</summary>
        private static void WithForcedJunctionFitting(PipeType pipeType, ElementId fittingSymId, Action create)
        {
            var rpm = pipeType?.RoutingPreferenceManager;
            if (rpm == null) { create(); return; }
            var rule = new RoutingPreferenceRule(fittingSymId, "SG temp force");
            rule.AddCriterion(new PrimarySizeCriterion(0.0, 100.0));   // any size (feet)
            rpm.AddRule(RoutingPreferenceRuleGroupType.Junctions, rule, 0);
            try { create(); }
            finally { try { rpm.RemoveRule(RoutingPreferenceRuleGroupType.Junctions, 0); } catch { } }
        }

        private (bool, XYZ) SprigDecision(Ctx ctx, XYZ T, Report rpt)
        {
            if (!ctx.UseSprigs) return (false, T);
            double topZ = ctx.CommonElev ? ctx.TermZ : T.Z + ctx.SprigLenFt;
            if (topZ - T.Z > SprigMinFt) return (true, new XYZ(T.X, T.Y, topZ));
            rpt.Outlet++;
            return (false, T);
        }

        private Pipe CreatePipe(Ctx ctx, XYZ a, XYZ b, double sizeFt, Report rpt)
        {
            if (a.DistanceTo(b) <= 0.01) return null;
            Pipe p = null;
            try { p = Pipe.Create(ctx.Doc, ctx.SysId, ctx.PipeTypeId, ctx.Level.Id, a, b); } catch { }
            if (p != null) { SetDiameter(p, sizeFt); rpt.Segs++; }
            else rpt.Fail++;
            return p;
        }

        private void Activate(Ctx ctx)
        {
            if (ctx.HeadSym != null && !ctx.HeadSym.IsActive) ctx.HeadSym.Activate();
            if (ctx.CapSym != null && !ctx.CapSym.IsActive) { ctx.CapSym.Activate(); ctx.Doc.Regenerate(); }
        }

        private bool Confirmed(int lines, int perLine)
        {
            long estimate = (long)lines * Math.Max(1, perLine);
            if (estimate <= 4000) return true;
            var confirm = new TaskDialog("Layout")
            {
                MainInstruction = $"This will place roughly {lines} lines and {estimate} heads.",
                MainContent = "That's a lot — double-check the spacings and the picked area. Continue?",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            return confirm.Show() == TaskDialogResult.Yes;
        }

        // ── Cap fittings ──

        /// <summary>The cap FamilySymbol from a pipe type's routing preferences (Caps
        /// group); falls back to any loaded pipe-fitting family named "cap".</summary>
        private static FamilySymbol ResolveCapSymbol(Document doc, PipeType pipeType)
        {
            var rpm = pipeType?.RoutingPreferenceManager;
            if (rpm != null)
            {
                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Caps);
                for (int i = 0; i < n; i++)
                {
                    RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Caps, i);
                    if (doc.GetElement(rule.MEPPartId) is FamilySymbol fs) return fs;
                }
            }
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PipeFitting).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => (fs.FamilyName ?? "").IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Cap the open outer end of a segment nearest <paramref name="nearPt"/>
        /// with the size-matched cap type, then connect it. Matching the cap size to the
        /// pipe means Revit needs no reducing coupling to bridge the joint.</summary>
        private void CapEnd(Ctx ctx, Pipe seg, XYZ nearPt, Report rpt)
        {
            if (seg == null || ctx.CapSym == null) return;
            Document doc = ctx.Doc;
            Connector end = OpenEndNear(seg, nearPt);
            if (end == null) return;

            // Pick the cap TYPE whose nominal size matches this branch, so the cap comes
            // in the same size as the pipe and no reducing coupling is needed.
            double dia = PipeDiameter(seg);
            FamilySymbol capSym = CapSymbolForSize(ctx, dia);
            if (capSym == null) return;
            if (!capSym.IsActive) { capSym.Activate(); doc.Regenerate(); }

            FamilyInstance cap;
            try { cap = doc.Create.NewFamilyInstance(end.Origin, capSym, StructuralType.NonStructural); }
            catch { return; }
            if (cap == null) return;
            doc.Regenerate();

            Connector capConn = cap.MEPModel?.ConnectorManager?.Connectors?.Cast<Connector>().FirstOrDefault();
            if (capConn == null) { try { doc.Delete(cap.Id); } catch { } return; }

            XYZ desired = end.CoordinateSystem.BasisZ.Negate();
            XYZ current = capConn.CoordinateSystem.BasisZ;
            double angle = current.AngleTo(desired);
            if (angle > 1e-6)
            {
                XYZ axis = current.CrossProduct(desired);
                if (axis.GetLength() < 1e-9) axis = Math.Abs(current.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;
                axis = axis.Normalize();
                try { ElementTransformUtils.RotateElement(doc, cap.Id, Line.CreateBound(capConn.Origin, capConn.Origin + axis), angle); }
                catch { }
                doc.Regenerate();
                capConn = cap.MEPModel.ConnectorManager.Connectors.Cast<Connector>().First();
            }

            XYZ delta = end.Origin - capConn.Origin;
            if (delta.GetLength() > 1e-9)
            {
                try { ElementTransformUtils.MoveElement(doc, cap.Id, delta); } catch { }
                doc.Regenerate();
                capConn = cap.MEPModel.ConnectorManager.Connectors.Cast<Connector>().First();
            }

            // A last resort in case the family is parametric (single type, size-driven).
            TrySetSize(cap, dia);
            // Connect the cap to the pipe. Because the cap type now matches the pipe size,
            // no reducing coupling is inserted.
            try { end.ConnectTo(capConn); } catch { }
            rpt.Caps++;
        }

        private static double PipeDiameter(Pipe p)
        {
            var d = p?.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            return d != null ? d.AsDouble() : 0.0;
        }

        /// <summary>The cap type in the resolved cap's family whose nominal size best
        /// matches <paramref name="diaFt"/> (within ~1/4"); the resolved default otherwise.</summary>
        private FamilySymbol CapSymbolForSize(Ctx ctx, double diaFt)
        {
            if (ctx.CapSym == null || diaFt <= 0) return ctx.CapSym;
            Family fam = ctx.CapSym.Family;
            if (fam == null) return ctx.CapSym;
            FamilySymbol best = null; double bestErr = double.MaxValue;
            foreach (ElementId sid in fam.GetFamilySymbolIds())
            {
                if (!(ctx.Doc.GetElement(sid) is FamilySymbol s)) continue;
                double sz = SymbolSize(s);
                if (sz <= 0) continue;
                double err = Math.Abs(sz - diaFt);
                if (err < bestErr) { bestErr = err; best = s; }
            }
            return best != null && bestErr < 0.02 ? best : ctx.CapSym;   // within ~1/4"
        }

        /// <summary>The nominal size (feet) of a fitting type — from a size parameter, or
        /// parsed from the type/family name (fab caps are named by size, e.g. "2", "1-1/2").</summary>
        private static double SymbolSize(FamilySymbol s)
        {
            foreach (var name in new[] { "Nominal Diameter", "Nominal Radius", "Nominal Size",
                                         "Size", "Diameter", "ND", "Pipe Size" })
            {
                Parameter p = s.LookupParameter(name);
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double v = p.AsDouble();
                    if (v > 0) return name.IndexOf("Radius", OIC) >= 0 ? v * 2.0 : v;
                }
            }
            double inch = ParseSizeInches(s.Name);
            if (inch <= 0) inch = ParseSizeInches(s.Family?.Name);
            return inch > 0 ? inch / 12.0 : 0;
        }

        /// <summary>Parse a leading pipe size in inches from a name — "2", "2\"", "2 in",
        /// "2.5", "1-1/2", "1 1/2", "3/4". Returns 0 if none in a plausible range.</summary>
        private static double ParseSizeInches(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*[- ]\s*(\d+)\s*/\s*(\d+)");  // 1-1/2 or 1 1/2
            if (m.Success) { double dd = Num(m.Groups[3]); return dd > 0 ? Num(m.Groups[1]) + Num(m.Groups[2]) / dd : 0; }
            m = System.Text.RegularExpressions.Regex.Match(s, @"(?<!\d)(\d+)\s*/\s*(\d+)");              // 3/4
            if (m.Success) { double dd = Num(m.Groups[2]); return dd > 0 ? Num(m.Groups[1]) / dd : 0; }
            m = System.Text.RegularExpressions.Regex.Match(s, @"\d+(?:\.\d+)?");                         // 2 or 2.5
            if (m.Success) { double v = Num(m.Groups[0]); if (v >= 0.25 && v <= 48) return v; }
            return 0;
        }

        private static double Num(System.Text.RegularExpressions.Group g)
            => double.TryParse(g.Value, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static void TrySetSize(FamilyInstance fi, double diaFt)
        {
            if (fi == null || diaFt <= 0) return;
            foreach (var name in new[] { "Nominal Diameter", "Nominal Radius", "Size", "Diameter" })
            {
                Parameter p = fi.LookupParameter(name);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                { try { p.Set(name.IndexOf("Radius", OIC) >= 0 ? diaFt / 2.0 : diaFt); } catch { } return; }
            }
        }

        // ── Working state ──

        private enum StationKind { Head, Crossing, Joint }

        private class LineWork
        {
            public List<Pipe> Segs { get; } = new List<Pipe>();
            public List<Station> Stations { get; } = new List<Station>();
            public bool HasLeftCap, HasRightCap;   // main mode: is Segs[0]/Segs[last] a real outer cap stub?
            public XYZ LeftCapPt, RightCapPt;
        }

        private class Station
        {
            public XYZ T;
            public XYZ Target;
            public FamilyInstance Head;
            public Pipe Sprig;
            public bool UseSprig;
            public StationKind Kind = StationKind.Head;
            public int NodeIndex = -1;      // >=0 in main mode (index into Segs/nodes); -1 in fill mode
            public Connector RunA, RunB, BranchConn;   // crossing: the two through ends
        }

        // ── Geometry + Revit helpers ──

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>Cumulative offsets from cycling the gap pattern until the running
        /// total would exceed <paramref name="span"/>. Empty/zero pattern → empty list.</summary>
        private static List<double> Tile(double[] gaps, double span)
        {
            var offs = new List<double>();
            if (gaps == null || gaps.Length == 0) return offs;
            double cum = 0;
            for (int i = 0, guard = 0; guard < 100000; i++, guard++)
            {
                double gap = gaps[i % gaps.Length];
                if (gap <= 1e-6) break;
                cum += gap;
                if (cum > span + 0.02) break;
                offs.Add(cum);
            }
            return offs;
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

        private static Connector FreeEndNear(MEPCurve curve, XYZ pt)
        {
            if (curve?.ConnectorManager == null) return null;
            Connector best = null; double bestD = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End || c.IsConnected) continue;
                double d = c.Origin.DistanceTo(pt);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best != null && bestD < 0.1 ? best : null;
        }

        private static Connector OpenEndNear(MEPCurve curve, XYZ pt)
        {
            if (curve?.ConnectorManager == null) return null;
            Connector best = null; double bestD = double.MaxValue;
            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End || c.IsConnected) continue;
                double d = pt == null ? 0 : c.Origin.DistanceTo(pt);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        private static Connector InletConnector(FamilyInstance head)
        {
            var cm = head?.MEPModel?.ConnectorManager;
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

        private static void ApplySwallow(Transaction tx)
        {
            try
            {
                var opts = tx.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new WarningSwallower());
                tx.SetFailureHandlingOptions(opts);
            }
            catch { }
        }

        private class WarningSwallower : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (var f in a.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning)
                        a.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}
