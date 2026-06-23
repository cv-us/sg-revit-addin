using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using WinColor = System.Drawing.Color;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Colorizes pipes &amp; fittings by the construction status carried on
    /// their workset (Existing / Demo / Modify / New), so the color survives
    /// export to Navisworks (.nwc).
    ///
    /// WHY NOT FACE PAINT: Revit's Paint tool sets a per-FACE finish that the
    /// NWC exporter ignores — Navisworks colors each object from its BODY
    /// MATERIAL (the material's Graphics-tab shading color). View / filter /
    /// workset overrides also don't export. So color must live on the
    /// material the element actually resolves to:
    ///   • Pipes: material is segment/type-driven and the instance material
    ///     parameter is read-only, so we create a colored per-status DUPLICATE
    ///     of each pipe type ("Welded" → "Welded - New", whose routing-
    ///     preference segment uses a Status-colored material) and swap the
    ///     pipe to it via ChangeTypeId. This preserves the system distinction
    ///     (welded / threaded / grooved) while coloring it.
    ///   • Fittings / sprinklers / accessories: loadable families — set their
    ///     instance material parameter directly.
    ///
    /// WORKFLOW TIP: because the colored types/segments/materials are real
    /// model objects, run the command, export the NWC, then CLOSE WITHOUT
    /// SAVING (and, on a workshared model, without synchronizing) to avoid
    /// persisting them in the fab model. "Clear All Coloring" also reverts
    /// in-session: pipes are re-typed back (the duplicate's name encodes the
    /// original) and fitting materials reset.
    ///
    /// ⚠ The pipe type/segment duplication is intricate and can't be fully
    /// validated without a live model — expect field iteration. Failures are
    /// reported per type, never silent, and the transaction only commits
    /// successful swaps.
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
                if (!doc.IsWorkshared)
                {
                    TaskDialog.Show("Colorize by Workset",
                        "This document is not workshared, so it has no worksets to read status from.");
                    return Result.Cancelled;
                }

                var userWorksets = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .OrderBy(w => w.Name)
                    .Select(w => (id: w.Id.IntegerValue, name: w.Name))
                    .ToList();
                if (userWorksets.Count == 0)
                {
                    TaskDialog.Show("Colorize by Workset", "No user worksets found.");
                    return Result.Cancelled;
                }

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

            List<Element> targets = CollectTargets(uidoc, dlg.Scope, targetCats);
            if (targets.Count == 0)
            {
                TaskDialog.Show("Colorize by Workset", "No pipes/fittings found in the chosen scope.");
                return Result.Cancelled;
            }

            var perStatus = new Dictionary<StatusBucket, int>();
            int pipesTyped = 0, fittingsMat = 0, viewOverrides = 0;
            int skippedNoStatus = 0, fittingNoMat = 0, pipeTypeFail = 0;
            var typeFailNotes = new HashSet<string>();

            // Per-run cache so each (origType, status) is built once.
            var typeCache = new Dictionary<string, ElementId>();
            var segCache = new Dictionary<string, ElementId>();

            using (var tx = new Transaction(doc, "Colorize by Workset"))
            {
                tx.Start();

                ElementId solidFillId = GetSolidFillPatternId(doc);
                var matIds = new Dictionary<StatusBucket, ElementId>();
                if (dlg.AssignMaterial)
                    foreach (var st in ColorizeStatusInfo.Buckets)
                        matIds[st] = GetOrCreateStatusMaterial(doc, st, dlg.StatusColors[st], solidFillId);

                foreach (var elem in targets)
                {
                    if (!dlg.WorksetStatus.TryGetValue(elem.WorksetId.IntegerValue, out StatusBucket status)
                        || status == StatusBucket.Ignore)
                    {
                        skippedNoStatus++;
                        continue;
                    }
                    perStatus[status] = (perStatus.TryGetValue(status, out int c) ? c : 0) + 1;

                    // ── Material (NWC-exporting body color) ──
                    if (dlg.AssignMaterial)
                    {
                        if (elem is Pipe pipe)
                        {
                            try
                            {
                                ElementId coloredTypeId = GetOrCreateColoredPipeType(
                                    doc, pipe, status, matIds[status], typeCache, segCache, typeFailNotes);
                                if (coloredTypeId != ElementId.InvalidElementId
                                    && pipe.GetTypeId() != coloredTypeId)
                                {
                                    pipe.ChangeTypeId(coloredTypeId);
                                    pipesTyped++;
                                }
                                else if (coloredTypeId == ElementId.InvalidElementId)
                                {
                                    pipeTypeFail++;
                                }
                            }
                            catch (Exception ex)
                            {
                                pipeTypeFail++;
                                typeFailNotes.Add(Trunc(ex.Message));
                            }
                        }
                        else
                        {
                            if (SetInstanceMaterial(elem, matIds[status], out _)) fittingsMat++;
                            else fittingNoMat++;
                        }
                    }

                    // ── View override (Revit only) ──
                    if (dlg.ApplyViewOverride)
                    {
                        try
                        {
                            var rc = ToRevit(dlg.StatusColors[status]);
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
                        catch { }
                    }
                }

                tx.Commit();
            }

            var lines = new List<string>
            {
                $"Colorize by Workset — {targets.Count} element(s) in scope.",
                ""
            };
            foreach (var st in ColorizeStatusInfo.Buckets)
                lines.Add($"  {ColorizeStatusInfo.Label(st)}: {(perStatus.TryGetValue(st, out int v) ? v : 0)}");
            lines.Add("");
            if (dlg.AssignMaterial)
            {
                lines.Add($"Pipes re-typed to colored duplicates: {pipesTyped}  (exports to NWC)");
                lines.Add($"Fittings/etc. given a material:      {fittingsMat}  (exports to NWC)");
                if (fittingNoMat > 0) lines.Add($"Fittings with no writable material param: {fittingNoMat}");
                if (pipeTypeFail > 0) lines.Add($"Pipes whose colored type failed: {pipeTypeFail}");
                foreach (var n in typeFailNotes.Take(4)) lines.Add("   • " + n);
            }
            if (dlg.ApplyViewOverride) lines.Add($"View overrides (Revit only): {viewOverrides}");
            if (skippedNoStatus > 0) lines.Add($"Skipped (Ignored / unmapped workset): {skippedNoStatus}");
            lines.Add("");
            lines.Add("TIP: export the NWC, then CLOSE WITHOUT SAVING / SYNCING to keep the");
            lines.Add("colored types out of the fab model. Or use Clear All Coloring to revert.");
            lines.Add("(Navisworks must be in Shaded render style to show the colors.)");

            TaskDialog.Show("Colorize by Workset", string.Join("\n", lines));
            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════
        //  CLEAR (revert)
        // ══════════════════════════════════════════════════════════════

        private Result ClearAll(Document doc, List<BuiltInCategory> targetCats, ref string message)
        {
            var all = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(targetCats))
                .WhereElementIsNotElementType()
                .ToList();

            int reTyped = 0, matReset = 0, clearedOverrides = 0;

            // Build a name → PipeType lookup once for the stateless revert.
            var pipeTypesByName = new FilteredElementCollector(doc).OfClass(typeof(PipeType))
                .Cast<PipeType>().GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

            using (var tx = new Transaction(doc, "Clear Colorize by Workset"))
            {
                tx.Start();

                foreach (var elem in all)
                {
                    try
                    {
                        if (elem is Pipe pipe)
                        {
                            var t = doc.GetElement(pipe.GetTypeId()) as PipeType;
                            string orig = ColorizeStatusInfo.OriginalTypeName(t?.Name);
                            if (orig != null && pipeTypesByName.TryGetValue(orig, out ElementId origId))
                            {
                                pipe.ChangeTypeId(origId);
                                reTyped++;
                            }
                        }
                        else
                        {
                            // Reset instance material to "by category" (best effort).
                            if (SetInstanceMaterial(elem, ElementId.InvalidElementId, out bool changed) && changed)
                                matReset++;
                        }
                    }
                    catch { }
                }

                // Clear element graphic overrides across graphical model views.
                var emptyOgs = new OverrideGraphicSettings();
                var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && IsGraphicalModelView(v)).ToList();
                foreach (var v in views)
                    foreach (var elem in all)
                    {
                        try { v.SetElementOverrides(elem.Id, emptyOgs); clearedOverrides++; } catch { }
                    }

                tx.Commit();
            }

            TaskDialog.Show("Colorize by Workset",
                "Cleared status coloring.\n\n" +
                $"Pipes reverted to original type: {reTyped}\n" +
                $"Fitting materials reset:         {matReset}\n" +
                $"View overrides cleared:          {clearedOverrides}\n\n" +
                "Colored Status-* types/materials are left in the project (reused, harmless).\n" +
                "If you never saved, just close without saving to drop them entirely.");
            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════
        //  PIPE: colored per-status duplicate type
        // ══════════════════════════════════════════════════════════════

        private ElementId GetOrCreateColoredPipeType(Document doc, Pipe pipe, StatusBucket status,
            ElementId statusMatId, Dictionary<string, ElementId> typeCache,
            Dictionary<string, ElementId> segCache, HashSet<string> failNotes)
        {
            var origType = doc.GetElement(pipe.GetTypeId()) as PipeType;
            if (origType == null) return ElementId.InvalidElementId;

            // If this pipe already wears a colored duplicate, re-base to the
            // original before deciding (so re-runs with a new status work).
            string maybeOrig = ColorizeStatusInfo.OriginalTypeName(origType.Name);
            if (maybeOrig != null)
            {
                var baseType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
                    .FirstOrDefault(t => t.Name == maybeOrig);
                if (baseType != null) origType = baseType;
            }

            string newName = origType.Name + ColorizeStatusInfo.TypeSuffix(status);
            string cacheKey = newName;
            if (typeCache.TryGetValue(cacheKey, out ElementId cached)) return cached;

            var existing = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
                .FirstOrDefault(t => t.Name == newName);
            if (existing != null) { typeCache[cacheKey] = existing.Id; return existing.Id; }

            PipeType newType;
            try { newType = origType.Duplicate(newName) as PipeType; }
            catch (Exception ex) { failNotes.Add($"{origType.Name}: duplicate failed ({Trunc(ex.Message)})"); return ElementId.InvalidElementId; }
            if (newType == null) return ElementId.InvalidElementId;

            // Recolor the duplicate's routing-preference segments.
            try
            {
                var rpm = newType.RoutingPreferenceManager;
                int n = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);
                for (int i = 0; i < n; i++)
                {
                    RoutingPreferenceRule rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Segments, i);
                    var origSeg = doc.GetElement(rule.MEPPartId) as PipeSegment;
                    if (origSeg == null) continue;

                    ElementId coloredSegId = GetOrCreateColoredSegment(doc, origSeg, statusMatId, segCache);
                    if (coloredSegId == ElementId.InvalidElementId) continue;

                    var newRule = new RoutingPreferenceRule(coloredSegId, rule.Description);
                    for (int c = 0; c < rule.NumberOfCriteria; c++)
                        newRule.AddCriterion(rule.GetCriterion(c));

                    rpm.RemoveRule(RoutingPreferenceRuleGroupType.Segments, i);
                    rpm.AddRule(RoutingPreferenceRuleGroupType.Segments, newRule, i);
                }
            }
            catch (Exception ex)
            {
                // Type exists but uncolored — still usable; flag it.
                failNotes.Add($"{origType.Name}: recolor failed ({Trunc(ex.Message)})");
            }

            typeCache[cacheKey] = newType.Id;
            return newType.Id;
        }

        private ElementId GetOrCreateColoredSegment(Document doc, PipeSegment origSeg,
            ElementId statusMatId, Dictionary<string, ElementId> segCache)
        {
            ElementId scheduleId = origSeg.ScheduleTypeId;
            string key = statusMatId.IntegerValue + ":" + scheduleId.IntegerValue;
            if (segCache.TryGetValue(key, out ElementId cached)) return cached;

            // Reuse any existing segment that already pairs this material + schedule.
            var existing = new FilteredElementCollector(doc).OfClass(typeof(PipeSegment)).Cast<PipeSegment>()
                .FirstOrDefault(s => s.MaterialId == statusMatId && s.ScheduleTypeId == scheduleId);
            if (existing != null) { segCache[key] = existing.Id; return existing.Id; }

            try
            {
                var sizes = origSeg.GetSizes().ToList();
                PipeSegment newSeg = PipeSegment.Create(doc, statusMatId, scheduleId, sizes);
                ElementId newSegId = newSeg?.Id ?? ElementId.InvalidElementId;
                if (newSegId != ElementId.InvalidElementId) segCache[key] = newSegId;
                return newSegId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  FITTINGS: instance material parameter
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Sets a writable INSTANCE material parameter to <paramref name="matId"/>.
        /// Only instance params (so coloring doesn't leak across other
        /// instances of the same type). Returns false if none is writable.
        /// </summary>
        private bool SetInstanceMaterial(Element elem, ElementId matId, out bool changed)
        {
            changed = false;
            Parameter p = elem.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)
                          ?? elem.LookupParameter("Material");
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId) return false;
            if (p.AsElementId() == matId) return true; // already set
            p.Set(matId);
            changed = true;
            return true;
        }

        // ══════════════════════════════════════════════════════════════
        //  MATERIAL + HELPERS
        // ══════════════════════════════════════════════════════════════

        private ElementId GetOrCreateStatusMaterial(Document doc, StatusBucket status, WinColor color, ElementId solidFillId)
        {
            string name = ColorizeStatusInfo.MaterialName(status);
            var rc = ToRevit(color);

            Material mat = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (mat == null)
            {
                ElementId id = Material.Create(doc, name);
                mat = doc.GetElement(id) as Material;
            }
            if (mat == null) return ElementId.InvalidElementId;

            // Shading color is what the NWC exporter reads.
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

        private List<Element> CollectTargets(UIDocument uidoc, ColorizeScope scope, List<BuiltInCategory> cats)
        {
            Document doc = uidoc.Document;
            var filter = new ElementMulticategoryFilter(cats);
            switch (scope)
            {
                case ColorizeScope.Selection:
                    return uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id))
                        .Where(e => e?.Category != null && cats.Contains((BuiltInCategory)e.Category.Id.IntegerValue))
                        .ToList();
                case ColorizeScope.ActiveView:
                    return new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WherePasses(filter).WhereElementIsNotElementType().ToList();
                default:
                    return new FilteredElementCollector(doc)
                        .WherePasses(filter).WhereElementIsNotElementType().ToList();
            }
        }

        private ElementId GetSolidFillPatternId(Document doc)
        {
            var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
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
        private static string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 80 ? s.Substring(0, 80) : s);
    }
}
