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
    /// Bulk-changes the Type Code (Hydratec) parameter on selected hangers
    /// from one value to another. Only hangers whose current Type Code
    /// matches the chosen "from" value are touched — the rest are left
    /// alone so a mixed selection can be processed in one pass.
    ///
    /// WORKFLOW:
    ///   1. User pre-selects hangers (no pick prompt).
    ///   2. Filter to recognised hanger families.
    ///   3. Pre-scan to collect distinct Type Code values present in the
    ///      selection — the dialog only offers codes that actually exist
    ///      so the user can't pick a no-op "from" value.
    ///   4. Dialog: choose "from" code from a dropdown, type the "to" code.
    ///   5. For each matching hanger, write the new code to Type Code
    ///      (Hydratec). Comparison is case-insensitive with whitespace
    ///      trimmed; the new value is written verbatim (preserves the
    ///      user's chosen casing).
    ///   6. Report counts.
    ///
    /// HANGER RECOGNITION:
    ///   Same set of family-name substrings as the other hanger commands —
    ///   "-Pipe Hanger", "-Pipe Trapeze", "Adjustable Ring Hanger". The
    ///   PipeAccessory category guard rules out tag families that happen
    ///   to match those substrings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ChangeTypeCodeCommand : IExternalCommand
    {
        private const string TypeCodeParam = "Type Code (Hydratec)";

        // Family-name substrings recognised as a hanger. Same list as the
        // sibling Hangers commands.
        private static readonly string[] HangerFamilyPatterns =
        {
            "-Pipe Hanger",
            "-Pipe Trapeze",
            "-Basic Adjustable",
            "Adjustable Ring Hanger",
            "Ring Hanger"
        };

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
                    TaskDialog.Show("Change Type Code",
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
                    TaskDialog.Show("Change Type Code",
                        "None of the selected elements are recognised hanger families.\n\n" +
                        "Looking for families containing:\n" +
                        string.Join("\n", HangerFamilyPatterns.Select(p => "  • " + p)));
                    return Result.Cancelled;
                }

                // ── Pre-scan distinct Type Codes present in the selection ──
                // The dialog populates its "From" dropdown from this set so
                // we don't offer codes the user has no reason to pick.
                var availableCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                int hangersWithNoCode = 0;

                foreach (var hanger in hangers)
                {
                    string code = GetStringParam(hanger, TypeCodeParam)?.Trim();
                    if (string.IsNullOrEmpty(code))
                        hangersWithNoCode++;
                    else
                        availableCodes.Add(code);
                }

                if (availableCodes.Count == 0)
                {
                    TaskDialog.Show("Change Type Code",
                        $"None of the {hangers.Count} selected hangers have a " +
                        $"\"{TypeCodeParam}\" value to swap from.\n\n" +
                        "Run \"Section IDs\" or set the parameter manually first.");
                    return Result.Cancelled;
                }

                // ── Show dialog ──
                string fromCode;
                string toCode;
                using (var dlg = new ChangeTypeCodeDialog(
                    hangers.Count, hangersWithNoCode, availableCodes.ToList()))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    fromCode = dlg.FromCode?.Trim() ?? "";
                    toCode = dlg.ToCode?.Trim() ?? "";
                }

                if (string.IsNullOrEmpty(fromCode) || string.IsNullOrEmpty(toCode))
                {
                    TaskDialog.Show("Change Type Code",
                        "Both From and To Type Code values are required.");
                    return Result.Cancelled;
                }

                if (string.Equals(fromCode, toCode, StringComparison.OrdinalIgnoreCase))
                {
                    TaskDialog.Show("Change Type Code",
                        "From and To codes are the same — nothing to change.");
                    return Result.Cancelled;
                }

                // ── Apply the change ──
                int updated = 0;
                int unchanged = 0;
                int skippedReadOnly = 0;

                using (var tw = new TransactionWrapper(doc, "Change Type Code"))
                {
                    foreach (var hanger in hangers)
                    {
                        string current = GetStringParam(hanger, TypeCodeParam)?.Trim() ?? "";
                        if (!string.Equals(current, fromCode, StringComparison.OrdinalIgnoreCase))
                        {
                            unchanged++;
                            continue;
                        }

                        var p = hanger.LookupParameter(TypeCodeParam);
                        if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                        {
                            skippedReadOnly++;
                            continue;
                        }

                        p.Set(toCode);
                        updated++;
                    }

                    tw.Commit();
                }

                // ── Report ──
                string report =
                    $"Change Type Code\n\n" +
                    $"Hangers in selection:           {hangers.Count}\n" +
                    $"Matched \"{fromCode}\" → \"{toCode}\": {updated}\n" +
                    $"Other type codes (unchanged):   {unchanged}\n";

                if (hangersWithNoCode > 0)
                    report += $"No Type Code (unchanged):       {hangersWithNoCode}\n";
                if (skippedReadOnly > 0)
                    report += $"Skipped (parameter read-only):  {skippedReadOnly}\n";

                TaskDialog.Show("Change Type Code", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Change Type Code failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ── Helpers ──

        private bool IsHanger(FamilyInstance fi)
        {
            // PipeAccessory category guard — keeps tag families with
            // matching names out of the selection.
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
