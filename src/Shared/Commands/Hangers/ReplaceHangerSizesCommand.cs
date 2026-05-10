using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Resizes selected pipe hangers to match the nominal diameter of the
    /// pipe they're attached to. Useful after a pipe-resize because Revit
    /// does not automatically propagate diameter changes to connected
    /// pipe-accessory hangers.
    ///
    /// IMPLEMENTATION NOTE — delete + recreate, not parameter set:
    /// Setting "Nominal Diameter" on an existing hanger instance causes its
    /// ring center to drift off the pipe (a family-level behavior; happens
    /// even via the Properties palette, not just the API). Rod-length
    /// compensation can't fully cancel the drift because the ring's
    /// position-relative-to-host is computed inside the family.
    ///
    /// To avoid the drift entirely the command instead deletes each
    /// mismatched hanger and creates a fresh instance at the same point /
    /// rotation, with all writable parameters captured and restored. The new
    /// instance's geometry is built from scratch for the target size — no
    /// prior parametric state, no drift. This is the same pattern used by
    /// SwapHydraCADHangersCommand for HydraCAD-to-SG family swaps.
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers
    ///   2. Command finds the closest near-horizontal pipe to each hanger
    ///      (BB-center XY matching, same approach as HangerGapCheck)
    ///   3. Compares hanger "Nominal Diameter" to pipe diameter
    ///   4. Reports mismatches with a preview, asks the user to confirm
    ///   5. On confirm, for each mismatched hanger:
    ///        a. Capture LocationPoint, rotation, and every writable
    ///           instance parameter that has a value
    ///        b. Delete the old instance
    ///        c. Create a fresh instance at the same point with the same
    ///           FamilySymbol via doc.Create.NewFamilyInstance
    ///        d. Set Nominal Diameter to the target (pipe's) value
    ///        e. Restore all other captured parameters
    ///        f. Apply rotation via ElementTransformUtils.RotateElement
    ///
    /// Drifted hangers (no near-horizontal pipe within 6") still get the
    /// optional orange-marker treatment so the user can find and re-attach
    /// them manually.
    ///
    /// Sister to SyncHangersToPipesCommand — that command also moves and
    /// rotates as part of a full sync. This command stays at the same point
    /// and rotation, only swaps the size.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReplaceHangerSizesCommand : IExternalCommand
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

        // ── Drift-marker constants (DirectShape cylinder placed above hangers
        // that have no nearby pipe, so the user can find and re-attach them) ──

        private const string DriftMarkerAppId = "SgRevitAddin";
        private const string DriftMarkerAppDataId = "MatchSizesDriftedMarker";
        private const string DriftMarkerMaterialName = "SG_DriftedHangerMarker";
        private const double DriftMarkerRadius = 2.0 / 12.0;   // 2"
        private const double DriftMarkerHeight = 4.0 / 12.0;   // 4"
        private const double DriftMarkerZOffset = 0.5;         // 6" above BB center

        /// <summary>
        /// Built-in parameter ids that we never copy from old → new during
        /// the delete+recreate. Setting these is either rejected by Revit or
        /// would corrupt the new instance's identity (Family, Type, Host id,
        /// etc. — Revit manages them).
        /// </summary>
        private static readonly HashSet<int> SkipBuiltInIds = new HashSet<int>
        {
            (int)BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM,
            (int)BuiltInParameter.SYMBOL_NAME_PARAM,
            (int)BuiltInParameter.ELEM_TYPE_PARAM,
            (int)BuiltInParameter.ALL_MODEL_TYPE_NAME,
            (int)BuiltInParameter.ALL_MODEL_FAMILY_NAME,
            (int)BuiltInParameter.HOST_ID_PARAM,
            (int)BuiltInParameter.ID_PARAM,
            (int)BuiltInParameter.IFC_GUID,
            (int)BuiltInParameter.PHASE_CREATED,
            (int)BuiltInParameter.PHASE_DEMOLISHED,
            (int)BuiltInParameter.LEVEL_PARAM,
            (int)BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            (int)BuiltInParameter.RBS_SYSTEM_NAME_PARAM,
            (int)BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM,
            (int)BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM,
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
                    TaskDialog.Show("Replace Hanger Sizes",
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
                    TaskDialog.Show("Replace Hanger Sizes",
                        "No near-horizontal pipes found in the project.");
                    return Result.Cancelled;
                }

                // ── Analyze each hanger ──
                int alreadyMatching = 0;
                int skippedNoDiameterParam = 0;
                int skippedReadOnly = 0;
                var mismatched = new List<(FamilyInstance hanger, double currentDiaFt,
                    double targetDiaFt)>();
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
                        alreadyMatching++;
                    else
                        mismatched.Add((hanger, hangerDiaFt, pipeDiaFt));
                }

                // ── Nothing to do? ──
                if (mismatched.Count == 0 && driftedHangers.Count == 0)
                {
                    string msg =
                        $"All {hangers.Count} selected hangers already match their pipe diameters.";
                    if (alreadyMatching > 0) msg += $"\n\nMatching: {alreadyMatching}";
                    if (skippedNoDiameterParam > 0) msg += $"\nNo \"Nominal Diameter\" parameter: {skippedNoDiameterParam}";
                    if (skippedReadOnly > 0) msg += $"\nRead-only diameter: {skippedReadOnly}";
                    TaskDialog.Show("Replace Hanger Sizes", msg);
                    return Result.Succeeded;
                }

                // ── Resize phase (delete + recreate) ──
                bool didResize = false;
                int recreated = 0;
                int failed = 0;
                var failedOldIds = new List<ElementId>(); // for reporting
                var newHangerIds = new List<ElementId>();  // for selection / drift markers (we only mark drift now)
                var recreateErrors = new List<string>();   // diagnostic info from each TryRecreate call

                if (mismatched.Count > 0)
                {
                    string preview = "";
                    foreach (var m in mismatched.Take(10))
                    {
                        preview += $"\n  ID {m.hanger.Id}: " +
                                   $"{InchString(m.currentDiaFt)} → {InchString(m.targetDiaFt)}";
                    }
                    if (mismatched.Count > 10)
                        preview += $"\n  …and {mismatched.Count - 10} more";

                    string body =
                        $"Hangers checked:     {hangers.Count}\n" +
                        $"Already matching:    {alreadyMatching}\n" +
                        $"Mismatched:          {mismatched.Count}";
                    if (driftedHangers.Count > 0) body += $"\nDrifted off pipe:    {driftedHangers.Count}";
                    if (skippedNoDiameterParam > 0) body += $"\nNo Nominal Diameter: {skippedNoDiameterParam}";
                    if (skippedReadOnly > 0) body += $"\nRead-only diameter:  {skippedReadOnly}";
                    body += "\n\nApproach: each mismatched hanger is DELETED and RECREATED at the " +
                            "same point and rotation with the new size. All writable parameters " +
                            "(Type Code, Rod Length, Distance off End, Comments, Hydratec fields, " +
                            "etc.) are captured before delete and restored on the new instance. " +
                            "This avoids a family-level bug where setting Nominal Diameter on an " +
                            "existing instance shifts the ring off the pipe.";
                    body += "\n\nNote: each hanger gets a NEW ElementId. Anything outside the " +
                            "project that references the old IDs (saved Trimble exports, external " +
                            "clash reports, etc.) won't carry over. In-project schedules, filters, " +
                            "and tags are unaffected.";
                    body += "\n\nMismatches:" + preview;
                    if (driftedHangers.Count > 0)
                        body += $"\n\nAfter recreation you'll be asked whether to mark the " +
                                $"{driftedHangers.Count} drifted hanger(s).";
                    body += "\n\nProceed?";

                    var confirm = TaskDialog.Show("Replace Hanger Sizes", body,
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                          | TaskDialogCommonButtons.Cancel,
                        TaskDialogResult.Yes);

                    if (confirm == TaskDialogResult.Cancel)
                        return Result.Cancelled;

                    if (confirm == TaskDialogResult.Yes)
                    {
                        didResize = true;
                        using (var tx = new Transaction(doc, "Replace Hanger Sizes"))
                        {
                            tx.Start();

                            foreach (var m in mismatched)
                            {
                                ElementId newId = TryRecreate(doc, m.hanger,
                                    m.targetDiaFt, recreateErrors);
                                if (newId != null && newId != ElementId.InvalidElementId)
                                {
                                    recreated++;
                                    newHangerIds.Add(newId);
                                }
                                else
                                {
                                    failed++;
                                    failedOldIds.Add(m.hanger.Id);
                                }
                            }

                            tx.Commit();
                        }
                    }
                }

                // ── Mark-drifted phase ──
                // Recreated hangers no longer need markers (delete+recreate avoids
                // the centerline drift issue entirely), so this only handles
                // drifted hangers — those with no near-horizontal pipe within 6".
                int markersPlaced = 0;
                int markersCleared = 0;
                bool markerPromptShown = false;

                if (driftedHangers.Count > 0)
                {
                    string markBody =
                        $"{driftedHangers.Count} hanger" +
                        (driftedHangers.Count != 1 ? "s have" : " has") +
                        " no near-horizontal pipe within 6\" — they may have drifted " +
                        "off their host pipes and could not be matched to a pipe " +
                        "diameter.\n\n" +
                        "Place orange location markers above them so you can find and " +
                        "re-attach them?";

                    markerPromptShown = true;
                    var markConfirm = TaskDialog.Show("Replace Hanger Sizes", markBody,
                        TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        TaskDialogResult.Yes);

                    if (markConfirm == TaskDialogResult.Yes)
                    {
                        using (var tx = new Transaction(doc, "Mark Drifted Hangers"))
                        {
                            tx.Start();

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

                        // Highlight drifted hangers so the user can tab through them
                        uidoc.Selection.SetElementIds(
                            driftedHangers.Select(d => d.hanger.Id).ToList());
                    }
                }

                // If we recreated hangers but the user didn't engage with the marker
                // prompt, leave the new hangers selected so the user can verify them
                if (newHangerIds.Count > 0 &&
                    (!markerPromptShown || markersPlaced == 0))
                {
                    uidoc.Selection.SetElementIds(newHangerIds);
                }

                // ── Final report ──
                string report = "";
                if (didResize)
                {
                    report += $"Recreated:  {recreated}";
                    if (failed > 0)
                        report += $"\nFailed:     {failed}";
                    report += "\n\nNew hangers were placed at the same location and rotation, " +
                              "with all writable parameters preserved. Each got a new ElementId.";
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
                            report += $"\n({markersCleared} previous markers cleared)";
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
                    report += "\n\nFailures: NewFamilyInstance produced an instance whose host " +
                              "binding silently failed (no host or empty bounding box). When that " +
                              "happens the bogus instance is deleted and the OLD hanger is " +
                              "preserved. Detailed step-by-step log was copied to your clipboard — " +
                              "paste it back if you want me to look at it.";
                    if (recreateErrors.Count > 0)
                    {
                        report += "\n\nLast few log lines:";
                        // Show last 5 entries — those typically contain the failure summary
                        foreach (var err in recreateErrors.Skip(Math.Max(0, recreateErrors.Count - 5)))
                            report += $"\n  • {err}";
                    }
                }

                // Always dump the full step-by-step log to clipboard so the user can
                // share it back when something doesn't work as expected.
                if (recreateErrors.Count > 0)
                {
                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("=== Replace Hanger Sizes — Diagnostic Log ===");
                        sb.AppendLine($"Hangers checked:    {hangers.Count}");
                        sb.AppendLine($"Already matching:   {alreadyMatching}");
                        sb.AppendLine($"Mismatched:         {mismatched.Count}");
                        sb.AppendLine($"Recreated:          {recreated}");
                        sb.AppendLine($"Failed:             {failed}");
                        sb.AppendLine();
                        foreach (var line in recreateErrors) sb.AppendLine(line);
                        System.Windows.Forms.Clipboard.SetText(sb.ToString());
                    }
                    catch { /* clipboard can fail in some session states */ }
                }

                TaskDialog.Show("Replace Hanger Sizes", report);

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

        // ── Delete + recreate ──

        /// <summary>
        /// Captured snapshot of a single instance parameter — used to copy
        /// values from the old hanger to the freshly placed new instance.
        /// Tracks the storage type so we restore via the right setter.
        /// </summary>
        private class ParameterSnapshot
        {
            public StorageType StorageType;
            public double DoubleValue;
            public int IntValue;
            public string StringValue;
            public ElementId IdValue;
        }

        /// <summary>
        /// Captures a snapshot of every writable instance parameter on the
        /// element that has a value. Skips read-only parameters and a list
        /// of built-ins that Revit manages (Family, Type, Host Id, etc.).
        /// Keyed by parameter name. If the family has duplicate parameter
        /// names (e.g. shared + built-in "Rod Length"), the last writable
        /// occurrence with a value wins.
        /// </summary>
        private Dictionary<string, ParameterSnapshot> CaptureWritableParameters(Element element)
        {
            var captured = new Dictionary<string, ParameterSnapshot>(
                StringComparer.OrdinalIgnoreCase);

            foreach (Parameter p in element.Parameters)
            {
                if (p == null || p.Definition == null) continue;
                if (p.IsReadOnly) continue;
                if (!p.HasValue) continue;
                if (SkipBuiltInIds.Contains(p.Id.IntegerValue)) continue;

                string name = p.Definition.Name;
                if (string.IsNullOrEmpty(name)) continue;

                var snap = new ParameterSnapshot { StorageType = p.StorageType };
                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double:
                            snap.DoubleValue = p.AsDouble();
                            break;
                        case StorageType.Integer:
                            snap.IntValue = p.AsInteger();
                            break;
                        case StorageType.String:
                            snap.StringValue = p.AsString() ?? "";
                            break;
                        case StorageType.ElementId:
                            snap.IdValue = p.AsElementId();
                            break;
                        default:
                            continue;
                    }
                }
                catch
                {
                    continue;
                }

                captured[name] = snap;
            }

            return captured;
        }

        /// <summary>
        /// Sets every writable parameter on the new instance whose name
        /// matches a captured value. Sets ALL occurrences with the same name
        /// (handles duplicates: shared + built-in pairs that show up
        /// twice in some families). Silently skips ones that reject the set.
        /// </summary>
        private void RestoreCapturedParameters(FamilyInstance newHanger,
            Dictionary<string, ParameterSnapshot> snapshots, string skipName = null)
        {
            foreach (Parameter p in newHanger.Parameters)
            {
                if (p == null || p.Definition == null) continue;
                if (p.IsReadOnly) continue;

                string name = p.Definition.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (skipName != null &&
                    string.Equals(name, skipName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (SkipBuiltInIds.Contains(p.Id.IntegerValue)) continue;

                if (!snapshots.TryGetValue(name, out var snap)) continue;
                if (p.StorageType != snap.StorageType) continue;

                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double:    p.Set(snap.DoubleValue); break;
                        case StorageType.Integer:   p.Set(snap.IntValue); break;
                        case StorageType.String:    p.Set(snap.StringValue ?? ""); break;
                        case StorageType.ElementId: p.Set(snap.IdValue ?? ElementId.InvalidElementId); break;
                    }
                }
                catch
                {
                    // Some params reject sets at certain times; not critical
                }
            }
        }

        /// <summary>
        /// Creates a fresh hanger instance at the same location, rotation,
        /// and FamilySymbol as the old one but with the new Nominal Diameter,
        /// then deletes the old. CRITICAL: creates BEFORE deleting, so if
        /// NewFamilyInstance fails for any reason the old hanger is left
        /// intact rather than vanishing.
        ///
        /// Tries multiple NewFamilyInstance overloads in fallback order:
        ///   1. (point, symbol, host, level, structuralType) — most explicit
        ///   2. (point, symbol, host, structuralType) — host-aware no level
        ///   3. (point, symbol, level, structuralType) — level-aware no host
        ///   4. (point, symbol, structuralType) — bare placement
        /// Different family authoring may accept only some of these.
        ///
        /// Returns the new ElementId on success, or null on failure.
        /// On failure, appends a diagnostic line to errorLog with the old
        /// element id and what went wrong.
        /// Must be called inside a transaction.
        /// </summary>
        /// <summary>
        /// Recreates a hanger at the same point/rotation with the new size.
        ///
        /// CRITICAL — verify after create:
        /// NewFamilyInstance can return a "valid-looking" FamilyInstance whose
        /// geometric host binding has silently failed (no exception thrown).
        /// The instance has IsValidObject=true, but its geometry isn't
        /// computed and its Host property is null. Setting parameters on
        /// such a hollow reference and then deleting the old hanger leaves
        /// the user with NOTHING visible — exactly the bug the user reported.
        ///
        /// To prevent that, each NewFamilyInstance attempt is followed by:
        ///   1. doc.Regenerate() to force geometry computation
        ///   2. Check IsValidObject
        ///   3. Check Host is non-null (for hosted families)
        ///   4. Check get_BoundingBox(null) is non-null AND non-degenerate
        /// If any verification fails, the bogus new instance is deleted and
        /// the next overload is tried. Only if a verified instance is
        /// produced do we delete the old hanger.
        ///
        /// Returns the new ElementId on success, null on failure.
        /// errorLog accumulates step-by-step diagnostic info; the caller
        /// dumps it to clipboard at the end of the operation.
        /// </summary>
        private ElementId TryRecreate(Document doc, FamilyInstance oldHanger,
            double targetDiaFt, List<string> errorLog)
        {
            ElementId oldId = oldHanger.Id;
            string idTag = $"id={oldId.IntegerValue}";

            try
            {
                // ── Capture state from the old instance (before any modification) ──
                var locPt = oldHanger.Location as LocationPoint;
                if (locPt == null)
                {
                    errorLog.Add($"{idTag}: SKIP — hanger has no LocationPoint");
                    return null;
                }
                XYZ originalPt = locPt.Point;
                double originalRotation = locPt.Rotation;

                FamilySymbol symbol = oldHanger.Symbol;
                if (symbol == null)
                {
                    errorLog.Add($"{idTag}: SKIP — hanger has no FamilySymbol");
                    return null;
                }

                Element hostPipe = oldHanger.Host;
                Level level = ResolveLevel(doc, oldHanger, hostPipe, originalPt);
                var snapshots = CaptureWritableParameters(oldHanger);

                if (!symbol.IsActive) symbol.Activate();

                errorLog.Add($"{idTag}: starting recreate; " +
                             $"family=\"{symbol.Family?.Name}\", type=\"{symbol.Name}\", " +
                             $"host={(hostPipe != null ? hostPipe.Id.IntegerValue.ToString() : "(null)")}, " +
                             $"level={(level != null ? level.Id.IntegerValue.ToString() : "(null)")}, " +
                             $"point=({originalPt.X:F2},{originalPt.Y:F2},{originalPt.Z:F2})");

                // ── Strategy chain: try each overload, verify, accept or delete-and-retry ──
                FamilyInstance newHanger = null;
                if (hostPipe != null && level != null)
                    newHanger = TryStrategy(doc, errorLog, idTag, "NFI(point, symbol, host, level)",
                        () => doc.Create.NewFamilyInstance(originalPt, symbol, hostPipe, level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural),
                        requireHost: true);

                if (newHanger == null && hostPipe != null)
                    newHanger = TryStrategy(doc, errorLog, idTag, "NFI(point, symbol, host)",
                        () => doc.Create.NewFamilyInstance(originalPt, symbol, hostPipe,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural),
                        requireHost: true);

                if (newHanger == null && level != null)
                    newHanger = TryStrategy(doc, errorLog, idTag, "NFI(point, symbol, level)",
                        () => doc.Create.NewFamilyInstance(originalPt, symbol, level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural),
                        requireHost: false);

                if (newHanger == null)
                    newHanger = TryStrategy(doc, errorLog, idTag, "NFI(point, symbol)",
                        () => doc.Create.NewFamilyInstance(originalPt, symbol,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural),
                        requireHost: false);

                if (newHanger == null)
                {
                    errorLog.Add($"{idTag}: ALL STRATEGIES FAILED — old hanger preserved.");
                    errorLog.Add($"{idTag}:   If this is a HydraCAD family (e.g. \"Adjustable " +
                                 "Ring Hanger\"), the family's geometry may require a host " +
                                 "relationship that NewFamilyInstance can't establish via the " +
                                 "API. Workaround: run \"Swap HydraCAD\" to convert it to the " +
                                 "SG -Pipe Hanger - Standard family first, then run Replace " +
                                 "Sizes.");
                    return null;
                }

                // ── New instance is verified. Safe to delete the old one. ──
                try
                {
                    doc.Delete(oldId);
                    errorLog.Add($"{idTag}: deleted old; new id={newHanger.Id.IntegerValue}");
                }
                catch (Exception ex)
                {
                    errorLog.Add($"{idTag}: WARNING — new id={newHanger.Id.IntegerValue} " +
                                 $"created but failed to delete old: {ex.Message}");
                }

                // ── Apply rotation about the vertical axis through the placement point ──
                if (Math.Abs(originalRotation) > 1e-9)
                {
                    try
                    {
                        Line zAxis = Line.CreateBound(originalPt, originalPt + XYZ.BasisZ);
                        ElementTransformUtils.RotateElement(
                            doc, newHanger.Id, zAxis, originalRotation);
                    }
                    catch (Exception ex)
                    {
                        errorLog.Add($"{idTag}: rotation by {originalRotation:F4} rad failed: " +
                                     ex.Message);
                    }
                }

                // ── Set Nominal Diameter to the TARGET (not the captured old value).
                //    Set ALL parameters named "Nominal Diameter" since some families have
                //    a shared + built-in pair with the same name. ──
                foreach (Parameter p in newHanger.Parameters)
                {
                    if (p == null || p.Definition == null || p.IsReadOnly) continue;
                    if (string.Equals(p.Definition.Name, NominalDiameterParam,
                        StringComparison.OrdinalIgnoreCase) &&
                        p.StorageType == StorageType.Double)
                    {
                        try { p.Set(targetDiaFt); } catch { }
                    }
                }

                // ── Restore the rest of the captured parameters ──
                RestoreCapturedParameters(newHanger, snapshots, skipName: NominalDiameterParam);

                // Final regen so any geometry that depends on the restored parameters
                // gets computed. Without this, the user might briefly see a default-sized
                // hanger before Revit's lazy regen catches up.
                doc.Regenerate();

                return newHanger.Id;
            }
            catch (Exception ex)
            {
                errorLog.Add($"{idTag}: unexpected exception: {ex.GetType().Name} {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Runs a NewFamilyInstance attempt, regenerates the document so the new
        /// instance's geometry actually gets computed, and verifies the result is
        /// a "real" instance (valid object, host bound if requested, non-degenerate
        /// bounding box). If the verification fails, the bogus instance is deleted
        /// so we don't leave orphans, and null is returned so the caller can try
        /// the next strategy.
        ///
        /// This is the core of the verify-after-create pattern that fixes the
        /// "NewFamilyInstance succeeded but no geometry appears" silent-failure
        /// mode that plagues hosted MEP families like the HydraCAD Adjustable
        /// Ring Hanger.
        /// </summary>
        private FamilyInstance TryStrategy(Document doc, List<string> log,
            string idTag, string label, Func<FamilyInstance> create, bool requireHost)
        {
            FamilyInstance fi;
            try
            {
                fi = create();
            }
            catch (Exception ex)
            {
                log.Add($"{idTag}: {label} threw {ex.GetType().Name}: {ex.Message}");
                return null;
            }

            if (fi == null)
            {
                log.Add($"{idTag}: {label} returned null");
                return null;
            }

            // Force geometry computation so the verification reads real data
            try { doc.Regenerate(); }
            catch (Exception ex)
            {
                log.Add($"{idTag}: {label} regenerate after create threw: {ex.Message}");
                TryDeleteOrphan(doc, fi.Id, log, idTag, label);
                return null;
            }

            if (!fi.IsValidObject)
            {
                log.Add($"{idTag}: {label} returned but IsValidObject=false");
                return null; // can't delete an invalid object
            }

            // Host check — only fail if we EXPECTED a host (placed via host overload)
            if (requireHost)
            {
                Element host = null;
                try { host = fi.Host; } catch { }
                if (host == null)
                {
                    log.Add($"{idTag}: {label} created id={fi.Id.IntegerValue} but Host is null " +
                            "(hosted-family geometric binding failed)");
                    TryDeleteOrphan(doc, fi.Id, log, idTag, label);
                    return null;
                }
            }

            // Bounding box check — null or near-zero size means no visible geometry
            BoundingBoxXYZ bb = null;
            try { bb = fi.get_BoundingBox(null); } catch { }
            if (bb == null)
            {
                log.Add($"{idTag}: {label} created id={fi.Id.IntegerValue} but BoundingBox is null " +
                        "(no geometry computed)");
                TryDeleteOrphan(doc, fi.Id, log, idTag, label);
                return null;
            }
            double bbDiag = (bb.Max - bb.Min).GetLength();
            if (bbDiag < 0.01)
            {
                log.Add($"{idTag}: {label} created id={fi.Id.IntegerValue} but BB diag is " +
                        $"{bbDiag:F4} ft (essentially zero — geometry not built)");
                TryDeleteOrphan(doc, fi.Id, log, idTag, label);
                return null;
            }

            log.Add($"{idTag}: {label} OK — id={fi.Id.IntegerValue}, BB diag {bbDiag:F2} ft");
            return fi;
        }

        private void TryDeleteOrphan(Document doc, ElementId orphanId,
            List<string> log, string idTag, string label)
        {
            try
            {
                doc.Delete(orphanId);
                log.Add($"{idTag}: {label} orphan id={orphanId.IntegerValue} deleted");
            }
            catch (Exception ex)
            {
                log.Add($"{idTag}: {label} could not delete orphan id={orphanId.IntegerValue}: " +
                        ex.Message);
            }
        }

        /// <summary>
        /// Resolves a Level to use for the new hanger placement, trying
        /// several sources in order:
        ///   1. The hanger's own LevelId
        ///   2. The host pipe's LevelId
        ///   3. The host pipe's "Reference Level" parameter
        ///   4. The level whose elevation is closest to the hanger's Z
        /// Last resort guarantees a non-null level as long as the doc has any.
        /// </summary>
        private Level ResolveLevel(Document doc, FamilyInstance oldHanger,
            Element hostPipe, XYZ hangerPt)
        {
            try
            {
                if (oldHanger.LevelId != null
                    && oldHanger.LevelId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(oldHanger.LevelId) as Level;
                    if (lvl != null) return lvl;
                }
            }
            catch { }

            if (hostPipe != null)
            {
                try
                {
                    if (hostPipe.LevelId != null
                        && hostPipe.LevelId != ElementId.InvalidElementId)
                    {
                        var lvl = doc.GetElement(hostPipe.LevelId) as Level;
                        if (lvl != null) return lvl;
                    }
                }
                catch { }

                try
                {
                    var p = hostPipe.LookupParameter("Reference Level");
                    if (p != null && p.HasValue
                        && p.StorageType == StorageType.ElementId)
                    {
                        var lvl = doc.GetElement(p.AsElementId()) as Level;
                        if (lvl != null) return lvl;
                    }
                }
                catch { }
            }

            // Last resort: closest level by elevation
            try
            {
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .ToList();
                if (levels.Count > 0)
                    return levels.OrderBy(l => Math.Abs(l.Elevation - hangerPt.Z)).First();
            }
            catch { }

            return null;
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

