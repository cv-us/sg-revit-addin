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
    /// Places pipe hangers at typical spacing along straight pipe runs.
    /// Rod length is determined by raybounce (ReferenceIntersector) upward to
    /// find the nearest structural element (deck, beam, roof) above each hanger.
    ///
    /// Two spacing modes:
    ///   • Evenly distributed — divides pipe length by max spacing, distributes
    ///     hangers equally along the entire run
    ///   • Exact spacing — places hangers at precisely the specified distance apart
    ///
    /// Preset spacing options: 10'-6" (default), 12'-0", 15'-0", or custom.
    ///
    /// Migrated from: "Auto Hang - Typical Spaced Runs - Hangers to Decks.dyn" (V18)
    ///
    /// WORKFLOW:
    ///   1. User selects pipes (optionally filtered by type)
    ///   2. Dialog: family, spacing mode/distance, type code, structural link, clash height
    ///   3. Filter pipes by type, slope, length
    ///   4. Calculate evenly-spaced or exact-interval hanger points along each pipe
    ///   5. Raybounce upward from each point to find structure above
    ///   6. Place hangers, set rod length, rotation, Hydratec parameters
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoHangTypicalSpacingCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;

        /// <summary>Minimum pipe length to hang (2 feet). Shorter pipes are skipped.</summary>
        private const double MinPipeLengthFeet = 2.0;

        /// <summary>Minimum distance from pipe ends for first/last hanger (6 inches).</summary>
        private const double EndOffsetFeet = 0.5;

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
                using (var dialog = new AutoHangTypicalSpacingDialog(
                    hangerFamilies, pipeTypeNames, linkNames))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // ── Step 4: Filter pipes ──
                    var filteredPipes = FilterPipes(allPipes, dialog.PipeTypeFilter);
                    if (filteredPipes.Count == 0)
                    {
                        TaskDialog.Show("Auto Hang",
                            "No qualifying pipes after filtering by type, slope, and length.");
                        return Result.Cancelled;
                    }

                    // ── Step 5: Find hanger family type ──
                    FamilySymbol hangerType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (hangerType == null)
                    {
                        TaskDialog.Show("Auto Hang",
                            $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    // ── Step 6: Place hangers ──
                    int hangersPlaced = 0;
                    int pipesProcessed = 0;
                    int raybounceHits = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Hang Typical Spacing"))
                    {
                        if (!hangerType.IsActive)
                            hangerType.Activate();

                        // Get or create raybounce view
                        View3D rayView = RayBounceHelpers.GetOrCreateRayBounceView(doc);
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

                            XYZ pipeStart = pipeLine.GetEndPoint(0);
                            XYZ pipeEnd = pipeLine.GetEndPoint(1);
                            XYZ pipeDir = (pipeEnd - pipeStart).Normalize();
                            double pipeRotation = Math.Atan2(pipeDir.Y, pipeDir.X);

                            // ── Calculate hanger points along this pipe ──
                            List<XYZ> hangerPoints = CalculateHangerPoints(
                                pipeStart, pipeEnd, pipeDir, pipeLength,
                                dialog.MaxSpacingFeet, dialog.EvenlyDistributed);

                            if (hangerPoints.Count == 0) continue;

                            bool anyPlaced = false;

                            foreach (XYZ hangerPoint in hangerPoints)
                            {
                                // ── Raybounce for rod length ──
                                double rodLength = pipeDiameter; // default
                                string structureName = "";

                                if (rayView != null)
                                {
                                    var hit = RayBounceHelpers.ShootRayUpward(
                                        doc, rayView, hangerPoint, dialog.MaxClashHeightFeet);

                                    if (hit != null)
                                    {
                                        rodLength = hit.Distance;
                                        raybounceHits++;
                                        structureName = GetElementTypeName(hit.HitElement);
                                    }
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

                                // Hydratec parameters
                                SetParamSafe(hanger, "Type Code (Hydratec)", dialog.HangerTypeCode);
                                SetParamSafe(hanger, "Additional Stocklist Information (Hydratec)",
                                    "CON1," + pipe.Id.ToString());

                                // C Clamp — off by default
                                SetParamSafe(hanger, "C Clamp", 0.0);

                                // Comments — hanger family name
                                SetParamSafe(hanger, "Comments", dialog.SelectedFamily);

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
                        $"Raybounce hits: {raybounceHits} (rod length from structure above)\n" +
                        $"Pipes analyzed: {filteredPipes.Count}");
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
        //  HANGER SPACING CALCULATION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Calculate hanger placement points along a pipe.
        ///
        /// Two modes:
        ///   Evenly distributed: divides usable pipe length by max spacing to get
        ///     hanger count, then spaces them equally. Hangers are offset from pipe
        ///     ends by EndOffsetFeet.
        ///   Exact spacing: places hangers at precisely maxSpacing apart, starting
        ///     from EndOffsetFeet from the start.
        /// </summary>
        private List<XYZ> CalculateHangerPoints(
            XYZ pipeStart, XYZ pipeEnd, XYZ pipeDir, double pipeLength,
            double maxSpacing, bool evenlyDistributed)
        {
            var points = new List<XYZ>();

            // Usable length after end offsets
            double usableLength = pipeLength - (2 * EndOffsetFeet);
            if (usableLength <= 0)
            {
                // Pipe too short for offsets — place one at midpoint
                points.Add((pipeStart + pipeEnd) / 2.0);
                return points;
            }

            if (evenlyDistributed)
            {
                // Calculate number of spaces (hangers = spaces)
                // e.g., 25' pipe with 10.5' max → ceil(25/10.5) = 3 spaces → 3 hangers
                //   but we want N hangers creating N+1 segments, where each segment <= maxSpacing
                //   Actually: segments = ceil(usableLength / maxSpacing)
                //   hangers = segments - 1 (placed at division points) + 2 ends = segments + 1
                //   No — for evenly distributed: number_of_spans = ceil(pipeLength / maxSpacing)
                //   number_of_hangers = number_of_spans + 1, BUT we offset from ends
                //   Let's use: number_of_spans = max(1, ceil(usableLength / maxSpacing))
                //   Then place hangers at each span boundary

                int numSpans = Math.Max(1, (int)Math.Ceiling(usableLength / maxSpacing));
                double actualSpacing = usableLength / numSpans;

                for (int i = 0; i <= numSpans; i++)
                {
                    double distFromStart = EndOffsetFeet + (i * actualSpacing);
                    XYZ point = pipeStart + pipeDir * distFromStart;
                    points.Add(point);
                }
            }
            else
            {
                // Exact spacing: place at fixed intervals
                double dist = EndOffsetFeet;
                while (dist <= pipeLength - EndOffsetFeet + 0.001) // small tolerance
                {
                    XYZ point = pipeStart + pipeDir * dist;
                    points.Add(point);
                    dist += maxSpacing;
                }

                // If no points placed (shouldn't happen), place at midpoint
                if (points.Count == 0)
                    points.Add((pipeStart + pipeEnd) / 2.0);
            }

            return points;
        }

        // ══════════════════════════════════════════════════════════════
        //  PIPE FILTERING
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Filter pipes by type name, slope, and minimum length.
        /// </summary>
        private List<Element> FilterPipes(List<Element> pipes, string typeFilter)
        {
            var result = new List<Element>();
            bool filterAll = (typeFilter == "ALL Pipes");

            foreach (Element pipe in pipes)
            {
                // Type filter
                if (!filterAll)
                {
                    string typeName = ParameterHelpers.GetParamValueAsString(pipe, "Type Name");
                    string familyAndType = ParameterHelpers.GetParamValueAsString(pipe, "Family and Type");

                    if (typeName.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        familyAndType.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                // Slope filter
                if (IsSteepPipe(pipe)) continue;

                // Length filter
                double length = GetPipeLength(pipe);
                if (length < MinPipeLengthFeet) continue;

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
        //  GEOMETRY / ELEMENT HELPERS
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

        private string GetElementTypeName(Element element)
        {
            if (element is FamilyInstance fi)
                return $"{fi.Symbol?.Family?.Name} : {fi.Symbol?.Name}";
            string typeName = ParameterHelpers.GetParamValueAsString(element, "Type Name");
            return !string.IsNullOrEmpty(typeName) ? typeName : element.Name;
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

        // ══════════════════════════════════════════════════════════════
        //  PARAMETER SETTERS
        // ══════════════════════════════════════════════════════════════

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
