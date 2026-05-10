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
    /// Places "SSB Hanger" Generic Annotation symbols along selected pipe runs,
    /// positioned 12 inches (1 foot) from each end of every pipe.
    ///
    /// The annotation family placed is the first loaded FamilySymbol whose family
    /// name contains "SSB Hanger" (case-insensitive).
    ///
    /// Pipes shorter than 2 feet are skipped (not enough room for two symbols at
    /// 1-foot offsets without overlapping).
    ///
    /// Each placed symbol is rotated to match the pipe's centerline direction.
    ///
    /// WORKFLOW:
    ///   1. Prompt user to select pipes (OST_PipeCurves, "Select pipes for SSB
    ///      symbols, press Finish")
    ///   2. Search for a loaded FamilySymbol whose family name contains "SSB Hanger"
    ///   3. For each pipe, compute points 12" from each end along the centerline
    ///   4. Skip pipes shorter than 2 feet
    ///   5. Place and rotate the symbol at both end-offset points
    ///   6. Report placement count
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SSBSymbolsCommand : IExternalCommand
    {
        private const string FamilyNameFragment = "SSB Hanger";
        private const double OffsetFromEndFeet = 1.0;   // 12 inches
        private const double MinPipeLengthFeet  = 2.0;   // skip shorter pipes

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Step 1: Select pipes ──
                IList<Reference> refs;
                try
                {
                    refs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new CategorySelectionFilter(BuiltInCategory.OST_PipeCurves),
                        "Select pipes for SSB symbols, press Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (refs == null || refs.Count == 0)
                {
                    TaskDialog.Show("SSB Symbols", "No pipes selected.");
                    return Result.Cancelled;
                }

                // ── Step 2: Locate SSB Hanger annotation symbol ──
                FamilySymbol symbol = FindSSBSymbol(doc);
                if (symbol == null)
                {
                    string available = GetAvailableAnnotationFamilies(doc);
                    TaskDialog.Show("SSB Symbols",
                        $"No Generic Annotation family containing \"{FamilyNameFragment}\" was found.\n\n" +
                        "Load a family whose name contains \"SSB Hanger\" before running this command.\n\n" +
                        "Available Generic Annotation families:\n" + available);
                    return Result.Cancelled;
                }

                // ── Steps 3-5: Place symbols at pipe ends ──
                int placed  = 0;
                int skipped = 0;

                using (var tw = new TransactionWrapper(doc, "Place SSB Hanger Symbols"))
                {
                    if (!symbol.IsActive)
                        symbol.Activate();

                    foreach (Reference r in refs)
                    {
                        Element pipeElem = doc.GetElement(r);
                        if (pipeElem == null) { skipped++; continue; }

                        LocationCurve pipeLoc = pipeElem.Location as LocationCurve;
                        if (pipeLoc?.Curve == null) { skipped++; continue; }

                        Line pipeLine = pipeLoc.Curve as Line;
                        if (pipeLine == null) { skipped++; continue; }

                        double length = pipeLine.Length;

                        // ── Step 4: Skip pipes that are too short ──
                        if (length < MinPipeLengthFeet)
                        {
                            skipped++;
                            continue;
                        }

                        XYZ start = pipeLine.GetEndPoint(0);
                        XYZ end   = pipeLine.GetEndPoint(1);
                        XYZ dir   = (end - start).Normalize();

                        // Points 12" from each end
                        XYZ ptNearStart = start + dir * OffsetFromEndFeet;
                        XYZ ptNearEnd   = end   - dir * OffsetFromEndFeet;

                        // Angle of the pipe direction in XY plane (for rotation)
                        double angle = Math.Atan2(dir.Y, dir.X);

                        foreach (XYZ pt in new[] { ptNearStart, ptNearEnd })
                        {
                            try
                            {
                                FamilyInstance inst = doc.Create.NewFamilyInstance(
                                    pt, symbol, activeView);

                                if (inst == null) { skipped++; continue; }

                                // Rotate symbol to match pipe direction
                                Line rotAxis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                                ElementTransformUtils.RotateElement(doc, inst.Id, rotAxis, angle);

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
                string summary = $"Placed {placed} SSB hanger symbol{(placed != 1 ? "s" : "")}.";
                if (skipped > 0)
                    summary += $"\n{skipped} pipe{(skipped != 1 ? "s" : "")} or point{(skipped != 1 ? "s" : "")} skipped " +
                               $"(too short or invalid geometry).";

                TaskDialog.Show("SSB Symbols — Complete", summary);
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
        /// Returns the first loaded Generic Annotation FamilySymbol whose family
        /// name contains "SSB Hanger" (case-insensitive), or null if none is found.
        /// </summary>
        private FamilySymbol FindSSBSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    fs.Family.Name.IndexOf(FamilyNameFragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Returns a formatted list of all Generic Annotation family names in the project,
        /// for use in the error message when the target family is absent.
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
    }
}
