using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Commands.Hangers.PlaceHangers;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Places pipe hangers at the downstream ends of threaded branchline pipes.
    /// Uses raybounce (ReferenceIntersector) upward to find the nearest structural
    /// element above each hanger point, then sets rod length accordingly. Hanger type
    /// codes are assigned based on the structural category hit (roof, floor, framing, stairs).
    ///
    /// For small-diameter pipes (&lt; 1.5") longer than 12 feet, an additional midpoint
    /// hanger is placed.
    ///
    /// WORKFLOW:
    ///   1. User selects threaded line pipes
    ///   2. Dialog: hanger family, type codes per structural category, placement distance, min length
    ///   3. Filter pipes: type name, slope, minimum length, connected POL fittings
    ///   4. Find downstream end of each pipe (opposite the POL/main connection)
    ///   5. Calculate hanger point at specified distance from downstream end
    ///   6. Optional: add midpoint hanger for small-diameter long pipes
    ///   7. Raybounce upward from each hanger point to find nearest structure
    ///   8. Place hangers, set rod length from raybounce distance, assign type code by category
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HangDownstreamCommand : IExternalCommand
    {
        private const double MaxSlopeAngle = 60.0;

        /// <summary>Small pipe threshold: 1.5 inches = 0.125 feet.</summary>
        private const double SmallPipeDiameterFeet = 1.5 / 12.0;

        /// <summary>Long pipe threshold for extra midpoint hanger: 12 feet.</summary>
        private const double LongPipeLengthFeet = 12.0;

        /// <summary>
        /// Pipe type names that qualify as threaded branchlines.
        /// Case-insensitive substring match.
        /// </summary>
        private static readonly string[] ThreadedTypeFilters = new[]
        {
            "Sched 40 Line",
            "Lines - Threaded",
            "Lines-Threaded"
        };

        /// <summary>
        /// Family name substrings that identify POL (pipe-o-let / outlet) fittings.
        /// If a pipe has a connected fitting matching one of these, that end is "upstream."
        /// </summary>
        private static readonly string[] POLFittingPatterns = new[]
        {
            "-POL",
            "O-LET",
            "OLET"
        };

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
                        "Select THREADED LINE pipes, then press Finish");
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

                // ── Step 2: Filter pipes ──
                var filteredPipes = FilterPipes(doc, allPipes);
                if (filteredPipes.Count == 0)
                {
                    TaskDialog.Show("Auto Hang",
                        "No qualifying threaded line pipes after filtering.\n" +
                        "Pipes must match type 'Lines - Threaded' or 'Sched 40 Line', " +
                        "not be vertical (>60°), and not be too short.");
                    return Result.Cancelled;
                }

                // ── Step 3: Show dialog ──
                IList<string> hangerFamilies = GetHangerFamilyNames(doc);
                if (hangerFamilies.Count == 0)
                {
                    TaskDialog.Show("Auto Hang", "No pipe accessory families found. Load a hanger family first.");
                    return Result.Failed;
                }

                using (var dialog = new HangDownstreamDialog(hangerFamilies))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    var cfg = new DownstreamConfig
                    {
                        SelectedFamily = dialog.SelectedFamily,
                        RoofTypeCode = dialog.RoofTypeCode,
                        FloorDeckTypeCode = dialog.FloorDeckTypeCode,
                        FramingTypeCode = dialog.FramingTypeCode,
                        StairsTypeCode = dialog.StairsTypeCode,
                        DistanceFromEndInches = dialog.DistanceFromEndInches,
                        MinPipeLengthInches = dialog.MinPipeLengthInches,
                        ShowCClamp = dialog.ShowCClamp
                    };
                    return RunPlacement(uidoc, cfg, allPipes, ref message);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Places downstream-end hangers on the given pre-picked pipes.
        /// Shared by this command's Execute and the unified Place Hangers
        /// command. Everything after the dialog lives here.
        /// </summary>
        public Result RunPlacement(UIDocument uidoc, DownstreamConfig cfg,
            IList<Element> allPipes, ref string message)
        {
            Document doc = uidoc.Document;
            try
            {
                var filteredPipes = FilterPipes(doc, allPipes.ToList());

                double distFromEndFeet = cfg.DistanceFromEndInches / 12.0;
                double minLengthFeet = cfg.MinPipeLengthInches / 12.0;

                // Apply minimum length filter
                filteredPipes = filteredPipes
                    .Where(p => GetPipeLength(p) >= minLengthFeet)
                    .ToList();

                if (filteredPipes.Count == 0)
                {
                    TaskDialog.Show("Auto Hang",
                        $"No pipes meet the minimum length of {cfg.MinPipeLengthInches}\".");
                    return Result.Cancelled;
                }

                // ── Step 4: Find hanger family type ──
                FamilySymbol hangerType = FindHangerFamilyType(doc, cfg.SelectedFamily);
                if (hangerType == null)
                {
                    TaskDialog.Show("Auto Hang",
                        $"Could not find family type for '{cfg.SelectedFamily}'.");
                    return Result.Failed;
                }

                // ── Step 5: Process pipes and place hangers ──
                int hangersPlaced = 0;
                int pipesProcessed = 0;
                int raybounceHits = 0;

                using (var tw = new TransactionWrapper(doc, "Auto Hang Downstream Ends"))
                {
                    if (!hangerType.IsActive)
                        hangerType.Activate();

                    // Get or create the raybounce 3D view
                    View3D rayView = RayBounceHelpers.GetOrCreateRayBounceView(doc);
                    doc.Regenerate(); // ensure view is ready for raybounce

                    foreach (Element pipe in filteredPipes)
                    {
                        Line pipeLine = GetPipeCenterline(pipe);
                        if (pipeLine == null) continue;

                        ElementId levelId = pipe.LookupParameter("Reference Level")
                            ?.AsElementId() ?? pipe.LevelId;
                        Level level = doc.GetElement(levelId) as Level;
                        if (level == null) continue;

                        double pipeDiameter = ParameterHelpers.GetPipeDiameterValue(pipe);
                        double pipeLength = GetPipeLength(pipe);

                        // Determine downstream end (opposite the POL/main connection)
                        XYZ downstreamEnd = GetDownstreamEnd(doc, pipe, pipeLine);

                        // Pipe direction from upstream to downstream
                        XYZ upstreamEnd = pipeLine.GetEndPoint(0);
                        if (IsCloserTo(upstreamEnd, downstreamEnd))
                            upstreamEnd = pipeLine.GetEndPoint(1);

                        XYZ pipeDir = (downstreamEnd - upstreamEnd).Normalize();
                        double pipeRotation = Math.Atan2(pipeDir.Y, pipeDir.X);

                        // ── Calculate hanger points ──
                        var hangerPoints = new List<XYZ>();

                        // Primary hanger: at specified distance from downstream end
                        if (pipeLength > distFromEndFeet)
                        {
                            XYZ primaryPoint = downstreamEnd - pipeDir * distFromEndFeet;
                            hangerPoints.Add(primaryPoint);
                        }
                        else
                        {
                            // Pipe too short for offset — place at midpoint
                            XYZ midpoint = (upstreamEnd + downstreamEnd) / 2.0;
                            hangerPoints.Add(midpoint);
                        }

                        // Extra midpoint hanger for small-diameter long pipes
                        if (pipeDiameter < SmallPipeDiameterFeet && pipeLength > LongPipeLengthFeet)
                        {
                            XYZ midpoint = (upstreamEnd + downstreamEnd) / 2.0;
                            // Only add if it's not too close to the primary point
                            if (hangerPoints.Count == 0 ||
                                hangerPoints[0].DistanceTo(midpoint) > 1.0) // at least 1 foot apart
                            {
                                hangerPoints.Add(midpoint);
                            }
                        }

                        bool anyPlaced = false;

                        foreach (XYZ hangerPoint in hangerPoints)
                        {
                            // ── Raybounce: shoot ray upward to find structure ──
                            double rodLength = pipeDiameter; // default rod length = pipe diameter
                            string typeCode = cfg.FramingTypeCode; // default
                            string structureName = "";

                            if (rayView != null)
                            {
                                var hit = RayBounceHelpers.ShootRayUpward(
                                    doc, rayView, hangerPoint);

                                if (hit != null)
                                {
                                    rodLength = hit.Distance;
                                    raybounceHits++;

                                    typeCode = RayBounceHelpers.GetTypeCodeForCategory(
                                        hit.CategoryName,
                                        cfg.RoofTypeCode,
                                        cfg.FloorDeckTypeCode,
                                        cfg.FramingTypeCode,
                                        cfg.StairsTypeCode);

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
                            SetParamSafe(hanger, "Rod Length", rodLength);

                            // Elevation from level
                            double elevFromLevel = hangerPoint.Z - level.Elevation;
                            SetParamSafe(hanger, "Elevation from Level", elevFromLevel);

                            // Hydratec family parameters
                            SetParamSafe(hanger, "Type Code (Hydratec)", typeCode);
                            if (!string.IsNullOrEmpty(structureName))
                                SetParamSafe(hanger, "Additional Stocklist Information (Hydratec)", structureName);

                            // C-clamp visibility
                            SetParamSafe(hanger, "C Clamp", cfg.ShowCClamp ? 1.0 : 0.0);

                            // Comments — structure info (duplicated for non-Hydratec families)
                            if (!string.IsNullOrEmpty(structureName))
                                SetParamSafe(hanger, "Comments", structureName);

                            hangersPlaced++;
                            anyPlaced = true;
                        }

                        if (anyPlaced) pipesProcessed++;
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Auto Hang — Complete",
                    $"Placed {hangersPlaced} hangers across {pipesProcessed} pipes.\n" +
                    $"Raybounce hits: {raybounceHits} (rod length set from structure above)\n" +
                    $"Pipes analyzed: {filteredPipes.Count} (after filtering)");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  PIPE FILTERING
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Filter pipes to only threaded branchlines that aren't vertical.
        /// Does NOT apply length filter (that comes after the dialog).
        /// </summary>
        private List<Element> FilterPipes(Document doc, List<Element> pipes)
        {
            var result = new List<Element>();

            foreach (Element pipe in pipes)
            {
                // Type name filter
                string typeName = ParameterHelpers.GetParamValueAsString(pipe, "Type Name");
                if (string.IsNullOrEmpty(typeName)) continue;

                bool matchesType = false;
                foreach (string filter in ThreadedTypeFilters)
                {
                    if (typeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matchesType = true;
                        break;
                    }
                }
                if (!matchesType) continue;

                // Slope filter — exclude steep/vertical pipes
                if (IsSteepPipe(pipe)) continue;

                result.Add(pipe);
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════
        //  DOWNSTREAM END DETECTION
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Determine the downstream end of a pipe. The "upstream" end is connected
        /// to a POL fitting (pipe-o-let) or the main. The downstream end is the
        /// opposite end where the hanger should be placed.
        /// </summary>
        private XYZ GetDownstreamEnd(Document doc, Element pipe, Line pipeLine)
        {
            XYZ start = pipeLine.GetEndPoint(0);
            XYZ end = pipeLine.GetEndPoint(1);

            // Check which end has a connected POL fitting
            var connectors = GetConnectedElements(pipe);

            foreach (var connected in connectors)
            {
                string familyName = GetFamilyName(connected);
                if (string.IsNullOrEmpty(familyName)) continue;

                bool isPOL = false;
                foreach (string pattern in POLFittingPatterns)
                {
                    if (familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isPOL = true;
                        break;
                    }
                }

                if (isPOL)
                {
                    // The POL is at the upstream end — find which pipe end it's near
                    XYZ polLocation = GetElementLocation(connected);
                    if (polLocation == null) continue;

                    // The downstream end is the one farther from the POL
                    double distToStart = polLocation.DistanceTo(start);
                    double distToEnd = polLocation.DistanceTo(end);

                    return distToStart < distToEnd ? end : start;
                }
            }

            // No POL found — default to end point (endpoint 1)
            return end;
        }

        /// <summary>
        /// Get all elements connected to this pipe via its connectors.
        /// Uses the Revit ConnectorManager API.
        /// </summary>
        private List<Element> GetConnectedElements(Element pipe)
        {
            var result = new List<Element>();
            var connMgr = GetConnectorManager(pipe);
            if (connMgr == null) return result;

            foreach (Connector connector in connMgr.Connectors)
            {
                var allRefs = connector.AllRefs;
                if (allRefs == null) continue;

                foreach (Connector refConnector in allRefs)
                {
                    Element connected = refConnector.Owner;
                    if (connected != null && connected.Id != pipe.Id)
                        result.Add(connected);
                }
            }

            return result;
        }

        /// <summary>
        /// Get the ConnectorManager from an element (pipe, fitting, etc.).
        /// </summary>
        private Autodesk.Revit.DB.ConnectorManager GetConnectorManager(Element element)
        {
            if (element is Autodesk.Revit.DB.Plumbing.Pipe pipe)
                return pipe.ConnectorManager;

            if (element is FamilyInstance fi)
            {
                var mepModel = fi.MEPModel;
                return mepModel?.ConnectorManager;
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  GEOMETRY HELPERS
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

        private double GetPipeLength(Element pipe)
        {
            Parameter p = pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            return p?.AsDouble() ?? 0;
        }

        private XYZ GetElementLocation(Element element)
        {
            if (element.Location is LocationPoint lp)
                return lp.Point;
            if (element.Location is LocationCurve lc)
            {
                XYZ s = lc.Curve.GetEndPoint(0);
                XYZ e = lc.Curve.GetEndPoint(1);
                return (s + e) / 2.0;
            }
            return null;
        }

        private bool IsCloserTo(XYZ point, XYZ target)
        {
            // Helper to check if two points are very close (essentially the same endpoint)
            return point.DistanceTo(target) < 0.01;
        }

        // ══════════════════════════════════════════════════════════════
        //  FAMILY / TYPE HELPERS
        // ══════════════════════════════════════════════════════════════

        private string GetFamilyName(Element element)
        {
            if (element is FamilyInstance fi)
                return fi.Symbol?.Family?.Name ?? "";
            return "";
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

