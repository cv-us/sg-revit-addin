using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Coordination
{
    /// <summary>
    /// Applies color overrides to pipes in the active view for visual coordination.
    /// Three modes:
    ///   1. By Size — color-codes pipes by nominal diameter
    ///   2. By Type — color-codes pipes by family type name
    ///   3. Reset — removes all graphic overrides from selected pipes
    ///
    /// Overrides are view-specific and do not modify the model.
    ///
    /// WORKFLOW:
    ///   1. User selects pipes (or selects all in view)
    ///   2. Dialog: choose mode (By Size / By Type / Reset)
    ///   3. Apply OverrideGraphicSettings per element in the active view
    ///   4. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorCodePipesCommand : IExternalCommand
    {
        // ── Color palettes ──

        /// <summary>Color assignments for pipe sizes (nominal diameter in inches).</summary>
        private static readonly (double MinDia, double MaxDia, string Label, int R, int G, int B)[] SizeColors = new[]
        {
            (0.0,   1.0,   "≤ 1\"",      0,   180, 0  ),   // Green
            (1.0,   1.25,  "1-1/4\"",     255, 105, 180),   // Pink
            (1.25,  1.5,   "1-1/2\"",     220, 20,  60 ),   // Red
            (1.5,   2.0,   "2\"",         30,  100, 220),   // Blue
            (2.0,   2.5,   "2-1/2\"",     255, 140, 0  ),   // Orange
            (2.5,   3.0,   "3\"",         148, 0,   211),   // Purple
            (3.0,   4.0,   "4\"",         0,   180, 180),   // Teal
            (4.0, 999.0,   "> 4\"",       128, 128, 0  ),   // Olive
        };

        /// <summary>Color assignments for pipe type names (substring match).</summary>
        private static readonly (string Substring, string Label, int R, int G, int B)[] TypeColors = new[]
        {
            ("Threaded",     "Threaded",       0,   180, 0  ),   // Green
            ("Welded",       "Welded Lines",   220, 20,  60 ),   // Red
            ("Mains",        "Welded Mains",   255, 0,   255),   // Magenta
            ("Grooved",      "Grooved",        30,  100, 220),   // Blue
            ("Flex",         "Flex",           255, 140, 0  ),   // Orange
        };

        private static readonly (int R, int G, int B) DefaultTypeColor = (128, 128, 128); // Gray for unmatched

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Collect pipes ──
                // Try pre-selection first
                var selectedIds = uidoc.Selection.GetElementIds();
                List<Element> pipes;

                if (selectedIds.Count > 0)
                {
                    pipes = selectedIds
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null && (e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves
                                               || e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_FlexPipeCurves))
                        .ToList();
                }
                else
                {
                    pipes = new List<Element>();
                }

                // If no pipes selected, get all pipes in active view
                if (pipes.Count == 0)
                {
                    pipes = new FilteredElementCollector(doc, activeView.Id)
                        .WherePasses(new ElementMulticategoryFilter(
                            new List<BuiltInCategory> {
                                BuiltInCategory.OST_PipeCurves,
                                BuiltInCategory.OST_FlexPipeCurves
                            }))
                        .WhereElementIsNotElementType()
                        .ToList();
                }

                if (pipes.Count == 0)
                {
                    TaskDialog.Show("Color Code Pipes", "No pipes found in the active view.");
                    return Result.Succeeded;
                }

                // ── Show dialog ──
                using (var dlg = new ColorCodePipesDialog(pipes.Count))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    using (Transaction tx = new Transaction(doc, "Color Code Pipes"))
                    {
                        tx.Start();

                        var summary = new Dictionary<string, int>();

                        switch (dlg.SelectedMode)
                        {
                            case ColorCodePipesDialog.ColorMode.BySize:
                                ApplyBySize(doc, activeView, pipes, summary);
                                break;

                            case ColorCodePipesDialog.ColorMode.ByType:
                                ApplyByType(doc, activeView, pipes, summary);
                                break;

                            case ColorCodePipesDialog.ColorMode.Reset:
                                ResetOverrides(doc, activeView, pipes);
                                break;
                        }

                        tx.Commit();

                        // Report
                        if (dlg.SelectedMode == ColorCodePipesDialog.ColorMode.Reset)
                        {
                            TaskDialog.Show("Color Code Pipes",
                                $"Reset color overrides on {pipes.Count} pipes.");
                        }
                        else
                        {
                            string report = $"Color-coded {pipes.Count} pipes by {(dlg.SelectedMode == ColorCodePipesDialog.ColorMode.BySize ? "size" : "type")}:\n\n";
                            foreach (var kvp in summary.OrderByDescending(x => x.Value))
                                report += $"  {kvp.Key}: {kvp.Value}\n";
                            TaskDialog.Show("Color Code Pipes", report);
                        }
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private void ApplyBySize(Document doc, View view, List<Element> pipes, Dictionary<string, int> summary)
        {
            foreach (var pipe in pipes)
            {
                double diameter = GetNominalDiameter(pipe);
                double diaInches = diameter * 12.0; // feet to inches

                int r = 128, g = 128, b = 128;
                string label = "Other";

                foreach (var sc in SizeColors)
                {
                    if (diaInches > sc.MinDia && diaInches <= sc.MaxDia)
                    {
                        r = sc.R; g = sc.G; b = sc.B;
                        label = sc.Label;
                        break;
                    }
                }

                ApplyColorOverride(doc, view, pipe.Id, r, g, b);

                if (!summary.ContainsKey(label)) summary[label] = 0;
                summary[label]++;
            }
        }

        private void ApplyByType(Document doc, View view, List<Element> pipes, Dictionary<string, int> summary)
        {
            foreach (var pipe in pipes)
            {
                string typeName = GetTypeName(pipe);

                int r = DefaultTypeColor.R, g = DefaultTypeColor.G, b = DefaultTypeColor.B;
                string label = "Other";
                bool matched = false;

                foreach (var tc in TypeColors)
                {
                    if (typeName.IndexOf(tc.Substring, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        r = tc.R; g = tc.G; b = tc.B;
                        label = tc.Label;
                        matched = true;
                        break;
                    }
                }

                if (!matched) label = typeName.Length > 30 ? typeName.Substring(0, 30) + "..." : typeName;

                ApplyColorOverride(doc, view, pipe.Id, r, g, b);

                if (!summary.ContainsKey(label)) summary[label] = 0;
                summary[label]++;
            }
        }

        private void ResetOverrides(Document doc, View view, List<Element> pipes)
        {
            var reset = new OverrideGraphicSettings();
            foreach (var pipe in pipes)
            {
                view.SetElementOverrides(pipe.Id, reset);
            }
        }

        private void ApplyColorOverride(Document doc, View view, ElementId elementId, int r, int g, int b)
        {
            var color = new Color((byte)r, (byte)g, (byte)b);
            var ogs = new OverrideGraphicSettings();

            // Set projection line color and weight
            ogs.SetProjectionLineColor(color);
            ogs.SetProjectionLineWeight(5);

            // Set surface foreground color for 3D views
            ogs.SetSurfaceForegroundPatternColor(color);

            view.SetElementOverrides(elementId, ogs);
        }

        private double GetNominalDiameter(Element pipe)
        {
            // Try "Diameter" parameter first (Pipe), then "Size" (FlexPipe)
            Parameter diaParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && diaParam.HasValue)
                return diaParam.AsDouble();

            // Fallback: try the "Size" parameter and parse it
            Parameter sizeParam = pipe.LookupParameter("Size");
            if (sizeParam != null && sizeParam.HasValue)
            {
                string sizeStr = sizeParam.AsString();
                if (!string.IsNullOrEmpty(sizeStr))
                {
                    // Try to parse "2\"" or "1 1/2\"" style
                    if (double.TryParse(sizeStr.Replace("\"", "").Trim(), out double parsed))
                        return parsed / 12.0; // inches to feet
                }
            }

            return 0;
        }

        private string GetTypeName(Element pipe)
        {
            var typeId = pipe.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var type = pipe.Document.GetElement(typeId);
                if (type != null)
                    return type.Name;
            }
            return "(Unknown)";
        }
    }
}
