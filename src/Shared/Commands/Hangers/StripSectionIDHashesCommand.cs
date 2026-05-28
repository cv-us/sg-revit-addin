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
    /// Removes every '#' character from Section_ID (Hydratec) on all selected
    /// hangers. Unlike StripSectionIDTypeCodeCommand (which strips the whole
    /// prefix before the first '(' for a chosen Type Code), this one simply
    /// deletes the hash marks wherever they appear and leaves everything else
    /// intact.
    ///
    /// EXAMPLE:
    ///   "#11T(5)"   →  "11T(5)"
    ///   "#05S(7½)"  →  "05S(7½)"
    ///   "12#R3R¼"   →  "12R3R¼"
    ///
    /// No Type Code filter and no dialog — it operates on every recognised
    /// hanger in the selection. Hangers whose Section_ID has no '#' are left
    /// untouched and tallied as "already clean".
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StripSectionIDHashesCommand : IExternalCommand
    {
        private const string SectionIdParam = "Section_ID (Hydratec)";

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
                var selectedIds = uidoc.Selection.GetElementIds();
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    TaskDialog.Show("Strip # From Section IDs",
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
                    TaskDialog.Show("Strip # From Section IDs",
                        "None of the selected elements are recognised hanger families.\n\n" +
                        "Looking for families containing:\n" +
                        string.Join("\n", HangerFamilyPatterns.Select(p => "  • " + p)));
                    return Result.Cancelled;
                }

                int updated = 0;
                int alreadyClean = 0;
                int noSectionId = 0;
                int skippedReadOnly = 0;

                using (var tw = new TransactionWrapper(doc, "Strip # From Section IDs"))
                {
                    foreach (var hanger in hangers)
                    {
                        var p = hanger.LookupParameter(SectionIdParam);
                        if (p == null || p.StorageType != StorageType.String)
                        {
                            noSectionId++;
                            continue;
                        }

                        string value = p.AsString() ?? "";
                        if (value.Length == 0)
                        {
                            noSectionId++;
                            continue;
                        }

                        if (value.IndexOf('#') < 0)
                        {
                            alreadyClean++;
                            continue;
                        }

                        if (p.IsReadOnly)
                        {
                            skippedReadOnly++;
                            continue;
                        }

                        p.Set(value.Replace("#", ""));
                        updated++;
                    }

                    tw.Commit();
                }

                string report =
                    $"Strip # From Section IDs\n\n" +
                    $"Hangers in selection:          {hangers.Count}\n" +
                    $"Stripped (# removed):          {updated}\n" +
                    $"Already had no #:              {alreadyClean}\n";

                if (noSectionId > 0)
                    report += $"No Section_ID value:           {noSectionId}\n";
                if (skippedReadOnly > 0)
                    report += $"Skipped (Section_ID read-only): {skippedReadOnly}\n";

                TaskDialog.Show("Strip # From Section IDs", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Strip # From Section IDs failed:\n" + ex.Message);
                return Result.Failed;
            }
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
    }
}
