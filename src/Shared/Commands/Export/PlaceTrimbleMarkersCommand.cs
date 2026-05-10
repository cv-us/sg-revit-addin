using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Places Trimble field point marker families at hanger and seismic brace locations
    /// for coordination with Trimble field layout tools. Supports three modes:
    ///   - Place Only: place markers without clearing existing ones
    ///   - Clear and Place: clear existing markers first, then place new ones
    ///   - Clear Only: remove Trimble markers without placing new ones
    ///
    /// Clears families matching these prefixes by default:
    ///   - "-Trimble-" (plugin-placed markers)
    ///   - "Trmb_FieldPoints_FieldPointGraphic_" (Trimble Field Link software markers)
    /// User can also specify additional custom prefixes to clear.
    ///
    /// Marker families by rod size:
    ///   - Pipes ≤ 4" nominal → "-Trimble-Hanger-3_8"
    ///   - Pipes > 4" nominal → "-Trimble-Hanger-1_2"
    ///   - Seismic brace anchors ��� "-Trimble-Seismic-Anchor"
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceTrimbleMarkersCommand : IExternalCommand
    {
        private const double LargeRodThreshold = 4.0 / 12.0; // 4 inches in feet

        private static readonly string[] DefaultClearPrefixes = new[]
        {
            "-Trimble-",
            "Trmb_FieldPoints_FieldPointGraphic_"
        };

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
                // ── Show dialog ──
                var dlg = new PlaceTrimbleMarkersDialog();
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                var mode = dlg.SelectedMode;
                bool doClear = mode == PlaceTrimbleMarkersDialog.TrimbleMode.ClearAndPlace ||
                               mode == PlaceTrimbleMarkersDialog.TrimbleMode.ClearOnly;
                bool doPlace = mode == PlaceTrimbleMarkersDialog.TrimbleMode.ClearAndPlace ||
                               mode == PlaceTrimbleMarkersDialog.TrimbleMode.PlaceOnly;

                // ── Build clear prefixes ──
                var clearPrefixes = new List<string>(DefaultClearPrefixes);
                if (dlg.ClearCustomPrefix && !string.IsNullOrWhiteSpace(dlg.ClearFamilyPrefix))
                {
                    var custom = dlg.ClearFamilyPrefix
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0);
                    clearPrefixes.AddRange(custom);
                }

                // ── Collect accessories if placing ──
                List<FamilyInstance> accessories = new List<FamilyInstance>();
                FamilySymbol sym38 = null, sym12 = null, symSeismic = null;
                var missing = new List<string>();

                if (doPlace)
                {
                    var selectedIds = uidoc.Selection.GetElementIds();

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

                    // Find marker family symbols
                    var familySymbols = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .ToList();

                    sym38 = FindSymbol(familySymbols, Hanger38Family);
                    sym12 = FindSymbol(familySymbols, Hanger12Family);
                    symSeismic = FindSymbol(familySymbols, SeismicAnchorFamily);

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
                }

                // ── Execute ──
                int cleared = 0;
                int placed38 = 0, placed12 = 0, placedSeismic = 0, skipped = 0;
                var errors = new List<string>();

                using (Transaction tx = new Transaction(doc, "Trimble Markers"))
                {
                    tx.Start();

                    if (doClear)
                    {
                        cleared = ClearMarkers(doc, activeView, clearPrefixes);
                    }

                    if (doPlace)
                    {
                        foreach (var accessory in accessories)
                        {
                            string familyName = accessory.Symbol?.Family?.Name ?? "";

                            if (familyName.IndexOf("Seismic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                familyName.IndexOf("Brace", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (symSeismic != null)
                                    placedSeismic += PlaceMarkerAtElement(doc, activeView, accessory, symSeismic, errors);
                                else skipped++;
                            }
                            else if (IsHanger(familyName))
                            {
                                double nomDia = GetNominalDiameter(accessory);
                                bool isLargeRod = nomDia > LargeRodThreshold;

                                if (isLargeRod && sym12 != null)
                                    placed12 += PlaceMarkerAtElement(doc, activeView, accessory, sym12, errors);
                                else if (!isLargeRod && sym38 != null)
                                    placed38 += PlaceMarkerAtElement(doc, activeView, accessory, sym38, errors);
                                else skipped++;
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                    }

                    tx.Commit();
                }

                // ── Report ──
                string report = "";

                if (doClear)
                    report += $"Cleared {cleared} existing Trimble marker{(cleared == 1 ? "" : "s")}.\n";

                if (doPlace)
                {
                    int totalPlaced = placed38 + placed12 + placedSeismic;
                    report += $"\nPlaced {totalPlaced} Trimble marker{(totalPlaced == 1 ? "" : "s")}:\n";
                    if (placed38 > 0) report += $"  3/8\" hangers: {placed38}\n";
                    if (placed12 > 0) report += $"  1/2\" hangers: {placed12}\n";
                    if (placedSeismic > 0) report += $"  Seismic anchors: {placedSeismic}\n";
                    if (skipped > 0) report += $"\n  Skipped: {skipped} (missing family or unrecognized type)\n";
                    if (missing.Count > 0)
                        report += $"\n  Missing families: {string.Join(", ", missing)}";
                }

                if (errors.Count > 0)
                    report += $"\n\n{errors.Count} error{(errors.Count == 1 ? "" : "s")}:\n" +
                              string.Join("\n", errors.Take(5));

                TaskDialog.Show("Trimble Markers", report.Trim());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Clears all family instances in the active view whose family name starts with
        /// any of the given prefixes. Searches both OST_GenericAnnotation (plugin markers)
        /// and OST_GenericModel (Trimble Field Link markers).
        /// </summary>
        private int ClearMarkers(Document doc, View view, List<string> prefixes)
        {
            var categories = new[]
            {
                BuiltInCategory.OST_GenericAnnotation,
                BuiltInCategory.OST_GenericModel
            };

            var toDelete = new List<ElementId>();

            foreach (var cat in categories)
            {
                var matches = new FilteredElementCollector(doc, view.Id)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .Where(e =>
                    {
                        var fi = e as FamilyInstance;
                        string fname = fi?.Symbol?.Family?.Name;
                        if (string.IsNullOrEmpty(fname)) return false;
                        return prefixes.Any(p =>
                            fname.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                    })
                    .Select(e => e.Id);

                toDelete.AddRange(matches);
            }

            if (toDelete.Count > 0)
                doc.Delete(toDelete);

            return toDelete.Count;
        }

        private int PlaceMarkerAtElement(Document doc, View view,
            FamilyInstance element, FamilySymbol symbol, List<string> errors)
        {
            try
            {
                XYZ pt = GetLocation(element);
                if (pt == null) return 0;

                // Offset Z by Rod Length so the marker is placed at the
                // insert location in the structure above, not at the hanger.
                double rodLength = GetRodLength(element);
                if (rodLength > 0)
                    pt = new XYZ(pt.X, pt.Y, pt.Z + rodLength);

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

        private double GetRodLength(FamilyInstance element)
        {
            var param = element.LookupParameter("Rod Length");
            if (param != null && param.HasValue)
                return param.AsDouble(); // already in feet
            return 0;
        }

        private XYZ GetLocation(FamilyInstance element)
        {
            var loc = element.Location as LocationPoint;
            if (loc != null) return loc.Point;

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

