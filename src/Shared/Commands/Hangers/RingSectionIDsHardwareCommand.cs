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
    /// Hardware-compensated variant of the Ring Section IDs command. Computes
    /// the ring-takeout length the same way, then adds 1.5" back for hanger
    /// assemblies whose Type Code starts with "01" or "02" (these carry an
    /// extra 1.5" of hardware between the rod end and the pipe that the ring
    /// takeout over-subtracts).
    ///
    /// The result is written to Section_ID (Hydratec) as TYPECODE(LENGTH)
    /// with NO leading '#'.
    ///
    /// EXAMPLE (1" hanger, ring takeout 1.5"):
    ///   Type 02D, Rod Length 4"
    ///     → 4" − 1.5" (ring takeout)          = 2.5"
    ///     → 2.5" + 1.5" (01/02 hardware add)  = 4.0"
    ///     → Section_ID = "02D(4)"
    ///
    /// Type codes that don't start with 01 or 02 get the plain ring takeout
    /// with no add-back, identical to the Ring Section IDs command (but
    /// without the '#').
    ///
    /// TAKEOUT TABLE (nominal diameter → takeout, inches):
    ///   1" / 1¼" / 1½"  →  1.5"
    ///   2"              →  2.0"
    ///   2½" / 3"        →  3.0"
    ///   4"              →  3.5"
    ///   6"              →  5.5"
    ///   8"              →  6.5"
    ///
    /// Does NOT modify Rod Length — only the Section_ID label.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RingSectionIDsHardwareCommand : IExternalCommand
    {
        private const string RodLengthParam = "Rod Length";
        private const string NominalDiameterParam = "Nominal Diameter";
        private const string TypeCodeParam = "Type Code (Hydratec)";
        private const string SectionIdParam = "Section_ID (Hydratec)";

        /// <summary>Hardware add-back for 01*/02* assemblies, in inches.</summary>
        private const double HardwareAddInches = 1.5;

        private static readonly string[] HangerFamilyPatterns =
        {
            "-Pipe Hanger",
            "-Pipe Trapeze",
            "-Basic Adjustable",
            "Adjustable Ring Hanger",
            "Ring Hanger"
        };

        /// <summary>Ring takeout lookup: nominal diameter (in) → takeout (in).</summary>
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

        private const double DiameterToleranceInches = 0.05;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Ring Section IDs (+Hardware)",
                        "No elements are currently selected.\n\n" +
                        "Select hangers first, then run this command.");
                    return Result.Cancelled;
                }

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
                    TaskDialog.Show("Ring Section IDs (+Hardware)",
                        "None of the selected elements are recognised hanger families.\n\n" +
                        "Looking for families containing:\n" +
                        string.Join("\n", HangerFamilyPatterns.Select(p => "  • " + p)));
                    return Result.Cancelled;
                }

                int updated = 0;
                int hardwareAdded = 0;
                int skippedNoRod = 0;
                int skippedNoDia = 0;
                int skippedSizeMissing = 0;
                int skippedNegative = 0;
                var unmatchedSizes = new SortedSet<double>();

                using (var tw = new TransactionWrapper(doc, "Ring Section IDs (+Hardware)"))
                {
                    foreach (var hanger in hangers)
                    {
                        double? rodLengthFt = GetDoubleParam(hanger, RodLengthParam);
                        if (!rodLengthFt.HasValue || rodLengthFt.Value <= 0)
                        {
                            skippedNoRod++;
                            continue;
                        }

                        double? nomDiaFt = GetDoubleParam(hanger, NominalDiameterParam);
                        if (!nomDiaFt.HasValue || nomDiaFt.Value <= 0)
                        {
                            skippedNoDia++;
                            continue;
                        }
                        double nomDiaInches = nomDiaFt.Value * 12.0;

                        double? takeoutInches = LookupTakeoutInches(nomDiaInches);
                        if (!takeoutInches.HasValue)
                        {
                            skippedSizeMissing++;
                            unmatchedSizes.Add(Math.Round(nomDiaInches, 2));
                            continue;
                        }

                        double rodLengthInches = rodLengthFt.Value * 12.0;
                        double remainingInches = rodLengthInches - takeoutInches.Value;

                        // Add the 1.5" hardware back for 01*/02* assemblies.
                        string typeCode = GetStringParam(hanger, TypeCodeParam)?.Trim() ?? "";
                        bool isHardwareType =
                            typeCode.StartsWith("01", StringComparison.OrdinalIgnoreCase) ||
                            typeCode.StartsWith("02", StringComparison.OrdinalIgnoreCase);
                        if (isHardwareType)
                        {
                            remainingInches += HardwareAddInches;
                            hardwareAdded++;
                        }

                        if (remainingInches <= 0)
                        {
                            skippedNegative++;
                            continue;
                        }

                        // Round to nearest ¼"
                        double roundedInches = Math.Round(remainingInches / 0.25) * 0.25;
                        int wholePart = (int)Math.Floor(roundedInches);
                        double fraction = roundedInches - wholePart;

                        // Format: TYPECODE(LENGTH) — no leading '#'
                        string fractionStr = FormatFraction(fraction);
                        string sectionId = typeCode + "(" + wholePart + fractionStr + ")";

                        SetStringParam(hanger, SectionIdParam, sectionId);
                        updated++;
                    }

                    tw.Commit();
                }

                string report = $"Ring Section IDs (+Hardware)\n\n" +
                                $"Hangers in selection:        {hangers.Count}\n" +
                                $"Updated:                     {updated}\n" +
                                $"  of which got +1.5\" (01/02): {hardwareAdded}\n";

                if (skippedNoRod > 0)
                    report += $"Skipped (no Rod Length):     {skippedNoRod}\n";
                if (skippedNoDia > 0)
                    report += $"Skipped (no Nominal Dia):    {skippedNoDia}\n";
                if (skippedSizeMissing > 0)
                {
                    report += $"Skipped (size not in table): {skippedSizeMissing}\n";
                    report += "  Unmatched sizes (in): " +
                              string.Join(", ", unmatchedSizes.Select(s => s.ToString("F2"))) + "\n";
                }
                if (skippedNegative > 0)
                    report += $"Skipped (takeout > rod):     {skippedNegative}\n";

                report += "\nSection_ID (Hydratec) populated as type(length) — no leading #.";

                TaskDialog.Show("Ring Section IDs (+Hardware)", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Ring Section IDs (+Hardware) failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        private double? LookupTakeoutInches(double nominalInches)
        {
            foreach (var (nom, take) in RingTakeouts)
            {
                if (Math.Abs(nom - nominalInches) < DiameterToleranceInches)
                    return take;
            }
            return null;
        }

        private string FormatFraction(double fraction)
        {
            string fStr = fraction.ToString("F6");
            if (fStr == "0.000000") return "";
            if (fStr == "0.250000") return "¼";
            if (fStr == "0.500000") return "½";
            return "¾";
        }

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
            var p = elem.LookupParameter(paramName);
            if (p == null || !p.HasValue) return null;
            if (p.StorageType == StorageType.String) return p.AsString();
            return p.AsValueString();
        }

        private void SetStringParam(Element elem, string paramName, string value)
        {
            var p = elem.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                p.Set(value);
        }
    }
}
