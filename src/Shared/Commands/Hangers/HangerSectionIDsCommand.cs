using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Populates the "Section_ID (Hydratec)" parameter on selected pipe hangers
    /// with a formatted string combining the rod length and type code.
    ///
    /// Format: (LENGTH#TYPECODE_FRACTION)
    /// Example: (12#R3R¼)  →  12¼" rod, type code R3R
    ///
    /// WORKFLOW:
    ///   1. User selects pipe accessories (hangers)
    ///   2. For each hanger:
    ///      a. Read "Rod Length" (feet → convert to inches, round to nearest ¼")
    ///      b. Read "Type Code (Hydratec)"
    ///      c. Build formatted Section ID string
    ///      d. Write to "Section_ID (Hydratec)" parameter
    ///
    /// This value displays in hanger tags and auto-updates with cut lengths
    /// when the AutoList process has been run.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangerSectionIDsCommand : IExternalCommand
    {
        // Hanger family name substrings used to identify valid hangers
        private static readonly string[] HangerFamilyPatterns = new[]
        {
            "-Pipe Hanger",
            "-Pipe Trapeze",
            "Adjustable Ring Hanger"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Select pipe accessories ──
                IList<Reference> accessoryRefs;
                try
                {
                    accessoryRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new CategorySelectionFilter(BuiltInCategory.OST_PipeAccessory),
                        "Select pipe hangers to populate Section IDs, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (accessoryRefs == null || accessoryRefs.Count == 0)
                {
                    TaskDialog.Show("Hanger Section IDs", "No pipe accessories selected.");
                    return Result.Cancelled;
                }

                // ── Step 2: Filter to recognized hanger families ──
                var hangers = new List<FamilyInstance>();
                foreach (var r in accessoryRefs)
                {
                    var fi = doc.GetElement(r) as FamilyInstance;
                    if (fi == null) continue;

                    string familyName = fi.Symbol?.Family?.Name ?? "";
                    if (IsHangerFamily(familyName))
                        hangers.Add(fi);
                }

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Hanger Section IDs",
                        "None of the selected elements are recognized hanger families.\n\n" +
                        "Looking for families containing:\n" +
                        string.Join("\n", HangerFamilyPatterns.Select(p => "  • " + p)));
                    return Result.Cancelled;
                }

                // ── Step 3: Process and write Section IDs ──
                int updated = 0;
                int skipped = 0;

                using (var tw = new TransactionWrapper(doc, "Insert Hanger Section IDs"))
                {
                    foreach (var hanger in hangers)
                    {
                        // Read Rod Length (internal units = feet)
                        double? rodLengthFeet = GetDoubleParam(hanger, "Rod Length");
                        if (!rodLengthFeet.HasValue || rodLengthFeet.Value <= 0)
                        {
                            skipped++;
                            continue;
                        }

                        // Read Type Code
                        string typeCode = GetStringParam(hanger, "Type Code (Hydratec)") ?? "";

                        // Convert rod length to inches, round to nearest quarter-inch
                        double lengthInches = rodLengthFeet.Value * 12.0;
                        double roundedInches = Math.Round(lengthInches / 0.25) * 0.25;

                        // Split into whole and fractional parts
                        int wholePart = (int)Math.Floor(roundedInches);
                        double fraction = roundedInches - wholePart;

                        // Build formatted string: (WHOLE#TYPECODE_FRACTION)
                        string fractionStr = FormatFraction(fraction);
                        string sectionId = "(" + wholePart + "#" + typeCode + fractionStr + ")";

                        // Write to Section_ID (Hydratec)
                        SetStringParam(hanger, "Section_ID (Hydratec)", sectionId);
                        updated++;
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Hanger Section IDs",
                    $"Completed:\n" +
                    $"  {updated} hanger(s) updated\n" +
                    (skipped > 0 ? $"  {skipped} skipped (no Rod Length value)\n" : "") +
                    $"\nSection_ID (Hydratec) parameter populated.\n" +
                    "This value displays in hanger tags and auto-updates\nwith cut lengths when AutoList is run.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Hanger Section IDs failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Checks if a family name matches any of the recognized hanger patterns.
        /// </summary>
        private bool IsHangerFamily(string familyName)
        {
            foreach (var pattern in HangerFamilyPatterns)
            {
                if (familyName.Contains(pattern))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Formats a fractional inch value as a Unicode fraction character.
        /// Returns empty string for whole inches, or the fraction + closing paren.
        /// </summary>
        private string FormatFraction(double fraction)
        {
            // Normalize to avoid floating-point comparison issues
            string fStr = fraction.ToString("F6");

            if (fStr == "0.000000")
                return "";       // Whole number — no fraction
            else if (fStr == "0.250000")
                return "\u00BC"; // ¼
            else if (fStr == "0.500000")
                return "\u00BD"; // ½
            else
                return "\u00BE"; // ¾
        }

        /// <summary>
        /// Reads a double parameter by name. Returns null if not found or empty.
        /// </summary>
        private double? GetDoubleParam(Element elem, string paramName)
        {
            var param = elem.LookupParameter(paramName);
            if (param == null || !param.HasValue) return null;

            if (param.StorageType == StorageType.Double)
                return param.AsDouble();
            if (param.StorageType == StorageType.Integer)
                return param.AsInteger();

            // Try parsing string value
            string val = param.AsString();
            if (!string.IsNullOrEmpty(val) && double.TryParse(val, out double d))
                return d;

            return null;
        }

        /// <summary>
        /// Reads a string parameter by name. Returns null if not found.
        /// </summary>
        private string GetStringParam(Element elem, string paramName)
        {
            var param = elem.LookupParameter(paramName);
            if (param == null || !param.HasValue) return null;

            if (param.StorageType == StorageType.String)
                return param.AsString();

            return param.AsValueString();
        }

        /// <summary>
        /// Sets a string parameter by name, silently skipping if not found.
        /// </summary>
        private void SetStringParam(Element elem, string paramName, string value)
        {
            var param = elem.LookupParameter(paramName);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                param.Set(value);
        }
    }
}
