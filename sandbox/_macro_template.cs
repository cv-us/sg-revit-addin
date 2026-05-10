/*
 * ═══════════════════════════════════════════════════════════════════
 * SG Revit Addin - Macro Template
 * ═══════════════════════════════════════════════════════════════════
 *
 * HOW TO USE THIS TEMPLATE:
 *
 *   1. Copy this file to sandbox/experiments/ with a descriptive name:
 *      sandbox/experiments/try_pipe_color_coding.cs
 *
 *   2. Rename the method from "MyMacroName" to something descriptive:
 *      public void TryPipeColorCoding()
 *
 *   3. Write your logic between the comment markers below
 *
 *   4. Open Revit → Manage → Macro Manager → Application tab
 *      → Create/select a module → Edit → Paste this code → Build → Run
 *
 *   5. Iterate: edit in your code editor, paste into Revit, run again
 *
 * ───────────────────────────────────────────────────────────────────
 *
 * KEY DIFFERENCES FROM PLUGIN COMMANDS:
 *
 *   MACRO:    this.ActiveUIDocument
 *   COMMAND:  commandData.Application.ActiveUIDocument
 *
 *   MACRO:    No transaction needed (Revit handles it)
 *   COMMAND:  Must use TransactionWrapper for model changes
 *
 * ───────────────────────────────────────────────────────────────────
 *
 * WHEN THIS MACRO IS READY:
 *   → Move it to sandbox/graduated/
 *   → Create a real command in src/Shared/Commands/{Domain}/
 *   → See docs/macro-to-command-workflow.md for full instructions
 *
 * ═══════════════════════════════════════════════════════════════════
 */

// ── Common imports you'll need ──
using System;
using System.Collections.Generic;
using System.Linq;

// ── Revit API ──
using Autodesk.Revit.DB;            // Document, Element, Transaction, XYZ, etc.
using Autodesk.Revit.DB.Plumbing;   // Pipe, PipingSystem, etc.
using Autodesk.Revit.UI;            // UIDocument, TaskDialog, Selection, etc.

// ── Add more as needed ──
// using Autodesk.Revit.DB.Mechanical;  // for ducts if coordinating
// using Autodesk.Revit.DB.Structure;   // for structural elements
// using System.IO;                      // for file export

public partial class ThisApplication
{
    public void MyMacroName()  // ← Rename this!
    {
        // ── Get the active document ──
        UIDocument uidoc = this.ActiveUIDocument;
        Document doc = uidoc.Document;

        // ════════════════════════════════════════
        // YOUR MACRO LOGIC HERE
        // ════════════════════════════════════════

        // Example: Count all pipes in the active view
        //
        // var pipes = new FilteredElementCollector(doc, doc.ActiveView.Id)
        //     .OfClass(typeof(Pipe))
        //     .Cast<Pipe>()
        //     .ToList();
        //
        // TaskDialog.Show("Pipe Count", $"Found {pipes.Count} pipes in this view.");

        // Example: Get all sprinklers and list their types
        //
        // var sprinklers = new FilteredElementCollector(doc, doc.ActiveView.Id)
        //     .OfCategory(BuiltInCategory.OST_Sprinklers)
        //     .WhereElementIsNotElementType()
        //     .Cast<FamilyInstance>()
        //     .ToList();
        //
        // var types = sprinklers.Select(s => s.Symbol.Name).Distinct();
        // TaskDialog.Show("Sprinklers", string.Join("\n", types));

        // ════════════════════════════════════════
        // END MACRO LOGIC
        // ════════════════════════════════════════

        TaskDialog.Show("SG Revit Addin", "Macro complete.");
    }
}
