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
    /// Places Unistrut 21A pipe trapeze hangers at auto-spaced intervals along pipe runs.
    /// Same full feature set as the regular Unistrut command (rod position, distance-down,
    /// extensions, type codes) but pre-selects the Unistrut 21A family and defaults to
    /// type codes "04" / "21A".
    ///
    /// WORKFLOW:
    ///   1. User selects pipes
    ///   2. Dialog: family, spacing, rod position, extension settings, structural source
    ///   3. Filter pipes by type, slope, length
    ///   4. Calculate hanger points at spacing intervals
    ///   5. At each point, find structural beam above
    ///   6. Calculate rod offsets and extension distances
    ///   7. Place Unistrut 21A trapeze family with all parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TrapezeUnistrut21ACommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;
        private const double MinPipeLengthFeet = 2.0;
        private const double EndOffsetFeet = 0.5;
        private const double StructuralSearchRadius = 10.0;
        private const double RodClearanceFeet = 0.5 / 12.0;
        private const double ClevisDiameterThresholdInches = 8.0;
        private const double MinSegmentLengthFeet = 1.0 / 12.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Select pipes ──
                // Use pipes already selected before the command was run; only prompt
                // to pick when nothing valid is pre-selected.
                var allPipes = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null &&
                        e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves)
                    .ToList();

                if (allPipes.Count == 0)
                {
                    IList<Reference> pipeRefs;
                    try
                    {
                        pipeRefs = uidoc.Selection.PickObjects(
                            ObjectType.Element,
                            new CategorySelectionFilter(BuiltInCategory.OST_PipeCurves),
                            "Select pipe runs for Unistrut 21A trapeze hangers, then press Finish");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return Result.Cancelled;
                    }

                    if (pipeRefs == null || pipeRefs.Count == 0)
                    {
                        TaskDialog.Show("Unistrut 21A", "No pipes selected.");
                        return Result.Cancelled;
                    }

                    allPipes = pipeRefs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList();
                }

                // ── Step 2: Gather info for dialog ──
                IList<string> trapezeFamilies = GetUnistrut21AFamilyNames(doc);
                if (trapezeFamilies.Count == 0)
                    trapezeFamilies = GetAllPipeAccessoryFamilyNames(doc);

                if (trapezeFamilies.Count == 0)
                {
                    TaskDialog.Show("Unistrut 21A",
                        "No pipe accessory families found.\n" +
                        "Load a Unistrut 21A trapeze hanger family first.");
                    return Result.Failed;
                }

                var pipeTypeNames = GetPipeTypeNames(doc);
                var links = StructuralFramingHelpers.GetRevitLinks(doc);
                var linkNames = links.Select(l => l.Name).ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new TrapezeUnistrut21ADialog(
                    trapezeFamilies, pipeTypeNames, linkNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ── Step 4: Filter pipes ──
                    var filteredPipes = FilterPipes(allPipes, dialog.PipeTypeFilter);
                    if (filteredPipes.Count == 0)
                    {
                        TaskDialog.Show("Unistrut 21A", "No qualifying pipes after filtering.");
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
                            TaskDialog.Show("Unistrut 21A",
                                $"Could not find linked model '{dialog.SelectedLinkName}'.");
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
                        TaskDialog.Show("Unistrut 21A", "No structural framing elements found.");
                        return Result.Failed;
                    }

                    // ── Step 6: Find family type(s) ──
                    FamilySymbol trapezeType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (trapezeType == null)
                    {
                        TaskDialog.Show("Unistrut 21A",
                            $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    FamilySymbol clevisType = FindClevisVariant(doc);

                    // ── Step 7: Place hangers ──
                    int hangersPlaced = 0;
                    int pipesProcessed = 0;
                    int structuralHits = 0;
                    int clevisCount = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Trapeze Unistrut 21A"))
                    {
                        if (!trapezeType.IsActive)
                            trapezeType.Activate();
                        if (clevisType != null && !clevisType.IsActive)
                            clevisType.Activate();

                        doc.Regenerate();

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
                            double pipeDiameterInches = pipeDiameter * 12.0;

                            XYZ pipeStart = pipeLine.GetEndPoint(0);
                            XYZ pipeEnd = pipeLine.GetEndPoint(1);
                            XYZ pipeDir = (pipeEnd - pipeStart).Normalize();
                            double pipeRotation = Math.Atan2(pipeDir.Y, pipeDir.X);

                            bool useClevis = (pipeDiameterInches > ClevisDiameterThresholdInches) && clevisType != null;
                            FamilySymbol familyToPlace = useClevis ? clevisType : trapezeType;

                            List<XYZ> hangerPoints = CalculateHangerPoints(
                                pipeStart, pipeEnd, pipeDir, pipeLength,
                                dialog.MaxSpacingFeet, dialog.EvenlyDistributed);

                            if (hangerPoints.Count == 0) continue;

                            bool anyPlaced = false;

                            foreach (XYZ hangerPoint in hangerPoints)
                            {
                                var beamResult = FindNearestBeamAbove(
                                    hangerPoint, pipeDir, framingList, dialog.MaxClashHeightFeet);

                                if (beamResult == null) continue;

                                structuralHits++;

                                var rodAnchors = CalculateRodAnchors(
                                    hangerPoint, beamResult, dialog.RodPositionMode);

                                if (rodAnchors == null) continue;

                                // ── Calculate unistrut-specific values ──
                                double pipeZ = hangerPoint.Z;
                                double topOfUnistrutZ = pipeZ - dialog.DistanceDownToUnistrutFeet;

                                double extensionInches = CalculateUnistrutExtension(
                                    rodAnchors, beamResult, dialog.ExtensionMeasuredFrom,
                                    dialog.ExtensionDistanceFeet);

                                XYZ placementPoint = new XYZ(hangerPoint.X, hangerPoint.Y, 0);

                                FamilyInstance trapeze = doc.Create.NewFamilyInstance(
                                    placementPoint, familyToPlace, level,
                                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                if (trapeze == null) continue;

                                if (useClevis) clevisCount++;

                                // ── Rotation ──
                                XYZ trapezeDir2D = new XYZ(
                                    rodAnchors.Anchor2.X - rodAnchors.Anchor1.X,
                                    rodAnchors.Anchor2.Y - rodAnchors.Anchor1.Y, 0);
                                double trapezeAngle = 0;
                                if (trapezeDir2D.GetLength() > 1e-10)
                                    trapezeAngle = Math.Atan2(trapezeDir2D.Y, trapezeDir2D.X);

                                double instanceRotation = trapezeAngle + Math.PI / 2.0;
                                NormalizeAngle(ref instanceRotation);

                                Line rotAxis = Line.CreateBound(
                                    placementPoint,
                                    new XYZ(placementPoint.X, placementPoint.Y, 1));
                                ElementTransformUtils.RotateElement(
                                    doc, trapeze.Id, rotAxis, instanceRotation);

                                // ── Set parameters ──
                                double levelElev = level.Elevation;

                                // Rod 1 (shorter rod)
                                double rod1TopElev = (pipeZ - levelElev) + rodAnchors.Rod1Length;
                                SetParamSafe(trapeze, "Rod 1 Top Elevation", rod1TopElev);
                                SetParamSafe(trapeze, "Rod 1 Offset", rodAnchors.Rod1Offset);

                                // Rod 2 (longer rod)
                                double rod2TopElev = (pipeZ - levelElev) + rodAnchors.Rod2Length;
                                SetParamSafe(trapeze, "Rod 2 Top Elevation", rod2TopElev);
                                SetParamSafe(trapeze, "Rod 2 Offset", rodAnchors.Rod2Offset);

                                // Supported pipe elevation
                                SetParamSafe(trapeze, "Supported Pipe Elevation",
                                    pipeZ - levelElev);

                                // Nominal diameter
                                SetParamSafe(trapeze, "Nominal Diameter", pipeDiameter);

                                // Supported Pipe Rotation Angle
                                double supportedPipeRotAngle = (pipeRotation - trapezeAngle) * 180.0 / Math.PI;
                                if (supportedPipeRotAngle < 0) supportedPipeRotAngle += 180.0;
                                while (supportedPipeRotAngle >= 360) supportedPipeRotAngle -= 360;
                                SetParamSafe(trapeze, "Supported Pipe Rotation Angle", supportedPipeRotAngle);

                                // Unistrut-specific parameters
                                SetParamSafe(trapeze, "Top of Unistrut Elevation",
                                    topOfUnistrutZ - levelElev);
                                SetParamSafe(trapeze, "Rod 1 Unistrut Extension",
                                    extensionInches / 12.0);
                                SetParamSafe(trapeze, "Rod 2 Unistrut Extension",
                                    extensionInches / 12.0);

                                // Hydratec parameters — combined pipe hanger + trapeze type codes (e.g. "04;21A")
                                SetParamSafe(trapeze, "Type Code (Hydratec)",
                                    dialog.PipeHangerTypeCode + ";" + dialog.TrapezeTypeCode);
                                SetParamSafe(trapeze, "Additional Stocklist Information (Hydratec)",
                                    "CON1," + pipe.Id.ToString());

                                // Comments
                                if (!string.IsNullOrEmpty(beamResult.Name))
                                    SetParamSafe(trapeze, "Comments", beamResult.Name);

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

                    TaskDialog.Show("Unistrut 21A — Complete",
                        $"Placed {hangersPlaced} Unistrut 21A trapeze hangers across {pipesProcessed} pipes.\n" +
                        $"Spacing: {spacingDesc}\n" +
                        $"Structural hits: {structuralHits}\n" +
                        (clevisCount > 0 ? $"Clevis (>8\" pipe): {clevisCount}\n" : "") +
                        $"Extension: {dialog.ExtensionDistanceFeet * 12:F1}\" " +
                        $"from {(dialog.ExtensionMeasuredFrom == "F" ? "framing center" : "hanger rod")}\n" +
                        $"Rod position: {(dialog.RodPositionMode == "C" ? "Closest side" : "Middle")} of structural");
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
        //  UNISTRUT EXTENSION CALCULATION
        // ══════════════════════════════════════════════════════════════

        private double CalculateUnistrutExtension(
            RodAnchorResult rodAnchors,
            StructuralFramingHelpers.FramingInfo beam,
            string extensionFrom,
            double extensionDistanceFeet)
        {
            if (extensionFrom == "F")
            {
                XYZ bStart = beam.Centerline.GetEndPoint(0);
                XYZ bEnd = beam.Centerline.GetEndPoint(1);
                XYZ bDir = new XYZ(bEnd.X - bStart.X, bEnd.Y - bStart.Y, 0);
                double bLen = bDir.GetLength();
                if (bLen < 1e-10)
                    return extensionDistanceFeet * 12.0;

                bDir = bDir.Normalize();
                XYZ toRod = new XYZ(rodAnchors.Anchor1.X - bStart.X, rodAnchors.Anchor1.Y - bStart.Y, 0);
                double distToCenter = Math.Abs(toRod.DotProduct(new XYZ(-bDir.Y, bDir.X, 0)));

                return (distToCenter + extensionDistanceFeet) * 12.0;
            }
            else
            {
                return extensionDistanceFeet * 12.0;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  STRUCTURAL BEAM DETECTION
        // ══════════════════════════════════════════════════════════════

        private StructuralFramingHelpers.FramingInfo FindNearestBeamAbove(
            XYZ hangerPoint, XYZ pipeDir,
            List<StructuralFramingHelpers.FramingInfo> framingList,
            double maxClashHeight)
        {
            StructuralFramingHelpers.FramingInfo best = null;
            double bestScore = double.MaxValue;

            foreach (var framing in framingList)
            {
                if (framing.BottomZ < hangerPoint.Z) continue;
                double vertDist = framing.BottomZ - hangerPoint.Z;
                if (vertDist > maxClashHeight) continue;

                XYZ fStart = framing.Centerline.GetEndPoint(0);
                XYZ fEnd = framing.Centerline.GetEndPoint(1);
                XYZ fDir2D = new XYZ(fEnd.X - fStart.X, fEnd.Y - fStart.Y, 0);
                double fLen2D = fDir2D.GetLength();
                if (fLen2D < 1e-10) continue;
                fDir2D = fDir2D.Normalize();

                XYZ toHanger = new XYZ(hangerPoint.X - fStart.X, hangerPoint.Y - fStart.Y, 0);
                double param = toHanger.DotProduct(fDir2D);

                if (param < -1.0 || param > fLen2D + 1.0) continue;

                XYZ closestPt2D = new XYZ(fStart.X, fStart.Y, 0) +
                    fDir2D * Math.Max(0, Math.Min(param, fLen2D));
                double horizDist = closestPt2D.DistanceTo(new XYZ(hangerPoint.X, hangerPoint.Y, 0));

                if (horizDist > StructuralSearchRadius) continue;

                double score = horizDist + vertDist * 0.5;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = framing;
                }
            }

            return best;
        }

        // ══════════════════════════════════════════════════════════════
        //  ROD ANCHOR CALCULATION
        // ══════════════════════════════════════════════════════════════

        private class RodAnchorResult
        {
            public XYZ Anchor1 { get; set; }
            public XYZ Anchor2 { get; set; }
            public double Rod1Length { get; set; }
            public double Rod2Length { get; set; }
            public double Rod1Offset { get; set; }
            public double Rod2Offset { get; set; }
        }

        private RodAnchorResult CalculateRodAnchors(
            XYZ hangerPoint, StructuralFramingHelpers.FramingInfo beam, string positionMode)
        {
            XYZ bStart = beam.Centerline.GetEndPoint(0);
            XYZ bEnd = beam.Centerline.GetEndPoint(1);
            XYZ bDir = (bEnd - bStart).Normalize();

            XYZ toHanger = hangerPoint - bStart;
            double param = toHanger.DotProduct(bDir);
            XYZ closestOnBeam = bStart + bDir * param;

            XYZ perpToBeam = new XYZ(
                hangerPoint.X - closestOnBeam.X,
                hangerPoint.Y - closestOnBeam.Y, 0);
            double perpDist = perpToBeam.GetLength();

            XYZ perpUnit;
            if (perpDist > 1e-10)
                perpUnit = perpToBeam.Normalize();
            else
                perpUnit = new XYZ(-bDir.Y, bDir.X, 0).Normalize();

            double beamHalfWidth = EstimateBeamHalfWidth(beam);
            double rodOffset = beamHalfWidth + RodClearanceFeet;

            XYZ anchor1, anchor2;

            if (positionMode == "M")
            {
                anchor1 = new XYZ(closestOnBeam.X, closestOnBeam.Y, beam.BottomZ);
                anchor2 = anchor1;
            }
            else
            {
                anchor1 = new XYZ(
                    closestOnBeam.X + perpUnit.X * rodOffset,
                    closestOnBeam.Y + perpUnit.Y * rodOffset,
                    beam.BottomZ);
                anchor2 = new XYZ(
                    closestOnBeam.X - perpUnit.X * rodOffset,
                    closestOnBeam.Y - perpUnit.Y * rodOffset,
                    beam.BottomZ);
            }

            double rod1Len = Math.Abs(anchor1.Z - hangerPoint.Z);
            double rod2Len = Math.Abs(anchor2.Z - hangerPoint.Z);
            double rod1Off = new XYZ(anchor1.X - hangerPoint.X, anchor1.Y - hangerPoint.Y, 0).GetLength();
            double rod2Off = new XYZ(anchor2.X - hangerPoint.X, anchor2.Y - hangerPoint.Y, 0).GetLength();

            if (rod1Len > rod2Len)
            {
                return new RodAnchorResult
                {
                    Anchor1 = anchor2, Anchor2 = anchor1,
                    Rod1Length = rod2Len, Rod2Length = rod1Len,
                    Rod1Offset = rod2Off, Rod2Offset = rod1Off
                };
            }

            return new RodAnchorResult
            {
                Anchor1 = anchor1, Anchor2 = anchor2,
                Rod1Length = rod1Len, Rod2Length = rod2Len,
                Rod1Offset = rod1Off, Rod2Offset = rod2Off
            };
        }

        private double EstimateBeamHalfWidth(StructuralFramingHelpers.FramingInfo beam)
        {
            BoundingBoxXYZ bb = beam.Element.get_BoundingBox(null);
            if (bb == null) return 0.25;
            XYZ bDir = (beam.Centerline.GetEndPoint(1) - beam.Centerline.GetEndPoint(0)).Normalize();
            double dx = bb.Max.X - bb.Min.X;
            double dy = bb.Max.Y - bb.Min.Y;
            double width = Math.Abs(bDir.X) > Math.Abs(bDir.Y) ? dy : dx;
            double halfWidth = width / 2.0;
            if (halfWidth < 0.1) halfWidth = 0.1;
            if (halfWidth > 1.0) halfWidth = 1.0;
            return halfWidth;
        }

        // ══════════════════════════════════════════════════════════════
        //  HANGER SPACING
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
                if (GetPipeLength(pipe) < MinSegmentLengthFeet) continue;
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

        private void NormalizeAngle(ref double radians)
        {
            while (radians < 0) radians += 2 * Math.PI;
            while (radians >= 2 * Math.PI) radians -= 2 * Math.PI;
        }

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

        private IList<string> GetUnistrut21AFamilyNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null
                    && f.FamilyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory
                    && f.Name.IndexOf("Unistrut", StringComparison.OrdinalIgnoreCase) >= 0
                    && f.Name.IndexOf("21A", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(f => f.Name)
                .OrderBy(n => n)
                .ToList();
        }

        private IList<string> GetAllPipeAccessoryFamilyNames(Document doc)
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

        private FamilySymbol FindClevisVariant(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    fs.Family.FamilyCategory?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory
                    && fs.Family.Name.IndexOf("Trapeze", StringComparison.OrdinalIgnoreCase) >= 0
                    && fs.Family.Name.IndexOf("Clevis", StringComparison.OrdinalIgnoreCase) >= 0);
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

