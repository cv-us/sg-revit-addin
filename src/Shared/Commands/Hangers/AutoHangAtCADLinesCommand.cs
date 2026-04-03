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
    /// Places pipe hanger families at every point where a selected pipe crosses
    /// a line from a linked CAD file (typically representing structural steel).
    ///
    /// Migrated from: "Auto Hang - Pipes Crossing Linked CAD File Lines.dyn"
    ///
    /// WORKFLOW:
    ///   1. User selects pipes in the model
    ///   2. User selects a linked CAD file
    ///   3. Dialog appears: pick hanger family, rod length, CAD layers, etc.
    ///   4. Command finds all pipe/CAD-line intersections
    ///   5. Places a hanger at each intersection, rotated to match the pipe
    ///   6. Sets parameters: Nominal Diameter, Rod Length, Elevation from Level
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoHangAtCADLinesCommand : IExternalCommand
    {
        // Maximum pipe slope angle (degrees) — steeper pipes are skipped
        private const double MaxSlopeAngle = 60.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Select pipes ──
                IList<Reference> pipeRefs;
                try
                {
                    pipeRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new CategorySelectionFilter(BuiltInCategory.OST_PipeCurves),
                        "Select pipes to hang, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (pipeRefs == null || pipeRefs.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No pipes selected.");
                    return Result.Cancelled;
                }

                // Filter out near-vertical pipes
                var pipes = new List<Element>();
                foreach (Reference r in pipeRefs)
                {
                    Element pipe = doc.GetElement(r);
                    if (pipe != null && !IsSteepPipe(pipe))
                        pipes.Add(pipe);
                }

                if (pipes.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No valid pipes after filtering steep/vertical pipes.");
                    return Result.Cancelled;
                }

                // ── Step 2: Select CAD link ──
                Reference cadRef;
                try
                {
                    cadRef = uidoc.Selection.PickObject(
                        ObjectType.Element,
                        "Select the linked CAD file containing structural lines");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                ImportInstance cadLink = doc.GetElement(cadRef) as ImportInstance;
                if (cadLink == null)
                {
                    TaskDialog.Show("Auto Hang", "Selected element is not a CAD link.");
                    return Result.Failed;
                }

                // ── Step 3: Get CAD layers ──
                Dictionary<string, int> layersWithCounts = CADLinkHelpers.GetLayersWithCurves(cadLink);
                if (layersWithCounts.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No line geometry found in the selected CAD link.");
                    return Result.Failed;
                }

                // ── Step 4: Get hanger families ──
                IList<string> hangerFamilies = GetHangerFamilyNames(doc);
                if (hangerFamilies.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No pipe accessory families found in the project.\nLoad a hanger family first.");
                    return Result.Failed;
                }

                // ── Step 5: Show dialog ──
                using (var dialog = new AutoHangAtCADLinesDialog(hangerFamilies, layersWithCounts))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ── Step 6: Extract CAD lines from selected layers ──
                    HashSet<string> selectedLayers = new HashSet<string>(dialog.SelectedLayers);
                    IList<Line> cadLines = CADLinkHelpers.GetLinesFromLayers(
                        cadLink, selectedLayers, dialog.MinLineLengthFeet);

                    if (cadLines.Count == 0)
                    {
                        TaskDialog.Show("Auto Hang", "No lines found in selected layers above minimum length.");
                        return Result.Failed;
                    }

                    // ── Step 7: Find hanger family type ──
                    FamilySymbol hangerType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (hangerType == null)
                    {
                        TaskDialog.Show("Auto Hang", $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    // ── Step 8: Find intersections and place hangers ──
                    double rodLengthFeet = dialog.RodLengthInches / 12.0;
                    int hangersPlaced = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Hang at CAD Lines"))
                    {
                        // Activate the family type if not already
                        if (!hangerType.IsActive)
                            hangerType.Activate();

                        foreach (Element pipe in pipes)
                        {
                            Line pipeLine = GetPipeCenterline(pipe);
                            if (pipeLine == null) continue;

                            // Get pipe's reference level
                            ElementId levelId = pipe.LookupParameter("Reference Level")
                                ?.AsElementId() ?? pipe.LevelId;
                            Level level = doc.GetElement(levelId) as Level;
                            if (level == null) continue;

                            // Get pipe diameter for parameter setting
                            double pipeDiameter = ParameterHelpers.GetPipeDiameterValue(pipe);

                            // Find all intersection points
                            List<XYZ> intersections = IntersectionHelpers.FindPipeCADIntersections(pipeLine, cadLines);

                            // Get pipe direction for hanger rotation
                            XYZ pipeDir = (pipeLine.GetEndPoint(1) - pipeLine.GetEndPoint(0)).Normalize();
                            double rotationAngle = Math.Atan2(pipeDir.Y, pipeDir.X);

                            foreach (XYZ point in intersections)
                            {
                                // Place the hanger instance
                                FamilyInstance hanger = doc.Create.NewFamilyInstance(
                                    point, hangerType, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                if (hanger == null) continue;

                                // Rotate to align with pipe
                                XYZ axis = Line.CreateBound(point, point + XYZ.BasisZ).Direction;
                                ElementTransformUtils.RotateElement(
                                    doc, hanger.Id,
                                    Line.CreateBound(point, new XYZ(point.X, point.Y, point.Z + 1)),
                                    rotationAngle);

                                // Set parameters (silent fail if parameter doesn't exist)
                                SetParamSafe(hanger, "Nominal Diameter", pipeDiameter);
                                SetParamSafe(hanger, "Rod Length", rodLengthFeet);

                                // Hydratec family parameters
                                SetParamSafe(hanger, "Type Code (Hydratec)", dialog.TypeCode);
                                SetParamSafe(hanger, "Additional Stocklist Information (Hydratec)", "CAD Line Crossing");

                                // Elevation from Level
                                double elevFromLevel = point.Z - level.Elevation;
                                SetParamSafe(hanger, "Elevation from Level", elevFromLevel);

                                hangersPlaced++;
                            }
                        }

                        tw.Commit();
                    }

                    TaskDialog.Show("Auto Hang — Complete",
                        $"Placed {hangersPlaced} hangers across {pipes.Count} pipes.\n" +
                        $"CAD lines processed: {cadLines.Count} (from {dialog.SelectedLayers.Count} layers).");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Check if a pipe is too steep (near-vertical) to hang.
        /// </summary>
        private bool IsSteepPipe(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            if (lc == null) return true;

            Line line = lc.Curve as Line;
            if (line == null) return true;

            XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            XYZ dirXY = new XYZ(dir.X, dir.Y, 0);
            double dirXYLen = dirXY.GetLength();
            if (dirXYLen < 1e-10) return true; // perfectly vertical

            double angle = Math.Acos(Math.Min(1.0, dirXYLen)) * 180.0 / Math.PI;
            return angle > MaxSlopeAngle;
        }

        /// <summary>
        /// Get the pipe's centerline as a Line.
        /// </summary>
        private Line GetPipeCenterline(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc?.Curve as Line;
        }

        /// <summary>
        /// Get all pipe accessory family names in the project.
        /// </summary>
        private IList<string> GetHangerFamilyNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null
                    && f.FamilyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                .Select(f => f.Name)
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>
        /// Find the first FamilySymbol (type) for a given family name.
        /// </summary>
        private FamilySymbol FindHangerFamilyType(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family.Name == familyName
                    && fs.Family.FamilyCategory?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory);
        }

        /// <summary>
        /// Set a parameter value without throwing if the parameter doesn't exist.
        /// </summary>
        private void SetParamSafe(FamilyInstance instance, string paramName, double value)
        {
            Parameter p = instance.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                p.Set(value);
        }

        private void SetParamSafe(FamilyInstance instance, string paramName, string value)
        {
            Parameter p = instance.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                p.Set(value);
        }
    }
}
