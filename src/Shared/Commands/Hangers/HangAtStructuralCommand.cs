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
    /// Places pipe hangers at every point where a selected pipe crosses a structural
    /// framing member (beam, joist, girder). Works with both local and linked structural models.
    ///
    /// WORKFLOW:
    ///   1. User selects pipes
    ///   2. Dialog: pick hanger family, attach-to mode, structural source, etc.
    ///   3. Command collects structural framing elements (local or linked)
    ///   4. Finds all pipe/structural crossings via 2D intersection
    ///   5. Places hangers at crossings with correct Z from structural top/bottom flange
    ///   6. Sets rotation, clamp angle, and all parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangAtStructuralCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;

        // Flange thickness offsets (feet) — used to place hanger just below/above flange
        private const double TopFlangeOffset = 0.069;    // ~0.83 inches
        private const double BottomFlangeOffset = 0.033;  // ~0.4 inches

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

                // Filter steep/vertical pipes
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

                // ── Step 2: Get available info for dialog ──
                IList<string> hangerFamilies = GetHangerFamilyNames(doc);
                if (hangerFamilies.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No pipe accessory families found. Load a hanger family first.");
                    return Result.Failed;
                }

                var links = StructuralFramingHelpers.GetRevitLinks(doc);
                var linkNames = links.Select(l => l.Name).ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new HangAtStructuralDialog(hangerFamilies, linkNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ── Step 4: Collect structural framing ──
                    List<StructuralFramingHelpers.FramingInfo> framingList;

                    if (dialog.UseLocalFraming)
                    {
                        var localFraming = StructuralFramingHelpers.GetLocalStructuralFraming(doc);
                        framingList = StructuralFramingHelpers.BuildFramingInfoList(localFraming);
                    }
                    else
                    {
                        // Find the selected link
                        RevitLinkInstance selectedLink = links.FirstOrDefault(l => l.Name == dialog.SelectedLinkName);
                        if (selectedLink == null)
                        {
                            TaskDialog.Show("Auto Hang", $"Could not find linked model '{dialog.SelectedLinkName}'.");
                            return Result.Failed;
                        }

                        Document linkDoc = selectedLink.GetLinkDocument();
                        Transform linkTransform = selectedLink.GetTotalTransform();

                        var linkedFraming = new FilteredElementCollector(linkDoc)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .Cast<Element>()
                            .Where(e => StructuralFramingHelpers.IsBeamLikeType(e))
                            .ToList();

                        framingList = StructuralFramingHelpers.BuildFramingInfoList(linkedFraming, linkTransform);
                    }

                    if (framingList.Count == 0)
                    {
                        TaskDialog.Show("Auto Hang", "No structural framing elements found.");
                        return Result.Failed;
                    }

                    // ── Step 5: Find hanger family type ──
                    FamilySymbol hangerType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (hangerType == null)
                    {
                        TaskDialog.Show("Auto Hang", $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    // ── Step 6: Find intersections and place hangers ──
                    double maxClashFeet = dialog.MaxClashHeightFeet;
                    int hangersPlaced = 0;
                    int pipesProcessed = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Hang at Structural"))
                    {
                        if (!hangerType.IsActive)
                            hangerType.Activate();

                        foreach (Element pipe in pipes)
                        {
                            Line pipeLine = GetPipeCenterline(pipe);
                            if (pipeLine == null) continue;

                            ElementId levelId = pipe.LookupParameter("Reference Level")
                                ?.AsElementId() ?? pipe.LevelId;
                            Level level = doc.GetElement(levelId) as Level;
                            if (level == null) continue;

                            double pipeDiameter = ParameterHelpers.GetPipeDiameterValue(pipe);
                            double pipeZ = pipeLine.GetEndPoint(0).Z;

                            XYZ pipeDir = (pipeLine.GetEndPoint(1) - pipeLine.GetEndPoint(0)).Normalize();
                            double pipeRotation = Math.Atan2(pipeDir.Y, pipeDir.X);

                            bool anyPlaced = false;

                            foreach (var framing in framingList)
                            {
                                // Quick vertical proximity check
                                double verticalDist = Math.Min(
                                    Math.Abs(framing.TopZ - pipeZ),
                                    Math.Abs(framing.BottomZ - pipeZ));
                                if (verticalDist > maxClashFeet) continue;

                                // 2D intersection test
                                XYZ intersection = IntersectionHelpers.GetSegmentIntersection2D(
                                    pipeLine, framing.Centerline);
                                if (intersection == null) continue;

                                // Calculate 3D hanger point
                                XYZ point3D = IntersectionHelpers.ProjectToPipeLine(intersection, pipeLine);

                                // Determine Z from structural element
                                double hangerZ;
                                double accessoryOffset;
                                if (dialog.AttachToBottom)
                                {
                                    hangerZ = framing.BottomZ + BottomFlangeOffset;
                                    accessoryOffset = BottomFlangeOffset;
                                }
                                else
                                {
                                    hangerZ = framing.TopZ - TopFlangeOffset;
                                    accessoryOffset = TopFlangeOffset;
                                }

                                // Use the pipe's XY but the structural Z for placement
                                XYZ hangerPoint = new XYZ(point3D.X, point3D.Y, pipeZ);

                                // Place hanger
                                FamilyInstance hanger = doc.Create.NewFamilyInstance(
                                    hangerPoint, hangerType, level,
                                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                if (hanger == null) continue;

                                // Rotate to pipe direction
                                Line rotAxis = Line.CreateBound(
                                    hangerPoint,
                                    new XYZ(hangerPoint.X, hangerPoint.Y, hangerPoint.Z + 1));
                                ElementTransformUtils.RotateElement(doc, hanger.Id, rotAxis, pipeRotation);

                                // ── Set parameters ──
                                SetParamSafe(hanger, "Nominal Diameter", pipeDiameter);

                                // Rod length = pipe diameter
                                SetParamSafe(hanger, "Rod Length", pipeDiameter);

                                // Elevation from level
                                double elevFromLevel = hangerPoint.Z - level.Elevation;
                                SetParamSafe(hanger, "Elevation from Level", elevFromLevel);

                                // Top accessory offset
                                SetParamSafe(hanger, "Top Accessory Offset", accessoryOffset);

                                // Hydratec family parameters
                                SetParamSafe(hanger, "Type Code (Hydratec)", dialog.WidemouthTypeCode);
                                SetParamSafe(hanger, "Additional Stocklist Information (Hydratec)", framing.Name);

                                // C-clamp visibility (0 = hide, 1 = show)
                                SetParamSafe(hanger, "C Clamp", dialog.ShowCClamp ? 1.0 : 0.0);

                                // Clamp angle
                                double clampAngle = StructuralFramingHelpers.CalculateClampAngle(
                                    hangerPoint, pipeRotation, framing.Centerline);
                                SetParamSafe(hanger, "Clamp Angle", clampAngle);

                                // Comments — structural element name (duplicated for non-Hydratec families)
                                SetParamSafe(hanger, "Comments", framing.Name);

                                hangersPlaced++;
                                anyPlaced = true;
                            }

                            if (anyPlaced) pipesProcessed++;
                        }

                        tw.Commit();
                    }

                    TaskDialog.Show("Auto Hang — Complete",
                        $"Placed {hangersPlaced} hangers across {pipesProcessed} pipes.\n" +
                        $"Structural members analyzed: {framingList.Count}\n" +
                        $"Attach to: {(dialog.AttachToBottom ? "BOTTOM" : "TOP")} of structural.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private bool IsSteepPipe(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            if (lc == null) return true;

            Line line = lc.Curve as Line;
            if (line == null) return true;

            XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            XYZ dirXY = new XYZ(dir.X, dir.Y, 0);
            double dirXYLen = dirXY.GetLength();
            if (dirXYLen < 1e-10) return true;

            double angle = Math.Acos(Math.Min(1.0, dirXYLen)) * 180.0 / Math.PI;
            return angle > MaxSlopeAngle;
        }

        private Line GetPipeCenterline(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc?.Curve as Line;
        }

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

        private FamilySymbol FindHangerFamilyType(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family.Name == familyName
                    && fs.Family.FamilyCategory?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory);
        }

        private void SetParamSafe(FamilyInstance inst, string name, double value)
        {
            Parameter p = inst.LookupParameter(name);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                p.Set(value);
        }

        private void SetParamSafe(FamilyInstance inst, string name, string value)
        {
            Parameter p = inst.LookupParameter(name);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                p.Set(value);
        }

        private void SetParamSafe(FamilyInstance inst, string name, int value)
        {
            Parameter p = inst.LookupParameter(name);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Integer)
                p.Set(value);
        }
    }
}
