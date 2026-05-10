using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.ModelCheck
{
    /// <summary>
    /// Checks upright sprinkler heads for NFPA clearance violations.
    /// Finds pipes and pipe accessories (hangers, trapeze, seismic braces)
    /// within the 3" clearance zone above each upright sprinkler deflector.
    ///
    /// WORKFLOW:
    ///   1. Collect upright sprinklers from selection or active view
    ///   2. Collect pipes and pipe accessories in the same view/area
    ///   3. For each upright, check if any pipe/accessory is within 3" horizontally
    ///      and within the deflector-to-ceiling zone vertically
    ///   4. Place annotation families at conflict locations
    ///   5. Write clash info to Comments parameter on offending elements
    ///   6. Highlight clashing elements in selection
    ///   7. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SprinklerClearanceCheckCommand : IExternalCommand
    {
        /// <summary>NFPA clearance radius around upright deflector (3 inches = 0.25 ft).</summary>
        private const double ClearanceRadius = 3.0 / 12.0; // 0.25 ft

        /// <summary>Vertical search height above sprinkler (12 inches = 1.0 ft).</summary>
        private const double SearchHeight = 1.0;

        /// <summary>Annotation family name to place at conflict locations.</summary>
        private const string AnnotationFamilyName = "-Clearance - Upright vs Hanger";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Collect upright sprinklers ──
                var allSprinklers = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_Sprinklers)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var uprights = allSprinklers
                    .Where(s => IsUpright(s))
                    .ToList();

                if (uprights.Count == 0)
                {
                    TaskDialog.Show("Sprinkler Clearance Check",
                        $"No upright sprinklers found in the active view.\n" +
                        $"(Total sprinklers in view: {allSprinklers.Count})");
                    return Result.Succeeded;
                }

                // ── Collect pipes and pipe accessories ──
                var pipes = new FilteredElementCollector(doc, activeView.Id)
                    .WherePasses(new ElementMulticategoryFilter(
                        new List<BuiltInCategory> {
                            BuiltInCategory.OST_PipeCurves,
                            BuiltInCategory.OST_FlexPipeCurves
                        }))
                    .WhereElementIsNotElementType()
                    .ToList();

                var accessories = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategory(BuiltInCategory.OST_PipeAccessory)
                    .WhereElementIsNotElementType()
                    .ToList();

                var allObstructions = new List<Element>();
                allObstructions.AddRange(pipes);
                allObstructions.AddRange(accessories);

                if (allObstructions.Count == 0)
                {
                    TaskDialog.Show("Sprinkler Clearance Check",
                        $"Found {uprights.Count} upright sprinklers but no pipes or pipe accessories in the active view.");
                    return Result.Succeeded;
                }

                // ── Check clearances ──
                var clashes = new List<(FamilyInstance Sprinkler, Element Obstruction, double Distance)>();
                var clashedElements = new HashSet<ElementId>();

                foreach (var upright in uprights)
                {
                    XYZ sprinklerPt = GetLocation(upright);
                    if (sprinklerPt == null) continue;

                    foreach (var obstruction in allObstructions)
                    {
                        // Skip self
                        if (obstruction.Id == upright.Id) continue;

                        XYZ obstructionPt = GetElementCenter(obstruction);
                        if (obstructionPt == null) continue;

                        // Check vertical: obstruction must be above sprinkler within search height
                        double vertDist = obstructionPt.Z - sprinklerPt.Z;
                        if (vertDist < -0.1 || vertDist > SearchHeight) continue;

                        // Check horizontal: within clearance radius
                        double horizDist = Math.Sqrt(
                            Math.Pow(obstructionPt.X - sprinklerPt.X, 2) +
                            Math.Pow(obstructionPt.Y - sprinklerPt.Y, 2));

                        if (horizDist <= ClearanceRadius)
                        {
                            double totalDist = sprinklerPt.DistanceTo(obstructionPt);
                            clashes.Add((upright, obstruction, totalDist));
                            clashedElements.Add(obstruction.Id);
                        }
                    }
                }

                if (clashes.Count == 0)
                {
                    TaskDialog.Show("Sprinkler Clearance Check",
                        $"Checked {uprights.Count} upright sprinklers against {allObstructions.Count} pipes/accessories.\n\n" +
                        "No clearance violations found.");
                    return Result.Succeeded;
                }

                // ── Apply results ─��
                int annotationsPlaced = 0;
                var errors = new List<string>();

                // Find annotation family
                FamilySymbol annotSymbol = FindAnnotationFamily(doc);

                using (Transaction tx = new Transaction(doc, "Sprinkler Clearance Check"))
                {
                    tx.Start();

                    // Clear previous check annotations in this view
                    if (annotSymbol != null)
                    {
                        ClearPreviousAnnotations(doc, activeView);
                    }

                    // Group clashes by sprinkler
                    var clashBySprinkler = clashes
                        .GroupBy(c => c.Sprinkler.Id)
                        .ToList();

                    foreach (var group in clashBySprinkler)
                    {
                        var sprinkler = group.First().Sprinkler;
                        XYZ pt = GetLocation(sprinkler);
                        if (pt == null) continue;

                        // Build clash description
                        var clashDescriptions = new List<string>();
                        foreach (var clash in group)
                        {
                            string name = GetElementDescription(clash.Obstruction);
                            double distInches = clash.Distance * 12.0;
                            clashDescriptions.Add($"{name} ({distInches:F1}\")");
                        }
                        string clashText = string.Join(", ", clashDescriptions);

                        // Write Comments on clashed elements
                        foreach (var clash in group)
                        {
                            try
                            {
                                Parameter comments = clash.Obstruction.LookupParameter("Comments");
                                if (comments != null && !comments.IsReadOnly)
                                {
                                    comments.Set("Model Check: Upright Clearance");
                                }
                            }
                            catch { /* non-critical */ }
                        }

                        // Place annotation
                        if (annotSymbol != null)
                        {
                            try
                            {
                                if (!annotSymbol.IsActive) annotSymbol.Activate();

                                var annot = doc.Create.NewFamilyInstance(
                                    pt, annotSymbol, activeView);

                                // Try to set text parameters on the annotation
                                SetParamIfExists(annot, "Model Check - Elements", clashText);
                                SetParamIfExists(annot, "Model Check - Text",
                                    $"{group.Count()} obstruction(s) within 3\" clearance zone");

                                annotationsPlaced++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Annotation at sprinkler {sprinkler.Id}: {ex.Message}");
                            }
                        }
                    }

                    tx.Commit();
                }

                // Highlight clashed elements
                if (clashedElements.Count > 0)
                {
                    uidoc.Selection.SetElementIds(clashedElements.ToList());
                }

                // ── Report ──
                var sprinklerCount = clashes.Select(c => c.Sprinkler.Id).Distinct().Count();
                string report = $"Sprinkler Clearance Check Results\n\n" +
                    $"Uprights checked: {uprights.Count}\n" +
                    $"Obstructions checked: {allObstructions.Count}\n\n" +
                    $"VIOLATIONS: {clashes.Count} clearance conflicts found\n" +
                    $"  Sprinklers affected: {sprinklerCount}\n" +
                    $"  Obstructing elements: {clashedElements.Count}\n";

                if (annotationsPlaced > 0)
                    report += $"\n  Annotations placed: {annotationsPlaced}";
                else if (annotSymbol == null)
                    report += $"\n  (Annotation family \"{AnnotationFamilyName}\" not loaded — no markers placed)";

                if (errors.Count > 0)
                    report += $"\n\n{errors.Count} errors:\n" + string.Join("\n", errors.Take(5));

                report += "\n\nClashing elements are highlighted in the selection.";

                TaskDialog.Show("Sprinkler Clearance Check", report);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private bool IsUpright(FamilyInstance sprinkler)
        {
            string familyName = sprinkler.Symbol?.Family?.Name ?? "";
            string typeName = sprinkler.Symbol?.Name ?? "";
            string combined = (familyName + " " + typeName).ToUpper();

            return combined.Contains("UPR") || combined.Contains("UPRIGHT");
        }

        private XYZ GetLocation(Element element)
        {
            var loc = element.Location as LocationPoint;
            return loc?.Point;
        }

        private XYZ GetElementCenter(Element element)
        {
            // For point-based elements
            var locPt = element.Location as LocationPoint;
            if (locPt != null) return locPt.Point;

            // For line-based elements (pipes), use midpoint
            var locCurve = element.Location as LocationCurve;
            if (locCurve?.Curve != null)
            {
                return locCurve.Curve.Evaluate(0.5, true);
            }

            // Fallback: bounding box center
            var bb = element.get_BoundingBox(null);
            if (bb != null)
            {
                return new XYZ(
                    (bb.Min.X + bb.Max.X) / 2.0,
                    (bb.Min.Y + bb.Max.Y) / 2.0,
                    (bb.Min.Z + bb.Max.Z) / 2.0);
            }

            return null;
        }

        private string GetElementDescription(Element element)
        {
            if (element is FamilyInstance fi)
            {
                string familyName = fi.Symbol?.Family?.Name ?? "";
                if (familyName.IndexOf("Hanger", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Hanger";
                if (familyName.IndexOf("Trapeze", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Trapeze";
                if (familyName.IndexOf("Seismic", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Seismic Brace";
                if (familyName.IndexOf("Sleeve", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Sleeve";
                return familyName;
            }

            // Pipe — include size
            Parameter diaParam = element.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diaParam != null && diaParam.HasValue)
            {
                double inches = diaParam.AsDouble() * 12.0;
                return $"Pipe ({inches:F0}\")";
            }

            return element.Category?.Name ?? "Element";
        }

        private FamilySymbol FindAnnotationFamily(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family.Name.IndexOf(AnnotationFamilyName,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ClearPreviousAnnotations(Document doc, View view)
        {
            var existing = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var fi = e as FamilyInstance;
                    return fi?.Symbol?.Family?.Name?.IndexOf(AnnotationFamilyName,
                        StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .Select(e => e.Id)
                .ToList();

            if (existing.Count > 0)
                doc.Delete(existing);
        }

        private void SetParamIfExists(Element element, string paramName, string value)
        {
            var param = element.LookupParameter(paramName);
            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                param.Set(value);
        }
    }
}
