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
    /// Places standard pipe trapeze hangers at auto-spaced intervals along pipe runs.
    /// Each trapeze has two rods that anchor to structural framing above the pipe.
    /// The rods are positioned on either side of the nearest structural beam, with
    /// configurable placement at the closest side or middle of the structural member.
    ///
    /// Migrated from: "Auto Trapeze Hang - Standard Pipe Trapeze - Auto Spaced.dyn" (V33)
    ///
    /// KEY DIFFERENCES FROM SINGLE-PIPE HANGERS:
    ///   - Two rods per hanger (Rod 1 and Rod 2) with separate offsets and top elevations
    ///   - Anchor points are on opposite sides of structural beams (offset by beam width/2 + clearance)
    ///   - Trapeze pipe elevation and supported pipe elevation tracked separately
    ///   - Rotation angle accounts for the trapeze cross-member direction
    ///   - Family type switches to Clevis variant for pipes > 8" diameter
    ///
    /// WORKFLOW:
    ///   1. User selects pipes
    ///   2. Dialog: trapeze family, spacing mode, rod position mode, structural source
    ///   3. Filter pipes by type, slope, length
    ///   4. Calculate evenly-spaced hanger points along each pipe
    ///   5. At each point, search upward for nearest structural beam
    ///   6. Calculate rod anchor points on both sides of beam centerline
    ///   7. Place trapeze family with rod offsets, top elevations, and rotation
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoTrapezeHangCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;
        private const double MinPipeLengthFeet = 2.0;
        private const double EndOffsetFeet = 0.5;

        /// <summary>Horizontal search radius for structural elements (feet).</summary>
        private const double StructuralSearchRadius = 10.0;

        /// <summary>Clearance from beam edge to rod center (feet). 0.5 inch = 0.5/12.</summary>
        private const double RodClearanceFeet = 0.5 / 12.0;

        /// <summary>Pipe diameter threshold (inches) for Clevis variant.</summary>
        private const double ClevisDiameterThresholdInches = 8.0;

        /// <summary>Minimum pipe segment length (feet) to skip fittings. 1 inch.</summary>
        private const double MinSegmentLengthFeet = 1.0 / 12.0;

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
                        "Select pipe runs for trapeze hangers, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (pipeRefs == null || pipeRefs.Count == 0)
                {
                    TaskDialog.Show("Auto Trapeze", "No pipes selected.");
                    return Result.Cancelled;
                }

                var allPipes = pipeRefs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList();

                // ── Step 2: Gather info for dialog ──
                IList<string> trapezeFamilies = GetTrapezeFamilyNames(doc);
                if (trapezeFamilies.Count == 0)
                {
                    TaskDialog.Show("Auto Trapeze",
                        "No pipe accessory families found.\n" +
                        "Load a trapeze hanger family (e.g. 'Pipe Trapeze Hanger - Single Pipe - Standard').");
                    return Result.Failed;
                }

                var pipeTypeNames = GetPipeTypeNames(doc);
                var links = StructuralFramingHelpers.GetRevitLinks(doc);
                var linkNames = links.Select(l => l.Name).ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new AutoTrapezeHangDialog(
                    trapezeFamilies, pipeTypeNames, linkNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ── Step 4: Filter pipes ──
                    var filteredPipes = FilterPipes(allPipes, dialog.PipeTypeFilter);
                    if (filteredPipes.Count == 0)
                    {
                        TaskDialog.Show("Auto Trapeze", "No qualifying pipes after filtering.");
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
                            TaskDialog.Show("Auto Trapeze", $"Could not find linked model '{dialog.SelectedLinkName}'.");
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
                        TaskDialog.Show("Auto Trapeze", "No structural framing elements found.");
                        return Result.Failed;
                    }

                    // ── Step 6: Find trapeze family type(s) ──
                    FamilySymbol trapezeType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (trapezeType == null)
                    {
                        TaskDialog.Show("Auto Trapeze",
                            $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    // Try to find Clevis variant for large pipes
                    FamilySymbol clevisType = FindClevisVariant(doc, dialog.SelectedFamily);

                    // ── Step 7: Place trapeze hangers ──
                    int hangersPlaced = 0;
                    int pipesProcessed = 0;
                    int structuralHits = 0;
                    int clevisCount = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Trapeze Hang"))
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

                            // Choose family type based on pipe diameter
                            bool useClevis = (pipeDiameterInches > ClevisDiameterThresholdInches) && clevisType != null;
                            FamilySymbol familyToPlace = useClevis ? clevisType : trapezeType;

                            // ── Calculate hanger points ──
                            List<XYZ> hangerPoints = CalculateHangerPoints(
                                pipeStart, pipeEnd, pipeDir, pipeLength,
                                dialog.MaxSpacingFeet, dialog.EvenlyDistributed);

                            if (hangerPoints.Count == 0) continue;

                            bool anyPlaced = false;

                            foreach (XYZ hangerPoint in hangerPoints)
                            {
                                // ── Find nearest structural beam above ──
                                var beamResult = FindNearestBeamAbove(
                                    hangerPoint, pipeDir, framingList, dialog.MaxClashHeightFeet);

                                if (beamResult == null) continue;

                                structuralHits++;

                                // ── Calculate rod anchor points ──
                                var rodAnchors = CalculateRodAnchors(
                                    hangerPoint, beamResult, dialog.RodPositionMode);

                                if (rodAnchors == null) continue;

                                // ── Calculate trapeze placement point (Z=0 for level-based) ──
                                double pipeZ = hangerPoint.Z;
                                double trapezePipeZ = pipeZ - (dialog.DistanceDownToTrapezeFeet);
                                XYZ placementPoint = new XYZ(hangerPoint.X, hangerPoint.Y, 0);

                                // ── Place trapeze instance ──
                                FamilyInstance trapeze = doc.Create.NewFamilyInstance(
                                    placementPoint, familyToPlace, level,
                                    Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                                if (trapeze == null) continue;

                                if (useClevis) clevisCount++;

                                // ── Rotation ──
                                // Trapeze direction = from rod anchor 1 to rod anchor 2 (perpendicular to beam)
                                XYZ trapezeDir2D = new XYZ(
                                    rodAnchors.Anchor2.X - rodAnchors.Anchor1.X,
                                    rodAnchors.Anchor2.Y - rodAnchors.Anchor1.Y, 0);
                                double trapezeAngle = 0;
                                if (trapezeDir2D.GetLength() > 1e-10)
                                {
                                    trapezeAngle = Math.Atan2(trapezeDir2D.Y, trapezeDir2D.X);
                                }

                                double instanceRotation = trapezeAngle + Math.PI / 2.0; // +90 degrees
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

                                // Trapeze pipe elevation (the cross-member)
                                SetParamSafe(trapeze, "Trapeze Pipe Elevation",
                                    trapezePipeZ - levelElev);

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

                                // Hydratec parameters — combined pipe hanger + trapeze type codes (e.g. "R3R;19A")
                                SetParamSafe(trapeze, "Type Code (Hydratec)",
                                    dialog.PipeHangerTypeCode + ";" + dialog.TrapezeTypeCode);
                                SetParamSafe(trapeze, "Additional Stocklist Information (Hydratec)",
                                    "CON1," + pipe.Id.ToString());

                                // Comments — structural member names
                                string comments = beamResult.Name;
                                SetParamSafe(trapeze, "Comments", comments);

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

                    TaskDialog.Show("Auto Trapeze — Complete",
                        $"Placed {hangersPlaced} trapeze hangers across {pipesProcessed} pipes.\n" +
                        $"Spacing: {spacingDesc}\n" +
                        $"Structural hits: {structuralHits}\n" +
                        (clevisCount > 0 ? $"Clevis (>8\" pipe): {clevisCount}\n" : "") +
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
        //  STRUCTURAL BEAM DETECTION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Find the nearest structural beam above the hanger point.
        /// Searches within StructuralSearchRadius horizontally and maxClashHeight vertically.
        /// Prefers beams that cross perpendicular to the pipe direction.
        /// </summary>
        private StructuralFramingHelpers.FramingInfo FindNearestBeamAbove(
            XYZ hangerPoint, XYZ pipeDir,
            List<StructuralFramingHelpers.FramingInfo> framingList,
            double maxClashHeight)
        {
            StructuralFramingHelpers.FramingInfo best = null;
            double bestScore = double.MaxValue;

            foreach (var framing in framingList)
            {
                // Must be above the pipe
                if (framing.BottomZ < hangerPoint.Z) continue;

                // Vertical check: within max clash height
                double vertDist = framing.BottomZ - hangerPoint.Z;
                if (vertDist > maxClashHeight) continue;

                // Horizontal proximity: closest point on centerline to hanger point (in XY)
                XYZ fStart = framing.Centerline.GetEndPoint(0);
                XYZ fEnd = framing.Centerline.GetEndPoint(1);
                XYZ fDir = (fEnd - fStart).Normalize();

                // Project hanger point onto framing centerline (2D)
                XYZ toHanger = new XYZ(hangerPoint.X - fStart.X, hangerPoint.Y - fStart.Y, 0);
                XYZ fDir2D = new XYZ(fDir.X, fDir.Y, 0);
                double fLen2D = fDir2D.GetLength();
                if (fLen2D < 1e-10) continue;
                fDir2D = fDir2D.Normalize();

                double param = toHanger.DotProduct(fDir2D);
                double framingLength2D = new XYZ(fEnd.X - fStart.X, fEnd.Y - fStart.Y, 0).GetLength();

                // Clamp parameter to framing extent with 1ft tolerance
                if (param < -1.0 || param > framingLength2D + 1.0) continue;

                XYZ closestPt2D = new XYZ(fStart.X, fStart.Y, 0) + fDir2D * Math.Max(0, Math.Min(param, framingLength2D));
                XYZ hangerPt2D = new XYZ(hangerPoint.X, hangerPoint.Y, 0);
                double horizDist = closestPt2D.DistanceTo(hangerPt2D);

                if (horizDist > StructuralSearchRadius) continue;

                // Score: prefer closest beam (combine horizontal and vertical distance)
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

        /// <summary>
        /// Data for the two rod anchor points of a trapeze hanger.
        /// </summary>
        private class RodAnchorResult
        {
            public XYZ Anchor1 { get; set; }
            public XYZ Anchor2 { get; set; }
            public double Rod1Length { get; set; }
            public double Rod2Length { get; set; }
            public double Rod1Offset { get; set; }
            public double Rod2Offset { get; set; }
        }

        /// <summary>
        /// Calculate the two rod anchor points on the structural beam.
        ///
        /// "C" (Closest Side): Rods are offset by beam-width/2 + clearance on each side
        ///                      of the beam centerline.
        /// "M" (Middle):        Both rods anchor at the beam centerline (same point).
        /// </summary>
        private RodAnchorResult CalculateRodAnchors(
            XYZ hangerPoint, StructuralFramingHelpers.FramingInfo beam, string positionMode)
        {
            // Find closest point on beam centerline to hanger point (in XY)
            XYZ bStart = beam.Centerline.GetEndPoint(0);
            XYZ bEnd = beam.Centerline.GetEndPoint(1);
            XYZ bDir = (bEnd - bStart).Normalize();

            XYZ toHanger = hangerPoint - bStart;
            double param = toHanger.DotProduct(bDir);
            XYZ closestOnBeam = bStart + bDir * param;

            // Perpendicular direction from beam centerline to hanger (in XY)
            XYZ perpToBeam = new XYZ(
                hangerPoint.X - closestOnBeam.X,
                hangerPoint.Y - closestOnBeam.Y, 0);
            double perpDist = perpToBeam.GetLength();

            XYZ perpUnit;
            if (perpDist > 1e-10)
                perpUnit = perpToBeam.Normalize();
            else
                perpUnit = new XYZ(-bDir.Y, bDir.X, 0).Normalize(); // fallback: perpendicular to beam

            // Estimate beam half-width from bounding box
            double beamHalfWidth = EstimateBeamHalfWidth(beam);
            double rodOffset = beamHalfWidth + RodClearanceFeet;

            XYZ anchor1, anchor2;

            if (positionMode == "M")
            {
                // Middle mode: both rods at beam centerline
                anchor1 = new XYZ(closestOnBeam.X, closestOnBeam.Y, beam.BottomZ);
                anchor2 = anchor1;
            }
            else
            {
                // Closest side mode: rods on opposite sides of beam
                anchor1 = new XYZ(
                    closestOnBeam.X + perpUnit.X * rodOffset,
                    closestOnBeam.Y + perpUnit.Y * rodOffset,
                    beam.BottomZ);
                anchor2 = new XYZ(
                    closestOnBeam.X - perpUnit.X * rodOffset,
                    closestOnBeam.Y - perpUnit.Y * rodOffset,
                    beam.BottomZ);
            }

            // Calculate rod lengths (from pipe point Z up to anchor Z)
            double rod1Len = Math.Abs(anchor1.Z - hangerPoint.Z);
            double rod2Len = Math.Abs(anchor2.Z - hangerPoint.Z);

            // Calculate rod offsets (horizontal distance from pipe center to each anchor)
            double rod1Off = new XYZ(anchor1.X - hangerPoint.X, anchor1.Y - hangerPoint.Y, 0).GetLength();
            double rod2Off = new XYZ(anchor2.X - hangerPoint.X, anchor2.Y - hangerPoint.Y, 0).GetLength();

            // Ensure Rod 1 is the shorter rod
            if (rod1Len > rod2Len)
            {
                return new RodAnchorResult
                {
                    Anchor1 = anchor2,
                    Anchor2 = anchor1,
                    Rod1Length = rod2Len,
                    Rod2Length = rod1Len,
                    Rod1Offset = rod2Off,
                    Rod2Offset = rod1Off
                };
            }

            return new RodAnchorResult
            {
                Anchor1 = anchor1,
                Anchor2 = anchor2,
                Rod1Length = rod1Len,
                Rod2Length = rod2Len,
                Rod1Offset = rod1Off,
                Rod2Offset = rod2Off
            };
        }

        /// <summary>
        /// Estimate half the width of the structural beam from its bounding box.
        /// The "width" is the smaller horizontal dimension (the one perpendicular
        /// to the beam's length direction).
        /// </summary>
        private double EstimateBeamHalfWidth(StructuralFramingHelpers.FramingInfo beam)
        {
            Element elem = beam.Element;
            BoundingBoxXYZ bb = elem.get_BoundingBox(null);
            if (bb == null) return 0.25; // default 3" half-width

            // Get beam direction
            XYZ bDir = (beam.Centerline.GetEndPoint(1) - beam.Centerline.GetEndPoint(0)).Normalize();

            // Bounding box dimensions in XY
            double dx = bb.Max.X - bb.Min.X;
            double dy = bb.Max.Y - bb.Min.Y;

            // The smaller of the two horizontal dimensions is the flange width
            // (the larger is along the beam's length)
            double width;
            if (Math.Abs(bDir.X) > Math.Abs(bDir.Y))
            {
                // Beam runs mostly in X → width is in Y
                width = dy;
            }
            else
            {
                // Beam runs mostly in Y → width is in X
                width = dx;
            }

            // Apply link transform if needed
            if (beam.LinkTransform != null && !beam.LinkTransform.IsIdentity)
            {
                // For linked elements, the bounding box is in link coordinates.
                // Width should still be approximately correct since scaling is uniform.
            }

            double halfWidth = width / 2.0;

            // Clamp to reasonable range (0.1' to 1.0' = 1.2" to 12")
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

        private IList<string> GetTrapezeFamilyNames(Document doc)
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

        /// <summary>
        /// Try to find a "Clevis" variant of the trapeze family for large pipes (>8").
        /// Looks for a family containing "Clevis" and "Trapeze" in the same category.
        /// </summary>
        private FamilySymbol FindClevisVariant(Document doc, string standardFamilyName)
        {
            // If the selected family already contains "Clevis", no variant needed
            if (standardFamilyName.IndexOf("Clevis", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

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
