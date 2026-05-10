using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Automatically inserts graphic scale bar annotations on sheets based on
    /// the scale of each view placed on the sheet.
    ///
    /// WORKFLOW:
    ///   1. Collect sheets (all or user-selected)
    ///   2. Delete existing "-Graphic Scale Bar" instances from those sheets
    ///   3. For each view on each sheet:
    ///      a. Read the view's scale
    ///      b. Map scale to the correct "-Graphic Scale Bar" family type
    ///      c. Place the scale bar on the sheet at a calculated position
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GraphicScaleBarsCommand : IExternalCommand
    {
        private const string ScaleBarFamilyName = "-Graphic Scale Bar";

        // Placement offsets (feet)
        private const double BaseXOffset = 0.084;         // ~1" from left edge
        private const double BaseYOffset = 2.0 / 12.0;    // 2" from bottom
        private const double StackSpacing = 2.0 / 12.0;   // 2" between stacked bars

        /// <summary>
        /// Maps Revit view scale denominator to the "-Graphic Scale Bar" family type name.
        /// Scale value is the denominator of 1:X (e.g., 48 = 1/4" = 1'-0").
        /// </summary>
        private static readonly Dictionary<int, string> ScaleToTypeName = new Dictionary<int, string>
        {
            // Standard architectural scales
            { 1,    "12\" = 1'-0\"" },
            { 2,    "6\" = 1'-0\"" },
            { 4,    "3\" = 1'-0\"" },
            { 8,    "1-1/2\" = 1'-0\"" },
            { 12,   "1\" = 1'-0\"" },
            { 16,   "3/4\" = 1'-0\"" },
            { 24,   "1/2\" = 1'-0\"" },
            { 32,   "3/8\" = 1'-0\"" },
            { 48,   "1/4\" = 1'-0\"" },
            { 64,   "3/16\" = 1'-0\"" },
            { 96,   "1/8\" = 1'-0\"" },
            { 128,  "3/32\" = 1'-0\"" },
            { 192,  "1/16\" = 1'-0\"" },
            { 256,  "3/64\" = 1'-0\"" },
            { 384,  "1/32\" = 1'-0\"" },
            { 768,  "1/64\" = 1'-0\"" },
            // Engineering scales
            { 120,  "1\" = 10'-0\"" },
            { 240,  "1\" = 20'-0\"" },
            { 360,  "1\" = 30'-0\"" },
            { 480,  "1\" = 40'-0\"" },
            { 600,  "1\" = 50'-0\"" },
            { 720,  "1\" = 60'-0\"" },
            { 840,  "1\" = 70'-0\"" },
            { 960,  "1\" = 80'-0\"" },
            { 1080, "1\" = 90'-0\"" },
            { 1200, "1\" = 100'-0\"" },
            { 1920, "1\" = 160'-0\"" },
            { 2400, "1\" = 200'-0\"" },
            { 3600, "1\" = 300'-0\"" },
            { 4800, "1\" = 400'-0\"" },
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Step 1: Collect all sheets ──
                var allSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(vs => !vs.IsPlaceholder)
                    .ToList();

                if (allSheets.Count == 0)
                {
                    TaskDialog.Show("Graphic Scale Bars", "No sheets found in the project.");
                    return Result.Cancelled;
                }

                // ── Step 2: Show dialog ──
                var sheetItems = allSheets
                    .Select(s => new GraphicScaleBarsDialog.SheetItem
                    {
                        Number = s.SheetNumber,
                        Name = s.Name
                    })
                    .ToList();

                var dialog = new GraphicScaleBarsDialog(sheetItems);
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                // Filter sheets based on selection
                List<ViewSheet> targetSheets;
                if (dialog.ProcessAllSheets)
                {
                    targetSheets = allSheets;
                }
                else
                {
                    var selectedNumbers = new HashSet<string>(dialog.SelectedSheetNumbers);
                    targetSheets = allSheets.Where(s => selectedNumbers.Contains(s.SheetNumber)).ToList();
                }

                // ── Step 3: Find scale bar family ──
                var scaleBarTypes = GetScaleBarFamilyTypes(doc);
                if (scaleBarTypes.Count == 0)
                {
                    TaskDialog.Show("Graphic Scale Bars",
                        "Scale bar family \"" + ScaleBarFamilyName + "\" not loaded in the project.\n\n" +
                        "Please load the family before running this command.");
                    return Result.Failed;
                }

                // ── Step 4: Process sheets ──
                int barsDeleted = 0;
                int barsPlaced = 0;
                int sheetsProcessed = 0;
                int viewsSkipped = 0;

                using (var tw = new TransactionWrapper(doc, "Insert Graphic Scale Bars"))
                {
                    // Activate all types we might use
                    foreach (var fs in scaleBarTypes.Values)
                    {
                        if (!fs.IsActive)
                            fs.Activate();
                    }

                    foreach (var sheet in targetSheets)
                    {
                        // Delete existing scale bars on this sheet
                        barsDeleted += DeleteExistingScaleBars(doc, sheet);

                        // Get viewports on the sheet
                        var viewportIds = sheet.GetAllViewports();
                        if (viewportIds == null || viewportIds.Count == 0)
                            continue;

                        // Get views from viewports, filter to valid views with known scales
                        var viewsWithScales = new List<(View view, int scale, string typeName)>();
                        foreach (var vpId in viewportIds)
                        {
                            var viewport = doc.GetElement(vpId) as Viewport;
                            if (viewport == null) continue;

                            var view = doc.GetElement(viewport.ViewId) as View;
                            if (view == null) continue;

                            int scale = view.Scale;
                            if (ScaleToTypeName.TryGetValue(scale, out string typeName)
                                && scaleBarTypes.ContainsKey(typeName))
                            {
                                viewsWithScales.Add((view, scale, typeName));
                            }
                            else
                            {
                                viewsSkipped++;
                            }
                        }

                        if (viewsWithScales.Count == 0)
                            continue;

                        // Deduplicate by scale — one bar per unique scale on the sheet
                        var uniqueScales = viewsWithScales
                            .GroupBy(v => v.scale)
                            .Select(g => g.First())
                            .ToList();

                        // Calculate placement position — bottom-left area of sheet
                        double sheetWidth = GetSheetWidth(doc, sheet);

                        for (int i = 0; i < uniqueScales.Count; i++)
                        {
                            var (view, scale, typeName) = uniqueScales[i];
                            var familySymbol = scaleBarTypes[typeName];

                            // Stack vertically: first bar at BaseYOffset, each subsequent bar above
                            double x = BaseXOffset;
                            double y = BaseYOffset + (i * StackSpacing);

                            XYZ point = new XYZ(x, y, 0);

                            try
                            {
                                // Place on sheet (ViewSheet is a View)
                                FamilyInstance instance = doc.Create.NewFamilyInstance(
                                    point, familySymbol, sheet);

                                if (instance != null)
                                    barsPlaced++;
                            }
                            catch (Exception)
                            {
                                // Skip if placement fails for this scale
                            }
                        }

                        sheetsProcessed++;
                    }

                    tw.Commit();
                }

                TaskDialog.Show("Graphic Scale Bars",
                    $"Completed:\n" +
                    $"  {sheetsProcessed} sheet(s) processed\n" +
                    $"  {barsDeleted} existing scale bar(s) removed\n" +
                    $"  {barsPlaced} new scale bar(s) placed\n" +
                    (viewsSkipped > 0 ? $"  {viewsSkipped} view(s) skipped (unrecognized scale)" : ""));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Insert Graphic Scale Bars failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Gets all loaded family types for the "-Graphic Scale Bar" family,
        /// keyed by type name.
        /// </summary>
        private Dictionary<string, FamilySymbol> GetScaleBarFamilyTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .Cast<FamilySymbol>()
                .Where(fs => fs.Family.Name == ScaleBarFamilyName)
                .ToDictionary(fs => fs.Name, fs => fs);
        }

        /// <summary>
        /// Deletes all existing "-Graphic Scale Bar" instances on a specific sheet.
        /// Returns count of deleted elements.
        /// </summary>
        private int DeleteExistingScaleBars(Document doc, ViewSheet sheet)
        {
            var existing = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    if (e is FamilyInstance fi)
                        return fi.Symbol?.Family?.Name == ScaleBarFamilyName;
                    return false;
                })
                .ToList();

            foreach (var elem in existing)
                doc.Delete(elem.Id);

            return existing.Count;
        }

        /// <summary>
        /// Gets the sheet width in feet from the titleblock.
        /// Falls back to 2.833 feet (34") if not found — standard ANSI D size.
        /// </summary>
        private double GetSheetWidth(Document doc, ViewSheet sheet)
        {
            // Try to get titleblock on the sheet
            var titleblock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault() as FamilyInstance;

            if (titleblock != null)
            {
                var widthParam = titleblock.LookupParameter("Sheet Width");
                if (widthParam != null && widthParam.HasValue)
                    return widthParam.AsDouble(); // Already in feet
            }

            return 2.833; // Fallback: ~34 inches
        }
    }
}
