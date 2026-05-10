using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.ModelCheck
{
    /// <summary>
    /// Measures the distance from upright sprinkler deflectors to the structural
    /// element above and checks against NFPA 13 allowable distances.
    ///
    /// NFPA 13 requirements:
    ///   - Unobstructed: 1" to 12" from deflector to ceiling
    ///   - Obstructed:   1" to 22" from deflector to ceiling
    ///
    /// Uses ReferenceIntersector (raybounce) to find the structural element above
    /// each sprinkler, then calculates the gap from the deflector top to the
    /// structural underside.
    ///
    /// WORKFLOW:
    ///   1. Dialog: select distance type (unobstructed/obstructed/custom),
    ///      sprinkler head height, annotation mode, linked structural model
    ///   2. Collect upright sprinklers from selection or active view
    ///   3. For each upright, raybounce upward to find structure
    ///   4. Calculate deflector distance = structure hit - sprinkler Z - head height
    ///   5. Compare against max allowable distance
    ///   6. Place annotation families with measured distance
    ///   7. Highlight violations
    ///   8. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeflectorDistanceCheckCommand : IExternalCommand
    {
        private const string AnnotationFamilyName = "-Model Check - Upright Sprinkler Deflector Distance";

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
                    .Where(s => IsUpright(s))
                    .ToList();

                if (allSprinklers.Count == 0)
                {
                    TaskDialog.Show("Deflector Distance Check",
                        "No upright sprinklers found in the active view.");
                    return Result.Succeeded;
                }

                // ── Show dialog ──
                using (var dlg = new DeflectorDistanceCheckDialog(allSprinklers.Count))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    double maxDistance = dlg.MaxDistance; // feet
                    double headHeight = dlg.HeadHeight;  // feet
                    bool annotateAll = dlg.AnnotateAll;

                    // ── Find or create 3D view for raybounce ──
                    View3D rayView = Find3DView(doc);
                    if (rayView == null)
                    {
                        TaskDialog.Show("Deflector Distance Check",
                            "No 3D view found. The command needs a 3D view for raybounce calculations.");
                        return Result.Failed;
                    }

                    // ── Set up ReferenceIntersector ──
                    var catFilter = new ElementMulticategoryFilter(new List<BuiltInCategory>
                    {
                        BuiltInCategory.OST_Floors,
                        BuiltInCategory.OST_Roofs,
                        BuiltInCategory.OST_Ceilings,
                        BuiltInCategory.OST_StructuralFraming,
                        BuiltInCategory.OST_Stairs
                    });

                    var intersector = new ReferenceIntersector(
                        catFilter, FindReferenceTarget.Face, rayView);
                    intersector.FindReferencesInRevitLinks = true;

                    // ── Process each sprinkler ──
                    var results = new List<(FamilyInstance Sprinkler, double Distance, bool Exceeds, string HitCategory)>();
                    int missed = 0;

                    foreach (var sprinkler in allSprinklers)
                    {
                        XYZ pt = GetLocation(sprinkler);
                        if (pt == null) { missed++; continue; }

                        // Shoot ray upward from sprinkler
                        var hit = intersector.FindNearest(pt, XYZ.BasisZ);
                        if (hit == null) { missed++; continue; }

                        double structureZ = pt.Z + hit.Proximity;
                        double deflectorDistance = hit.Proximity - headHeight;

                        if (deflectorDistance < 0) deflectorDistance = 0;

                        bool exceeds = deflectorDistance > maxDistance || deflectorDistance < (1.0 / 12.0);

                        // Get hit category name
                        string hitCat = "Structure";
                        try
                        {
                            var hitRef = hit.GetReference();
                            Element hitElem = doc.GetElement(hitRef);
                            if (hitElem == null)
                            {
                                // Might be in a link
                                var linkInstance = doc.GetElement(hitRef.LinkedElementId) as RevitLinkInstance;
                                if (linkInstance != null)
                                {
                                    var linkDoc = linkInstance.GetLinkDocument();
                                    hitElem = linkDoc?.GetElement(hitRef.LinkedElementId);
                                }
                            }
                            if (hitElem?.Category != null)
                                hitCat = hitElem.Category.Name;
                        }
                        catch { /* use default */ }

                        results.Add((sprinkler, deflectorDistance, exceeds, hitCat));
                    }

                    // ─��� Place annotations and highlight ──
                    FamilySymbol annotSymbol = FindAnnotationFamily(doc);
                    int annotationsPlaced = 0;
                    var exceedingIds = new List<ElementId>();

                    using (Transaction tx = new Transaction(doc, "Deflector Distance Check"))
                    {
                        tx.Start();

                        // Clear previous annotations
                        if (annotSymbol != null)
                            ClearPreviousAnnotations(doc, activeView);

                        foreach (var result in results)
                        {
                            if (result.Exceeds)
                                exceedingIds.Add(result.Sprinkler.Id);

                            // Place annotation if mode is "all" or if it exceeds
                            if (annotSymbol != null && (annotateAll || result.Exceeds))
                            {
                                XYZ pt = GetLocation(result.Sprinkler);
                                if (pt != null)
                                {
                                    try
                                    {
                                        if (!annotSymbol.IsActive) annotSymbol.Activate();
                                        var annot = doc.Create.NewFamilyInstance(
                                            pt, annotSymbol, activeView);

                                        double distInches = result.Distance * 12.0;
                                        string status = result.Exceeds ? "EXCEEDS" : "OK";
                                        SetParamIfExists(annot, "Model Check - Text",
                                            $"{distInches:F1}\" {status}");

                                        annotationsPlaced++;
                                    }
                                    catch { /* non-critical */ }
                                }
                            }
                        }

                        tx.Commit();
                    }

                    // Highlight exceeding sprinklers
                    if (exceedingIds.Count > 0)
                        uidoc.Selection.SetElementIds(exceedingIds);

                    // ── Report ──
                    int exceedCount = results.Count(r => r.Exceeds);
                    int okCount = results.Count(r => !r.Exceeds);
                    double maxDistInches = maxDistance * 12.0;

                    string report = $"Deflector Distance Check Results\n\n" +
                        $"Uprights checked: {allSprinklers.Count}\n" +
                        $"Max allowable distance: {maxDistInches:F0}\"\n" +
                        $"Head height: {headHeight * 12.0:F1}\"\n\n" +
                        $"OK: {okCount}\n" +
                        $"EXCEEDS: {exceedCount}\n" +
                        $"Missed (no structure above): {missed}\n";

                    if (annotationsPlaced > 0)
                        report += $"\nAnnotations placed: {annotationsPlaced}";
                    else if (annotSymbol == null)
                        report += $"\n(Annotation family \"{AnnotationFamilyName}\" not loaded)";

                    if (exceedCount > 0)
                    {
                        report += "\n\nExceeding sprinklers are highlighted.";

                        // Show worst offenders
                        var worst = results
                            .Where(r => r.Exceeds)
                            .OrderByDescending(r => r.Distance)
                            .Take(5);
                        report += "\n\nWorst violations:";
                        foreach (var w in worst)
                            report += $"\n  ID {w.Sprinkler.Id}: {w.Distance * 12.0:F1}\" ({w.HitCategory})";
                    }

                    TaskDialog.Show("Deflector Distance Check", report);
                }

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

        private XYZ GetLocation(FamilyInstance element)
        {
            var loc = element.Location as LocationPoint;
            return loc?.Point;
        }

        private View3D Find3DView(Document doc)
        {
            // Look for existing "3D-Raybounce" view first
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == "3D-Raybounce");

            if (existing != null) return existing;

            // Find any non-template 3D view
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);
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
