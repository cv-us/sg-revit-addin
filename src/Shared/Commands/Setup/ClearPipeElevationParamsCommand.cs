using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Setup
{
    /// <summary>
    /// Removes the pipe elevation shared parameters from the project.
    /// These six parameters (PipeStartPointTOS, PipeStartPointAFF, PipeMidPointTOS,
    /// PipeMidPointAFF, PipeEndPointTOS, PipeEndPointAFF) are created by
    /// PipeElevationsCommand and bound to the Pipes category. This command
    /// cleans them out when they are no longer needed or before re-setup.
    ///
    /// WORKFLOW:
    ///   1. Enumerate all project parameters bound to OST_PipeCurves
    ///   2. Match against the six known elevation parameter names
    ///   3. Delete the matching SharedParameterElement definitions
    ///   4. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClearPipeElevationParamsCommand : IExternalCommand
    {
        /// <summary>
        /// The six pipe elevation shared parameter names to remove.
        /// </summary>
        private static readonly string[] TargetParamNames = new[]
        {
            "PipeStartPointTOS",
            "PipeStartPointAFF",
            "PipeMidPointTOS",
            "PipeMidPointAFF",
            "PipeEndPointTOS",
            "PipeEndPointAFF"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                // Find SharedParameterElements matching our target names
                var sharedParams = new FilteredElementCollector(doc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>()
                    .ToList();

                var targetSet = new HashSet<string>(TargetParamNames, StringComparer.OrdinalIgnoreCase);

                var toDelete = sharedParams
                    .Where(sp => targetSet.Contains(sp.Name))
                    .ToList();

                if (toDelete.Count == 0)
                {
                    TaskDialog.Show("Clear Pipe Elevation Parameters",
                        "No pipe elevation parameters found in the project.\n\n" +
                        "Parameters searched for:\n" +
                        string.Join("\n", TargetParamNames.Select(n => "  • " + n)));
                    return Result.Succeeded;
                }

                // Confirm before deleting
                var td = new TaskDialog("Clear Pipe Elevation Parameters")
                {
                    MainInstruction = $"Found {toDelete.Count} of {TargetParamNames.Length} pipe elevation parameters.",
                    MainContent = "Parameters to delete:\n" +
                        string.Join("\n", toDelete.Select(sp => "  • " + sp.Name)) +
                        "\n\nThis will remove the parameter definitions and all stored values " +
                        "from every pipe in the project. This cannot be undone.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };

                if (td.Show() != TaskDialogResult.Yes)
                    return Result.Cancelled;

                // Delete within a transaction
                int deleted = 0;
                var errors = new List<string>();

                using (Transaction tx = new Transaction(doc, "Clear Pipe Elevation Parameters"))
                {
                    tx.Start();

                    foreach (var sp in toDelete)
                    {
                        try
                        {
                            doc.Delete(sp.Id);
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{sp.Name}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                // Report
                string report = $"Deleted {deleted} of {toDelete.Count} pipe elevation parameters.";
                if (errors.Count > 0)
                    report += "\n\nErrors:\n" + string.Join("\n", errors);

                TaskDialog.Show("Clear Pipe Elevation Parameters", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

