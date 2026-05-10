using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SgRevitAddin.Commands.Setup
{
    /// <summary>
    /// Loads custom Revit family (.rfa) files from a specified folder into the
    /// current project. Families that are already loaded (by name) are skipped.
    ///
    /// WORKFLOW:
    ///   1. Dialog: pick folder (defaults to C:\SG\Revit Families\{version})
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
        /// Base path for family storage.
        /// </summary>
        private const string DefaultBasePath = @"C:\SG\Revit Families\";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Detect Revit version for default subfolder ──
                // Families are saved per Revit year. Revit can upgrade older-year
                // families on load, but cannot open newer-year ones. So we pick
                // the year folder that exactly matches the running Revit version,
                // falling back to the newest available folder <= the Revit year.
                string revitVersion = commandData.Application.Application.VersionNumber;
                int versionNum;
                int.TryParse(revitVersion, out versionNum);
                string defaultFolder = ResolveVersionFolder(DefaultBasePath, versionNum);

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

                            // Skip if already loaded
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

        /// <summary>
        /// Picks the best year subfolder under <paramref name="basePath"/> for the
        /// given Revit version. Prefers an exact-year match, otherwise falls back
        /// to the highest-numbered year folder that is &lt;= the Revit version
        /// (so we never hand a newer-format family to an older Revit).
        ///
        /// If no year folders are present, returns the base path itself so the
        /// user can still browse and pick manually.
        /// </summary>
        private static string ResolveVersionFolder(string basePath, int revitYear)
        {
            if (!Directory.Exists(basePath))
                return basePath;

            // Enumerate subfolders whose name is a 4-digit year
            var yearFolders = new List<int>();
            foreach (var dir in Directory.GetDirectories(basePath))
            {
                string name = Path.GetFileName(dir);
                if (name.Length == 4 && int.TryParse(name, out int y))
                    yearFolders.Add(y);
            }

            if (yearFolders.Count == 0)
                return basePath;

            yearFolders.Sort();

            // Exact match?
            if (yearFolders.Contains(revitYear))
                return Path.Combine(basePath, revitYear.ToString());

            // Highest folder <= revitYear (safe: Revit can upgrade older families)
            int best = -1;
            foreach (int y in yearFolders)
            {
                if (y <= revitYear && y > best)
                    best = y;
            }
            if (best > 0)
                return Path.Combine(basePath, best.ToString());

            // All folders are newer than the running Revit - fall back to the
            // lowest-numbered one and let Revit reject incompatible families
            // with a clear error rather than us guessing.
            return Path.Combine(basePath, yearFolders[0].ToString());
        }
    }
}

