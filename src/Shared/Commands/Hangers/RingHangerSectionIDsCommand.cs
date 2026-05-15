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
    /// Adjustable-ring-hanger variant of <see cref="HangerSectionIDsCommand"/>.
    /// Subtracts a ring takeout from each hanger's Rod Length based on its
    /// Nominal Diameter, then writes the type code + remaining length into
    /// the Section_ID (Hydratec) parameter (Constraints group on the
    /// Properties palette) using the same tag-friendly format as the
    /// non-ring section-IDs command.
    ///
    /// FORMAT: (WHOLE#TYPECODE_FRACTION) — e.g. (22#R3R¼)
    ///
    /// TAKEOUT TABLE (ring/pipe nominal diameter → takeout, inches):
    ///   1" / 1¼" / 1½"  →  1.5"
    ///   2"              →  2.0"
    ///   2½" / 3"        →  3.0"
    ///   4"              →  3.5"
    ///   6"              →  5.5"
    ///   8"              →  6.5"
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers (no pick prompt)
    ///   2. Filter to families containing "Adjustable Ring Hanger"
    ///   3. For each hanger:
    ///      a. Read Rod Length (feet) and Nominal Diameter (feet)
    ///      b. Look up takeout by nominal diameter (tolerance-matched)
    ///      c. remaining = rod_length − takeout
    ///      d. Round to nearest ¼", format with Type Code
    ///      e. Write to Section_ID (Hydratec)
    ///   4. Report counts (updated / skipped reasons)
    ///
    /// Does NOT modify Rod Length — only the Section_ID label, which is what
    /// tags read. The original rod length stays intact for downstream tools
    /// (Hanger Gap Check, raybounce syncs, etc.).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RingHangerSectionIDsCommand : IExternalCommand
    {
        private const string RingHangerFamilyPattern = "Adjustable Ring Hanger";

        private const string RodLengthParam = "Rod Length";
        private const string NominalDiameterParam = "Nominal Diameter";
        private const string TypeCodeParam = "Type Code (Hydratec)";
        private const string SectionIdParam = "Section_ID (Hydratec)";

        /// <summary>
        /// Ring takeout lookup. Keys are nominal hanger diameter in inches;
        /// values are the takeout (rod-end-to-pipe-top hardware length) in
        /// inches. Matched against the hanger's Nominal Diameter parameter
        /// with a small tolerance to absorb floating-point conversion noise.
        /// </summary>
        private static readonly (double NominalInches, double TakeoutInches)[] RingTakeouts =
        {
            (1.00, 1.5),
            (1.25, 1.5),
            (1.50, 1.5),
            (2.00, 2.0),
            (2.50, 3.0),
            (3.00, 3.0),
            (4.00, 3.5),
            (6.00, 5.5),
            (8.00, 6.5)
        };

        /// <summary>Tolerance for matching nominal diameter (inches).</summary>
        private const double DiameterToleranceInches = 0.05;

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
                    TaskDialog.Show("Ring Hanger Section IDs",
                        "No elements are currently selected.\n\n" +
                        "Select Adjustable Ring Hanger family instances first, " +
                        "then run this command.");
                    return Result.Cancelled;
                }

                // ── Filter to Adjustable Ring Hanger family instances ──
                var hangers = new List<FamilyInstance>();
                foreach (var id in selectedIds)
                {
                    var fi = doc.GetElement(id) as FamilyInstance;
                    if (fi == null) continue;

                    string familyName = fi.Symbol?.Family?.Name ?? "";
                    if (familyName.IndexOf(RingHangerFamilyPattern,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hangers.Add(fi);
                    }
                }

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Ring Hanger Section IDs",
                        "None of the selected elements are Adjustable Ring Hanger " +
                        "family instances.\n\n" +
                        "Looking for families containing: \"" + RingHangerFamilyPattern + "\"");
                    return Result.Cancelled;
                }

                // ── Process and write ──
                int updated = 0;
                int skippedNoRod = 0;
                int skippedNoDia = 0;
                int skippedSizeMissing = 0;
                int skippedNegative = 0;
                var unmatchedSizes = new SortedSet<double>();

                using (var tw = new TransactionWrapper(doc, "Ring Hanger Section IDs"))
                {
                    foreach (var hanger in hangers)
                    {
                        // Rod length (feet)
                        double? rodLengthFt = GetDoubleParam(hanger, RodLengthParam);
                        if (!rodLengthFt.HasValue || rodLengthFt.Value <= 0)
                        {
                            skippedNoRod++;
                            continue;
                        }

                        // Nominal diameter (feet → inches)
                        double? nomDiaFt = GetDoubleParam(hanger, NominalDiameterParam);
                        if (!nomDiaFt.HasValue || nomDiaFt.Value <= 0)
                        {
                            skippedNoDia++;
                            continue;
                        }
                        double nomDiaInches = nomDiaFt.Value * 12.0;

                        // Takeout lookup
                        double? takeoutInches = LookupTakeoutInches(nomDiaInches);
                        if (!takeoutInches.HasValue)
                        {
                            skippedSizeMissing++;
                            unmatchedSizes.Add(Math.Round(nomDiaInches, 2));
                            continue;
                        }

                        // Remaining length (inches)
                        double rodLengthInches = rodLengthFt.Value * 12.0;
                        double remainingInches = rodLengthInches - takeoutInches.Value;
                        if (remainingInches <= 0)
                        {
                            skippedNegative++;
                            continue;
                        }

                        // Round to nearest ¼"
                        double roundedInches = Math.Round(remainingInches / 0.25) * 0.25;
                        int wholePart = (int)Math.Floor(roundedInches);
                        double fraction = roundedInches - wholePart;

                        // Format and write
                        string typeCode = GetStringParam(hanger, TypeCodeParam) ?? "";
                        string fractionStr = FormatFraction(fraction);
                        string sectionId = "(" + wholePart + "#" + typeCode + fractionStr + ")";

                        SetStringParam(hanger, SectionIdParam, sectionId);
                        updated++;
                    }

                    tw.Commit();
                }

                // ── Report ──
                string report = $"Ring Hanger Section IDs\n\n" +
                                $"Ring hangers in selection: {hangers.Count}\n" +
                                $"Updated:                   {updated}\n";

                if (skippedNoRod > 0)
                    report += $"Skipped (no Rod Length):   {skippedNoRod}\n";
                if (skippedNoDia > 0)
                    report += $"Skipped (no Nominal Dia):  {skippedNoDia}\n";
                if (skippedSizeMissing > 0)
                {
                    report += $"Skipped (size not in table): {skippedSizeMissing}\n";
                    report += "  Unmatched sizes (in): " +
                              string.Join(", ", unmatchedSizes.Select(s => s.ToString("F2"))) + "\n";
                }
                if (skippedNegative > 0)
                    report += $"Skipped (takeout > rod):   {skippedNegative}\n";

                report += $"\nSection_ID (Hydratec) populated as (length#type) " +
                          $"with ring takeout subtracted from Rod Length.";

                TaskDialog.Show("Ring Hanger Section IDs", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Ring Hanger Section IDs failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Tolerance-matches a nominal diameter (in inches) against the takeout
        /// table. Returns null if no entry within DiameterToleranceInches.
        /// </summary>
        private double? LookupTakeoutInches(double nominalInches)
        {
            foreach (var (nom, take) in RingTakeouts)
            {
                if (Math.Abs(nom - nominalInches) < DiameterToleranceInches)
                    return take;
            }
            return null;
        }

        /// <summary>
        /// Renders ¼/½/¾ fractions as Unicode characters; returns "" for whole.
        /// Matches HangerSectionIDsCommand's format so tags stay consistent.
        /// </summary>
        private string FormatFraction(double fraction)
        {
            string fStr = fraction.ToString("F6");
            if (fStr == "0.000000") return "";
            if (fStr == "0.250000") return "¼"; // ¼
            if (fStr == "0.500000") return "½"; // ½
            return "¾";                         // ¾
        }

        private double? GetDoubleParam(Element elem, string paramName)
        {
            var param = elem.LookupParameter(paramName);
            if (param == null || !param.HasValue) return null;

            if (param.StorageType == StorageType.Double)
                return param.AsDouble();
            if (param.StorageType == StorageType.Integer)
                return param.AsInteger();

            string val = param.AsString();
            if (!string.IsNullOrEmpty(val) && double.TryParse(val, out double d))
                return d;
            return null;
        }

        private string GetStringParam(Element elem, string paramName)
        {
            var param = elem.LookupParameter(paramName);
            if (param == null || !param.HasValue) return null;
            if (param.StorageType == StorageType.String) return param.AsString();
            return param.AsValueString();
        }

        private void SetStringParam(Element elem, string paramName, string value)
        {
            var param = elem.LookupParameter(paramName);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                param.Set(value);
        }
    }
}
