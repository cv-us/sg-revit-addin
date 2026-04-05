using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Places a Generic Annotation family instance at every point where a pipe
    /// crosses a structural grid line in plan (XY projection).
    ///
    /// The annotation family used must have a name containing "Beam Penetration"
    /// (case-insensitive). If no such family is loaded the command reports what
    /// Generic Annotation families are available and exits.
    ///
    /// WORKFLOW:
    ///   1. Prompt user to select pipes and grids together ("Select PIPES and
    ///      GRIDS, then press Finish")
    ///   2. Separate selection into pipes (OST_PipeCurves) and grids (OST_Grids)
    ///   3. Locate the "Beam Penetration" annotation family symbol in the project
    ///   4. For each pipe × grid pair, flatten both curves to Z=0 and find the
    ///      2D line-line intersection point
    ///   5. If the intersection lies within both segments, place the symbol at
    ///      that XY location using the pipe's Z elevation
    ///   6. Report placement count
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BeamPenetrationSymbolsCommand : IExternalCommand
    {
        private const string FamilyNameFragment = "Beam Penetration";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Step 1: Prompt user to select pipes and grids ──
                IList<Reference> refs;
                try
                {
                    refs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new PipeOrGridFilter(),
                        "Select PIPES and GRIDS, then press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (refs == null || refs.Count == 0)
                {
                    TaskDialog.Show("Beam Penetration Symbols", "No elements selected.");
                    return Result.Cancelled;
                }

                // ── Step 2: Separate pipes and grids ──
                var pipes = new List<Element>();
                var grids = new List<Grid>();

                foreach (var r in refs)
                {
                    Element e = doc.GetElement(r);
                    if (e == null) continue;

                    if (e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeCurves)
                        pipes.Add(e);
                    else if (e is Grid g)
                        grids.Add(g);
                }

                if (pipes.Count == 0 || grids.Count == 0)
                {
                    TaskDialog.Show("Beam Penetration Symbols",
                        "Please select at least one pipe AND at least one grid line.\n\n" +
                        $"Selected: {pipes.Count} pipe(s), {grids.Count} grid(s).");
                    return Result.Cancelled;
                }

                // ── Step 3: Locate annotation family symbol ──
                FamilySymbol symbol = FindAnnotationSymbol(doc);
                if (symbol == null)
                {
                    string available = GetAvailableAnnotationFamilies(doc);
                    TaskDialog.Show("Beam Penetration Symbols",
                        $"No Generic Annotation family containing \"{FamilyNameFragment}\" was found.\n\n" +
                        "Load a family whose name contains \"Beam Penetration\" before running this command.\n\n" +
                        "Available Generic Annotation families:\n" + available);
                    return Result.Cancelled;
                }

                // ── Steps 4-5: Find intersections and place symbols ──
                int placed = 0;
                int skipped = 0;

                using (var tw = new TransactionWrapper(doc, "Place Beam Penetration Symbols"))
                {
                    if (!symbol.IsActive)
                        symbol.Activate();

                    foreach (Element pipeElem in pipes)
                    {
                        LocationCurve pipeLoc = pipeElem.Location as LocationCurve;
                        if (pipeLoc?.Curve == null) { skipped++; continue; }

                        Line pipeLine = pipeLoc.Curve as Line;
                        if (pipeLine == null) { skipped++; continue; }

                        XYZ pipeStart = pipeLine.GetEndPoint(0);
                        XYZ pipeEnd   = pipeLine.GetEndPoint(1);
                        double pipeZ  = (pipeStart.Z + pipeEnd.Z) / 2.0;

                        // Flatten pipe to Z=0
                        XYZ pipeStart2D = new XYZ(pipeStart.X, pipeStart.Y, 0);
                        XYZ pipeEnd2D   = new XYZ(pipeEnd.X,   pipeEnd.Y,   0);

                        foreach (Grid grid in grids)
                        {
                            Curve gridCurve = grid.Curve;
                            if (gridCurve == null) continue;

                            XYZ gridStart = gridCurve.GetEndPoint(0);
                            XYZ gridEnd   = gridCurve.GetEndPoint(1);

                            // Flatten grid to Z=0
                            XYZ gridStart2D = new XYZ(gridStart.X, gridStart.Y, 0);
                            XYZ gridEnd2D   = new XYZ(gridEnd.X,   gridEnd.Y,   0);

                            // Find 2D line-line intersection
                            XYZ intersection2D = Intersect2D(
                                pipeStart2D, pipeEnd2D,
                                gridStart2D, gridEnd2D);

                            if (intersection2D == null)
                                continue;

                            // Place symbol at intersection XY, pipe Z elevation
                            XYZ placementPt = new XYZ(intersection2D.X, intersection2D.Y, pipeZ);

                            try
                            {
                                doc.Create.NewFamilyInstance(placementPt, symbol, activeView);
                                placed++;
                            }
                            catch
                            {
                                skipped++;
                            }
                        }
                    }

                    tw.Commit();
                }

                // ── Step 6: Report ──
                string summary = $"Placed {placed} beam penetration symbol{(placed != 1 ? "s" : "")}.";
                if (skipped > 0)
                    summary += $"\n{skipped} intersection{(skipped != 1 ? "s" : "")} could not be processed.";

                TaskDialog.Show("Beam Penetration Symbols", summary);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  Family Lookup
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Searches all loaded FamilySymbols in the Generic Annotation category
        /// for one whose family name contains the "Beam Penetration" fragment.
        /// Returns the first match, or null if none is found.
        /// </summary>
        private FamilySymbol FindAnnotationSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    fs.Family.Name.IndexOf(FamilyNameFragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Returns a formatted list of all Generic Annotation family names loaded
        /// in the project, for use in the error message when the target family is absent.
        /// </summary>
        private string GetAvailableAnnotationFamilies(Document doc)
        {
            var names = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .Cast<FamilySymbol>()
                .Select(fs => fs.Family.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (names.Count == 0)
                return "  (none loaded)";

            return string.Join("\n", names.Select(n => "  " + n));
        }

        // ══════════════════════════════════════════════════════════════
        //  2D Intersection
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Computes the intersection point of two finite line segments projected onto the XY plane.
        /// Returns the intersection XYZ (Z=0) if the intersection falls within both segments,
        /// or null if the lines are parallel or the intersection is outside either segment.
        /// </summary>
        private XYZ Intersect2D(XYZ p1, XYZ p2, XYZ p3, XYZ p4)
        {
            // Direction vectors
            double d1X = p2.X - p1.X;
            double d1Y = p2.Y - p1.Y;
            double d2X = p4.X - p3.X;
            double d2Y = p4.Y - p3.Y;

            double denom = d1X * d2Y - d1Y * d2X;

            // Lines are parallel (or collinear) — no single intersection point
            if (Math.Abs(denom) < 1e-9)
                return null;

            double dx = p3.X - p1.X;
            double dy = p3.Y - p1.Y;

            double t = (dx * d2Y - dy * d2X) / denom;
            double u = (dx * d1Y - dy * d1X) / denom;

            // Intersection must be within both segments [0,1]
            const double tol = 1e-6;
            if (t < -tol || t > 1.0 + tol) return null;
            if (u < -tol || u > 1.0 + tol) return null;

            return new XYZ(p1.X + t * d1X, p1.Y + t * d1Y, 0);
        }

        // ══════════════════════════════════════════════════════════════
        //  Selection Filter
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Allows selection of pipes (OST_PipeCurves) and grids (OST_Grids) only.
        /// </summary>
        private class PipeOrGridFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem?.Category == null) return false;
                int catId = elem.Category.Id.IntegerValue;
                return catId == (int)BuiltInCategory.OST_PipeCurves
                    || catId == (int)BuiltInCategory.OST_Grids;
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
