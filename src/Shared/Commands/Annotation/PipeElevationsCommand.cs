using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Calculates and populates elevation parameters on selected pipes and fittings
    /// relative to two independent reference systems (TOS and AFF).
    ///
    /// WORKFLOW:
    ///   1. User selects pipes/fittings (or processes current selection)
    ///   2. Dialog: pick TOS and AFF reference methods
    ///   3. For each element:
    ///      a. Get element center Z elevation
    ///      b. Get reference elevation for TOS and AFF
    ///      c. Calculate offset = element Z - reference Z
    ///      d. Format as feet-inches-fraction string
    ///      e. Write "PipeElevationTOS" and "PipeElevationAFF"
    ///   4. For pipes only: calculate and write "Slope" parameter
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PipeElevationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Collect levels and reference planes for dialog ──
                var levelNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.ProjectElevation)
                    .Select(l => l.Name)
                    .ToList();

                var refPlaneNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(ReferencePlane))
                    .Cast<ReferencePlane>()
                    .Where(rp => !string.IsNullOrEmpty(rp.Name))
                    .Select(rp => rp.Name)
                    .OrderBy(n => n)
                    .ToList();

                // ── Step 2: Show dialog ──
                var dialog = new PipeElevationsDialog(levelNames, refPlaneNames);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                // ── Step 3: Select elements ──
                IList<Reference> elementRefs;
                try
                {
                    var filter = new PipeAndFittingFilter(dialog.ProcessPipes, dialog.ProcessFittings);
                    string prompt = "Select " +
                        (dialog.ProcessPipes && dialog.ProcessFittings ? "pipes and fittings" :
                         dialog.ProcessPipes ? "pipes" : "fittings") +
                        " to calculate elevations, then press Finish";
                    elementRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element, filter, prompt);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (elementRefs == null || elementRefs.Count == 0)
                {
                    TaskDialog.Show("Pipe Elevations", "No elements selected.");
                    return Result.Cancelled;
                }

                // ── Step 4: Resolve reference elevations ──
                double tosRefZ = ResolveReferenceElevation(doc,
                    dialog.TOSMethod, dialog.TOSReferencePlaneName,
                    dialog.TOSZElevationFeet, dialog.TOSLevelName);

                double affRefZ = ResolveReferenceElevation(doc,
                    dialog.AFFMethod, dialog.AFFReferencePlaneName,
                    dialog.AFFZElevationFeet, dialog.AFFLevelName);

                // ── Step 5: Process elements ──
                int pipesUpdated = 0;
                int fittingsUpdated = 0;
                int skipped = 0;

                using (var tw = new TransactionWrapper(doc, "Insert Pipe & Fitting Elevations"))
                {
                    foreach (var r in elementRefs)
                    {
                        Element elem = doc.GetElement(r);
                        if (elem == null) { skipped++; continue; }

                        // Get element center Z
                        double? elemZ = GetElementZ(elem);
                        if (!elemZ.HasValue) { skipped++; continue; }

                        bool isPipe = elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves;

                        // Calculate TOS and AFF elevations
                        double tosElevation;
                        double affElevation;

                        if (dialog.TOSMethod == "Deck")
                        {
                            // For deck method, use raybounce to find deck above
                            double? deckZ = FindDeckAbove(doc, elem, elemZ.Value);
                            tosElevation = deckZ.HasValue ? elemZ.Value - deckZ.Value : elemZ.Value - tosRefZ;
                        }
                        else
                        {
                            tosElevation = elemZ.Value - tosRefZ;
                        }

                        if (dialog.AFFMethod == "Deck")
                        {
                            double? deckZ = FindDeckBelow(doc, elem, elemZ.Value);
                            affElevation = deckZ.HasValue ? elemZ.Value - deckZ.Value : elemZ.Value - affRefZ;
                        }
                        else
                        {
                            affElevation = elemZ.Value - affRefZ;
                        }

                        // Format elevation strings
                        string tosStr = FormatElevation(tosElevation, "TOS");
                        string affStr = FormatElevation(affElevation, "AFF");

                        // Write parameters
                        SetParamSafe(elem, "PipeElevationTOS", tosStr);
                        SetParamSafe(elem, "PipeElevationAFF", affStr);

                        // Pipe-specific: calculate slope
                        if (isPipe)
                        {
                            string slopeStr = CalculateSlopeString(elem);
                            if (slopeStr != null)
                                SetParamSafe(elem, "Slope", slopeStr);
                            pipesUpdated++;
                        }
                        else
                        {
                            fittingsUpdated++;
                        }
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Pipe Elevations",
                    $"Completed:\n" +
                    (pipesUpdated > 0 ? $"  {pipesUpdated} pipe(s) updated\n" : "") +
                    (fittingsUpdated > 0 ? $"  {fittingsUpdated} fitting(s)/accessory(ies) updated\n" : "") +
                    (skipped > 0 ? $"  {skipped} element(s) skipped\n" : "") +
                    $"\nParameters written: PipeElevationTOS, PipeElevationAFF" +
                    (pipesUpdated > 0 ? ", Slope" : ""));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Pipe Elevations failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Resolves the reference Z elevation based on the selected method.
        /// </summary>
        private double ResolveReferenceElevation(Document doc, string method,
            string refPlaneName, double userZ, string levelName)
        {
            switch (method)
            {
                case "Plane":
                    var plane = new FilteredElementCollector(doc)
                        .OfClass(typeof(ReferencePlane))
                        .Cast<ReferencePlane>()
                        .FirstOrDefault(rp => rp.Name == refPlaneName);
                    if (plane != null)
                    {
                        var bubble = plane.BubbleEnd;
                        return bubble.Z;
                    }
                    return 0;

                case "Z":
                    return userZ;

                case "Level":
                    var level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault(l => l.Name == levelName);
                    return level?.ProjectElevation ?? 0;

                case "Deck":
                default:
                    return 0; // Deck uses raybounce per-element
            }
        }

        /// <summary>
        /// Gets the Z elevation of an element's center point (in feet).
        /// For pipes: midpoint of the centerline curve.
        /// For fittings/accessories: location point Z.
        /// </summary>
        private double? GetElementZ(Element elem)
        {
            if (elem.Location is LocationPoint lp)
                return lp.Point.Z;

            if (elem.Location is LocationCurve lc)
            {
                var curve = lc.Curve;
                double param = (curve.GetEndParameter(0) + curve.GetEndParameter(1)) / 2.0;
                return curve.Evaluate(param, false).Z;
            }

            // Fallback: bounding box center
            var bb = elem.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min.Z + bb.Max.Z) / 2.0;

            return null;
        }

        /// <summary>
        /// Attempts to find the structural deck/floor above the element using raybounce.
        /// Returns the Z of the underside of the deck, or null if not found.
        /// </summary>
        private double? FindDeckAbove(Document doc, Element elem, double elemZ)
        {
            XYZ origin = GetElementXY(elem, elemZ);
            if (origin == null) return null;

            // Cast ray upward
            XYZ direction = XYZ.BasisZ;
            var refIntersector = new ReferenceIntersector(
                GetStructuralCategoryFilter(),
                FindReferenceTarget.Face,
                (View3D)Get3DView(doc));

            if (refIntersector == null) return null;

            try
            {
                var result = refIntersector.FindNearest(origin, direction);
                if (result != null)
                    return origin.Z + result.Proximity;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Attempts to find the structural deck/floor below the element.
        /// </summary>
        private double? FindDeckBelow(Document doc, Element elem, double elemZ)
        {
            XYZ origin = GetElementXY(elem, elemZ);
            if (origin == null) return null;

            XYZ direction = -XYZ.BasisZ;
            try
            {
                var view3d = Get3DView(doc);
                if (view3d == null) return null;

                var refIntersector = new ReferenceIntersector(
                    GetStructuralCategoryFilter(),
                    FindReferenceTarget.Face,
                    view3d);

                var result = refIntersector.FindNearest(origin, direction);
                if (result != null)
                    return origin.Z - result.Proximity;
            }
            catch { }

            return null;
        }

        private XYZ GetElementXY(Element elem, double z)
        {
            if (elem.Location is LocationPoint lp)
                return new XYZ(lp.Point.X, lp.Point.Y, z);

            if (elem.Location is LocationCurve lc)
            {
                var curve = lc.Curve;
                double param = (curve.GetEndParameter(0) + curve.GetEndParameter(1)) / 2.0;
                var pt = curve.Evaluate(param, false);
                return new XYZ(pt.X, pt.Y, z);
            }

            return null;
        }

        private ElementMulticategoryFilter GetStructuralCategoryFilter()
        {
            var cats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Roofs
            };
            return new ElementMulticategoryFilter(cats);
        }

        private View3D Get3DView(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);
        }

        /// <summary>
        /// Calculates slope string for a pipe element.
        /// Returns formatted slope category or "Varies".
        /// </summary>
        private string CalculateSlopeString(Element pipe)
        {
            if (!(pipe.Location is LocationCurve lc)) return null;

            var curve = lc.Curve;
            XYZ pt1 = curve.GetEndPoint(0);
            XYZ pt2 = curve.GetEndPoint(1);

            // Create reference point at pt2 XY but pt1 Z (right angle)
            XYZ pt3 = new XYZ(pt2.X, pt2.Y, pt1.Z);

            double rise = Math.Abs(pt2.Z - pt1.Z);    // Vertical component (feet)
            double run = pt1.DistanceTo(pt3);           // Horizontal component (feet)

            if (run < 0.001) return "Varies"; // Vertical pipe

            // Rise per 10 feet, converted to inches
            double risePerTenFt = (rise / run) * 10.0 * 12.0; // inches per 10 feet

            // Classify slope
            if (risePerTenFt < 0.0125 * 12)
                return "Varies";
            else if (risePerTenFt < 0.0375 * 12)
                return "\u00BC\" / 10 Ft";  // ¼" / 10 Ft
            else if (risePerTenFt < 0.0625 * 12)
                return "\u00BD\" / 10 Ft";  // ½" / 10 Ft
            else if (risePerTenFt < 0.0875 * 12)
                return "\u00BE\" / 10 Ft";  // ¾" / 10 Ft
            else
                return "1\" / 10 Ft";
        }

        /// <summary>
        /// Formats an elevation value (in feet) as a feet-inches-fraction string.
        /// Example: +42'-3 1/2" TOS
        /// </summary>
        private string FormatElevation(double elevationFeet, string suffix)
        {
            string sign = elevationFeet >= 0 ? "+" : "-";
            double absElevation = Math.Abs(elevationFeet);

            // Convert to inches and round to nearest 1/4"
            double totalInches = absElevation * 12.0;
            double roundedInches = Math.Round(totalInches / 0.25) * 0.25;

            int wholeFeet = (int)Math.Floor(roundedInches / 12.0);
            double remainingInches = roundedInches - (wholeFeet * 12.0);

            int wholeInches = (int)Math.Floor(remainingInches);
            double fraction = remainingInches - wholeInches;

            // Format fraction
            string fractionStr = "";
            if (Math.Abs(fraction - 0.25) < 0.01)
                fractionStr = " 1/4";
            else if (Math.Abs(fraction - 0.5) < 0.01)
                fractionStr = " 1/2";
            else if (Math.Abs(fraction - 0.75) < 0.01)
                fractionStr = " 3/4";

            return $"{sign}{wholeFeet}'-{wholeInches}{fractionStr}\" {suffix}";
        }

        /// <summary>
        /// Sets a string parameter by name, silently skipping if not found.
        /// </summary>
        private void SetParamSafe(Element elem, string paramName, string value)
        {
            var param = elem.LookupParameter(paramName);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                param.Set(value);
        }

        /// <summary>
        /// Selection filter that accepts pipes, fittings, and/or accessories.
        /// </summary>
        private class PipeAndFittingFilter : ISelectionFilter
        {
            private readonly bool _allowPipes;
            private readonly bool _allowFittings;

            public PipeAndFittingFilter(bool allowPipes, bool allowFittings)
            {
                _allowPipes = allowPipes;
                _allowFittings = allowFittings;
            }

            public bool AllowElement(Element elem)
            {
                if (elem?.Category == null) return false;
                int catId = elem.Category.Id.IntegerValue;

                if (_allowPipes && catId == (int)BuiltInCategory.OST_PipeCurves)
                    return true;
                if (_allowFittings && catId == (int)BuiltInCategory.OST_PipeFitting)
                    return true;
                if (_allowFittings && catId == (int)BuiltInCategory.OST_PipeAccessory)
                    return true;

                return false;
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
