using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using WinColor = System.Drawing.Color;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Colorizes pipes &amp; fittings by the construction status carried on
    /// their workset (Existing / Demo / Modify / New), to support a
    /// sprinkler construction-status workflow that must survive export to
    /// Navisworks (.nwc).
    ///
    /// WHY FACE PAINT: Revit view filters, workset overrides, and
    /// view-specific element graphic overrides do NOT export to NWC. Only
    /// MATERIAL color bakes into the geometry and survives the append on the
    /// GC's side. So the primary path assigns a per-status material to each
    /// element via <see cref="Document.Paint(ElementId, Face, ElementId)"/>
    /// — the most dependable way to color both pipes (whose segment-driven
    /// material parameter is usually read-only) and fittings (loadable
    /// families that resist a clean material write). The view-override path
    /// is offered too, but clearly labeled in-Revit-only.
    ///
    /// RELIABLE STRIP: because paint isn't cumulative and
    /// <see cref="Document.RemovePaint(ElementId, Face)"/> is idempotent,
    /// "Clear All Coloring" un-paints every targeted element face and clears
    /// element graphic overrides — restoring the elements no matter how many
    /// times the command was run. The Status-* materials are left in the
    /// project (reused, harmless).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ColorizeByWorksetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Worksharing required ──
                if (!doc.IsWorkshared)
                {
                    TaskDialog.Show("Colorize by Workset",
                        "This document is not workshared, so it has no worksets to read status from.\n\n" +
                        "Enable worksharing (and put systems on status worksets) first.");
                    return Result.Cancelled;
                }

                // ── User worksets ──
                var userWorksets = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .OrderBy(w => w.Name)
                    .Select(w => (id: w.Id.IntegerValue, name: w.Name))
                    .ToList();

                if (userWorksets.Count == 0)
                {
                    TaskDialog.Show("Colorize by Workset", "No user worksets found in this document.");
                    return Result.Cancelled;
                }

                // ── Whole-model counts per workset (for the dialog preview) ──
                var coreCats = new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting
                };
                var counts = new Dictionary<int, int>();
                foreach (var e in new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(coreCats))
                    .WhereElementIsNotElementType())
                {
                    int w = e.WorksetId.IntegerValue;
                    counts[w] = (counts.TryGetValue(w, out int c) ? c : 0) + 1;
                }

                // ── Dialog ──
                using (var dlg = new ColorizeByWorksetDialog(userWorksets, counts))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    var targetCats = new List<BuiltInCategory>
                    {
                        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting
                    };
                    if (dlg.IncludeExtraCategories)
                    {
                        targetCats.Add(BuiltInCategory.OST_Sprinklers);
                        targetCats.Add(BuiltInCategory.OST_PipeAccessory);
                    }

                    if (dlg.Action == ColorizeByWorksetDialog.ColorizeAction.Clear)
                        return ClearAll(doc, targetCats, ref message);

                    return Apply(uidoc, dlg, targetCats, ref message);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  APPLY
        // ══════════════════════════════════════════════════════════════

        private Result Apply(UIDocument uidoc, ColorizeByWorksetDialog dlg,
            List<BuiltInCategory> targetCats, ref string message)
        {
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // ── Collect target elements per scope ──
            List<Element> targets = CollectTargets(uidoc, dlg.Scope, targetCats);
            if (targets.Count == 0)
            {
                TaskDialog.Show("Colorize by Workset", "No pipes/fittings found in the chosen scope.");
                return Result.Cancelled;
            }

            int total = targets.Count;
            var perStatus = new Dictionary<StatusBucket, int>();
            int paintedFaces = 0, viewOverrides = 0, skippedNoStatus = 0, paintFailed = 0, noGeom = 0;

            using (var tx = new Transaction(doc, "Colorize by Workset"))
            {
                tx.Start();

                ElementId solidFillId = GetSolidFillPatternId(doc);

                // Ensure Status-* materials exist with the chosen colors.
                var matIds = new Dictionary<StatusBucket, ElementId>();
                if (dlg.AssignMaterial)
                {
                    foreach (var st in ColorizeStatusInfo.Buckets)
                        matIds[st] = GetOrCreateStatusMaterial(doc, st, dlg.StatusColors[st], solidFillId);
                }

                var geomOpt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

                foreach (var elem in targets)
                {
                    StatusBucket status;
                    if (!dlg.WorksetStatus.TryGetValue(elem.WorksetId.IntegerValue, out status)
                        || status == StatusBucket.Ignore)
                    {
                        skippedNoStatus++;
                        continue;
                    }

                    perStatus[status] = (perStatus.TryGetValue(status, out int c) ? c : 0) + 1;
                    var rc = ToRevit(dlg.StatusColors[status]);

                    // Material (paint faces) — the NWC-exporting path.
                    if (dlg.AssignMaterial)
                    {
                        try
                        {
                            int faces = PaintElementFaces(doc, elem, matIds[status], geomOpt);
                            if (faces > 0) paintedFaces += faces;
                            else noGeom++;
                        }
                        catch { paintFailed++; }
                    }

                    // View graphic override — in-Revit only.
                    if (dlg.ApplyViewOverride)
                    {
                        try
                        {
                            var ogs = new OverrideGraphicSettings();
                            ogs.SetProjectionLineColor(rc);
                            ogs.SetSurfaceForegroundPatternColor(rc);
                            ogs.SetCutForegroundPatternColor(rc);
                            if (solidFillId != ElementId.InvalidElementId)
                            {
                                ogs.SetSurfaceForegroundPatternId(solidFillId);
                                ogs.SetCutForegroundPatternId(solidFillId);
                            }
                            activeView.SetElementOverrides(elem.Id, ogs);
                            viewOverrides++;
                        }
                        catch { /* non-critical */ }
                    }
                }

                tx.Commit();
            }

            // ── Report ──
            var lines = new List<string>
            {
                $"Colorize by Workset — {total} element(s) in scope.",
                ""
            };
            foreach (var st in ColorizeStatusInfo.Buckets)
                lines.Add($"  {ColorizeStatusInfo.Label(st)}: {(perStatus.TryGetValue(st, out int v) ? v : 0)}");
            lines.Add("");
            if (dlg.AssignMaterial)
                lines.Add($"Material painted onto {paintedFaces} face(s) (exports to NWC).");
            if (dlg.ApplyViewOverride)
                lines.Add($"View overrides applied (Revit only): {viewOverrides}.");
            if (skippedNoStatus > 0)
                lines.Add($"Skipped (workset Ignored / unmapped): {skippedNoStatus}.");
            if (noGeom > 0)
                lines.Add($"No paintable geometry (param-only/empty): {noGeom}.");
            if (paintFailed > 0)
                lines.Add($"Paint failed on {paintFailed} element(s) — see those individually.");
            lines.Add("");
            lines.Add("Run \"Clear All Coloring\" in the dialog to revert everything.");

            TaskDialog.Show("Colorize by Workset", string.Join("\n", lines));
            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════
        //  CLEAR
        // ══════════════════════════════════════════════════════════════

        private Result ClearAll(Document doc, List<BuiltInCategory> targetCats, ref string message)
        {
            // Reset works whole-model regardless of scope/run count: un-paint
            // every targeted element face and clear element graphic overrides
            // in every non-template graphical view.
            var allTargets = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(targetCats))
                .WhereElementIsNotElementType()
                .ToList();

            int unpainted = 0, clearedOverrides = 0;

            using (var tx = new Transaction(doc, "Clear Colorize by Workset"))
            {
                tx.Start();

                var geomOpt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

                // Un-paint.
                foreach (var elem in allTargets)
                {
                    try
                    {
                        if (RemoveElementPaint(doc, elem, geomOpt)) unpainted++;
                    }
                    catch { /* keep going */ }
                }

                // Clear element graphic overrides on these elements in every
                // graphical model view.
                var emptyOgs = new OverrideGraphicSettings();
                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && IsGraphicalModelView(v))
                    .ToList();

                foreach (var v in views)
                {
                    foreach (var elem in allTargets)
                    {
                        try { v.SetElementOverrides(elem.Id, emptyOgs); clearedOverrides++; }
                        catch { }
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Colorize by Workset",
                "Cleared all status coloring.\n\n" +
                $"Un-painted: {unpainted} element(s).\n" +
                $"Cleared view overrides: {clearedOverrides} (across model views).\n\n" +
                "Status-* materials are kept in the project for reuse.");
            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        private List<Element> CollectTargets(UIDocument uidoc, ColorizeScope scope, List<BuiltInCategory> cats)
        {
            Document doc = uidoc.Document;
            var filter = new ElementMulticategoryFilter(cats);

            switch (scope)
            {
                case ColorizeScope.Selection:
                    return uidoc.Selection.GetElementIds()
                        .Select(id => doc.GetElement(id))
                        .Where(e => e != null && e.Category != null
                            && cats.Contains((BuiltInCategory)e.Category.Id.IntegerValue))
                        .ToList();

                case ColorizeScope.ActiveView:
                    return new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WherePasses(filter)
                        .WhereElementIsNotElementType()
                        .ToList();

                default:
                    return new FilteredElementCollector(doc)
                        .WherePasses(filter)
                        .WhereElementIsNotElementType()
                        .ToList();
            }
        }

        /// <summary>Paints every face of every solid in the element. Returns faces painted.</summary>
        private int PaintElementFaces(Document doc, Element elem, ElementId matId, Options opt)
        {
            GeometryElement ge = elem.get_Geometry(opt);
            if (ge == null) return 0;
            return PaintGeometry(doc, elem.Id, ge, matId);
        }

        private int PaintGeometry(Document doc, ElementId id, GeometryElement ge, ElementId matId)
        {
            int painted = 0;
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Faces.Size > 0)
                {
                    foreach (Face f in s.Faces)
                    {
                        try { doc.Paint(id, f, matId); painted++; } catch { }
                    }
                }
                else if (go is GeometryInstance gi)
                {
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    if (inner != null) painted += PaintGeometry(doc, id, inner, matId);
                }
            }
            return painted;
        }

        /// <summary>Removes paint from every face of the element. Returns true if any face was painted.</summary>
        private bool RemoveElementPaint(Document doc, Element elem, Options opt)
        {
            GeometryElement ge = elem.get_Geometry(opt);
            if (ge == null) return false;
            return RemoveGeometryPaint(doc, elem.Id, ge);
        }

        private bool RemoveGeometryPaint(Document doc, ElementId id, GeometryElement ge)
        {
            bool any = false;
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Faces.Size > 0)
                {
                    foreach (Face f in s.Faces)
                    {
                        try
                        {
                            if (doc.IsPainted(id, f)) { doc.RemovePaint(id, f); any = true; }
                        }
                        catch { }
                    }
                }
                else if (go is GeometryInstance gi)
                {
                    GeometryElement inner = null;
                    try { inner = gi.GetInstanceGeometry(); } catch { }
                    if (inner != null) any |= RemoveGeometryPaint(doc, id, inner);
                }
            }
            return any;
        }

        /// <summary>Find-or-create a project material named "Status-{X}" set to the given color.</summary>
        private ElementId GetOrCreateStatusMaterial(Document doc, StatusBucket status, WinColor color, ElementId solidFillId)
        {
            string name = ColorizeStatusInfo.MaterialName(status);
            var rc = ToRevit(color);

            Material mat = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

            if (mat == null)
            {
                ElementId id = Material.Create(doc, name);
                mat = doc.GetElement(id) as Material;
            }
            if (mat == null) return ElementId.InvalidElementId;

            // Shading color is what exports to NWC as object color.
            mat.Color = rc;
            mat.SurfaceForegroundPatternColor = rc;
            mat.CutForegroundPatternColor = rc;
            mat.Transparency = 0;
            mat.Shininess = 0;
            if (solidFillId != ElementId.InvalidElementId)
            {
                try { mat.SurfaceForegroundPatternId = solidFillId; } catch { }
                try { mat.CutForegroundPatternId = solidFillId; } catch { }
            }
            return mat.Id;
        }

        private ElementId GetSolidFillPatternId(Document doc)
        {
            var solid = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern() != null && f.GetFillPattern().IsSolidFill);
            return solid?.Id ?? ElementId.InvalidElementId;
        }

        private static bool IsGraphicalModelView(View v)
        {
            switch (v.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.ThreeD:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                    return true;
                default:
                    return false;
            }
        }

        private static Color ToRevit(WinColor c) => new Color(c.R, c.G, c.B);
    }
}
