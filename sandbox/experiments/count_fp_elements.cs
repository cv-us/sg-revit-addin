/*
 * count_fp_elements.cs — Basic Fire Protection Element Counter
 *
 * A simple first macro that counts pipes, sprinklers, and fittings
 * in the active view and shows the results in a dialog.
 *
 * Good for verifying:
 *   - The macro environment works
 *   - You can access FP elements
 *   - FilteredElementCollector patterns work
 *
 * TO RUN:
 *   1. Open a Revit project that has fire protection elements
 *   2. Manage → Macro Manager → Application → create/select module → Edit
 *   3. Paste this code → Build → Close editor
 *   4. Select CountFPElements → Run
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

public partial class ThisApplication
{
    public void CountFPElements()
    {
        UIDocument uidoc = this.ActiveUIDocument;
        Document doc = uidoc.Document;
        View activeView = doc.ActiveView;

        // ── Count pipes in the active view ──
        var pipes = new FilteredElementCollector(doc, activeView.Id)
            .OfClass(typeof(Pipe))
            .Cast<Pipe>()
            .ToList();

        // ── Count sprinkler heads ──
        var sprinklers = new FilteredElementCollector(doc, activeView.Id)
            .OfCategory(BuiltInCategory.OST_Sprinklers)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        // ── Count pipe fittings (tees, elbows, reducers) ──
        var fittings = new FilteredElementCollector(doc, activeView.Id)
            .OfCategory(BuiltInCategory.OST_PipeFitting)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        // ── Count pipe accessories (hangers, valves, etc.) ──
        var accessories = new FilteredElementCollector(doc, activeView.Id)
            .OfCategory(BuiltInCategory.OST_PipeAccessory)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        // ── Summarize pipe sizes ──
        var pipeSizes = pipes
            .GroupBy(p => Math.Round(p.Diameter * 12, 2))  // convert feet → inches, group by size
            .OrderBy(g => g.Key)
            .Select(g => $"  {g.Key}\" — {g.Count()} pipes")
            .ToList();

        string pipeSizeSummary = pipeSizes.Any()
            ? "\n\nPipe sizes:\n" + string.Join("\n", pipeSizes)
            : "";

        // ── Summarize sprinkler types ──
        var headTypes = sprinklers
            .GroupBy(s => s.Symbol.Name)
            .Select(g => $"  {g.Key} — {g.Count()}")
            .ToList();

        string headSummary = headTypes.Any()
            ? "\n\nSprinkler types:\n" + string.Join("\n", headTypes)
            : "";

        // ── Show results ──
        string message = $"View: {activeView.Name}\n\n"
            + $"Pipes: {pipes.Count}\n"
            + $"Sprinklers: {sprinklers.Count}\n"
            + $"Fittings: {fittings.Count}\n"
            + $"Accessories: {accessories.Count}\n"
            + $"Total FP elements: {pipes.Count + sprinklers.Count + fittings.Count + accessories.Count}"
            + pipeSizeSummary
            + headSummary;

        TaskDialog.Show("SSG FP Suite — Element Count", message);
    }
}
