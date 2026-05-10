using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SgRevitAddin.Commands.ModelCheck
{
    /// <summary>
    /// Finds pipes that are shorter than the minimum fabrication nipple length
    /// for their nominal diameter.
    ///
    /// Threaded nipple minimums (by nominal diameter):
    ///   0.5"  → 1.5"    0.75" → 1.5"    1.0"  → 2.0"    1.25" → 2.0"
    ///   1.5"  → 2.0"    2.0"  → 2.5"    2.5"  → 3.0"    3.0"  → 3.0"
    ///   4.0"  → 4.0"    6.0"  → 4.5"    larger → 4.5"
    ///
    /// Welded minimum: 16.0 inches for all sizes.
    ///
    /// A pipe is classified as threaded when its pipe type name contains "Threaded";
    /// all others are treated as welded.
    ///
    /// WORKFLOW:
    ///   1. Use pre-selection if pipes are already selected; otherwise collect all
    ///      pipes in the active view via FilteredElementCollector
    ///   2. For each pipe read Length and Diameter (Revit internal feet units)
    ///   3. Convert diameter to inches, look up minimum nipple length
    ///   4. Flag pipes whose length (in inches) falls below the minimum
    ///   5. Report flagged count broken down by pipe type and size
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipesTooShortCommand : IExternalCommand
    {
        // Threaded nipple minimums: nominal diameter (inches) → minimum length (inches)
        private static readonly Dictionary<double, double> ThreadedMinimums = new Dictionary<double, double>
        {
            { 0.5,  1.5 },
            { 0.75, 1.5 },
            { 1.0,  2.0 },
            { 1.25, 2.0 },
            { 1.5,  2.0 },
            { 2.0,  2.5 },
            { 2.5,  3.0 },
            { 3.0,  3.0 },
            { 4.0,  4.0 },
            { 6.0,  4.5 },
        };

        private const double ThreadedDefaultMinimumInches = 4.5;
        private const double WeldedMinimumInches = 16.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Step 1: Use pre-selection or fall back to all pipes in view ──
                List<Pipe> pipes = GetPipes(uidoc, doc, activeView);

                if (pipes.Count == 0)
                {
                    TaskDialog.Show("Pipes Too Short",
                        "No pipes found to check.");
                    return Result.Succeeded;
                }

                // ── Steps 2-4: Evaluate each pipe ──
                var tooShort = new List<PipeFinding>();

                foreach (Pipe pipe in pipes)
                {
                    double lengthFeet = GetParamDouble(pipe, BuiltInParameter.CURVE_ELEM_LENGTH);
                    double diamFeet   = GetParamDouble(pipe, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);

                    if (lengthFeet <= 0 || diamFeet <= 0)
                        continue;

                    double lengthInches = lengthFeet * 12.0;
                    double diamInches   = diamFeet   * 12.0;

                    bool isThreaded = IsThreadedPipe(pipe);
                    double minInches = GetMinimumLength(diamInches, isThreaded);

                    if (lengthInches < minInches)
                    {
                        tooShort.Add(new PipeFinding
                        {
                            PipeId       = pipe.Id,
                            TypeName     = GetPipeTypeName(pipe),
                            DiamInches   = diamInches,
                            LengthInches = lengthInches,
                            MinInches    = minInches,
                            IsThreaded   = isThreaded
                        });
                    }
                }

                // ── Step 5: Report ──
                ShowReport(pipes.Count, tooShort);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Pipe Collection
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns pipes from the current selection if any pipes are pre-selected;
        /// otherwise collects all pipes visible in the active view.
        /// </summary>
        private List<Pipe> GetPipes(UIDocument uidoc, Document doc, View activeView)
        {
            var preSelected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Pipe>()
                .ToList();

            if (preSelected.Count > 0)
                return preSelected;

            return new FilteredElementCollector(doc, activeView.Id)
                .OfClass(typeof(Pipe))
                .Cast<Pipe>()
                .ToList();
        }

        // ══════════════════════════════════════════════════════════════
        //  Classification and Minimum Lookup
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns true if the pipe type name contains "Threaded" (case-insensitive).
        /// </summary>
        private bool IsThreadedPipe(Pipe pipe)
        {
            string typeName = GetPipeTypeName(pipe);
            return typeName.IndexOf("Threaded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetPipeTypeName(Pipe pipe)
        {
            return pipe.PipeType?.Name ?? string.Empty;
        }

        /// <summary>
        /// Returns the minimum fabrication length in inches for the given nominal diameter.
        /// Uses nearest standard size matching (within 0.1") for the lookup key.
        /// </summary>
        private double GetMinimumLength(double diamInches, bool isThreaded)
        {
            if (isThreaded)
            {
                // Find the closest key within 0.1" tolerance
                foreach (var kvp in ThreadedMinimums.OrderBy(k => Math.Abs(k.Key - diamInches)))
                {
                    if (Math.Abs(kvp.Key - diamInches) < 0.1)
                        return kvp.Value;
                }
                return ThreadedDefaultMinimumInches;
            }
            else
            {
                return WeldedMinimumInches;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Reporting
        // ══════════════════════════════════════════════════════════════

        private void ShowReport(int totalChecked, List<PipeFinding> tooShort)
        {
            if (tooShort.Count == 0)
            {
                TaskDialog.Show("Pipes Too Short",
                    $"Checked {totalChecked} pipe{(totalChecked != 1 ? "s" : "")}.\n\n" +
                    "No pipes are shorter than the minimum fabrication nipple length.");
                return;
            }

            // Group by type name + rounded diameter for a compact summary
            var grouped = tooShort
                .GroupBy(f => $"{(f.IsThreaded ? "Threaded" : "Welded")} — {RoundDiam(f.DiamInches)}\" dia")
                .OrderBy(g => g.Key);

            var sb = new StringBuilder();
            sb.AppendLine($"Checked {totalChecked} pipe{(totalChecked != 1 ? "s" : "")}.");
            sb.AppendLine($"Found {tooShort.Count} pipe{(tooShort.Count != 1 ? "s" : "")} shorter than the minimum nipple length:\n");

            foreach (var g in grouped)
            {
                var first = g.First();
                sb.AppendLine($"  {g.Key}");
                sb.AppendLine($"    Count: {g.Count()}   Minimum: {first.MinInches}\"");
            }

            TaskDialog.Show("Pipes Too Short — Results", sb.ToString());
        }

        private string RoundDiam(double d)
        {
            // Round to nearest 1/8" for display
            return Math.Round(d * 8) / 8.0 == Math.Floor(d)
                ? d.ToString("F0")
                : d.ToString("F3").TrimEnd('0').TrimEnd('.');
        }

        // ══════════════════════════════════════════════════════════════
        //  Parameter Helpers
        // ══════════════════════════════════════════════════════════════

        private double GetParamDouble(Element elem, BuiltInParameter bip)
        {
            var p = elem.get_Parameter(bip);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return p.AsDouble();
            return 0;
        }

        // ══════════════════════════════════════════════════════════════
        //  Data Class
        // ══════════════════════════════════════════════════════════════

        private class PipeFinding
        {
            public ElementId PipeId       { get; set; }
            public string    TypeName     { get; set; }
            public double    DiamInches   { get; set; }
            public double    LengthInches { get; set; }
            public double    MinInches    { get; set; }
            public bool      IsThreaded   { get; set; }
        }
    }
}

