using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Places pipe hangers at typical spacing along straight pipe runs, attaching
    /// them to structural framing members (beams, joists, girders) that run PARALLEL
    /// to the pipes. Unlike the "Hangers to Decks" command which raybounces straight
    /// up, this command searches perpendicular to the pipe direction to find nearby
    /// parallel structural members.
    ///
    /// Features unique to this variant:
    ///   • Perpendicular search for parallel structural framing (not vertical raybounce)
    ///   • Top or bottom flange attachment
    ///   • Automatic widemouth type selection when flange thickness > 0.75"
    ///   • C-clamp angle calculation toward structural member centerline
    ///   • Top accessory offset based on attachment mode and flange thickness
    ///
    /// WORKFLOW:
    ///   1. User selects pipes
    ///   2. Dialog: family, spacing, type codes, attach mode, structural source
    ///   3. Filter pipes by type, slope, length
    ///   4. Calculate evenly-spaced or exact-interval hanger points
    ///   5. At each hanger point, search perpendicular to pipe for nearby structural framing
    ///   6. Determine attachment point (top/bottom flange), flange thickness, clamp angle
    ///   7. Place hangers with correct type code (standard vs widemouth)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangParallelStructuralCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;
        private const double MinPipeLengthFeet = 2.0;
        private const double EndOffsetFeet = 0.5;

        /// <summary>Search distance perpendicular to pipe for parallel structure (feet).</summary>
        private const double PerpendicularSearchDist = 10.0;

        /// <summary>Vertical search range above/below pipe (feet).</summary>
        private const double VerticalSearchRange = 5.0;

        /// <summary>Flange thickness threshold (inches). Above this → widemouth hanger.</summary>
        private const double WidemouthFlangeThresholdInches = 0.75;

        /// <summary>Top flange offset when attaching to TOP (feet). ~0.84 inches.</summary>
        private const double TopAttachOffset = 0.070;

        /// <summary>Base value for bottom flange offset calculation (inches).</summary>
        private const double BottomOffsetBase = 0.847;

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
                        "Select pipe runs to hang, then press Finish");
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

                var allPipes = pipeRefs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList();

                // ── Step 2: Gather info for dialog ──
                IList<string> hangerFamilies = GetHangerFamilyNames(doc);
                if (hangerFamilies.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No pipe accessory families found. Load a hanger family first.");
                    return Result.Failed;
                }

                var pipeTypeNames = GetPipeTypeNames(doc);
                var links = StructuralFramingHelpers.GetRevitLinks(doc);
                var linkNames = links.Select(l => l.Name).ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new HangParallelStructuralDialog(
                    hangerFamilies, pipeTypeNames, linkNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ── Step 4: Filter pipes ──
                    var filteredPipes = FilterPipes(allPipes, dialog.PipeTypeFilter);
                    if (filteredPipes.Count == 0)
                    {
                        TaskDialog.Show("Auto Hang", "No qualifying pipes after filtering.");
                        return Result.Cancelled;
                    }

                    // ── Step 5: Collect structural framing ──
                    List<StructuralFramingHelpers.FramingInfo> framingList;

                    if (dialog.UseLocalFraming)
                    {
                        var localFraming = StructuralFramingHelpers.GetLocalStructuralFraming(doc);
                        framingList = StructuralFramingHelpers.BuildFramingInfoList(localFraming);
                    }
                    else
                    {
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

                    // ── Step 6: Find hanger family type ──
                    FamilySymbol hangerType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (hangerType == null)
                    {
                        TaskDialog.Show("Auto Hang", $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    // ── Step 7: Place hangers ──
                    int hangersPlaced = 0;
                    int pipesProcessed = 0;
                    int structuralHits = 0;
                    int widemouthCount = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Hang Parallel Structural"))
                    {
                        if (!hangerType.IsActive)
                            hangerType.Activate();

                        foreach (Element pipe in filteredPipes)
                        {
                            Line pipeLine = GetPipeCenterline(pipe);
                            if (pipeLine == null) continue;

                            double pipeLength = pipeLine.Length;
                            if (pipeLength < MinPipeLengthFeet) continue;

                            ElementId levelId = pipe.LookupParameter("Reference Level")
                                ?.AsElementId() ?? pipe.LevelId;
                            Level level = doc.GetElement(levelId) as Level;
                            if (level == null) continue;

                            double pipeDiameter = ParameterHelpers.GetPipeDiameterValue(pipe);

                            XYZ pipeStart = pipeLine.GetEndPoint(0);
                            XYZ pipeEnd = pipeLine.GetEndPoint(1);
                            XYZ pipeDir = (pipeEnd - pipeStart).Normalize();
                            double pipeRotation = Math.Atan2(pipeDir.Y, pipeDir.X);

                            // Perpendicular direction (rotated 90° about Z)
                            XYZ perpDir = new XYZ(-pipeDir.Y, pipeDir.X, 0).Normalize();

                            // ── Calculate hanger points ──
                            List<XYZ> hangerPoints = CalculateHangerPoints(
                                pipeStart, pipeEnd, pipeDir, pipeLength,
                                dialog.MaxSpacingFeet, dialog.EvenlyDistributed);

                            if (hangerPoints.Count == 0) continue;

                            bool anyPlaced = false;

                            foreach (XYZ hangerPoint in hangerPoints)
                            {
                                // ── Find nearest parallel structural member ──
                                var nearestFraming = FindNearestParallelFraming(
                                    hangerPoint, perpDir, framingList);

                                double rodLength = pipeDiameter; // default
                                double topAccessoryOffset = 0;
                                double clampAngle = 0;
                                string typeCode = dialog.HangerTypeCode;
                                string structureName = "";
                                bool isWidemouth = false;

                                if (nearestFraming != null)
                                {
                                    structuralHits++;

                                    // Determine attachment Z
                                    double attachZ;
                                    double flangeThicknessInches = EstimateFlangeThickness(nearestFraming);

                                    if (dialog.AttachToBottom)
                                    {
                                        attachZ = nearestFraming.BottomZ;
                                        topAccessoryOffset = (BottomOffsetBase - flangeThicknessInches) / 12.0;
                                        if (topAccessoryOffset < 0) topAccessoryOffset = 0;
                                    }
                                    else
                                    {
                                        attachZ = nearestFraming.TopZ;
                                        topAccessoryOffset = TopAttachOffset;
                                    }

                                    // Rod length = distance from pipe to structural attachment
                                    rodLength = Math.Abs(attachZ - hangerPoint.Z);
                                    if (rodLength < 0.01) rodLength = pipeDiameter;

                                    // Widemouth check: flange > 0.75"
                                    isWidemouth = flangeThicknessInches > WidemouthFlangeThresholdInches;
                                    if (isWidemouth)
                                    {
                                        typeCode = dialog.WidemouthTypeCode;
                                        widemouthCount++;
                                    }

                                    // Clamp angle
                                    clampAngle = StructuralFramingHelpers.CalculateClampAngle(
                                        hangerPoint, pipeRotation, nearestFraming.Centerline);

                                    structureName = nearestFraming.Name;
                                }

                                // ── Place hanger ──
                                FamilyInstance hanger = doc.Create.NewFamilyInstance(
                                    hangerPoint, hangerType, level,
                                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                if (hanger == null) continue;

                                // Rotate to pipe direction
                                Line rotAxis = Line.CreateBound(
                                    hangerPoint,
                                    new XYZ(hangerPoint.X, hangerPoint.Y, hangerPoint.Z + 1));
                                ElementTransformUtils.RotateElement(
                                    doc, hanger.Id, rotAxis, pipeRotation);

                                // ── Set parameters ──
                                SetParamSafe(hanger, "Nominal Diameter", pipeDiameter);
                                SetParamSafe(hanger, "Rod Length", Math.Round(rodLength, 4));

                                double elevFromLevel = hangerPoint.Z - level.Elevation;
                                SetParamSafe(hanger, "Elevation from Level", elevFromLevel);

                                // Top accessory offset
                                SetParamSafe(hanger, "Top Accessory Offset", topAccessoryOffset);

                                // Clamp angle
                                SetParamSafe(hanger, "Clamp Angle", clampAngle);

                                // Hydratec parameters
                                SetParamSafe(hanger, "Type Code (Hydratec)", typeCode);
                                SetParamSafe(hanger, "Additional Stocklist Information (Hydratec)",
                                    !string.IsNullOrEmpty(structureName) ? structureName : "CON1," + pipe.Id.ToString());

                                // C-Clamp
                                SetParamSafe(hanger, "C Clamp", dialog.ShowCClamp ? 1.0 : 0.0);

                                // Comments
                                SetParamSafe(hanger, "Comments", structureName);

                                hangersPlaced++;
                                anyPlaced = true;
                            }

                            if (anyPlaced) pipesProcessed++;
                        }

                        tw.Commit();
                    }

                    string spacingDesc = dialog.EvenlyDistributed
                        ? $"evenly distributed (max {dialog.MaxSpacingFeet:F1}' apart)"
                        : $"exact spacing at {dialog.MaxSpacingFeet:F1}' intervals";

                    TaskDialog.Show("Auto Hang — Complete",
                        $"Placed {hangersPlaced} hangers across {pipesProcessed} pipes.\n" +
                        $"Spacing: {spacingDesc}\n" +
                        $"Structural hits: {structuralHits} (of {hangersPlaced} hangers found parallel framing)\n" +
                        $"Widemouth hangers: {widemouthCount}\n" +
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

        // ══════════════════════════════════════════════════════════════
        //  PARALLEL STRUCTURAL SEARCH
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Find the nearest structural framing member that runs parallel to the pipe,
        /// by searching perpendicular to the pipe direction from the hanger point.
        ///
        /// The search creates a perpendicular "probe" extending PerpendicularSearchDist
        /// in each direction from the hanger point, and checks which structural members
        /// fall within vertical range.
        /// </summary>
        private StructuralFramingHelpers.FramingInfo FindNearestParallelFraming(
            XYZ hangerPoint, XYZ perpDir,
            List<StructuralFramingHelpers.FramingInfo> framingList)
        {
            StructuralFramingHelpers.FramingInfo nearest = null;
            double nearestDist = double.MaxValue;

            foreach (var framing in framingList)
            {
                // Vertical proximity check
                double vertDist = Math.Min(
                    Math.Abs(framing.TopZ - hangerPoint.Z),
                    Math.Abs(framing.BottomZ - hangerPoint.Z));
                if (vertDist > VerticalSearchRange) continue;

                // Perpendicular distance: project hanger→framing vector onto perpDir
                XYZ framingMid = (framing.Centerline.GetEndPoint(0) + framing.Centerline.GetEndPoint(1)) / 2.0;
                XYZ toFraming = new XYZ(framingMid.X - hangerPoint.X, framingMid.Y - hangerPoint.Y, 0);

                double perpDist = Math.Abs(toFraming.DotProduct(perpDir));
                if (perpDist > PerpendicularSearchDist) continue;

                // Also check that the framing member is alongside this section of pipe
                // (the hanger point's projection falls within the framing member's extent)
                XYZ framingStart = framing.Centerline.GetEndPoint(0);
                XYZ framingEnd = framing.Centerline.GetEndPoint(1);
                XYZ framingDir = (framingEnd - framingStart).Normalize();

                XYZ toHanger = new XYZ(hangerPoint.X - framingStart.X, hangerPoint.Y - framingStart.Y, 0);
                double alongParam = toHanger.DotProduct(new XYZ(framingDir.X, framingDir.Y, 0));
                double framingLength2D = new XYZ(framingEnd.X - framingStart.X, framingEnd.Y - framingStart.Y, 0).GetLength();

                // Allow some tolerance beyond the member ends
                if (alongParam < -1.0 || alongParam > framingLength2D + 1.0) continue;

                // Score by perpendicular distance (closer = better)
                if (perpDist < nearestDist)
                {
                    nearestDist = perpDist;
                    nearest = framing;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Estimate flange thickness from the structural member's bounding box depth.
        /// For W-shapes, flange thickness is typically (total depth - web depth) / 2,
        /// but since we don't have detailed geometry, we estimate from the bounding box.
        /// Returns thickness in inches.
        /// </summary>
        private double EstimateFlangeThickness(StructuralFramingHelpers.FramingInfo framing)
        {
            double totalDepthFeet = framing.TopZ - framing.BottomZ;
            double totalDepthInches = totalDepthFeet * 12.0;

            // Typical W-shape flange thickness as fraction of total depth:
            // W8: ~0.4", W10: ~0.5", W12: ~0.5", W14: ~0.6", W16: ~0.7"
            // Conservative estimate: ~5% of total depth, min 0.3", max 1.5"
            double estimatedFlange = totalDepthInches * 0.05;
            if (estimatedFlange < 0.3) estimatedFlange = 0.3;
            if (estimatedFlange > 1.5) estimatedFlange = 1.5;

            return Math.Round(estimatedFlange, 3);
        }

        // ══════════════════════════════════════════════════════════════
        //  HANGER SPACING (same as Decks version)
        // ══════════════════════════════════════════════════════════════

        private List<XYZ> CalculateHangerPoints(
            XYZ pipeStart, XYZ pipeEnd, XYZ pipeDir, double pipeLength,
            double maxSpacing, bool evenlyDistributed)
        {
            var points = new List<XYZ>();
            double usableLength = pipeLength - (2 * EndOffsetFeet);

            if (usableLength <= 0)
            {
                points.Add((pipeStart + pipeEnd) / 2.0);
                return points;
            }

            if (evenlyDistributed)
            {
                int numSpans = Math.Max(1, (int)Math.Ceiling(usableLength / maxSpacing));
                double actualSpacing = usableLength / numSpans;

                for (int i = 0; i <= numSpans; i++)
                {
                    double distFromStart = EndOffsetFeet + (i * actualSpacing);
                    points.Add(pipeStart + pipeDir * distFromStart);
                }
            }
            else
            {
                double dist = EndOffsetFeet;
                while (dist <= pipeLength - EndOffsetFeet + 0.001)
                {
                    points.Add(pipeStart + pipeDir * dist);
                    dist += maxSpacing;
                }
                if (points.Count == 0)
                    points.Add((pipeStart + pipeEnd) / 2.0);
            }

            return points;
        }

        // ══════════════════════════════════════════════════════════════
        //  PIPE FILTERING
        // ══════════════════════════════════════════════════════════════

        private List<Element> FilterPipes(List<Element> pipes, string typeFilter)
        {
            var result = new List<Element>();
            bool filterAll = (typeFilter == "ALL Pipes");

            foreach (Element pipe in pipes)
            {
                if (!filterAll)
                {
                    string typeName = ParameterHelpers.GetParamValueAsString(pipe, "Type Name");
                    string familyAndType = ParameterHelpers.GetParamValueAsString(pipe, "Family and Type");
                    if (typeName.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        familyAndType.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                if (IsSteepPipe(pipe)) continue;
                if (GetPipeLength(pipe) < MinPipeLengthFeet) continue;
                result.Add(pipe);
            }
            return result;
        }

        private bool IsSteepPipe(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            if (lc == null) return true;
            Line line = lc.Curve as Line;
            if (line == null) return true;
            XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
            double dirXYLen = new XYZ(dir.X, dir.Y, 0).GetLength();
            if (dirXYLen < 1e-10) return true;
            double angle = Math.Acos(Math.Min(1.0, dirXYLen)) * 180.0 / Math.PI;
            return angle > MaxSlopeAngle;
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        private Line GetPipeCenterline(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc?.Curve as Line;
        }

        private double GetPipeLength(Element pipe)
        {
            Parameter p = pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            return p?.AsDouble() ?? 0;
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

        private IList<string> GetPipeTypeNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType))
                .Cast<Autodesk.Revit.DB.Plumbing.PipeType>()
                .Select(pt => pt.Name)
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

