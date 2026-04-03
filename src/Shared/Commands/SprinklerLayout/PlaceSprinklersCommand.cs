using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SSG_FP_Suite.Commands.SprinklerLayout
{
    /// <summary>
    /// STUB COMMAND — Place sprinkler heads in rooms based on coverage rules.
    ///
    /// This is a template showing the standard command pattern.
    /// Replace the TaskDialog placeholder with your actual logic.
    ///
    /// EXAMPLE WORKFLOW this command could implement:
    ///   1. User selects rooms or an area
    ///   2. Command reads the hazard classification (light, ordinary, etc.)
    ///   3. Looks up max coverage per head from NFPA 13 tables
    ///   4. Calculates a grid of sprinkler positions
    ///   5. Places sprinkler family instances at each position
    ///
    /// HOW TO USE UTILS in your command:
    ///   using SSG_FP_Suite.Utils;
    ///
    ///   // Safe transaction handling
    ///   using (var tw = new TransactionWrapper(doc, "Place Sprinklers"))
    ///   {
    ///       // ... place sprinklers ...
    ///       tw.Commit();
    ///   }
    ///
    ///   // Get existing sprinklers to check for conflicts
    ///   var existing = ElementFilters.GetSprinklersInView(doc, doc.ActiveView.Id);
    ///
    ///   // Convert display units
    ///   double spacingFeet = UnitConversion.InchesToFeet(spacingInches);
    /// </summary>
    [Transaction(TransactionMode.Manual)]   // Manual = this command modifies the model
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceSprinklersCommand : IExternalCommand
    {
        /// <summary>
        /// Entry point — Revit calls this when the user clicks the ribbon button.
        /// </summary>
        /// <param name="commandData">Access to the Revit application, active document, etc.</param>
        /// <param name="message">Set this to an error message if returning Result.Failed</param>
        /// <param name="elements">Add elements here to highlight them on failure</param>
        /// <returns>Result.Succeeded, Result.Failed, or Result.Cancelled</returns>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // ── Step 1: Get the active document ──
            // commandData.Application.ActiveUIDocument = the UI-level document (for selection, view control)
            // uidoc.Document = the database-level document (for querying/modifying elements)
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Step 2: Your command logic goes here ──
            // TODO: Replace this placeholder with actual sprinkler placement logic

            TaskDialog.Show("SSG FP Suite", "Place Sprinklers command - not yet implemented.");

            // ── Step 3: Return result ──
            return Result.Succeeded;
        }
    }
}
