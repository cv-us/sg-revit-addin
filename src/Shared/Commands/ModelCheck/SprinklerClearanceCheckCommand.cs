using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SSG_FP_Suite.Commands.ModelCheck
{
    /// <summary>
    /// STUB COMMAND — Check upright sprinkler deflector-to-ceiling clearances.
    ///
    /// This is a READ-ONLY command (QA/QC check). It inspects the model and
    /// reports issues but does NOT modify anything. That's why it uses
    /// TransactionMode.ReadOnly instead of Manual.
    ///
    /// EXAMPLE WORKFLOW this command could implement:
    ///   1. Collect all upright sprinklers in the active view
    ///   2. For each sprinkler, raybounce upward to find the ceiling/structure
    ///   3. Measure the distance from deflector to ceiling
    ///   4. Flag any that are outside NFPA 13 range (1" to 12" for unobstructed)
    ///   5. Show results in a dialog or highlight problem elements
    ///
    /// NOTE: ModelCheck commands use [Transaction(TransactionMode.ReadOnly)].
    /// This tells Revit the command won't change anything, which is slightly
    /// faster and prevents accidental modifications.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]   // ReadOnly = this command only reads, never modifies
    [Regeneration(RegenerationOption.Manual)]
    public class SprinklerClearanceCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // TODO: Replace with actual clearance check logic

            TaskDialog.Show("SSG FP Suite", "Sprinkler Clearance Check command - not yet implemented.");

            return Result.Succeeded;
        }
    }
}
