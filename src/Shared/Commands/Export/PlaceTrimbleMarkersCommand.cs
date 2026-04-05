using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Export
{
    /// <summary>
    /// Places Trimble field point marker families at hanger and seismic brace locations
    /// for coordination with Trimble field layout tools. Different marker symbols are
    /// used for different rod diameters and element types:
    ///   - 3/8" hangers → "-Trimble-Hanger-3_8" (CircleX type)
    ///   - 1/2" hangers → "-Trimble-Hanger-1_2" (SquarePlusCircle type)
    ///   - Seismic brace anchors → "-Trimble-Seismic-Anchor" (SquarePlusCircle type)
    ///
    /// WORKFLOW:
    ///   1. User selects pipe accessories (hangers and/or seismic braces)
    ///   2. Clear any existing Trimble markers in the active view
    ///   3. Classify each element by type (hanger 3/8", hanger 1/2", seismic anchor)
    ///   4. Place the appropriate Trimble marker family at each location
    ///   5. Report summary
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceTrimbleMarkersCommand : IExternalCommand
    {
        /// <summary>Diameter threshold: pipes > 4" nominal get 1/2" rod, otherwise 3/8".</summary>
        private const double LargeRodThreshold = 4.0 / 12.0; // 4 inches in feet

        private const string TrimbleFamilyPrefix = "-Trimble-";
        private const string Hanger38Family = "-Trimble-Hanger-3_8";
        private const string Hanger12Family = "-Trimble-Hanger-1_2";
        private const string SeismicAnchorFamily = "-Trimble-Seismic-Anchor";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            try
            {
                // ── Collect pipe accessories from selection or prompt ──
                var selectedIds = uidoc.Selection.GetElementIds();
                List<FamilyInstance> accessories;

                if (selectedIds.Count > 0)
                {
                    accessories = selectedIds
                        .Select(id => doc.GetElement(id))
                        .OfType<FamilyInstance>()
                        .Where(fi => fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                        .ToList();
                }
                else
                {
                    // Prompt user to select
                    try
                    {
                        var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                            new PipeAccessoryFilter(),
                            "Select pipe hangers and/or seismic braces, then press Finish.");
                        accessories = refs
                            .Select(r => doc.GetElement(r.ElementId))
                            .OfType<FamilyInstance>()
                            .ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return Result.Cancelled;
                    }
                }

                if (accessories.Count == 0)
                {
                    TaskDialog.Show("Place Trimble Markers", "No pipe accessories selected.");
                    return Result.Succeeded;
                }

                // ── Find Trimble marker families ──
                var familySymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                FamilySymbol sym38 = FindSymbol(familySymbols, Hanger38Family);
                FamilySymbol sym12 = FindSymbol(familySymbols, Hanger12Family);
                FamilySymbol symSeismic = FindSymbol(familySymbols, SeismicAnchorFamily);

                var missing = new List<string>();
                if (sym38 == null) missing.Add(Hanger38Family);
                if (sym12 == null) missing.Add(Hanger12Family);
                if (symSeismic == null) missing.Add(SeismicAnchorFamily);

                if (missing.Count == 3)
                {
                    TaskDialog.Show("Place Trimble Markers",
                        "No Trimble marker families found in the project.\n\n" +
                        "Load these families first:\n" +
                        string.Join("\n", missing.Select(m => "  • " + m)));
                    return Result.Succeeded;
                }

                // ── Classify elements and place markers ──
                int placed38 = 0, placed12 = 0, placedSeismic = 0, skipped = 0;
                var errors = new List<string>();

                using (Transaction tx = new Transaction(doc, "Place Trimble Markers"))
                {
                    tx.Start();

                    // Clear existing Trimble markers in view first
                    ClearExistingMarkers(doc, activeView);

                    foreach (var accessory in accessories)
                    {
                        string familyName = accessory.Symbol?.Family?.Name ?? "";

                        if (familyName.IndexOf("Seismic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            familyName.IndexOf("Brace", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Seismic brace — place at anchor points
                            if (symSeismic != null)
                            {
                                placedSeismic += PlaceMarkerAtElement(doc, activeView,
                                    accessory, symSeismic, errors);
                            }
                            else skipped++;
                        }
                        else if (IsHanger(familyName))
                        {
                            // Hanger — check rod diameter
                            double nomDia = GetNominalDiameter(accessory);
                            bool isLargeRod = nomDia > LargeRodThreshold;

                            if (isLargeRod && sym12 != null)
                            {
                                placed12 += PlaceMarkerAtElement(doc, activeView,
                                    accessory, sym12, errors);
                            }
                            else if (!isLargeRod && sym38 != null)
                            {
                                placed38 += PlaceMarkerAtElement(doc, activeView,
                                    accessory, sym38, errors);
                            }
                            else skipped++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }

                    tx.Commit();
                }

                // ── Report ──
                int totalPlaced = placed38 + placed12 + placedSeismic;
                string report = $"Placed {totalPlaced} Trimble markers:\n\n";
                if (placed38 > 0) report += $"  3/8\" hangers: {placed38}\n";
                if (placed12 > 0) report += $"  1/2\" hangers: {placed12}\n";
                if (placedSeismic > 0) report += $"  Seismic anchors: {placedSeismic}\n";
                if (skipped > 0) report += $"\n  Skipped: {skipped} (missing family or unrecognized type)\n";
                if (missing.Count > 0)
                    report += $"\n  Missing families: {string.Join(", ", missing)}";
                if (errors.Count > 0)
                    report += $"\n\n{errors.Count} errors:\n" + string.Join("\n", errors.Take(5));

                TaskDialog.Show("Place Trimble Markers", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private int PlaceMarkerAtElement(Document doc, View view,
            FamilyInstance element, FamilySymbol symbol, List<string> errors)
        {
            try
            {
                XYZ pt = GetLocation(element);
                if (pt == null) return 0;

                if (!symbol.IsActive) symbol.Activate();
                doc.Create.NewFamilyInstance(pt, symbol, view);
                return 1;
            }
            catch (Exception ex)
            {
                errors.Add($"Element {element.Id}: {ex.Message}");
                return 0;
            }
        }

        private void ClearExistingMarkers(Document doc, View view)
        {
            var existing = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var fi = e as FamilyInstance;
                    return fi?.Symbol?.Family?.Name?.StartsWith(TrimbleFamilyPrefix,
                        StringComparison.OrdinalIgnoreCase) == true;
                })
                .Select(e => e.Id)
                .ToList();

            if (existing.Count > 0)
                doc.Delete(existing);
        }

        private bool IsHanger(string familyName)
        {
            return familyName.IndexOf("Hanger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   familyName.IndexOf("Ring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   familyName.IndexOf("Adjustable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private double GetNominalDiameter(FamilyInstance hanger)
        {
            var param = hanger.LookupParameter("Nominal Diameter");
            if (param != null && param.HasValue)
                return param.AsDouble();
            return 0;
        }

        private XYZ GetLocation(FamilyInstance element)
        {
            var loc = element.Location as LocationPoint;
            if (loc != null) return loc.Point;

            // Line-based family: use midpoint
            var locCurve = element.Location as LocationCurve;
            if (locCurve?.Curve != null)
                return locCurve.Curve.Evaluate(0.5, true);

            return null;
        }

        private FamilySymbol FindSymbol(List<FamilySymbol> allSymbols, string familyName)
        {
            return allSymbols.FirstOrDefault(fs =>
                fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Selection filter for pipe accessories only.</summary>
        private class PipeAccessoryFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
