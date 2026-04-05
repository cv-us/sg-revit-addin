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
    /// Formats all selected pipe hanger symbols so their tick marks face a consistent
    /// direction (forward slash "/" or backslash "\") based on user preference.
    ///
    /// WORKFLOW:
    ///   1. User selects pipe accessories (hangers)
    ///   2. Dialog: pick direction (Forward / Backslash / Default)
    ///   3. For each hanger with "-Pipe Hanger" in its family name:
    ///      a. Read the element's rotation angle
    ///      b. Calculate the expected "Flip Symbol" value based on angle + direction
    ///      c. Set "Flip Symbol" parameter only if it differs from current value
    ///
    /// ANGLE-TO-FLIP MAPPING:
    ///   Angle 0-45°   → base flip = 0
    ///   Angle 45-135°  → base flip = 1
    ///   Angle 135-225° → base flip = 0
    ///   Angle 225-315° → base flip = 1
    ///   Angle 315-360° → base flip = 0
    ///
    ///   "Back"    → uses base flip value directly
    ///   "Forward" → uses inverted base flip value (0→1, 1→0)
    ///   "Default" → sets all to 0 (unflipped)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FormatHangerTicksCommand : IExternalCommand
    {
        private const string HangerFamilyFilter = "-Pipe Hanger";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Select pipe accessories ──
                IList<Reference> accessoryRefs;
                try
                {
                    accessoryRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new CategorySelectionFilter(BuiltInCategory.OST_PipeAccessory),
                        "Select pipe hangers to format, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (accessoryRefs == null || accessoryRefs.Count == 0)
                {
                    TaskDialog.Show("Format Hanger Ticks", "No pipe accessories selected.");
                    return Result.Cancelled;
                }

                // ── Step 2: Filter to hanger family instances ──
                var hangers = new List<FamilyInstance>();
                foreach (var r in accessoryRefs)
                {
                    var el = doc.GetElement(r) as FamilyInstance;
                    if (el == null) continue;

                    // Filter by family name containing "-Pipe Hanger"
                    string familyName = el.Symbol?.Family?.Name ?? "";
                    if (familyName.Contains(HangerFamilyFilter))
                        hangers.Add(el);
                }

                if (hangers.Count == 0)
                {
                    TaskDialog.Show("Format Hanger Ticks",
                        "None of the selected elements are pipe hanger families.\n" +
                        "Looking for families with \"" + HangerFamilyFilter + "\" in the name.");
                    return Result.Cancelled;
                }

                // ── Step 3: Show dialog ──
                var dialog = new FormatHangerTicksDialog();
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                string direction = dialog.SymbolDirection; // "Forward", "Back", or "Default"

                // ── Step 4: Calculate and apply flip values ──
                int updated = 0;
                int skipped = 0;

                using (var tw = new TransactionWrapper(doc, "Format Hanger Ticks"))
                {
                    foreach (var hanger in hangers)
                    {
                        // Get element rotation angle (degrees)
                        double angleDeg = GetRotationAngle(hanger);

                        // Calculate desired flip value
                        int desiredFlip = CalculateFlipValue(angleDeg, direction);

                        // Read current flip value
                        int currentFlip = GetFlipSymbolValue(hanger);

                        // Only update if different
                        if (currentFlip != desiredFlip)
                        {
                            SetFlipSymbol(hanger, desiredFlip);
                            updated++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Format Hanger Ticks",
                    $"Formatted {hangers.Count} hanger(s):\n" +
                    $"  {updated} updated\n" +
                    $"  {skipped} already correct\n\n" +
                    $"Direction: {direction}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Format Hanger Ticks failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Gets the rotation angle (in degrees, 0-360) of a family instance.
        /// Uses the FacingOrientation or location rotation if available.
        /// </summary>
        private double GetRotationAngle(FamilyInstance fi)
        {
            // Try location point rotation first
            if (fi.Location is LocationPoint lp)
            {
                double radians = lp.Rotation;
                double degrees = radians * 180.0 / Math.PI;
                // Normalize to 0-360
                degrees = degrees % 360.0;
                if (degrees < 0) degrees += 360.0;
                return Math.Round(degrees);
            }

            // For line-based families, compute angle from the location curve direction
            if (fi.Location is LocationCurve lc)
            {
                var curve = lc.Curve;
                var dir = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                double radians = Math.Atan2(dir.Y, dir.X);
                double degrees = radians * 180.0 / Math.PI;
                degrees = degrees % 360.0;
                if (degrees < 0) degrees += 360.0;
                return Math.Round(degrees);
            }

            // Fallback: use FacingOrientation
            try
            {
                var facing = fi.FacingOrientation;
                double radians = Math.Atan2(facing.Y, facing.X);
                double degrees = radians * 180.0 / Math.PI;
                degrees = degrees % 360.0;
                if (degrees < 0) degrees += 360.0;
                return Math.Round(degrees);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Calculates the desired "Flip Symbol" value (0 or 1) based on the element's
        /// rotation angle and the user's direction preference.
        ///
        /// Angle-to-base-flip mapping:
        ///   0-45°   → 0
        ///   45-135°  → 1
        ///   135-225° → 0
        ///   225-315° → 1
        ///   315-360° → 0
        ///
        /// "Back"    → base flip value (direct)
        /// "Forward" → inverted base flip (0↔1)
        /// "Default" → always 0
        /// </summary>
        private int CalculateFlipValue(double angleDeg, string direction)
        {
            if (direction == "Default")
                return 0;

            // Base flip from angle ranges
            int baseFlip;
            if (angleDeg >= 0 && angleDeg < 45)
                baseFlip = 0;
            else if (angleDeg >= 45 && angleDeg < 135)
                baseFlip = 1;
            else if (angleDeg >= 135 && angleDeg < 225)
                baseFlip = 0;
            else if (angleDeg >= 225 && angleDeg < 315)
                baseFlip = 1;
            else if (angleDeg >= 315 && angleDeg < 360)
                baseFlip = 0;
            else
                baseFlip = 0; // fallback

            if (direction == "Back")
                return baseFlip;

            // "Forward" → inverted
            return baseFlip == 0 ? 1 : 0;
        }

        /// <summary>
        /// Reads the current "Flip Symbol" parameter value (0 or 1).
        /// </summary>
        private int GetFlipSymbolValue(FamilyInstance fi)
        {
            var param = fi.LookupParameter("Flip Symbol");
            if (param != null && param.HasValue)
                return param.AsInteger();
            return 0;
        }

        /// <summary>
        /// Sets the "Flip Symbol" parameter on the family instance.
        /// </summary>
        private void SetFlipSymbol(FamilyInstance fi, int value)
        {
            var param = fi.LookupParameter("Flip Symbol");
            if (param != null && !param.IsReadOnly)
                param.Set(value);
        }
    }
}
