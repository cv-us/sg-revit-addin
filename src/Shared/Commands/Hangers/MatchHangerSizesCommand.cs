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
                int skippedNoPipe = 0;
                int skippedNoDiameterParam = 0;
                int skippedReadOnly = 0;
                var mismatched = new List<(FamilyInstance hanger, double currentDiaFt,
                    double targetDiaFt, Parameter diaParam)>();

                foreach (var hanger in hangers)
                {
                    XYZ hangerPt = GetVisualLocation(hanger);
                    if (hangerPt == null) { skippedNoPipe++; continue; }

                    var nearest = FindClosestPipe(hangerPt, pipeCurves);
                    if (nearest == null) { skippedNoPipe++; continue; }

                    var pipeDiaParam = nearest.Value.pipe.get_Parameter(
                        BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    if (pipeDiaParam == null || !pipeDiaParam.HasValue) { skippedNoPipe++; continue; }
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
                        alreadyMatching++;
                    else
                        mismatched.Add((hanger, hangerDiaFt, pipeDiaFt, hangerDiaParam));
                }

                // ── Nothing to do? ──
                if (mismatched.Count == 0)
                {
                    string msg =
                        $"All {hangers.Count} selected hangers already match their pipe diameters.\n";
                    if (alreadyMatching > 0) msg += $"\nMatching: {alreadyMatching}";
                    if (skippedNoPipe > 0) msg += $"\nNo nearby pipe: {skippedNoPipe}";
                    if (skippedNoDiameterParam > 0) msg += $"\nNo \"Nominal Diameter\" parameter: {skippedNoDiameterParam}";
                    if (skippedReadOnly > 0) msg += $"\nRead-only diameter: {skippedReadOnly}";
                    TaskDialog.Show("Match Hanger Sizes", msg);
                    return Result.Succeeded;
                }

                // ── Confirm ──
                string preview = "";
                foreach (var m in mismatched.Take(10))
                    preview += $"\n  ID {m.hanger.Id}: {InchString(m.currentDiaFt)} → {InchString(m.targetDiaFt)}";
                if (mismatched.Count > 10)
                    preview += $"\n  …and {mismatched.Count - 10} more";

                string body =
                    $"Hangers checked:     {hangers.Count}\n" +
                    $"Already matching:    {alreadyMatching}\n" +
                    $"Mismatched:          {mismatched.Count}";
                if (skippedNoPipe > 0) body += $"\nNo nearby pipe:      {skippedNoPipe}";
                if (skippedNoDiameterParam > 0) body += $"\nNo Nominal Diameter: {skippedNoDiameterParam}";
                if (skippedReadOnly > 0) body += $"\nRead-only diameter:  {skippedReadOnly}";
                body += "\n\nMismatches:" + preview +
                        "\n\nResize the mismatched hangers to match their pipes?";

                var confirm = TaskDialog.Show("Match Hanger Sizes", body,
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                    TaskDialogResult.Yes);

                if (confirm != TaskDialogResult.Yes)
                    return Result.Cancelled;

                // ── Apply ──
                int resized = 0;
                int failed = 0;
                var failedIds = new List<ElementId>();

                using (var tx = new Transaction(doc, "Match Hanger Sizes"))
                {
                    tx.Start();
                    foreach (var m in mismatched)
                    {
                        try
                        {
                            if (m.diaParam.Set(m.targetDiaFt))
                                resized++;
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
                    tx.Commit();
                }

                // Highlight failures so the user can investigate
                if (failedIds.Count > 0)
                    uidoc.Selection.SetElementIds(failedIds);

                string report = $"Resized: {resized}";
                if (failed > 0)
                {
                    report += $"\nFailed:  {failed} (highlighted in selection)";
                    report += "\n\nFailures usually mean the family's diameter is a type " +
                              "parameter or otherwise locked. Switch the family type manually " +
                              "or use Sync Hangers to Pipes (which also moves and rotates).";
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
    }
}
