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
    /// Places standard pipe trapeze hangers at user-specified locations marked by
    /// detail lines. The user draws detail lines perpendicular to (or crossing) pipes
    /// where they want trapeze hangers. Each intersection of a detail line with a pipe
    /// becomes a trapeze location.
    ///
    /// This is the user-locations variant of AutoTrapezeHangCommand — it replaces
    /// auto-spacing with precise manual control via detail lines.
    ///
    /// Migrated from: "Auto Trapeze Hang - Standard Pipe Trapeze - User Locations.dyn" (V12)
    ///
    /// WORKFLOW:
    ///   1. User draws detail lines across pipes in plan view to mark trapeze locations
    ///   2. User selects BOTH the pipes AND the detail lines
    ///   3. Dialog: trapeze family, rod position mode, type codes, structural source
    ///   4. Command finds intersection points (detail line × pipe)
    ///   5. At each intersection, search upward for nearest structural beam
    ///   6. Calculate rod anchor points on both sides of beam centerline
    ///   7. Place trapeze family with rod offsets, top elevations, and rotation
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoTrapezeUserLocationsCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;

        /// <summary>Horizontal search radius for structural elements (feet).</summary>
        private const double StructuralSearchRadius = 10.0;

        /// <summary>Clearance from beam edge to rod center (feet). 0.5 inch = 0.5/12.</summary>
        private const double RodClearanceFeet = 0.5 / 12.0;

        /// <summary>Pipe diameter threshold (inches) for Clevis variant.</summary>
        private const double ClevisDiameterThresholdInches = 8.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Select pipes AND detail lines ──
                IList<Reference> selRefs;
                try
                {
                    selRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new PipesAndLinesFilter(),
                        "Select PIPES and DETAIL LINES for trapeze hangers, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (selRefs == null || selRefs.Count == 0)
                {
                    TaskDialog.Show("Auto Trapeze", "No elements selected.");
                    return Result.Cancelled;
                }

                // Separate pipes from detail lines
                var pipes = new List<Element>();
                var detailLines = new List<Element>();

                foreach (Reference r in selRefs)
                {
                    Element elem = doc.GetElement(r);
                    if (elem == null) continue;

                    if (elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves)
                        pipes.Add(elem);
                    else if (elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Lines)
                        detailLines.Add(elem);
                }

                if (pipes.Count == 0)
                {
                    TaskDialog.Show("Auto Trapeze", "No pipes selected. Select both pipes AND detail lines.");
                    return Result.Cancelled;
                }

                if (detailLines.Count == 0)
                {
                    TaskDialog.Show("Auto Trapeze",
                        "No detail lines selected.\n\n" +
                        "Draw detail lines across pipes where you want trapeze hangers, " +
                        "then select both the pipes and the detail lines.");
                    return Result.Cancelled;
                }

                // Filter steep/vertical pipes
                pipes = pipes.Where(p => !IsSteepPipe(p)).ToList();
                if (pipes.Count == 0)
                {
                    TaskDialog.Show("Auto Trapeze", "No valid pipes after filtering steep/vertical pipes.");
                    return Result.Cancelled;
                }

                // ── Step 2: Gather info for dialog ──
                IList<string> trapezeFamilies = GetTrapezeFamilyNames(doc);
                if (trapezeFamilies.Count == 0)
                {
                    TaskDialog.Show("Auto Trapeze",
                        "No pipe accessory families found.\n" +
                        "Load a trapeze hanger family first.");
                    return Result.Failed;
                }

                var pipeTypeNames = GetPipeTypeNames(doc);
                var links = StructuralFramingHelpers.GetRevitLinks(doc);
                var linkNames = links.Select(l => l.Name).ToList();

                // ── Step 3: Show dialog ──
                using (var dialog = new AutoTrapezeUserLocationsDialog(
                    trapezeFamilies, pipeTypeNames, linkNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // Apply pipe type filter
                    if (dialog.PipeTypeFilter != "ALL Pipes")
                    {
                        pipes = pipes.Where(p =>
                        {
                            string tn = ParameterHelpers.GetParamValueAsString(p, "Type Name");
                            string ft = ParameterHelpers.GetParamValueAsString(p, "Family and Type");
                            return tn.IndexOf(dialog.PipeTypeFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   ft.IndexOf(dialog.PipeTypeFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                        }).ToList();

                        if (pipes.Count == 0)
                        {
                            TaskDialog.Show("Auto Trapeze", "No pipes match the selected type filter.");
                            return Result.Cancelled;
                        }
                    }

                    // ── Step 4: Collect structural framing ──
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

                    // ── Step 5: Find trapeze family type(s) ──
                    FamilySymbol trapezeType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (trapezeType == null)
                    {
                        TaskDialog.Show("Auto Trapeze",
                            $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    FamilySymbol clevisType = FindClevisVariant(doc, dialog.SelectedFamily);

                    // ── Step 6: Find detail line × pipe intersections ──
                    var trapezeLocations = FindDetailLinePipeIntersections(doc, detailLines, pipes);

                    if (trapezeLocations.Count == 0)
                    {
                        TaskDialog.Show("Auto Trapeze",
                            "No intersections found between detail lines and pipes.\n\n" +
                            "Make sure the detail lines cross the pipes in plan view.");
                        return Result.Cancelled;
                    }

                    // ── Step 7: Place trapeze hangers ──
                    int hangersPlaced = 0;
                    int structuralHits = 0;
                    int clevisCount = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Trapeze User Locations"))
                    {
                        if (!trapezeType.IsActive)
                            trapezeType.Activate();
                        if (clevisType != null && !clevisType.IsActive)
                            clevisType.Activate();

                        doc.Regenerate();

                        foreach (var loc in trapezeLocations)
                        {
                            Element pipe = loc.Pipe;
                            XYZ hangerPoint = loc.Point;

                            ElementId levelId = pipe.LookupParameter("Reference Level")
                                ?.AsElementId() ?? pipe.LevelId;
                            Level level = doc.GetElement(levelId) as Level;
                            if (level == null) continue;

                            double pipeDiameter = ParameterHelpers.GetPipeDiameterValue(pipe);
                            double pipeDiameterInches = pipeDiameter * 12.0;

                            // Pipe direction for rotation
                            Line pipeLine = GetPipeCenterline(pipe);
                            if (pipeLine == null) continue;
                            XYZ pipeDir = (pipeLine.GetEndPoint(1) - pipeLine.GetEndPoint(0)).Normalize();
                            double pipeRotation = Math.Atan2(pipeDir.Y, pipeDir.X);

                            // Choose family type based on pipe diameter
                            bool useClevis = (pipeDiameterInches > ClevisDiameterThresholdInches) && clevisType != null;
                            FamilySymbol familyToPlace = useClevis ? clevisType : trapezeType;

                            // ── Find nearest structural beam above ──
                            var beamResult = FindNearestBeamAbove(
                                hangerPoint, pipeDir, framingList, dialog.MaxClashHeightFeet);

                            if (beamResult == null) continue;

                            structuralHits++;

                            // ── Calculate rod anchor points ──
                            var rodAnchors = CalculateRodAnchors(
                                hangerPoint, beamResult, dialog.RodPositionMode);

                            if (rodAnchors == null) continue;

                            // ── Placement point (Z=0 for level-based placement) ──
                            double pipeZ = hangerPoint.Z;
                            double trapezePipeZ = pipeZ - dialog.DistanceDownToTrapezeFeet;
                            XYZ placementPoint = new XYZ(hangerPoint.X, hangerPoint.Y, 0);

                            // ── Place trapeze instance ──
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

                            // Comments — structural member name
                            if (!string.IsNullOrEmpty(beamResult.Name))
                                SetParamSafe(trapeze, "Comments", beamResult.Name);

                            hangersPlaced++;
                        }

                        tw.Commit();
                    }

                    TaskDialog.Show("Auto Trapeze — Complete",
                        $"Placed {hangersPlaced} trapeze hangers at user-marked locations.\n" +
                        $"Detail lines used: {detailLines.Count}\n" +
                        $"Structural hits: {structuralHits}\n" +
                        (clevisCount > 0 ? $"Clevis (>8\" pipe): {clevisCount}\n" : "") +
                        $"Pipes involved: {trapezeLocations.Select(l => l.Pipe.Id).Distinct().Count()}\n" +
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
        //  INTERSECTION FINDING
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Data class for a trapeze location — the intersection of a detail line with a pipe.
        /// </summary>
        private class TrapezeLocation
        {
            public Element Pipe { get; set; }
            public XYZ Point { get; set; }
        }

        /// <summary>
        /// Find all intersection points between detail lines and pipes (2D in plan).
        /// Each crossing produces a trapeze location at the pipe's Z elevation.
        /// </summary>
        private List<TrapezeLocation> FindDetailLinePipeIntersections(
            Document doc, List<Element> detailLines, List<Element> pipes)
        {
            var results = new List<TrapezeLocation>();

            foreach (Element detailLine in detailLines)
            {
                Line dlLine = GetDetailLineGeometry(detailLine);
                if (dlLine == null) continue;

                foreach (Element pipe in pipes)
                {
                    Line pipeLine = GetPipeCenterline(pipe);
                    if (pipeLine == null) continue;

                    XYZ intersection = IntersectionHelpers.GetSegmentIntersection2D(
                        dlLine, pipeLine);

                    if (intersection != null)
                    {
                        XYZ point3D = IntersectionHelpers.ProjectToPipeLine(intersection, pipeLine);

                        results.Add(new TrapezeLocation
                        {
                            Pipe = pipe,
                            Point = point3D
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Extract the geometry Line from a detail line element.
        /// </summary>
        private Line GetDetailLineGeometry(Element detailLine)
        {
            if (detailLine is CurveElement ce)
                return ce.GeometryCurve as Line;

            LocationCurve lc = detailLine.Location as LocationCurve;
            return lc?.Curve as Line;
        }

        // ══════════════════════════════════════════════════════════════
        //  SELECTION FILTER
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Selection filter that allows both pipes (OST_PipeCurves) and
        /// detail lines (OST_Lines).
        /// </summary>
        private class PipesAndLinesFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                int catId = elem.Category?.Id.IntegerValue ?? 0;
                return catId == (int)BuiltInCategory.OST_PipeCurves ||
                       catId == (int)BuiltInCategory.OST_Lines;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  STRUCTURAL BEAM DETECTION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Find the nearest structural beam above the hanger point.
        /// Searches within StructuralSearchRadius horizontally and maxClashHeight vertically.
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
                if (framing.BottomZ < hangerPoint.Z) continue;

                double vertDist = framing.BottomZ - hangerPoint.Z;
                if (vertDist > maxClashHeight) continue;

                XYZ fStart = framing.Centerline.GetEndPoint(0);
                XYZ fEnd = framing.Centerline.GetEndPoint(1);
                XYZ fDir = (fEnd - fStart).Normalize();

                XYZ toHanger = new XYZ(hangerPoint.X - fStart.X, hangerPoint.Y - fStart.Y, 0);
                XYZ fDir2D = new XYZ(fDir.X, fDir.Y, 0);
                double fLen2D = fDir2D.GetLength();
                if (fLen2D < 1e-10) continue;
                fDir2D = fDir2D.Normalize();

                double param = toHanger.DotProduct(fDir2D);
                double framingLength2D = new XYZ(fEnd.X - fStart.X, fEnd.Y - fStart.Y, 0).GetLength();

                if (param < -1.0 || param > framingLength2D + 1.0) continue;

                XYZ closestPt2D = new XYZ(fStart.X, fStart.Y, 0) +
                    fDir2D * Math.Max(0, Math.Min(param, framingLength2D));
                XYZ hangerPt2D = new XYZ(hangerPoint.X, hangerPoint.Y, 0);
                double horizDist = closestPt2D.DistanceTo(hangerPt2D);

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

        /// <summary>
        /// Calculate the two rod anchor points on the structural beam.
        /// "C" = closest side (rods offset by beam-width/2 + clearance).
        /// "M" = middle (both rods at beam centerline).
        /// </summary>
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

            // Ensure Rod 1 is the shorter rod
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
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        private void NormalizeAngle(ref double radians)
        {
            while (radians < 0) radians += 2 * Math.PI;
            while (radians >= 2 * Math.PI) radians -= 2 * Math.PI;
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

        private Line GetPipeCenterline(Element pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc?.Curve as Line;
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

        private FamilySymbol FindClevisVariant(Document doc, string standardFamilyName)
        {
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
