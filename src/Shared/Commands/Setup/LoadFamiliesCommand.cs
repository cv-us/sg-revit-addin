using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SSG_FP_Suite.Commands.Setup
{
    /// <summary>
    /// Loads custom Revit family (.rfa) files from a specified folder into the
    /// current project. Families that are already loaded (by name) are skipped.
    ///
    /// Migrated from: "! Setup - Load Custom Families.dyn" (V02)
    ///
    /// WORKFLOW:
    ///   1. Dialog: pick folder (defaults to C:\BIM Support\Revit\Families\{version})
    ///   2. Enumerate all .rfa files (optionally recursive)
    ///   3. Check which families are already loaded by name
    ///   4. Load missing families via doc.LoadFamily()
    ///   5. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadFamiliesCommand : IExternalCommand
    {
        /// <summary>
        /// Base path used by legacy Dynamo script for family storage.
        /// </summary>
        private const string DefaultBasePath = @"C:\BIM Support\Revit\Families\";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Detect Revit version for default subfolder ──
                string revitVersion = commandData.Application.Application.VersionNumber;
                int versionNum;
                int.TryParse(revitVersion, out versionNum);
                string versionSubfolder = versionNum >= 2021 ? "2021" : "2017";
                string defaultFolder = DefaultBasePath + versionSubfolder;

                // ── Show dialog ──
                using (var dlg = new LoadFamiliesDialog(defaultFolder))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    string folderPath = dlg.FolderPath;
                    bool includeSubfolders = dlg.IncludeSubfolders;

                    if (!Directory.Exists(folderPath))
                    {
                        TaskDialog.Show("Load Families",
                            $"Folder not found:\n{folderPath}");
                        return Result.Failed;
                    }

                    // ── Enumerate .rfa files ──
                    SearchOption searchOpt = includeSubfolders
                        ? SearchOption.AllDirectories
                        : SearchOption.TopDirectoryOnly;

                    string[] rfaFiles = Directory.GetFiles(folderPath, "*.rfa", searchOpt);

                    if (rfaFiles.Length == 0)
                    {
                        TaskDialog.Show("Load Families",
                            $"No .rfa files found in:\n{folderPath}");
                        return Result.Succeeded;
                    }

                    // ── Get already-loaded family names ──
                    HashSet<string> loadedFamilyNames = new HashSet<string>(
                        new FilteredElementCollector(doc)
                            .OfClass(typeof(Family))
                            .Cast<Family>()
                            .Select(f => f.Name),
                        StringComparer.OrdinalIgnoreCase);

                    // ── Load families ──
                    int loaded = 0;
                    int skipped = 0;
                    int failed = 0;
                    var failedNames = new List<string>();

                    using (var tw = new TransactionWrapper(doc, "Load Custom Families"))
                    {
                        foreach (string rfaPath in rfaFiles)
                        {
                            string familyName = Path.GetFileNameWithoutExtension(rfaPath);

                            // Skip if already loaded (matches Dynamo Clockwork behavior)
                            if (loadedFamilyNames.Contains(familyName))
                            {
                                skipped++;
                                continue;
                            }

                            try
                            {
                                Family loadedFamily;
                                bool success = doc.LoadFamily(rfaPath, out loadedFamily);

                                if (success)
                                {
                                    loaded++;
                                    // Add to set so duplicates in different subfolders don't re-attempt
                                    loadedFamilyNames.Add(familyName);
                                }
                                else
                                {
                                    // LoadFamily returns false if already loaded (belt + suspenders)
                                    skipped++;
                                }
                            }
                            catch (Exception)
                            {
                                failed++;
                                failedNames.Add(familyName);
                            }
                        }

                        tw.Commit();
                    }

                    // ── Summary ──
                    string summary = $"Load Custom Families Complete:\n\n" +
                                     $"Folder: {folderPath}\n" +
                                     $"Files found: {rfaFiles.Length}\n\n" +
                                     $"Loaded: {loaded} new famil{(loaded != 1 ? "ies" : "y")}\n" +
                                     $"Already loaded: {skipped}";

                    if (failed > 0)
                    {
                        summary += $"\nFailed: {failed}";
                        if (failedNames.Count <= 10)
                            summary += "\n  " + string.Join("\n  ", failedNames);
                    }

                    TaskDialog.Show("Load Custom Families", summary);

                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
