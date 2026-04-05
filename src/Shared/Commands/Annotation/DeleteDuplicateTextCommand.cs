using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Finds and deletes duplicate TextNote elements in the active view.
    ///
    /// Two text notes are considered duplicates when they share identical text content
    /// AND their XY locations are within 0.5 feet of each other. The first instance
    /// found in each group is kept; all others are deleted.
    ///
    /// WORKFLOW:
    ///   1. Collect all TextNote elements in the active view
    ///   2. Group by exact text content
    ///   3. Within each group, cluster by XY proximity (0.5 ft threshold)
    ///   4. Confirm deletion count with the user via TaskDialog
    ///   5. Delete duplicates in a single transaction and report the count
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeleteDuplicateTextCommand : IExternalCommand
    {
        private const double ProximityThresholdFeet = 0.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Step 1: Collect all TextNotes in the active view ──
                var allNotes = new FilteredElementCollector(doc, activeView.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .ToList();

                if (allNotes.Count == 0)
                {
                    TaskDialog.Show("Delete Duplicate Text Notes",
                        "No text notes found in the active view.");
                    return Result.Succeeded;
                }

                // ── Step 2: Group by exact text content ──
                var grouped = allNotes.GroupBy(tn => tn.Text ?? string.Empty);

                // ── Step 3: Within each group, identify duplicates by XY proximity ──
                var toDelete = new List<ElementId>();

                foreach (var group in grouped)
                {
                    var notesInGroup = group.ToList();
                    if (notesInGroup.Count < 2)
                        continue;

                    // Track which notes have already been assigned to a cluster
                    var assigned = new bool[notesInGroup.Count];

                    for (int i = 0; i < notesInGroup.Count; i++)
                    {
                        if (assigned[i])
                            continue;

                        // Note i is the "keeper" for its cluster
                        XYZ posI = GetNotePosition(notesInGroup[i]);

                        for (int j = i + 1; j < notesInGroup.Count; j++)
                        {
                            if (assigned[j])
                                continue;

                            XYZ posJ = GetNotePosition(notesInGroup[j]);

                            // XY distance only
                            double xyDist = Math.Sqrt(
                                Math.Pow(posI.X - posJ.X, 2) +
                                Math.Pow(posI.Y - posJ.Y, 2));

                            if (xyDist <= ProximityThresholdFeet)
                            {
                                toDelete.Add(notesInGroup[j].Id);
                                assigned[j] = true;
                            }
                        }

                        assigned[i] = true;
                    }
                }

                // ── Step 4: Confirm with user ──
                if (toDelete.Count == 0)
                {
                    TaskDialog.Show("Delete Duplicate Text Notes",
                        "No duplicate text notes found in the active view.");
                    return Result.Succeeded;
                }

                TaskDialogResult confirm = TaskDialog.Show(
                    "Delete Duplicate Text Notes",
                    $"Found {toDelete.Count} duplicate text note{(toDelete.Count != 1 ? "s" : "")}.\n\n" +
                    "Delete them?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirm != TaskDialogResult.Yes)
                    return Result.Cancelled;

                // ── Step 5: Delete duplicates ──
                using (var tw = new TransactionWrapper(doc, "Delete Duplicate Text Notes"))
                {
                    foreach (ElementId id in toDelete)
                    {
                        try { doc.Delete(id); }
                        catch { /* skip if already gone */ }
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Delete Duplicate Text Notes",
                    $"Deleted {toDelete.Count} duplicate text note{(toDelete.Count != 1 ? "s" : "")}.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Returns the XYZ location of a TextNote (its insertion point).
        /// Falls back to the origin if the location is unavailable.
        /// </summary>
        private XYZ GetNotePosition(TextNote note)
        {
            // TextNote.Coord gives the insertion point in model coordinates
            try { return note.Coord; }
            catch { return XYZ.Zero; }
        }
    }
}
