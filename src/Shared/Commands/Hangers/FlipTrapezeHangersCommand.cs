using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Rotates selected trapeze hanger family instances 180° around their Z-axis
    /// and swaps the Rod 1 / Rod 2 parameter values to match the new physical orientation.
    ///
    /// When a trapeze hanger is flipped, "Rod 1" and "Rod 2" physically swap sides.
    /// This command corrects the elevation and offset parameters to match.
    ///
    /// Parameters swapped:
    ///   - Rod 1 Top Elevation  ↔  Rod 2 Top Elevation
    ///   - Rod 1 Offset         ↔  Rod 2 Offset
    ///
    /// WORKFLOW:
    ///   1. User selects trapeze hanger instances (pre-selection or pick)
    ///   2. Each selected hanger is rotated 180° around its vertical Z-axis
    ///   3. Rod 1 and Rod 2 parameter values are swapped to reflect the flipped geometry
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FlipTrapezeHangersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Get trapeze hangers from pre-selection or pick ──
                List<FamilyInstance> hangers = GetTrapezeHangers(uidoc, doc);
                if (hangers == null)
                    return Result.Cancelled;

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Flip Trapeze Hangers",
                        "No trapeze hanger instances found in the selection.\n" +
                        "Select family instances whose name contains \"-Pipe Trapeze\".");
                    return Result.Cancelled;
                }

                // ── Step 2: Flip each hanger ──
                int flipped = 0;
                int failed = 0;

                using (var tw = new TransactionWrapper(doc, "Flip Trapeze Hangers"))
                {
                    foreach (FamilyInstance hanger in hangers)
                    {
                        try
                        {
                            LocationPoint lp = hanger.Location as LocationPoint;
                            if (lp == null) { failed++; continue; }

                            XYZ origin = lp.Point;
                            Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);

                            // Read rod parameters before rotating
                            double rod1TopElev = GetParam(hanger, "Rod 1 Top Elevation");
                            double rod2TopElev = GetParam(hanger, "Rod 2 Top Elevation");
                            double rod1Offset  = GetParam(hanger, "Rod 1 Offset");
                            double rod2Offset  = GetParam(hanger, "Rod 2 Offset");

                            // Rotate 180° around Z-axis
                            ElementTransformUtils.RotateElement(doc, hanger.Id, axis, Math.PI);

                            // Swap Rod 1 ↔ Rod 2 parameter values to match the flipped orientation
                            SetParam(hanger, "Rod 1 Top Elevation", rod2TopElev);
                            SetParam(hanger, "Rod 2 Top Elevation", rod1TopElev);
                            SetParam(hanger, "Rod 1 Offset",         rod2Offset);
                            SetParam(hanger, "Rod 2 Offset",         rod1Offset);

                            flipped++;
                        }
                        catch
                        {
                            failed++;
                        }
                    }

                    tw.Commit();
                }

                // ── Step 3: Report ──
                string summary = $"Flipped {flipped} trapeze hanger{(flipped != 1 ? "s" : "")}.";
                if (failed > 0)
                    summary += $"\n{failed} hanger{(failed != 1 ? "s" : "")} could not be flipped.";

                TaskDialog.Show("Flip Trapeze Hangers — Complete", summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Returns trapeze hanger instances from pre-selection or user pick.
        /// Returns null if the user cancels.
        /// </summary>
        private List<FamilyInstance> GetTrapezeHangers(UIDocument uidoc, Document doc)
        {
            // Check pre-selection first
            var preSelected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilyInstance>()
                .Where(IsTrapeze)
                .ToList();

            if (preSelected.Count > 0)
                return preSelected;

            // Prompt user to pick
            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new CategorySelectionFilter(BuiltInCategory.OST_PipeAccessory),
                    "Select trapeze hangers to flip, then press Finish");

                return refs
                    .Select(r => doc.GetElement(r) as FamilyInstance)
                    .Where(fi => fi != null && IsTrapeze(fi))
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if the family instance is a trapeze hanger (name contains "-Pipe Trapeze").
        /// </summary>
        private bool IsTrapeze(FamilyInstance fi)
        {
            string name = fi.Symbol?.Family?.Name ?? string.Empty;
            return name.IndexOf("-Pipe Trapeze", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private double GetParam(FamilyInstance fi, string paramName)
        {
            Parameter p = fi.LookupParameter(paramName);
            if (p != null && p.StorageType == StorageType.Double)
                return p.AsDouble();
            return 0.0;
        }

        private void SetParam(FamilyInstance fi, string paramName, double value)
        {
            Parameter p = fi.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                p.Set(value);
        }
    }
}
