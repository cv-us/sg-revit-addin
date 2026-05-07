using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Resizes selected pipe hangers to match the nominal diameter of the
    /// pipe they're attached to. Useful after a pipe-resize because Revit
    /// does not automatically propagate diameter changes to connected
    /// pipe-accessory hangers.
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers
    ///   2. Command finds the closest near-horizontal pipe to each hanger
    ///      (using bounding-box-center XY matching, same approach as
    ///      HangerGapCheck — robust against connector-hosted families
    ///      where LocationPoint sits at a pipe endpoint)
    ///   3. Compares hanger "Nominal Diameter" to pipe diameter
    ///   4. Reports mismatches with a preview, asks the user to confirm
    ///   5. On confirm, sets each mismatched hanger's "Nominal Diameter"
    ///      parameter to the matched pipe's diameter
    ///
    /// Sister to SyncHangersToPipesCommand — that command also moves and
    /// rotates hangers, this one only resizes (lighter / safer for
    /// already-placed hangers).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MatchHangerSizesCommand : IExternalCommand
    {
        private const string NominalDiameterParam = "Nominal Diameter";
        private const string RodLengthParam = "Rod Length";

        /// <summary>
        /// Pipes whose direction is more vertical than this are excluded
        /// from candidate matching. 0.5 ≈ 30° off horizontal — keeps
        /// horizontal mains and sloped armovers, excludes sprigs/risers.
        /// </summary>
        private const double MaxPipeSlopeFromHorizontal = 0.5;

        /// <summary>Maximum XY distance from hanger BB center to pipe centerline (feet).</summary>
        private const double MaxHangerToPipeXyDist = 0.5; // 6 inches

        /// <summary>Tolerance when comparing pipe vs hanger diameters (feet).</summary>
        private const double DiameterMatchTolerance = 1.0 / 32.0 / 12.0; // 1/32" — well below any real size step

        // ── Drift-marker constants (DirectShape cylinder placed above hangers
        // that have no nearby pipe, so the user can find and re-attach them) ──

        private const string DriftMarkerAppId = "SSG_FP_Suite";
        private const string DriftMarkerAppDataId = "MatchSizesDriftedMarker";
        private const string DriftMarkerMaterialName = "SSG_DriftedHangerMarker";
        private const double DriftMarkerRadius = 2.0 / 12.0;   // 2"
        private const double DriftMarkerHeight = 4.0 / 12.0;   // 4"
        private const double DriftMarkerZOffset = 0.5;         // 6" above BB center

        /// <summary>
        /// Standard NPS nominal-to-outside-diameter lookup, both values in
        /// inches. Used to compute rod-length compensation when resizing a
        /// hanger: the visible ring is sized to fit the pipe OD, so when
        /// nominal changes the centerline shifts by half the OD delta. We
        /// counter that shift with an opposite change to Rod Length so both
        /// the rod-top (at structure) and the pipe centerline stay put.
        ///
        /// Covers all common fire protection sizes (1/2" through 12"). Sizes
        /// outside this table fall back to nominal=OD which is good enough
        /// to flag the case but won't compensate accurately.
        /// </summary>
        private static readonly (double nominalIn, double odIn)[] NpsTable = new[]
        {
            (0.50,  0.840),  //   1/2"
            (0.75,  1.050),  //   3/4"
            (1.00,  1.315),  //   1"
            (1.25,  1.660),  // 1-1/4"
            (1.50,  1.900),  // 1-1/2"
            (2.00,  2.375),  //   2"
            (2.50,  2.875),  // 2-1/2"
            (3.00,  3.500),  //   3"
            (3.50,  4.000),  // 3-1/2"
            (4.00,  4.500),  //   4"
            (5.00,  5.563),  //   5"
            (6.00,  6.625),  //   6"
            (8.00,  8.625),  //   8"
            (10.00,10.750),  //  10"
            (12.00,12.750),  //  12"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Collect hangers from selection ──
                var hangers = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .OfType<FamilyInstance>()
                    .Where(IsHanger)
                    .ToList();

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Match Hanger Sizes",
                        "No pipe hangers found in the current selection.\n\n" +
                        "Select hanger family instances (family name contains \"-Pipe Hanger\" " +
                        "or \"-Pipe Trapeze\") and run the command again.");
                    return Result.Cancelled;
                }

                // ── Collect near-horizontal pipes ──
                var pipeCurves = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .Select(p => (pipe: (Element)p, curve: (p.Location as LocationCurve)?.Curve))
                    .Where(t => t.curve != null && IsNearHorizontal(t.curve))
                    .ToList();

                if (pipeCurves.Count == 0)
                {
                    TaskDialog.Show("Match Hanger Sizes",
                        "No near-horizontal pipes found in the project.");
                    return Result.Cancelled;
                }

                // ── Analyze each hanger ──
                int alreadyMatching = 0;
                int skippedNoDiameterParam = 0;
                int skippedReadOnly = 0;
                var mismatched = new List<(FamilyInstance hanger, double currentDiaFt,
                    double targetDiaFt, Parameter diaParam,
                    Parameter rodParam, double oldRodLengthFt)>();
                // Hangers with no nearby pipe — capture location so we can mark them later
                var driftedHangers = new List<(FamilyInstance hanger, XYZ location)>();

                foreach (var hanger in hangers)
                {
                    XYZ hangerPt = GetVisualLocation(hanger);
                    if (hangerPt == null) continue; // can't mark something with no geometry

                    var nearest = FindClosestPipe(hangerPt, pipeCurves);
                    if (nearest == null)
                    {
                        driftedHangers.Add((hanger, hangerPt));
                        continue;
                    }

                    var pipeDiaParam = nearest.Value.pipe.get_Parameter(
                        BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    if (pipeDiaParam == null || !pipeDiaParam.HasValue)
                    {
                        // Pipe has no diameter parameter — rare data oddity, skip silently
                        continue;
                    }
                    double pipeDiaFt = pipeDiaParam.AsDouble();

                    var hangerDiaParam = hanger.LookupParameter(NominalDiameterParam);
                    if (hangerDiaParam == null || !hangerDiaParam.HasValue)
                    {
                        skippedNoDiameterParam++;
                        continue;
                    }
                    if (hangerDiaParam.IsReadOnly)
                    {
                        skippedReadOnly++;
                        continue;
                    }

                    double hangerDiaFt = hangerDiaParam.AsDouble();
                    if (Math.Abs(pipeDiaFt - hangerDiaFt) <= DiameterMatchTolerance)
                    {
                        alreadyMatching++;
                    }
                    else
                    {
                        // Capture the rod length BEFORE any change so we can
                        // compute the compensated value during the apply step.
                        var rodParam = hanger.LookupParameter(RodLengthParam);
                        double oldRodLengthFt = (rodParam != null && rodParam.HasValue)
                            ? rodParam.AsDouble() : 0.0;

                        mismatched.Add((hanger, hangerDiaFt, pipeDiaFt,
                            hangerDiaParam, rodParam, oldRodLengthFt));
                    }
                }

                // ── Nothing to do? ──
                if (mismatched.Count == 0 && driftedHangers.Count == 0)
                {
                    string msg =
                        $"All {hangers.Count} selected hangers already match their pipe diameters.";
                    if (alreadyMatching > 0) msg += $"\n\nMatching: {alreadyMatching}";
                    if (skippedNoDiameterParam > 0) msg += $"\nNo \"Nominal Diameter\" parameter: {skippedNoDiameterParam}";
                    if (skippedReadOnly > 0) msg += $"\nRead-only diameter: {skippedReadOnly}";
                    TaskDialog.Show("Match Hanger Sizes", msg);
                    return Result.Succeeded;
                }

                // ── Resize phase ──
                bool didResize = false;
                int resized = 0;
                int rodAdjusted = 0;
                int rodSkippedNegative = 0;
                int failed = 0;
                var failedIds = new List<ElementId>();

                if (mismatched.Count > 0)
                {
                    string preview = "";
                    foreach (var m in mismatched.Take(10))
                    {
                        double comp = ComputeRodCompensationFt(m.currentDiaFt, m.targetDiaFt);
                        string sign = comp > 0 ? "−" : "+";
                        string rodInfo = (m.rodParam != null && m.oldRodLengthFt > 0)
                            ? $"  (rod ~{sign}{InchString(Math.Abs(comp))})"
                            : "";
                        preview += $"\n  ID {m.hanger.Id}: " +
                                   $"{InchString(m.currentDiaFt)} → {InchString(m.targetDiaFt)}{rodInfo}";
                    }
                    if (mismatched.Count > 10)
                        preview += $"\n  …and {mismatched.Count - 10} more";

                    int withoutRodComp = mismatched.Count(m => m.rodParam == null || m.oldRodLengthFt <= 0);

                    string body =
                        $"Hangers checked:     {hangers.Count}\n" +
                        $"Already matching:    {alreadyMatching}\n" +
                        $"Mismatched:          {mismatched.Count}";
                    if (driftedHangers.Count > 0) body += $"\nDrifted off pipe:    {driftedHangers.Count}";
                    if (skippedNoDiameterParam > 0) body += $"\nNo Nominal Diameter: {skippedNoDiameterParam}";
                    if (skippedReadOnly > 0) body += $"\nRead-only diameter:  {skippedReadOnly}";
                    body += "\n\nResizing changes the ring radius, which would shift the pipe " +
                            "centerline up (downsize) or down (upsize). To keep both the rod " +
                            "top (at structure) and the pipe centerline in place, the command " +
                            "measures the actual ring shift after each resize and adjusts rod " +
                            "length to undo it.";
                    body += "\n\nMismatches (~rod values are estimates; actual amounts are " +
                            "measured at apply time):" + preview;
                    if (withoutRodComp > 0)
                        body += $"\n\nNote: {withoutRodComp} hanger(s) have no \"Rod Length\" " +
                                "parameter and will be resized without compensation.";
                    if (driftedHangers.Count > 0)
                        body += $"\n\nAfter resizing, you'll be asked whether to mark the " +
                                $"{driftedHangers.Count} drifted hanger(s).";
                    body += "\n\nResize the mismatched hangers and adjust rod lengths?";

                    var confirm = TaskDialog.Show("Match Hanger Sizes", body,
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                          | TaskDialogCommonButtons.Cancel,
                        TaskDialogResult.Yes);

                    if (confirm == TaskDialogResult.Cancel)
                        return Result.Cancelled;

                    if (confirm == TaskDialogResult.Yes)
                    {
                        didResize = true;
                        using (var tx = new Transaction(doc, "Match Hanger Sizes"))
                        {
                            tx.Start();

                            // Phase 1: snapshot each hanger's bottom-of-bounding-box Z
                            // BEFORE any change. We'll compare against this after the
                            // resize to figure out how much the ring actually shifted —
                            // a static OD-based formula misses family-specific offsets.
                            var bbMinZBefore = new Dictionary<ElementId, double>();
                            foreach (var m in mismatched)
                            {
                                var bb = m.hanger.get_BoundingBox(null);
                                if (bb != null)
                                    bbMinZBefore[m.hanger.Id] = bb.Min.Z;
                            }

                            // Phase 2: set Nominal Diameter on every mismatched hanger
                            var resizedSuccessIdx = new List<int>();
                            for (int i = 0; i < mismatched.Count; i++)
                            {
                                var m = mismatched[i];
                                try
                                {
                                    if (m.diaParam.Set(m.targetDiaFt))
                                    {
                                        resized++;
                                        resizedSuccessIdx.Add(i);
                                    }
                                    else
                                    {
                                        failed++;
                                        failedIds.Add(m.hanger.Id);
                                    }
                                }
                                catch
                                {
                                    failed++;
                                    failedIds.Add(m.hanger.Id);
                                }
                            }

                            // Phase 3: force regen so the geometry — and thus the
                            // bounding boxes — reflect the new diameters
                            doc.Regenerate();

                            // Phase 4: for each successfully resized hanger, measure
                            // how far the ring center moved and counter that with rod
                            // length. The math:
                            //   bbShift = (BB-min after) − (BB-min before)
                            //   ringRadiusChange = (newOD − oldOD) / 2     ← geometry
                            //   centerlineShift = bbShift + ringRadiusChange
                            //   new_rod_length = old_rod_length + centerlineShift
                            // (positive centerlineShift = ring went UP relative to
                            // pipe = lengthen rod to bring it back down)
                            foreach (int idx in resizedSuccessIdx)
                            {
                                var m = mismatched[idx];

                                if (m.rodParam == null || m.rodParam.IsReadOnly
                                    || m.oldRodLengthFt <= 0)
                                    continue;

                                try
                                {
                                    double newRodLengthFt;

                                    if (bbMinZBefore.TryGetValue(m.hanger.Id, out double bbBefore))
                                    {
                                        var bbAfter = m.hanger.get_BoundingBox(null);
                                        if (bbAfter == null)
                                        {
                                            // Couldn't measure after — fall back to OD formula
                                            double comp = ComputeRodCompensationFt(
                                                m.currentDiaFt, m.targetDiaFt);
                                            newRodLengthFt = m.oldRodLengthFt - comp;
                                        }
                                        else
                                        {
                                            double bbShift = bbAfter.Min.Z - bbBefore;
                                            double oldOd = LookupOdFt(m.currentDiaFt);
                                            double newOd = LookupOdFt(m.targetDiaFt);
                                            double ringRadiusChange = (newOd - oldOd) / 2.0;
                                            double centerlineShift = bbShift + ringRadiusChange;
                                            newRodLengthFt = m.oldRodLengthFt + centerlineShift;
                                        }
                                    }
                                    else
                                    {
                                        // No before-measurement available — fall back
                                        double comp = ComputeRodCompensationFt(
                                            m.currentDiaFt, m.targetDiaFt);
                                        newRodLengthFt = m.oldRodLengthFt - comp;
                                    }

                                    if (newRodLengthFt <= 0.5 / 12.0)
                                    {
                                        rodSkippedNegative++;
                                        continue;
                                    }
                                    if (m.rodParam.Set(newRodLengthFt))
                                        rodAdjusted++;
                                }
                                catch
                                {
                                    // non-critical — hanger is resized, no compensation
                                }
                            }

                            tx.Commit();
                        }
                    }
                }

                // ── Mark-drifted phase ──
                int markersPlaced = 0;
                int markersCleared = 0;
                bool markerPromptShown = false;

                if (driftedHangers.Count > 0)
                {
                    string markBody;
                    if (mismatched.Count == 0)
                    {
                        // No resize was offered; this is the only action available
                        markBody =
                            $"Hangers checked:     {hangers.Count}\n" +
                            $"Already matching:    {alreadyMatching}\n" +
                            $"Drifted off pipe:    {driftedHangers.Count}\n\n" +
                            $"{driftedHangers.Count} hanger" +
                            (driftedHangers.Count != 1 ? "s have" : " has") +
                            " no near-horizontal pipe within 6\" — they may have drifted " +
                            "off their host pipes.\n\n" +
                            "Place orange location markers above them so you can find and " +
                            "re-attach them to a pipe?";
                    }
                    else
                    {
                        markBody =
                            $"{driftedHangers.Count} hanger" +
                            (driftedHangers.Count != 1 ? "s have" : " has") +
                            " no near-horizontal pipe within 6\" and could not be matched. " +
                            "They may have drifted off their host pipes.\n\n" +
                            "Place orange location markers above them so you can find and " +
                            "re-attach them?";
                    }

                    markerPromptShown = true;
                    var markConfirm = TaskDialog.Show("Match Hanger Sizes", markBody,
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        TaskDialogResult.No);

                    if (markConfirm == TaskDialogResult.Yes)
                    {
                        using (var tx = new Transaction(doc, "Mark Drifted Hangers"))
                        {
                            tx.Start();

                            // Always wipe prior drift markers when placing fresh ones —
                            // keeps the project from accumulating stale markers across runs
                            markersCleared = ClearPreviousDriftMarkers(doc);

                            ElementId materialId = GetOrCreateDriftMarkerMaterial(doc);
                            foreach (var (hanger, location) in driftedHangers)
                            {
                                try
                                {
                                    XYZ markerBase = new XYZ(
                                        location.X, location.Y, location.Z + DriftMarkerZOffset);
                                    CreateDriftMarker(doc, markerBase, materialId);
                                    markersPlaced++;
                                }
                                catch { /* non-critical */ }
                            }
                            tx.Commit();
                        }

                        // Highlight the drifted hangers in the selection so the user can
                        // immediately tell which ones to look at
                        uidoc.Selection.SetElementIds(driftedHangers.Select(d => d.hanger.Id).ToList());
                    }
                }

                // Highlight resize failures (overrides drift selection if both exist —
                // failures are more urgent)
                if (failedIds.Count > 0)
                    uidoc.Selection.SetElementIds(failedIds);

                // ── Final report ──
                string report = "";
                if (didResize)
                {
                    report += $"Resized:        {resized}\n" +
                              $"Rod adjusted:   {rodAdjusted}";
                    if (rodSkippedNegative > 0)
                        report += $"\nRod skipped:    {rodSkippedNegative} (compensation would have made rod < 1/2\")";
                    if (failed > 0)
                        report += $"\nFailed:         {failed} (highlighted in selection)";
                }
                else if (mismatched.Count > 0)
                {
                    report += $"Resize skipped ({mismatched.Count} mismatches not addressed).";
                }

                if (markerPromptShown)
                {
                    if (markersPlaced > 0)
                    {
                        if (report.Length > 0) report += "\n\n";
                        report += $"Drift markers placed: {markersPlaced}";
                        if (markersCleared > 0)
                            report += $"  ({markersCleared} previous markers cleared)";
                        report += "\n(Drifted hangers are highlighted in selection.)";
                    }
                    else
                    {
                        if (report.Length > 0) report += "\n\n";
                        report += $"Drifted hangers: {driftedHangers.Count} (markers not placed)";
                    }
                }

                if (failed > 0)
                {
                    report += "\n\nFailures usually mean the family's diameter is a type " +
                              "parameter or otherwise locked. Switch the family type manually " +
                              "or use Sync Hangers to Pipes.";
                }
                TaskDialog.Show("Match Hanger Sizes", report);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Helpers ──

        private bool IsHanger(FamilyInstance fi)
        {
            // Must be in PipeAccessory category (rules out tags, sprinklers, etc.)
            if (fi.Category == null) return false;
            if (fi.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeAccessory)
                return false;

            string familyName = fi.Symbol?.Family?.Name ?? "";
            return familyName.IndexOf("-Pipe Hanger", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Pipe Trapeze", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Basic Adjustable", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Ring Hanger", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsNearHorizontal(Curve curve)
        {
            try
            {
                XYZ direction = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                return Math.Abs(direction.Z) < MaxPipeSlopeFromHorizontal;
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns the XYZ that best represents where a hanger visually sits
        /// in the model. Uses the bounding-box center rather than LocationPoint
        /// because connector-hosted hanger families have their family origin
        /// at the host pipe's reference point (a pipe endpoint), not at the
        /// rendered hanger geometry.
        /// </summary>
        private XYZ GetVisualLocation(Element element)
        {
            BoundingBoxXYZ bb = element.get_BoundingBox(null);
            if (bb != null)
            {
                return new XYZ(
                    (bb.Min.X + bb.Max.X) * 0.5,
                    (bb.Min.Y + bb.Max.Y) * 0.5,
                    (bb.Min.Z + bb.Max.Z) * 0.5);
            }
            var locPt = element.Location as LocationPoint;
            return locPt?.Point;
        }

        private (Element pipe, XYZ closestPoint, Curve curve)?
            FindClosestPipe(XYZ hangerPoint, List<(Element pipe, Curve curve)> pipeCurves)
        {
            if (hangerPoint == null) return null;

            Element bestPipe = null;
            XYZ bestPoint = null;
            Curve bestCurve = null;
            double bestXyDist = double.MaxValue;

            foreach (var (pipe, curve) in pipeCurves)
            {
                IntersectionResult projResult = curve.Project(hangerPoint);
                if (projResult == null) continue;

                XYZ closest = projResult.XYZPoint;
                double xyDist = Math.Sqrt(
                    Math.Pow(closest.X - hangerPoint.X, 2) +
                    Math.Pow(closest.Y - hangerPoint.Y, 2));

                if (xyDist < bestXyDist)
                {
                    bestXyDist = xyDist;
                    bestPipe = pipe;
                    bestPoint = closest;
                    bestCurve = curve;
                }
            }

            if (bestPipe == null || bestXyDist > MaxHangerToPipeXyDist) return null;
            return (bestPipe, bestPoint, bestCurve);
        }

        /// <summary>
        /// Returns the standard NPS outside diameter (in feet) for a given
        /// nominal diameter (in feet). Snaps to the nearest known nominal
        /// in NpsTable. Falls back to nominal=OD if the size isn't in the
        /// table — non-ideal but at least keeps the math going.
        /// </summary>
        private double LookupOdFt(double nominalFt)
        {
            double nominalIn = nominalFt * 12.0;
            double bestOd = nominalIn;
            double bestDiff = double.MaxValue;
            foreach (var (n, od) in NpsTable)
            {
                double diff = Math.Abs(n - nominalIn);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestOd = od;
                }
            }
            // Only trust the table if we're within 1/16" of a known nominal
            if (bestDiff <= 0.0625) return bestOd / 12.0;
            return nominalFt; // fallback
        }

        /// <summary>
        /// Computes the rod-length compensation in feet. Positive value means
        /// the rod must SHORTEN (upsize: ring grew, centerline would have
        /// dropped). Negative means the rod must LENGTHEN (downsize).
        ///
        /// Formula: half the OD delta — ring radius is half of OD, and that's
        /// the amount the centerline shifts when rod top is anchored.
        /// </summary>
        private double ComputeRodCompensationFt(double oldNominalFt, double newNominalFt)
        {
            double oldOd = LookupOdFt(oldNominalFt);
            double newOd = LookupOdFt(newNominalFt);
            return (newOd - oldOd) / 2.0;
        }

        /// <summary>
        /// Formats a length-in-feet value as a friendly inch string for the
        /// preview list. Snaps common nominal fractions; falls back to a
        /// 2-decimal-inch string for non-standard values.
        /// </summary>
        private string InchString(double feet)
        {
            double inches = feet * 12.0;
            int whole = (int)Math.Floor(inches);
            double frac = inches - whole;
            if (Math.Abs(frac) < 0.05) return $"{whole}\"";
            if (Math.Abs(frac - 0.25) < 0.05) return whole > 0 ? $"{whole}-1/4\"" : "1/4\"";
            if (Math.Abs(frac - 0.5)  < 0.05) return whole > 0 ? $"{whole}-1/2\"" : "1/2\"";
            if (Math.Abs(frac - 0.75) < 0.05) return whole > 0 ? $"{whole}-3/4\"" : "3/4\"";
            return $"{inches:F2}\"";
        }

        // ── Drift-marker helpers (DirectShape cylinder, orange) ──

        /// <summary>
        /// Creates an orange DirectShape cylinder above a drifted hanger so the
        /// user can find and re-attach it. Tagged with our ApplicationId /
        /// ApplicationDataId so it can be cleaned up on subsequent runs without
        /// touching unrelated DirectShapes (including Hanger Gap Check markers,
        /// which use a different ApplicationDataId).
        /// Must be called inside a transaction.
        /// </summary>
        private void CreateDriftMarker(Document doc, XYZ basePoint, ElementId materialId)
        {
            var arc1 = Arc.Create(basePoint, DriftMarkerRadius, 0, Math.PI,
                XYZ.BasisX, XYZ.BasisY);
            var arc2 = Arc.Create(basePoint, DriftMarkerRadius, Math.PI, 2 * Math.PI,
                XYZ.BasisX, XYZ.BasisY);
            var profile = CurveLoop.Create(new List<Curve> { arc1, arc2 });

            var solidOptions = new SolidOptions(materialId, ElementId.InvalidElementId);
            Solid cylinder = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { profile }, XYZ.BasisZ, DriftMarkerHeight, solidOptions);

            var ds = DirectShape.CreateElement(doc,
                new ElementId(BuiltInCategory.OST_GenericModel));
            ds.ApplicationId = DriftMarkerAppId;
            ds.ApplicationDataId = DriftMarkerAppDataId;
            ds.SetShape(new GeometryObject[] { cylinder });
        }

        /// <summary>
        /// Returns the ElementId of a project-wide material named
        /// DriftMarkerMaterialName, creating it (orange) if it doesn't already
        /// exist. Idempotent across runs. Must be called inside a transaction.
        /// </summary>
        private ElementId GetOrCreateDriftMarkerMaterial(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, DriftMarkerMaterialName,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing.Id;

            ElementId newId = Material.Create(doc, DriftMarkerMaterialName);
            if (doc.GetElement(newId) is Material newMat)
            {
                var orange = new Color(255, 140, 0); // bright orange
                newMat.Color = orange;
                newMat.SurfaceForegroundPatternColor = orange;
                newMat.CutForegroundPatternColor = orange;
                newMat.Transparency = 0;
                newMat.Shininess = 0;
            }
            return newId;
        }

        /// <summary>
        /// Deletes all existing drift-marker DirectShapes from the project.
        /// Filters by both ApplicationId and ApplicationDataId so it only
        /// touches markers placed by THIS command — Hanger Gap Check markers
        /// (different ApplicationDataId) are untouched, as are any other
        /// addins' DirectShapes. Returns count deleted.
        /// Must be called inside a transaction.
        /// </summary>
        private int ClearPreviousDriftMarkers(Document doc)
        {
            var ids = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(ds => ds.ApplicationId == DriftMarkerAppId
                          && ds.ApplicationDataId == DriftMarkerAppDataId)
                .Select(ds => ds.Id)
                .ToList();

            if (ids.Count > 0)
                doc.Delete(ids);
            return ids.Count;
        }
    }
}
