using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.ModelCheck
{
    /// <summary>
    /// Identifies pipe hangers whose vertical gap between top-of-pipe and the
    /// structure they hang from exceeds a configurable threshold (default 6").
    ///
    /// Per Hydratec / NFPA convention, when this gap exceeds 6 inches the
    /// hanger may need additional bracing or restraint. The command flags
    /// such hangers by placing a marker family at the hanger location so
    /// they're easy to spot in plan and 3D views.
    ///
    /// GAP MATH (by Type Code (Hydratec)):
    ///   - Type 02 (adjustable ring + 1.5" hardware):
    ///       gap = rod_length - 1.5" - (pipe_OD / 2)
    ///   - All other types (e.g. 03A, 04, etc.):
    ///       gap = rod_length - (pipe_OD / 2)
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers
    ///   2. Dialog: pick which Type Codes to check, which pipe sizes,
    ///      and the gap threshold (default 6")
    ///   3. For each matching hanger, find the closest pipe centerline,
    ///      read pipe Outside Diameter and hanger Rod Length
    ///   4. Apply the type-code-specific math
    ///   5. If gap > threshold, place "-Hanger Gap Marker" family at
    ///      the hanger's location and add to selection
    ///   6. Report summary
    ///
    /// REQUIRED FAMILY:
    ///   "-Hanger Gap Marker" — Generic Model category, visible in both
    ///   plan and 3D. Suggested geometry: red sphere or cylinder at the
    ///   family origin. If the family isn't loaded, the command still runs
    ///   and reports flagged hangers via selection highlight.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangerGapCheckCommand : IExternalCommand
    {
        private const string MarkerFamilyName = "-Hanger Gap Marker";
        private const string TypeCodeParam = "Type Code (Hydratec)";
        private const string RodLengthParam = "Rod Length";

        /// <summary>Hardware offset for Type 02 adjustable hangers, in feet (1.5 inches).</summary>
        private const double Type02HardwareOffset = 1.5 / 12.0;

        /// <summary>Vertical offset above the hanger location to place the marker, in feet.</summary>
        private const double MarkerZOffset = 0.5; // 6 inches

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Collect hangers from current selection ──
                var selectedIds = uidoc.Selection.GetElementIds();
                var hangers = selectedIds
                    .Select(id => doc.GetElement(id))
                    .OfType<FamilyInstance>()
                    .Where(IsHanger)
                    .ToList();

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Hanger Gap Check",
                        "No pipe hangers found in the current selection.\n\n" +
                        "Select hanger family instances (family name contains \"-Pipe Hanger\" " +
                        "or \"-Pipe Trapeze\") and run the command again.");
                    return Result.Cancelled;
                }

                // ── Get pipe centerlines from the entire project (any view) ──
                var pipeCurves = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .Select(p => (pipe: (Element)p, curve: (p.Location as LocationCurve)?.Curve))
                    .Where(t => t.curve != null)
                    .ToList();

                if (pipeCurves.Count == 0)
                {
                    TaskDialog.Show("Hanger Gap Check",
                        "No pipes found in the project.");
                    return Result.Cancelled;
                }

                // ── Pre-scan: collect available type codes and sizes from the selection ──
                // (lets the dialog show only relevant choices)
                var availableTypeCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var availableNominalSizes = new SortedSet<double>();

                foreach (var hanger in hangers)
                {
                    string tc = GetParamString(hanger, TypeCodeParam);
                    if (!string.IsNullOrWhiteSpace(tc))
                        availableTypeCodes.Add(tc.Trim());

                    var nearestPipe = FindClosestPipe(GetLocation(hanger), pipeCurves);
                    if (nearestPipe != null)
                    {
                        double diaFt = GetParamDouble(
                            nearestPipe.Value.pipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        if (diaFt > 0)
                            availableNominalSizes.Add(diaFt);
                    }
                }

                if (availableTypeCodes.Count == 0)
                {
                    TaskDialog.Show("Hanger Gap Check",
                        $"None of the {hangers.Count} selected hangers have a " +
                        $"\"{TypeCodeParam}\" parameter populated.\n\n" +
                        "Run \"Section IDs\" or set the parameter manually before running this check.");
                    return Result.Cancelled;
                }

                // ── Show dialog ──
                using (var dlg = new HangerGapCheckDialog(
                    hangers.Count, availableTypeCodes.ToList(), availableNominalSizes.ToList()))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    var selectedTypeCodes = new HashSet<string>(
                        dlg.SelectedTypeCodes, StringComparer.OrdinalIgnoreCase);
                    var selectedSizes = new HashSet<double>(dlg.SelectedSizes);
                    double thresholdFt = dlg.ThresholdInches / 12.0;

                    // ── Find marker family symbol ──
                    FamilySymbol marker = FindMarkerFamily(doc);

                    // ── Process each hanger ──
                    var flaggedIds = new List<ElementId>();
                    int matchedCount = 0;
                    int skippedNoPipe = 0;
                    int skippedNoRod = 0;
                    int markersPlaced = 0;
                    var worstOffenders = new List<(FamilyInstance hanger, double gapInches, string typeCode)>();

                    using (var tx = new Transaction(doc, "Hanger Gap Check"))
                    {
                        tx.Start();

                        // Clear any previous markers in the project (keeps re-runs clean)
                        if (marker != null)
                            ClearPreviousMarkers(doc);

                        foreach (var hanger in hangers)
                        {
                            string typeCode = GetParamString(hanger, TypeCodeParam)?.Trim() ?? "";
                            if (!selectedTypeCodes.Contains(typeCode)) continue;

                            XYZ hangerPt = GetLocation(hanger);
                            if (hangerPt == null) continue;

                            var nearest = FindClosestPipe(hangerPt, pipeCurves);
                            if (nearest == null) { skippedNoPipe++; continue; }

                            // Filter by selected pipe sizes (nominal diameter, in feet)
                            double pipeDiaFt = GetParamDouble(
                                nearest.Value.pipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                            if (!selectedSizes.Any(s => Math.Abs(s - pipeDiaFt) < 0.001)) continue;

                            matchedCount++;

                            // Read rod length (feet)
                            double rodLengthFt = GetParamDouble(hanger, RodLengthParam);
                            if (rodLengthFt <= 0) { skippedNoRod++; continue; }

                            // Read actual outside diameter (feet)
                            double pipeODFt = GetPipeOutsideDiameter(nearest.Value.pipe, pipeDiaFt);

                            // Compute gap
                            double gapFt = ComputeGap(typeCode, rodLengthFt, pipeODFt);

                            if (gapFt > thresholdFt)
                            {
                                flaggedIds.Add(hanger.Id);
                                worstOffenders.Add((hanger, gapFt * 12.0, typeCode));

                                // Place marker
                                if (marker != null)
                                {
                                    try
                                    {
                                        if (!marker.IsActive) marker.Activate();
                                        XYZ markerPt = new XYZ(
                                            hangerPt.X, hangerPt.Y, hangerPt.Z + MarkerZOffset);
                                        doc.Create.NewFamilyInstance(markerPt, marker,
                                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                        markersPlaced++;
                                    }
                                    catch { /* non-critical, still flag via selection */ }
                                }
                            }
                        }

                        tx.Commit();
                    }

                    // ── Highlight flagged hangers ──
                    if (flaggedIds.Count > 0)
                        uidoc.Selection.SetElementIds(flaggedIds);

                    // ── Report ──
                    string report =
                        $"Hanger Gap Check Results\n\n" +
                        $"Hangers in selection:        {hangers.Count}\n" +
                        $"Matched filter (type+size):  {matchedCount}\n" +
                        $"Threshold:                   {dlg.ThresholdInches:F1}\"\n\n" +
                        $"FLAGGED (gap > threshold):   {flaggedIds.Count}\n";

                    if (skippedNoPipe > 0)
                        report += $"\nSkipped (no nearby pipe): {skippedNoPipe}";
                    if (skippedNoRod > 0)
                        report += $"\nSkipped (no rod length):  {skippedNoRod}";

                    if (markersPlaced > 0)
                    {
                        report += $"\n\nMarkers placed: {markersPlaced} " +
                                  $"({MarkerFamilyName})";
                    }
                    else if (marker == null && flaggedIds.Count > 0)
                    {
                        report += $"\n\nMarker family \"{MarkerFamilyName}\" is not loaded — " +
                                  $"flagged hangers are selected but no markers were placed.\n" +
                                  $"Load the family via Setup > Load Families to enable markers.";
                    }

                    if (worstOffenders.Count > 0)
                    {
                        report += "\n\nLargest gaps:";
                        foreach (var w in worstOffenders.OrderByDescending(o => o.gapInches).Take(5))
                            report += $"\n  ID {w.hanger.Id}: {w.gapInches:F2}\" (Type {w.typeCode})";
                    }

                    if (flaggedIds.Count > 0)
                        report += "\n\nFlagged hangers are highlighted in the selection.";

                    TaskDialog.Show("Hanger Gap Check", report);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Math ──

        /// <summary>
        /// Computes the vertical gap from top-of-pipe to bottom-of-structure.
        /// Type 02 adjustable hangers have an additional 1.5" hardware offset.
        /// </summary>
        private double ComputeGap(string typeCode, double rodLengthFt, double pipeODFt)
        {
            double gapFt = rodLengthFt - (pipeODFt / 2.0);

            if (string.Equals(typeCode, "02", StringComparison.OrdinalIgnoreCase))
                gapFt -= Type02HardwareOffset;

            return gapFt;
        }

        // ── Helpers ──

        private bool IsHanger(FamilyInstance fi)
        {
            string familyName = fi.Symbol?.Family?.Name ?? "";
            return familyName.IndexOf("-Pipe Hanger", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Pipe Trapeze", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("-Basic Adjustable", StringComparison.OrdinalIgnoreCase) >= 0
                || familyName.IndexOf("Ring Hanger", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private XYZ GetLocation(Element element)
        {
            var loc = element.Location as LocationPoint;
            return loc?.Point;
        }

        private string GetParamString(Element element, string paramName)
        {
            var p = element.LookupParameter(paramName);
            if (p == null) return null;
            if (p.StorageType == StorageType.String) return p.AsString();
            return p.AsValueString();
        }

        private double GetParamDouble(Element element, string paramName)
        {
            var p = element.LookupParameter(paramName);
            return (p != null && p.HasValue) ? p.AsDouble() : 0.0;
        }

        private double GetParamDouble(Element element, BuiltInParameter bip)
        {
            var p = element.get_Parameter(bip);
            return (p != null && p.HasValue) ? p.AsDouble() : 0.0;
        }

        /// <summary>
        /// Returns the pipe's actual outside diameter in feet. Falls back to
        /// nominal diameter (also in feet) if the actual OD parameter is absent.
        /// </summary>
        private double GetPipeOutsideDiameter(Element pipe, double nominalFallbackFt)
        {
            // Built-in parameter for outside diameter (actual, not nominal)
            var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);
            if (p != null && p.HasValue && p.AsDouble() > 0)
                return p.AsDouble();

            // Some content uses a shared parameter named "Outside Diameter"
            var named = pipe.LookupParameter("Outside Diameter");
            if (named != null && named.HasValue && named.AsDouble() > 0)
                return named.AsDouble();

            // Fallback to nominal
            return nominalFallbackFt;
        }

        private (Element pipe, XYZ closestPoint, Curve curve)?
            FindClosestPipe(XYZ hangerPoint, List<(Element pipe, Curve curve)> pipeCurves)
        {
            if (hangerPoint == null) return null;

            Element bestPipe = null;
            XYZ bestPoint = null;
            Curve bestCurve = null;
            double bestDist = double.MaxValue;

            foreach (var (pipe, curve) in pipeCurves)
            {
                IntersectionResult projResult = curve.Project(hangerPoint);
                if (projResult == null) continue;

                double dist = projResult.Distance;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPipe = pipe;
                    bestPoint = projResult.XYZPoint;
                    bestCurve = curve;
                }
            }

            // Reject if nothing within ~1 ft (sanity check — hangers should be on the pipe)
            if (bestPipe == null || bestDist > 1.0) return null;

            return (bestPipe, bestPoint, bestCurve);
        }

        private FamilySymbol FindMarkerFamily(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => string.Equals(fs.Family.Name,
                    MarkerFamilyName, StringComparison.OrdinalIgnoreCase));
        }

        private void ClearPreviousMarkers(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var fi = e as FamilyInstance;
                    return string.Equals(fi?.Symbol?.Family?.Name,
                        MarkerFamilyName, StringComparison.OrdinalIgnoreCase);
                })
                .Select(e => e.Id)
                .ToList();

            if (existing.Count > 0)
                doc.Delete(existing);
        }
    }
}
