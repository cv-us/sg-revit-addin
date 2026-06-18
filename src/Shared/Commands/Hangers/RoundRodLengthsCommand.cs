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
    /// Rounds the Rod Length of every selected hanger UP to the nearest
    /// half inch. Rods already on a full or half inch are left untouched;
    /// rods are never rounded down.
    ///
    /// EXAMPLES:
    ///   8 41/256"  (8.160") → 8 1/2"
    ///   11 17/32"  (11.531") → 12"
    ///   8 1/2"     (already on a half inch) → unchanged
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers (no pick prompt).
    ///   2. Filter to recognised hanger families.
    ///   3. Pre-count how many need rounding.
    ///   4. Dialog: confirm + "also set Y Grip" option (remembered).
    ///   5. Round each eligible hanger's Rod Length up; optionally Y Grip.
    ///   6. Report counts.
    ///
    /// UNITS:
    ///   Rod Length is stored in feet (Revit internal). All rounding math
    ///   is done in inches, then converted back to feet on write.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoundRodLengthsCommand : IExternalCommand
    {
        private const string RodLengthParam = "Rod Length";
        private const string YGripParam = "Y Grip";

        // Family-name substrings recognised as a hanger (matches the other
        // Hangers commands).
        private static readonly string[] HangerFamilyPatterns =
        {
            "-Pipe Hanger",
            "-Pipe Trapeze",
            "-Basic Adjustable",
            "Adjustable Ring Hanger",
            "Ring Hanger"
        };

        /// <summary>
        /// Tolerance for "already on a half inch", in half-inch units. A rod
        /// within this of a half-inch boundary is treated as already there
        /// (absorbs floating-point noise; ~0.0005").
        /// </summary>
        private const double HalfInchTol = 0.001;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Read selection ──
                var selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Round Rods Up",
                        "No elements are currently selected.\n\n" +
                        "Select hangers first, then run this command.");
                    return Result.Cancelled;
                }

                // ── Filter to hangers ──
                var hangers = new List<FamilyInstance>();
                foreach (var id in selectedIds)
                {
                    var fi = doc.GetElement(id) as FamilyInstance;
                    if (fi != null && IsHanger(fi)) hangers.Add(fi);
                }

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Round Rods Up",
                        "None of the selected elements are recognised hanger families.\n\n" +
                        "Looking for families containing:\n" +
                        string.Join("\n", HangerFamilyPatterns.Select(p => "  • " + p)));
                    return Result.Cancelled;
                }

                // ── Pre-count how many need rounding ──
                int willChange = 0;
                foreach (var h in hangers)
                {
                    if (TryGetRodFeet(h, out double ft) && NeedsRounding(ft))
                        willChange++;
                }

                // ── Dialog ──
                bool updateYGrip;
                using (var dlg = new RoundRodLengthsDialog(hangers.Count, willChange))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;
                    updateYGrip = dlg.UpdateYGrip;
                }

                // ── Apply ──
                int rounded = 0;
                int alreadyOnHalf = 0;
                int skippedNoRod = 0;
                int skippedReadOnly = 0;

                using (var tw = new TransactionWrapper(doc, "Round Rods Up"))
                {
                    foreach (var h in hangers)
                    {
                        var rodParam = h.LookupParameter(RodLengthParam);
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

                        double targetFt = RoundUpToHalfInchFeet(currentFt);
                        if (Math.Abs(targetFt - currentFt) < (HalfInchTol / 24.0))
                        {
                            alreadyOnHalf++;
                            continue;
                        }

                        if (rodParam.IsReadOnly)
                        {
                            skippedReadOnly++;
                            continue;
                        }

                        rodParam.Set(targetFt);
                        rounded++;

                        if (updateYGrip)
                        {
                            var yParam = h.LookupParameter(YGripParam);
                            if (yParam != null && !yParam.IsReadOnly && yParam.StorageType == StorageType.Double)
                                yParam.Set(targetFt);
                        }
                    }

                    tw.Commit();
                }

                // ── Report ──
                string report =
                    "Round Rods Up\n\n" +
                    $"Hangers in selection:           {hangers.Count}\n" +
                    $"Rounded up to next half inch:   {rounded}\n" +
                    $"Already on a half inch:         {alreadyOnHalf}\n";
                if (skippedNoRod > 0)
                    report += $"Skipped (no Rod Length):        {skippedNoRod}\n";
                if (skippedReadOnly > 0)
                    report += $"Skipped (Rod Length read-only): {skippedReadOnly}\n";
                if (updateYGrip && rounded > 0)
                    report += "\nY Grip updated to match.";

                TaskDialog.Show("Round Rods Up", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Round Rods Up failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ── Rounding ──

        /// <summary>Does this length (feet) sit off a half-inch boundary?</summary>
        private static bool NeedsRounding(double feet)
        {
            double halves = feet * 12.0 * 2.0;          // length in half-inch units
            double up = Math.Ceiling(halves - HalfInchTol);
            return Math.Abs(up - halves) > HalfInchTol;  // not already on a boundary
        }

        /// <summary>Round a length (feet) UP to the nearest half inch, return feet.</summary>
        private static double RoundUpToHalfInchFeet(double feet)
        {
            double halves = feet * 12.0 * 2.0;
            double up = Math.Ceiling(halves - HalfInchTol);
            double inches = up / 2.0;
            return inches / 12.0;
        }

        private static bool TryGetRodFeet(Element h, out double feet)
        {
            feet = 0;
            var p = h.LookupParameter(RodLengthParam);
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return false;
            feet = p.AsDouble();
            return feet > 0;
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
    }
}
