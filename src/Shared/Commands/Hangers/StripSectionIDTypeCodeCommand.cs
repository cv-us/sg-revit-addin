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
    /// Strips the Type Code prefix from <c>Section_ID (Hydratec)</c> on
    /// selected hangers whose <c>Type Code (Hydratec)</c> matches a
    /// user-chosen value.
    ///
    /// EXAMPLE:
    ///   Selection contains hangers with Section_ID values like
    ///   "#11T(5)", "#11T(7½)", "#03A(12)". User picks Type Code "11T".
    ///   Result:
    ///     "#11T(5)"   →  "(5)"
    ///     "#11T(7½)"  →  "(7½)"
    ///     "#03A(12)"  →  (unchanged — different Type Code)
    ///
    /// PARSING:
    ///   Everything before the FIRST '(' is removed; the rest is kept
    ///   verbatim. If a hanger's Section_ID has no '(' at all it's
    ///   tallied under "no parenthesis to keep" and left alone.
    ///
    /// HANGER RECOGNITION:
    ///   Same family-name substring set as the other Hangers commands,
    ///   guarded by the PipeAccessory category.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StripSectionIDTypeCodeCommand : IExternalCommand
    {
        private const string TypeCodeParam = "Type Code (Hydratec)";
        private const string SectionIdParam = "Section_ID (Hydratec)";

        // Family-name substrings recognised as a hanger.
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
                    TaskDialog.Show("Strip Section ID Type Code",
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
                    TaskDialog.Show("Strip Section ID Type Code",
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
                    TaskDialog.Show("Strip Section ID Type Code",
                        $"None of the {hangers.Count} selected hangers have a " +
                        $"\"{TypeCodeParam}\" value.\n\n" +
                        "Run \"Section IDs\" or set Type Code (Hydratec) manually first.");
                    return Result.Cancelled;
                }

                // ── Show dialog ──
                string targetCode;
                using (var dlg = new StripSectionIDTypeCodeDialog(
                    hangers.Count, availableCodes.ToList()))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;
                    targetCode = dlg.TypeCode?.Trim() ?? "";
                }

                if (string.IsNullOrEmpty(targetCode))
                {
                    TaskDialog.Show("Strip Section ID Type Code", "No Type Code selected.");
                    return Result.Cancelled;
                }

                // ── Apply ──
                int updated = 0;
                int alreadyStripped = 0;
                int otherTypeCode = 0;
                int noSectionId = 0;
                int noParen = 0;
                int skippedReadOnly = 0;

                using (var tw = new TransactionWrapper(doc, "Strip Section ID Type Code"))
                {
                    foreach (var hanger in hangers)
                    {
                        string current = GetStringParam(hanger, TypeCodeParam)?.Trim() ?? "";
                        if (!string.Equals(current, targetCode, StringComparison.OrdinalIgnoreCase))
                        {
                            otherTypeCode++;
                            continue;
                        }

                        var sectionParam = hanger.LookupParameter(SectionIdParam);
                        if (sectionParam == null || sectionParam.StorageType != StorageType.String)
                        {
                            noSectionId++;
                            continue;
                        }

                        string sectionValue = sectionParam.AsString() ?? "";
                        if (string.IsNullOrEmpty(sectionValue))
                        {
                            noSectionId++;
                            continue;
                        }

                        int parenIdx = sectionValue.IndexOf('(');
                        if (parenIdx < 0)
                        {
                            // No '(' at all — nothing to keep after the strip.
                            noParen++;
                            continue;
                        }

                        if (parenIdx == 0)
                        {
                            // Already starts with '(' — prefix already stripped.
                            alreadyStripped++;
                            continue;
                        }

                        string stripped = sectionValue.Substring(parenIdx);

                        if (sectionParam.IsReadOnly)
                        {
                            skippedReadOnly++;
                            continue;
                        }

                        sectionParam.Set(stripped);
                        updated++;
                    }

                    tw.Commit();
                }

                // ── Report ──
                string report =
                    $"Strip Section ID Type Code\n\n" +
                    $"Hangers in selection:           {hangers.Count}\n" +
                    $"Type Code \"{targetCode}\":\n" +
                    $"  Stripped:                     {updated}\n" +
                    $"  Already stripped:             {alreadyStripped}\n";

                if (otherTypeCode > 0)
                    report += $"Other type codes (left alone): {otherTypeCode}\n";
                if (noSectionId > 0)
                    report += $"No Section_ID value:           {noSectionId}\n";
                if (noParen > 0)
                    report += $"No '(' in Section_ID:          {noParen}\n";
                if (skippedReadOnly > 0)
                    report += $"Skipped (Section_ID read-only): {skippedReadOnly}\n";

                TaskDialog.Show("Strip Section ID Type Code", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Strip Section ID Type Code failed:\n" + ex.Message);
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
