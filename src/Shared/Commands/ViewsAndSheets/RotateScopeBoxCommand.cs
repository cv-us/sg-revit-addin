using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Rotates a scope box to match the angle of a grid line.
    ///
    /// The user selects a scope box and a grid (local, linked, or enters a manual
    /// angle). The command computes the angle between the grid direction and the
    /// X-axis, then rotates the scope box to match.
    ///
    /// WORKFLOW:
    ///   1. User selects a scope box (pre-select or pick)
    ///   2. Dialog: choose angle source (local grid, linked grid, or manual)
    ///   3. Compute current scope box orientation from its geometry
    ///   4. Compute target angle from the selected grid line direction
    ///   5. Rotate scope box by the delta angle around its center Z-axis
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RotateScopeBoxCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Select scope box ──
                Element scopeBox = null;

                // Check pre-selection first
                var preSelected = uidoc.Selection.GetElementIds();
                if (preSelected.Count == 1)
                {
                    Element elem = doc.GetElement(preSelected.First());
                    if (elem?.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_VolumeOfInterest)
                        scopeBox = elem;
                }

                if (scopeBox == null)
                {
                    try
                    {
                        Reference scopeRef = uidoc.Selection.PickObject(
                            ObjectType.Element,
                            new CategorySelectionFilter(BuiltInCategory.OST_VolumeOfInterest),
                            "Select a scope box to rotate");
                        scopeBox = doc.GetElement(scopeRef);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return Result.Cancelled;
                    }
                }

                if (scopeBox == null)
                {
                    TaskDialog.Show("Rotate Scope Box", "No scope box selected.");
                    return Result.Cancelled;
                }

                // ── Step 2: Get local grids for dialog ──
                var localGrids = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Grids)
                    .WhereElementIsNotElementType()
                    .Cast<Grid>()
                    .OrderBy(g => g.Name)
                    .ToList();

                var gridNames = localGrids.Select(g => g.Name).ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new RotateScopeBoxDialog(scopeBox.Name, gridNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    double targetAngleDeg;

                    switch (dialog.AngleSource)
                    {
                        case RotateScopeBoxDialog.AngleSourceOption.LocalGrid:
                        {
                            Grid selectedGrid = localGrids
                                .FirstOrDefault(g => g.Name == dialog.SelectedGridName);
                            if (selectedGrid == null)
                            {
                                TaskDialog.Show("Rotate Scope Box",
                                    $"Grid '{dialog.SelectedGridName}' not found.");
                                return Result.Failed;
                            }
                            targetAngleDeg = GetGridAngleDegrees(selectedGrid);
                            break;
                        }
                        case RotateScopeBoxDialog.AngleSourceOption.LinkedGrid:
                        {
                            // Prompt user to pick a linked element
                            Reference linkedRef;
                            try
                            {
                                linkedRef = uidoc.Selection.PickObject(
                                    ObjectType.LinkedElement,
                                    "Select a grid line in the linked model");
                            }
                            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                            {
                                return Result.Cancelled;
                            }

                            double? angle = GetLinkedGridAngle(doc, linkedRef);
                            if (angle == null)
                            {
                                TaskDialog.Show("Rotate Scope Box",
                                    "Could not determine angle from the selected linked element.\n" +
                                    "Make sure you selected a grid line.");
                                return Result.Failed;
                            }
                            targetAngleDeg = angle.Value;
                            break;
                        }
                        case RotateScopeBoxDialog.AngleSourceOption.ManualAngle:
                        default:
                            targetAngleDeg = dialog.ManualAngleDegrees;
                            break;
                    }

                    // ── Step 4: Get current scope box angle ──
                    double currentAngleDeg = GetScopeBoxAngleDegrees(scopeBox);

                    // ── Step 5: Compute delta and rotate ──
                    double deltaAngleDeg = targetAngleDeg - currentAngleDeg;

                    // Normalize delta to -180..180
                    while (deltaAngleDeg > 180) deltaAngleDeg -= 360;
                    while (deltaAngleDeg < -180) deltaAngleDeg += 360;

                    if (Math.Abs(deltaAngleDeg) < 0.001)
                    {
                        TaskDialog.Show("Rotate Scope Box",
                            $"Scope box is already aligned.\n\n" +
                            $"Current angle: {currentAngleDeg:F2}°\n" +
                            $"Target angle: {targetAngleDeg:F2}°");
                        return Result.Succeeded;
                    }

                    double deltaRad = deltaAngleDeg * Math.PI / 180.0;

                    // Get scope box center for rotation axis
                    BoundingBoxXYZ bb = scopeBox.get_BoundingBox(null);
                    XYZ center = (bb.Min + bb.Max) / 2.0;
                    Line rotAxis = Line.CreateBound(
                        center,
                        new XYZ(center.X, center.Y, center.Z + 1));

                    using (var tw = new TransactionWrapper(doc, "Rotate Scope Box"))
                    {
                        ElementTransformUtils.RotateElement(doc, scopeBox.Id, rotAxis, deltaRad);
                        tw.Commit();
                    }

                    TaskDialog.Show("Rotate Scope Box",
                        $"Scope box '{scopeBox.Name}' rotated.\n\n" +
                        $"Previous angle: {currentAngleDeg:F2}°\n" +
                        $"Target angle: {targetAngleDeg:F2}°\n" +
                        $"Rotation applied: {deltaAngleDeg:F2}°");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  ANGLE COMPUTATION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Get the current rotation angle of a scope box in degrees (0-360).
        ///
        /// Extracts the scope box geometry, filters to horizontal edges (Z component
        /// of direction ≈ 0), takes the bottom edges, makes a best-fit line from
        /// their start points, and measures the angle of that line vs. X-axis.
        ///
        /// </summary>
        private double GetScopeBoxAngleDegrees(Element scopeBox)
        {
            Options opts = new Options { ComputeReferences = false };
            GeometryElement geom = scopeBox.get_Geometry(opts);
            if (geom == null) return 0;

            // Collect all line edges from the scope box geometry
            var allLines = new List<Line>();
            foreach (GeometryObject gObj in geom)
            {
                if (gObj is Line line)
                {
                    allLines.Add(line);
                }
            }

            if (allLines.Count == 0) return 0;

            // Filter to horizontal lines (direction Z component ≈ 0)
            var horizontalLines = allLines
                .Where(l =>
                {
                    XYZ dir = (l.GetEndPoint(1) - l.GetEndPoint(0)).Normalize();
                    return Math.Abs(dir.Z) < 0.01;
                })
                .ToList();

            if (horizontalLines.Count == 0) return 0;

            // Sort by Z (ascending) and take the bottom set
            horizontalLines = horizontalLines
                .OrderBy(l => l.GetEndPoint(0).Z)
                .ToList();

            // Take the first 4 lines (bottom face edges)
            var bottomLines = horizontalLines.Take(Math.Min(4, horizontalLines.Count)).ToList();

            // Get start points and compute best-fit direction
            // Use the longest horizontal line's direction as the scope box orientation
            Line longestLine = bottomLines.OrderByDescending(l => l.Length).First();
            XYZ direction = (longestLine.GetEndPoint(1) - longestLine.GetEndPoint(0)).Normalize();

            // Angle about Z-axis from X-axis
            double angleDeg = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;

            // Normalize to 0-360
            if (angleDeg < 0) angleDeg += 360;

            return angleDeg;
        }

        /// <summary>
        /// Get the angle of a local grid line in degrees (0-360) from the X-axis.
        /// </summary>
        private double GetGridAngleDegrees(Grid grid)
        {
            Curve curve = grid.Curve;
            if (curve == null) return 0;

            XYZ start = curve.GetEndPoint(0);
            XYZ end = curve.GetEndPoint(1);
            XYZ direction = new XYZ(end.X - start.X, end.Y - start.Y, 0).Normalize();

            double angleDeg = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
            if (angleDeg < 0) angleDeg += 360;

            return angleDeg;
        }

        /// <summary>
        /// Get the angle of a linked grid element from a picked reference.
        /// </summary>
        private double? GetLinkedGridAngle(Document doc, Reference linkedRef)
        {
            if (linkedRef == null) return null;

            // Get the link instance
            RevitLinkInstance linkInst = doc.GetElement(linkedRef.ElementId) as RevitLinkInstance;
            if (linkInst == null) return null;

            Document linkDoc = linkInst.GetLinkDocument();
            if (linkDoc == null) return null;

            Transform linkTransform = linkInst.GetTotalTransform();

            // Get the linked element
            Element linkedElem = linkDoc.GetElement(linkedRef.LinkedElementId);
            if (linkedElem == null) return null;

            // Get curve from the grid
            Grid linkedGrid = linkedElem as Grid;
            if (linkedGrid == null) return null;

            Curve curve = linkedGrid.Curve;
            if (curve == null) return null;

            // Transform endpoints to host coordinates
            XYZ start = linkTransform.OfPoint(curve.GetEndPoint(0));
            XYZ end = linkTransform.OfPoint(curve.GetEndPoint(1));
            XYZ direction = new XYZ(end.X - start.X, end.Y - start.Y, 0).Normalize();

            double angleDeg = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
            if (angleDeg < 0) angleDeg += 360;

            return angleDeg;
        }
    }
}

