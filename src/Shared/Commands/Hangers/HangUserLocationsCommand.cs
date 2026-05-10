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
    /// Places pipe hangers at user-specified locations marked by detail lines.
    /// The user draws detail lines perpendicular to (or crossing) pipes where
    /// they want hangers. Each intersection of a detail line with a pipe becomes
    /// a hanger location. Rod length is set by raybounce upward to the nearest
    /// structural element.
    ///
    /// This gives precise manual control over hanger placement — useful near
    /// supports, valves, equipment, or anywhere auto-spacing is inappropriate.
    ///
    /// WORKFLOW:
    ///   1. User draws detail lines across pipes in plan view to mark hanger locations
    ///   2. User selects BOTH the pipes AND the detail lines
    ///   3. Dialog: hanger family, pipe filter, type code
    ///   4. Command finds intersection points (detail line × pipe)
    ///   5. Raybounce upward from each intersection to find structure above
    ///   6. Place hangers at intersections with rod length from raybounce
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangUserLocationsCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;

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
                        "Select PIPES and DETAIL LINES, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (selRefs == null || selRefs.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No elements selected.");
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
                    TaskDialog.Show("Auto Hang", "No pipes selected. Select both pipes AND detail lines.");
                    return Result.Cancelled;
                }

                if (detailLines.Count == 0)
                {
                    TaskDialog.Show("Auto Hang",
                        "No detail lines selected.\n\n" +
                        "Draw detail lines across pipes where you want hangers placed, " +
                        "then select both the pipes and the detail lines.");
                    return Result.Cancelled;
                }

                // Filter steep/vertical pipes
                pipes = pipes.Where(p => !IsSteepPipe(p)).ToList();
                if (pipes.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No valid pipes after filtering steep/vertical pipes.");
                    return Result.Cancelled;
                }

                // ── Step 2: Show dialog ──
                IList<string> hangerFamilies = GetHangerFamilyNames(doc);
                if (hangerFamilies.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No pipe accessory families found.");
                    return Result.Failed;
                }

                var pipeTypeNames = GetPipeTypeNames(doc);

                using (var dialog = new HangUserLocationsDialog(hangerFamilies, pipeTypeNames))
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
                            TaskDialog.Show("Auto Hang", "No pipes match the selected type filter.");
                            return Result.Cancelled;
                        }
                    }

                    // ── Step 3: Find hanger family type ──
                    FamilySymbol hangerType = FindHangerFamilyType(doc, dialog.SelectedFamily);
                    if (hangerType == null)
                    {
                        TaskDialog.Show("Auto Hang",
                            $"Could not find family type for '{dialog.SelectedFamily}'.");
                        return Result.Failed;
                    }

                    // ── Step 4: Find detail line × pipe intersections ──
                    var hangerLocations = FindDetailLinePipeIntersections(doc, detailLines, pipes);

                    if (hangerLocations.Count == 0)
                    {
                        TaskDialog.Show("Auto Hang",
                            "No intersections found between detail lines and pipes.\n\n" +
                            "Make sure the detail lines cross the pipes in plan view.");
                        return Result.Cancelled;
                    }

                    // ── Step 5: Place hangers ──
                    int hangersPlaced = 0;
                    int raybounceHits = 0;

                    using (var tw = new TransactionWrapper(doc, "Auto Hang User Locations"))
                    {
                        if (!hangerType.IsActive)
                            hangerType.Activate();

                        View3D rayView = RayBounceHelpers.GetOrCreateRayBounceView(doc);
                        doc.Regenerate();

                        foreach (var loc in hangerLocations)
                        {
                            Element pipe = loc.Pipe;
                            XYZ hangerPoint = loc.Point;

                            ElementId levelId = pipe.LookupParameter("Reference Level")
                                ?.AsElementId() ?? pipe.LevelId;
                            Level level = doc.GetElement(levelId) as Level;
                            if (level == null) continue;

                            double pipeDiameter = ParameterHelpers.GetPipeDiameterValue(pipe);

                            // Pipe direction for rotation
                            Line pipeLine = GetPipeCenterline(pipe);
                            if (pipeLine == null) continue;
                            XYZ pipeDir = (pipeLine.GetEndPoint(1) - pipeLine.GetEndPoint(0)).Normalize();
                            double pipeRotation = Math.Atan2(pipeDir.Y, pipeDir.X);

                            // ── Raybounce for rod length ──
                            double rodLength = pipeDiameter; // default
                            string structureName = "";

                            if (rayView != null)
                            {
                                var hit = RayBounceHelpers.ShootRayUpward(doc, rayView, hangerPoint);
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

                            // Comments — structural element name
                            if (!string.IsNullOrEmpty(structureName))
                                SetParamSafe(hanger, "Comments", structureName);

                            hangersPlaced++;
                        }

                        tw.Commit();
                    }

                    TaskDialog.Show("Auto Hang — Complete",
                        $"Placed {hangersPlaced} hangers at user-marked locations.\n" +
                        $"Detail lines used: {detailLines.Count}\n" +
                        $"Raybounce hits: {raybounceHits} (rod length from structure above)\n" +
                        $"Pipes involved: {hangerLocations.Select(l => l.Pipe.Id).Distinct().Count()}");
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
        /// Data class for a hanger location — the intersection of a detail line with a pipe.
        /// </summary>
        private class HangerLocation
        {
            public Element Pipe { get; set; }
            public XYZ Point { get; set; }
        }

        /// <summary>
        /// Find all intersection points between detail lines and pipes (2D in plan).
        /// Each crossing produces a hanger location at the pipe's Z elevation.
        /// </summary>
        private List<HangerLocation> FindDetailLinePipeIntersections(
            Document doc, List<Element> detailLines, List<Element> pipes)
        {
            var results = new List<HangerLocation>();

            foreach (Element detailLine in detailLines)
            {
                Line dlLine = GetDetailLineGeometry(detailLine);
                if (dlLine == null) continue;

                foreach (Element pipe in pipes)
                {
                    Line pipeLine = GetPipeCenterline(pipe);
                    if (pipeLine == null) continue;

                    // 2D intersection (ignore Z)
                    XYZ intersection = IntersectionHelpers.GetSegmentIntersection2D(
                        dlLine, pipeLine);

                    if (intersection != null)
                    {
                        // Project to the pipe's 3D line to get correct Z
                        XYZ point3D = IntersectionHelpers.ProjectToPipeLine(intersection, pipeLine);

                        results.Add(new HangerLocation
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
            // Detail lines can be CurveElement (DetailCurve)
            if (detailLine is CurveElement ce)
            {
                return ce.GeometryCurve as Line;
            }

            // Fallback: LocationCurve
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
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

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

