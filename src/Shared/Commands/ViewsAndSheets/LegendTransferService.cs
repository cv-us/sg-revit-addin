using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using SgRevitAddin.Commands.ViewsAndSheets.Models;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Core transfer logic for the Legend Transfer command. Separated from
    /// the UI so it can be unit-tested or scripted.
    ///
    /// PIPELINE per legend:
    ///   1. Skip if a legend with the same name already exists in target.
    ///   2. Duplicate any existing target legend (View.Duplicate) — this is
    ///      Revit's required workaround since the API has no factory for
    ///      creating Legend views from scratch.
    ///   3. Rename the new view to the source legend's name, set its scale.
    ///   4. Delete the elements duplicated in from the seed (we want an empty
    ///      legend, not a copy of the seed's contents).
    ///   5. Copy the source legend's elements into the new legend via
    ///      ElementTransformUtils.CopyElements with a DuplicateTypeNames
    ///      handler set to "use destination types" (so types that already
    ///      exist in the target aren't re-imported under a "_1" suffix).
    ///
    /// TRANSACTION SHAPE:
    ///   The whole batch is wrapped in a TransactionGroup so the user can
    ///   undo it as a single action. Each legend is its own inner Transaction,
    ///   so one failure doesn't abort the rest — it rolls back just that
    ///   legend and we continue.
    ///
    /// VALIDATION before calling Transfer:
    ///   - source != target
    ///   - target is modifiable
    ///   - target has at least one existing legend (the seed for Duplicate)
    ///   - source has at least one legend (the dialog enforces this already)
    ///
    /// LOGGING:
    ///   Best-effort append to %APPDATA%\SgRevitAddin\LegendTransfer\log.txt.
    ///   Failures to write the log are swallowed; the in-memory result list
    ///   is the authoritative record.
    /// </summary>
    public static class LegendTransferService
    {
        private const string TransactionGroupName = "Transfer Legends";
        private const string LogSubfolder = "SgRevitAddin\\LegendTransfer";
        private const string LogFile = "log.txt";

        /// <summary>
        /// Validation result returned from <see cref="Validate"/>. If
        /// <see cref="IsValid"/> is false, <see cref="ErrorMessage"/> holds
        /// a user-friendly explanation suitable for a TaskDialog.
        /// </summary>
        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static ValidationResult Validate(Document source, Document target)
        {
            if (source == null || target == null)
                return Fail("Both source and target documents must be selected.");
            if (ReferenceEquals(source, target))
                return Fail("Source and target must be different documents.");
            if (target.IsReadOnly)
                return Fail($"Target document \"{target.Title}\" is read-only and can't be modified.");

            int srcLegends = CountLegends(source);
            if (srcLegends == 0)
                return Fail($"Source document \"{source.Title}\" has no legend views to transfer.");

            int tgtLegends = CountLegends(target);
            if (tgtLegends == 0)
            {
                return Fail(
                    $"Target document \"{target.Title}\" has no existing legend views.\n\n" +
                    "Revit's API cannot create a Legend view from scratch — Legend Transfer " +
                    "duplicates an existing legend in the target as a starting point, then " +
                    "renames it and replaces its contents.\n\n" +
                    "Create at least one (empty) legend in the target manually, then re-run.");
            }

            return new ValidationResult { IsValid = true };
        }

        private static ValidationResult Fail(string msg)
            => new ValidationResult { IsValid = false, ErrorMessage = msg };

        private static int CountLegends(Document doc)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(View)).Cast<View>()
                .Count(v => v.ViewType == ViewType.Legend && !v.IsTemplate);

        /// <summary>
        /// Transfers the given legends from source to target. Reports
        /// progress via <paramref name="onProgress"/> (called once per
        /// legend with the index just completed). Returns one
        /// <see cref="TransferResult"/> per requested legend.
        /// </summary>
        public static List<TransferResult> Transfer(
            Document source,
            Document target,
            IList<LegendInfo> legends,
            Action<int, string> onProgress = null)
        {
            var results = new List<TransferResult>();
            if (legends == null || legends.Count == 0) return results;

            // Snapshot existing target legend names for the conflict check.
            var targetNames = new HashSet<string>(
                new FilteredElementCollector(target)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate)
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            // Seed legend (any one will do — Duplicate just gives us a Legend
            // we can rename and clear).
            View seedLegend = new FilteredElementCollector(target)
                .OfClass(typeof(View)).Cast<View>()
                .First(v => v.ViewType == ViewType.Legend && !v.IsTemplate);

            using (var group = new TransactionGroup(target, TransactionGroupName))
            {
                group.Start();

                for (int i = 0; i < legends.Count; i++)
                {
                    LegendInfo info = legends[i];
                    var result = new TransferResult { LegendName = info.Name };
                    results.Add(result);
                    onProgress?.Invoke(i, info.Name);

                    // ── Conflict check (no transaction needed) ──
                    if (targetNames.Contains(info.Name))
                    {
                        result.Status = TransferStatus.Skipped;
                        result.Reason = "A legend with this name already exists in the target.";
                        continue;
                    }

                    // ── Per-legend transaction ──
                    using (var tx = new Transaction(target, $"Transfer legend: {info.Name}"))
                    {
                        try
                        {
                            tx.Start();
                            TransferOne(source, target, info, seedLegend);
                            tx.Commit();
                            targetNames.Add(info.Name);
                            result.Status = TransferStatus.Success;
                        }
                        catch (Exception ex)
                        {
                            if (tx.HasStarted() && !tx.HasEnded())
                                tx.RollBack();
                            result.Status = TransferStatus.Failed;
                            result.Reason = ex.Message;
                        }
                    }
                }

                onProgress?.Invoke(legends.Count, null);
                group.Assimilate();
            }

            TryAppendLog(source, target, results);
            return results;
        }

        /// <summary>
        /// Does the work for one legend inside an already-started transaction:
        /// duplicate the seed, rename, set scale, clear duplicated content,
        /// copy in the source elements.
        /// </summary>
        private static void TransferOne(
            Document source, Document target,
            LegendInfo info, View seedLegend)
        {
            // 1. Duplicate the seed legend.
            ElementId dupId = seedLegend.Duplicate(ViewDuplicateOption.Duplicate);
            if (dupId == null || dupId == ElementId.InvalidElementId)
                throw new InvalidOperationException("Failed to duplicate the seed legend.");
            View newLegend = target.GetElement(dupId) as View
                ?? throw new InvalidOperationException("Duplicated id did not resolve to a View.");

            // 2. Rename + rescale.
            newLegend.Name = info.Name;
            if (info.Scale > 0) newLegend.Scale = info.Scale;

            // 3. Empty the duplicated view (it carries the seed's elements).
            var existingIds = new FilteredElementCollector(target, newLegend.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            if (existingIds.Count > 0)
                target.Delete(existingIds);

            // 4. Collect source elements and copy them across.
            var sourceIds = new FilteredElementCollector(source, info.Legend.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();

            if (sourceIds.Count > 0)
            {
                using (var opts = new CopyPasteOptions())
                {
                    opts.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
                    ElementTransformUtils.CopyElements(
                        info.Legend,
                        sourceIds,
                        newLegend,
                        Transform.Identity,
                        opts);
                }
            }
        }

        /// <summary>
        /// On any type-name conflict during CopyElements, reuse the existing
        /// type in the destination doc. Matches Revit's default "Use
        /// destination types" prompt behaviour without showing a modal.
        /// </summary>
        private class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
                => DuplicateTypeAction.UseDestinationTypes;
        }

        // ── Optional file log ──

        private static void TryAppendLog(Document source, Document target, List<TransferResult> results)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, LogSubfolder);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, LogFile);

                var lines = new List<string>
                {
                    "─────────────────────────────────────────────────",
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  Legend Transfer",
                    $"  Source: {source.Title}",
                    $"  Target: {target.Title}",
                    $"  Requested: {results.Count}    " +
                        $"OK: {results.Count(r => r.Status == TransferStatus.Success)}    " +
                        $"Skipped: {results.Count(r => r.Status == TransferStatus.Skipped)}    " +
                        $"Failed: {results.Count(r => r.Status == TransferStatus.Failed)}"
                };
                foreach (var r in results)
                {
                    string tag = r.Status.ToString().PadRight(8);
                    lines.Add($"  [{tag}] {r.LegendName}" +
                              (string.IsNullOrEmpty(r.Reason) ? "" : "  — " + r.Reason));
                }
                File.AppendAllLines(path, lines);
            }
            catch
            {
                // Log writes are best-effort; never let logging failures
                // affect the transfer result the user sees.
            }
        }
    }
}
