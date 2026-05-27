using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Bulk-uniformizes Rod Length on selected hangers that share a Type Code
    /// and sit BELOW a user-defined length threshold.
    ///
    /// USE CASE:
    ///   A run of hangers on the same pipe should all have the same rod
    ///   length but were placed sloppily — measured values vary by ±a few
    ///   inches. Below the same hangers, on a LOWER pipe (longer rods),
    ///   are placed at intentionally-different longer rod lengths. This
    ///   command lets you sweep the upper-pipe sloppy values to a single
    ///   uniform length without touching the longer rods on the lower
    ///   pipe.
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers (no pick prompt).
    ///   2. Filter to recognised hanger families.
    ///   3. Pre-scan distinct Type Codes present.
    ///   4. Dialog: pick Type Code (dropdown), enter max length (in),
    ///      enter target length (in).
    ///   5. For each hanger:
    ///        - skip if Type Code doesn't match
    ///        - skip if Rod Length is missing or read-only
    ///        - skip if Rod Length exceeds max (likely on a lower pipe)
    ///        - else set Rod Length to target (idempotent — already-at-
    ///          target hangers are tallied but not re-written)
    ///   6. Report counts.
    ///
    /// HANGER RECOGNITION:
    ///   Same family-name substring set as the other Hangers commands,
    ///   guarded by the PipeAccessory category.
    ///
    /// UNITS:
    ///   Dialog inputs are inches; the Rod Length parameter is stored
    ///   in feet (Revit internal). Conversion is local to this command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class UniformRodLengthsCommand : IExternalCommand
    {
        private const string TypeCodeParam = "Type Code (Hydratec)";
        private const string RodLengthParam = "Rod Length";

        // Family-name substrings recognised as a hanger.
        private static readonly string[] HangerFamilyPatterns =
        {
            "-Pipe Hanger",
            "-Pipe Trapeze",
            "-Basic Adjustable",
            "Adjustable Ring Hanger",
            "Ring Hanger"
        };

        /// <summary>Length equality tolerance, in feet (~0.01 inch).</summary>
        private const double LengthTolFt = 0.001;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Read current selection (no pick prompt) ──
                var selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Uniform Rod Lengths",
                        "No elements are currently selected.\n\n" +
                        "Select hangers first, then run this command.");
                    return Result.Cancelled;
                }

                // ── Filter to recognised hanger families ──
                var hangers = new List<FamilyInstance>();
                foreach (var id in selectedIds)
                {
                    var fi = doc.GetElement(id) as FamilyInstance;
                    if (fi == null) continue;
                    if (!IsHanger(fi)) continue;
                    hangers.Add(fi);
                }

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Uniform Rod Lengths",
                        "None of the selected elements are recognised hanger families.\n\n" +
                        "Looking for families containing:\n" +
                        string.Join("\n", HangerFamilyPatterns.Select(p => "  • " + p)));
                    return Result.Cancelled;
                }

                // ── Pre-scan distinct Type Codes present ──
                var availableCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var hanger in hangers)
                {
                    string code = GetStringParam(hanger, TypeCodeParam)?.Trim();
                    if (!string.IsNullOrEmpty(code))
                        availableCodes.Add(code);
                }

                if (availableCodes.Count == 0)
                {
                    TaskDialog.Show("Uniform Rod Lengths",
                        $"None of the {hangers.Count} selected hangers have a " +
                        $"\"{TypeCodeParam}\" value.\n\n" +
                        "Run \"Section IDs\" or set Type Code (Hydratec) manually first.");
                    return Result.Cancelled;
                }

                // ── Show dialog ──
                string typeCode;
                double maxInches;
                double targetInches;

                using (var dlg = new UniformRodLengthsDialog(
                    hangers.Count, availableCodes.ToList()))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    typeCode = dlg.TypeCode?.Trim() ?? "";
                    maxInches = dlg.MaxInches;
                    targetInches = dlg.TargetInches;
                }

                if (string.IsNullOrEmpty(typeCode))
                {
                    TaskDialog.Show("Uniform Rod Lengths", "No Type Code selected.");
                    return Result.Cancelled;
                }
                if (maxInches <= 0 || targetInches <= 0)
                {
                    TaskDialog.Show("Uniform Rod Lengths",
                        "Both max length and target length must be greater than zero.");
                    return Result.Cancelled;
                }
                if (targetInches > maxInches)
                {
                    TaskDialog.Show("Uniform Rod Lengths",
                        $"Target length ({targetInches:F2}\") exceeds max length " +
                        $"({maxInches:F2}\"). A target above the max-length cutoff " +
                        "would never get applied — pick a target ≤ max.");
                    return Result.Cancelled;
                }

                double maxFt = maxInches / 12.0;
                double targetFt = targetInches / 12.0;

                // ── Apply ──
                int updated = 0;
                int alreadyAtTarget = 0;
                int otherTypeCode = 0;
                int aboveMax = 0;
                int skippedReadOnly = 0;
                int skippedNoRod = 0;

                using (var tw = new TransactionWrapper(doc, "Uniform Rod Lengths"))
                {
                    foreach (var hanger in hangers)
                    {
                        string current = GetStringParam(hanger, TypeCodeParam)?.Trim() ?? "";
                        if (!string.Equals(current, typeCode, StringComparison.OrdinalIgnoreCase))
                        {
                            otherTypeCode++;
                            continue;
                        }

                        var rodParam = hanger.LookupParameter(RodLengthParam);
                        if (rodParam == null || !rodParam.HasValue || rodParam.StorageType != StorageType.Double)
                        {
                            skippedNoRod++;
                            continue;
                        }

                        double currentFt = rodParam.AsDouble();
                        if (currentFt <= 0)
                        {
                            skippedNoRod++;
                            continue;
                        }

                        if (currentFt > maxFt + LengthTolFt)
                        {
                            // Above the threshold — likely the longer rod
                            // on a lower pipe. Leave it alone.
                            aboveMax++;
                            continue;
                        }

                        if (Math.Abs(currentFt - targetFt) < LengthTolFt)
                        {
                            alreadyAtTarget++;
                            continue;
                        }

                        if (rodParam.IsReadOnly)
                        {
                            skippedReadOnly++;
                            continue;
                        }

                        rodParam.Set(targetFt);
                        updated++;
                    }

                    tw.Commit();
                }

                // ── Report ──
                string report =
                    $"Uniform Rod Lengths\n\n" +
                    $"Hangers in selection:           {hangers.Count}\n" +
                    $"Type Code \"{typeCode}\":          \n" +
                    $"  Updated to {targetInches:F2}\":           {updated}\n" +
                    $"  Already at {targetInches:F2}\":           {alreadyAtTarget}\n" +
                    $"  Above {maxInches:F2}\" (left alone):  {aboveMax}\n";

                if (otherTypeCode > 0)
                    report += $"Other type codes (left alone):  {otherTypeCode}\n";
                if (skippedNoRod > 0)
                    report += $"Skipped (no Rod Length):        {skippedNoRod}\n";
                if (skippedReadOnly > 0)
                    report += $"Skipped (Rod Length read-only): {skippedReadOnly}\n";

                TaskDialog.Show("Uniform Rod Lengths", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Uniform Rod Lengths failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ── Helpers ──

        private bool IsHanger(FamilyInstance fi)
        {
            if (fi.Category == null) return false;
            if (fi.Category.Id.IntegerValue != (int)BuiltInCategory.OST_PipeAccessory)
                return false;

            string familyName = fi.Symbol?.Family?.Name ?? "";
            foreach (var pattern in HangerFamilyPatterns)
            {
                if (familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private string GetStringParam(Element elem, string paramName)
        {
            var p = elem.LookupParameter(paramName);
            if (p == null || !p.HasValue) return null;
            if (p.StorageType == StorageType.String) return p.AsString();
            return p.AsValueString();
        }
    }
}
