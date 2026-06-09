using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Commands.ViewsAndSheets.Models;
using SgRevitAddin.Commands.ViewsAndSheets.ViewModels;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Entry point for the Legend Transfer command. Gathers the currently
    /// open Revit documents, opens the WPF dialog so the user can choose
    /// source + target + which legends to transfer, then dispatches the work
    /// to <see cref="LegendTransferService"/> and shows a results dialog.
    ///
    /// The modal WPF window blocks Revit's UI thread, so the service runs on
    /// the API thread — no <c>ExternalEvent</c> needed. Progress updates use
    /// <see cref="Dispatcher"/> to flush the message queue between legends so
    /// the progress bar can repaint.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LegendTransferCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document activeDoc = uiapp.ActiveUIDocument?.Document;

            try
            {
                // ── Gather open documents ──
                var openDocs = new List<DocumentInfo>();
                foreach (Document d in uiapp.Application.Documents)
                {
                    if (d == null || d.IsFamilyDocument || d.IsLinked) continue;
                    openDocs.Add(new DocumentInfo(d));
                }

                if (openDocs.Count < 2)
                {
                    TaskDialog.Show("Legend Transfer",
                        "At least two project documents must be open in this Revit " +
                        "session — one to copy from and one to copy to.\n\n" +
                        $"Currently open: {openDocs.Count} project document" +
                        $"{(openDocs.Count == 1 ? "" : "s")}.\n\n" +
                        "Open the legend-library file in this same Revit session and " +
                        "re-run the command.");
                    return Result.Cancelled;
                }

                var activeInfo = openDocs.FirstOrDefault(d => ReferenceEquals(d.Document, activeDoc));

                // ── Show dialog ──
                var vm = new LegendTransferViewModel(openDocs, activeInfo);
                var window = new LegendTransferWindow(vm);
                window.OwnedByRevit();

                // Intercept the Transfer click so we can run the service
                // while the dialog is still on screen (for progress updates),
                // then close it afterward.
                bool transferred = false;
                vm.TransferRequested += picked =>
                {
                    transferred = true;
                    RunTransfer(vm, picked);
                    window.CloseAsTransferred();
                };

                window.ShowDialog();
                return transferred ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Legend Transfer", "Legend Transfer failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ── Service dispatch + progress wiring ──

        private static void RunTransfer(LegendTransferViewModel vm, IList<LegendInfo> picked)
        {
            Document sourceDoc = vm.SelectedSourceDoc?.Document;
            Document targetDoc = vm.SelectedTargetDoc?.Document;

            // Re-validate at run time (defensive — the VM disables Transfer
            // when these conditions aren't met, but documents could in theory
            // close between dialog open and click).
            var v = LegendTransferService.Validate(sourceDoc, targetDoc);
            if (!v.IsValid)
            {
                TaskDialog.Show("Legend Transfer", v.ErrorMessage);
                return;
            }

            vm.IsTransferring = true;
            vm.ProgressMaximum = picked.Count;
            vm.ProgressValue = 0;
            vm.ProgressLabel = "Starting…";
            DoEvents();

            List<TransferResult> results;
            try
            {
                results = LegendTransferService.Transfer(
                    sourceDoc, targetDoc, picked,
                    (index, currentName) =>
                    {
                        vm.ProgressValue = index;
                        vm.ProgressLabel = currentName == null
                            ? "Finishing up…"
                            : $"({index + 1}/{picked.Count})  {currentName}";
                        DoEvents();
                    });
            }
            finally
            {
                vm.IsTransferring = false;
            }

            ShowSummary(sourceDoc, targetDoc, results);
        }

        /// <summary>
        /// Pump pending UI messages so the progress bar repaints between
        /// legends. The whole command runs on Revit's API thread, so this is
        /// the WPF equivalent of WinForms' Application.DoEvents().
        /// </summary>
        private static void DoEvents()
        {
            Dispatcher.CurrentDispatcher.Invoke(
                () => { }, DispatcherPriority.Background);
        }

        // ── Summary dialog ──

        private static void ShowSummary(Document src, Document tgt, List<TransferResult> results)
        {
            int ok = results.Count(r => r.Status == TransferStatus.Success);
            int skipped = results.Count(r => r.Status == TransferStatus.Skipped);
            int failed = results.Count(r => r.Status == TransferStatus.Failed);

            var sb = new StringBuilder();
            sb.AppendLine($"Source:  {src.Title}");
            sb.AppendLine($"Target:  {tgt.Title}");
            sb.AppendLine();
            sb.AppendLine($"Transferred: {ok}");
            sb.AppendLine($"Skipped:     {skipped}");
            sb.AppendLine($"Failed:      {failed}");

            if (skipped > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Skipped:");
                foreach (var r in results.Where(r => r.Status == TransferStatus.Skipped))
                    sb.AppendLine($"  • {r.LegendName} — {r.Reason}");
            }
            if (failed > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Failed:");
                foreach (var r in results.Where(r => r.Status == TransferStatus.Failed))
                    sb.AppendLine($"  • {r.LegendName} — {r.Reason}");
            }

            sb.AppendLine();
            sb.AppendLine(
                @"A log was appended to %APPDATA%\SgRevitAddin\LegendTransfer\log.txt");

            var td = new TaskDialog("Legend Transfer — Complete")
            {
                MainInstruction = failed == 0
                    ? (ok > 0 ? $"Transferred {ok} legend{(ok == 1 ? "" : "s")}." : "Nothing transferred.")
                    : $"Completed with {failed} failure{(failed == 1 ? "" : "s")}.",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            td.Show();
        }
    }
}
