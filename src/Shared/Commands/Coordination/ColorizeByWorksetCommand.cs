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
                        BuiltInCategory.OST_PipeCurves,
                        BuiltInCategory.OST_PipeFitting,
                        BuiltInCategory.OST_FlexPipeCurves
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

            // ── PRE-PASS: bind a Material parameter to By-Category fab families
            //    (fittings/accessories whose solids are "By Category" with NO
            //    material param). EditFamily → add a type Material param → wire
            //    every solid's material to it → reload. After this the normal
            //    symbol-duplicate path can color them per status. Must run
            //    OUTSIDE the coloring transaction (EditFamily/LoadFamily own theirs).
            //    Discarded on close-without-saving, like everything else here. ──
            int famBound = 0, famEditFail = 0;
            var famBoundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var famEditFailNotes = new HashSet<string>();
            if (dlg.AssignMaterial && dlg.DeepColor)
            {
                var famsToEdit = new Dictionary<int, Family>();
                foreach (var fi in targets.OfType<FamilyInstance>())
                {
                    if (!dlg.WorksetStatus.TryGetValue(fi.WorksetId.IntegerValue, out var st) || st == StatusBucket.Ignore) continue;
                    var fam = fi.Symbol?.Family;
                    if (fam == null || famsToEdit.ContainsKey(fam.Id.IntegerValue)) continue;
                    if (FamilyHasMaterialParam(doc, fam)) continue; // already colorable
                    famsToEdit[fam.Id.IntegerValue] = fam;
                }
                foreach (var fam in famsToEdit.Values)
                {
                    if (BindMaterialParamToFamily(doc, fam, out string note)) { famBound++; famBoundNames.Add(fam.Name); }
                    else { famEditFail++; if (note != null) famEditFailNotes.Add(note); }
                }
                // Family reloads can invalidate cached element refs → re-collect.
                if (famBound > 0) targets = CollectTargets(uidoc, dlg.Scope, targetCats);
            }

            var perStatus = new Dictionary<StatusBucket, int>();
            int pipesTyped = 0, fittingsMat = 0, viewOverrides = 0;
            int skippedNoStatus = 0, fittingNoMat = 0, pipeTypeFail = 0, flexCount = 0;
            var typeFailNotes = new HashSet<string>();
            var fittingNoMatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var flexTally = new Dictionary<StatusBucket, int>();
            StatusBucket flexStatus = StatusBucket.New;
            bool flexColored = false;

            // Per-run cache so each (origType, status) is built once.
            var typeCache = new Dictionary<string, ElementId>();
            var segCache = new Dictionary<string, ElementId>();
            var symCache = new Dictionary<string, ElementId>();

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
                        // Rigid pipe: colored per-status duplicate type (material
                        // is segment-driven) → ChangeTypeId.
                        if (elem is Pipe)
                        {
                            try
                            {
                                var curve = (MEPCurve)elem;
                                var origType = doc.GetElement(curve.GetTypeId()) as MEPCurveType;
                                ElementId coloredTypeId = origType == null
                                    ? ElementId.InvalidElementId
                                    : GetOrCreateColoredMepType(doc, origType, status, matIds[status], typeCache, segCache, typeFailNotes);
                                if (coloredTypeId != ElementId.InvalidElementId && curve.GetTypeId() != coloredTypeId)
                                {
                                    curve.ChangeTypeId(coloredTypeId);
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
                        // Flex pipe: FlexPipeType has NO material parameter and the
                        // body is "By Category", so there's no per-element hook.
                        // Tally its status; we color all flex ONE global color below.
                        else if (elem is FlexPipe)
                        {
                            flexCount++;
                            flexTally[status] = (flexTally.TryGetValue(status, out int fc) ? fc : 0) + 1;
                        }
                        // Loadable families (fittings / accessories / sprinklers):
                        // writable instance material param, else symbol-duplicate
                        // with material params set. The pre-pass above bound a
                        // material param to families that lacked one.
                        else if (elem is FamilyInstance fi)
                        {
                            string famLabel = FamilyLabel(fi);
                            try
                            {
                                if (ColorLoadableInstance(doc, fi, status, matIds[status], symCache))
                                    fittingsMat++;
                                else
                                { fittingNoMat++; fittingNoMatNames.Add(famLabel); }
                            }
                            catch (Exception ex)
                            {
                                fittingNoMat++;
                                fittingNoMatNames.Add(famLabel);
                                typeFailNotes.Add(Trunc(ex.Message));
                            }
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

                // ── Global flex color: set the OST_FlexPipeCurves category
                //    material (By-Category source) to the dominant flex status's
                //    color. One color for all flex — the only option, since flex
                //    has no per-element material hook. ──
                if (dlg.AssignMaterial && dlg.DeepColor && flexCount > 0 && flexTally.Count > 0)
                {
                    flexStatus = flexTally.OrderByDescending(k => k.Value).First().Key;
                    flexColored = SetCategoryMaterial(doc, BuiltInCategory.OST_FlexPipeCurves, matIds[flexStatus]);
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
                lines.Add($"Pipes re-typed to colored duplicates:    {pipesTyped}  (exports to NWC)");
                lines.Add($"Fittings/sprinklers/accessories colored: {fittingsMat}  (exports to NWC)");
                if (dlg.DeepColor)
                {
                    if (famBound > 0)
                    {
                        lines.Add($"By-Category fab families bound to a material param: {famBound}");
                        foreach (var n in famBoundNames.OrderBy(s => s).Take(12)) lines.Add("     + " + n);
                        if (famBoundNames.Count > 12) lines.Add($"     …and {famBoundNames.Count - 12} more.");
                    }
                    if (flexColored)
                        lines.Add($"Flex pipes ({flexCount}) → ONE global color: {ColorizeStatusInfo.Label(flexStatus)}  (By-Category, can't be per-status)");
                    else if (flexCount > 0)
                        lines.Add($"Flex pipes ({flexCount}): left uncolored (couldn't set the flex category material)");
                    if (famEditFail > 0)
                    {
                        lines.Add($"Families that couldn't be bound: {famEditFail}");
                        foreach (var n in famEditFailNotes.Take(8)) lines.Add("     ✗ " + n);
                    }
                }
                if (fittingNoMat > 0)
                {
                    lines.Add($"Fittings still not colored (no material param): {fittingNoMat}");
                    foreach (var n in fittingNoMatNames.OrderBy(s => s).Take(10)) lines.Add("     ✗ " + n);
                    if (fittingNoMatNames.Count > 10) lines.Add($"     …and {fittingNoMatNames.Count - 10} more.");
                }
                if (pipeTypeFail > 0) lines.Add($"Pipes whose colored type failed: {pipeTypeFail}");
                foreach (var n in typeFailNotes.Take(4)) lines.Add("   • " + n);
            }
            if (dlg.ApplyViewOverride) lines.Add($"View overrides (Revit only): {viewOverrides}");
            if (skippedNoStatus > 0) lines.Add($"Skipped (Ignored / unmapped workset): {skippedNoStatus}");
            lines.Add("");
            lines.Add("TIP: export the NWC, then CLOSE WITHOUT SAVING / SYNCING to keep the");
            lines.Add("colored types AND the in-memory family edits out of the fab model.");
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

            // Name → type lookups for the stateless revert (pipe + flex types).
            var mepTypesByName = new FilteredElementCollector(doc).OfClass(typeof(MEPCurveType))
                .Cast<MEPCurveType>().GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

            using (var tx = new Transaction(doc, "Clear Colorize by Workset"))
            {
                tx.Start();

                foreach (var elem in all)
                {
                    try
                    {
                        if (elem is Pipe || elem is FlexPipe)
                        {
                            var curve = (MEPCurve)elem;
                            var t = doc.GetElement(curve.GetTypeId()) as MEPCurveType;
                            string orig = ColorizeStatusInfo.OriginalTypeName(t?.Name);
                            if (orig != null && mepTypesByName.TryGetValue(orig, out ElementId origId))
                            {
                                curve.ChangeTypeId(origId);
                                reTyped++;
                            }
                        }
                        else if (elem is FamilyInstance fi)
                        {
                            // If the symbol is a colored duplicate, swap it back.
                            var sym = fi.Symbol;
                            string origSym = ColorizeStatusInfo.OriginalTypeName(sym?.Name);
                            if (origSym != null && sym != null)
                            {
                                var baseSym = sym.Family.GetFamilySymbolIds()
                                    .Select(id => doc.GetElement(id) as FamilySymbol)
                                    .FirstOrDefault(s => s != null && s.Name == origSym);
                                if (baseSym != null)
                                {
                                    if (!baseSym.IsActive) baseSym.Activate();
                                    fi.Symbol = baseSym;
                                    reTyped++;
                                    continue;
                                }
                            }
                            // Else reset a writable instance material to by-category.
                            Parameter ip = fi.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM) ?? fi.LookupParameter("Material");
                            if (ip != null && !ip.IsReadOnly && ip.StorageType == StorageType.ElementId
                                && ip.AsElementId() != ElementId.InvalidElementId)
                            {
                                ip.Set(ElementId.InvalidElementId);
                                matReset++;
                            }
                        }
                    }
                    catch { }
                }

                // Revert the global flex category color (set during deep-color).
                SetCategoryMaterial(doc, BuiltInCategory.OST_FlexPipeCurves, ElementId.InvalidElementId);

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
        //  PIPE + FLEX: colored per-status duplicate type
        // ══════════════════════════════════════════════════════════════

        private ElementId GetOrCreateColoredMepType(Document doc, MEPCurveType origType, StatusBucket status,
            ElementId statusMatId, Dictionary<string, ElementId> typeCache,
            Dictionary<string, ElementId> segCache, HashSet<string> failNotes)
        {
            if (origType == null) return ElementId.InvalidElementId;
            Type typeClass = origType.GetType(); // PipeType or FlexPipeType

            // If this curve already wears a colored duplicate, re-base to the
            // original before deciding (so re-runs with a new status work).
            string maybeOrig = ColorizeStatusInfo.OriginalTypeName(origType.Name);
            if (maybeOrig != null)
            {
                var baseType = new FilteredElementCollector(doc).OfClass(typeClass).Cast<MEPCurveType>()
                    .FirstOrDefault(t => t.Name == maybeOrig);
                if (baseType != null) origType = baseType;
            }

            string newName = origType.Name + ColorizeStatusInfo.TypeSuffix(status);
            string cacheKey = newName;
            if (typeCache.TryGetValue(cacheKey, out ElementId cached)) return cached;

            var existing = new FilteredElementCollector(doc).OfClass(typeClass).Cast<MEPCurveType>()
                .FirstOrDefault(t => t.Name == newName);
            if (existing != null) { typeCache[cacheKey] = existing.Id; return existing.Id; }

            MEPCurveType newType;
            try { newType = origType.Duplicate(newName) as MEPCurveType; }
            catch (Exception ex) { failNotes.Add($"{origType.Name}: duplicate failed ({Trunc(ex.Message)})"); return ElementId.InvalidElementId; }
            if (newType == null) return ElementId.InvalidElementId;

            if (newType is PipeType)
            {
                // Rigid pipe: material is segment-driven. Recolor the duplicate's
                // routing-preference segments.
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
            }
            else
            {
                // Flex pipe (FlexPipeType): no routing-preference segments — the
                // body material is a TYPE-level Material parameter. Set it directly.
                int set = SetAllTypeMaterials(doc, newType, statusMatId);
                if (set == 0)
                    failNotes.Add($"{origType.Name}: flex type has no settable material parameter");
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
        //  LOADABLE FAMILIES: fittings / accessories / sprinklers
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Colors a loadable family instance for NWC by (1) trying a writable
        /// INSTANCE material param, else (2) duplicating the symbol with all of
        /// its material params set to the status color and reassigning the
        /// instance to that symbol. Returns false if the family exposes no
        /// material parameter wired to its solids (e.g. "By Category" /
        /// hardcoded) — those cannot be recolored without editing the .rfa.
        /// </summary>
        private bool ColorLoadableInstance(Document doc, FamilyInstance fi, StatusBucket status,
            ElementId matId, Dictionary<string, ElementId> symCache)
        {
            // (1) Writable instance material param — colors just this instance.
            Parameter ip = fi.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM) ?? fi.LookupParameter("Material");
            if (ip != null && !ip.IsReadOnly && ip.StorageType == StorageType.ElementId)
            {
                if (ip.AsElementId() != matId) ip.Set(matId);
                return true;
            }

            // (2) Symbol-duplicate fallback (isolated per status, reversible by
            //     the name suffix). Works only if the symbol has a material param.
            FamilySymbol sym = fi.Symbol;
            if (sym == null) return false;

            // Re-base if the instance already wears a colored duplicate.
            string maybeOrig = ColorizeStatusInfo.OriginalTypeName(sym.Name);
            FamilySymbol baseSym = sym;
            if (maybeOrig != null)
            {
                var fam = sym.Family;
                var sibling = fam.GetFamilySymbolIds().Select(id => doc.GetElement(id) as FamilySymbol)
                    .FirstOrDefault(s => s != null && s.Name == maybeOrig);
                if (sibling != null) baseSym = sibling;
            }

            string newName = baseSym.Name + ColorizeStatusInfo.TypeSuffix(status);
            string cacheKey = baseSym.Family.Id.IntegerValue + ":" + newName;

            FamilySymbol dup = null;
            if (symCache.TryGetValue(cacheKey, out ElementId cachedId))
                dup = doc.GetElement(cachedId) as FamilySymbol;
            if (dup == null)
            {
                var fam = baseSym.Family;
                dup = fam.GetFamilySymbolIds().Select(id => doc.GetElement(id) as FamilySymbol)
                    .FirstOrDefault(s => s != null && s.Name == newName);
            }
            if (dup == null)
            {
                try { dup = baseSym.Duplicate(newName) as FamilySymbol; }
                catch { return false; }
            }
            if (dup == null) return false;

            int set = SetAllTypeMaterials(doc, dup, matId);
            symCache[cacheKey] = dup.Id;
            if (set == 0) return false; // no material param on the family → can't color

            if (!dup.IsActive) dup.Activate();
            if (fi.Symbol.Id != dup.Id) fi.Symbol = dup;
            return true;
        }

        /// <summary>
        /// Sets every writable Material-type parameter on the type/symbol to
        /// <paramref name="matId"/> (covers multi-material bodies — valve
        /// body/handle/trim, etc., and flex pipe types). Returns how many were set.
        /// </summary>
        private int SetAllTypeMaterials(Document doc, ElementType sym, ElementId matId)
        {
            int n = 0;
            foreach (Parameter p in sym.Parameters)
            {
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId) continue;
                if (!IsMaterialParam(doc, p)) continue;
                try { p.Set(matId); n++; } catch { }
            }
            return n;
        }

        /// <summary>True if the ElementId parameter is a Material parameter.</summary>
        private bool IsMaterialParam(Document doc, Parameter p)
        {
            // The built-in material id param is always material.
            if (p.Id.IntegerValue == (int)BuiltInParameter.MATERIAL_ID_PARAM) return true;
            // Otherwise: its current value resolves to a Material, or its name says so.
            ElementId cur = p.AsElementId();
            if (cur != ElementId.InvalidElementId && doc.GetElement(cur) is Material) return true;
            string nm = p.Definition?.Name ?? "";
            return nm.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ══════════════════════════════════════════════════════════════
        //  BY-CATEGORY FAB FAMILIES: bind a material param (in-memory edit)
        // ══════════════════════════════════════════════════════════════

        private const string StatusMatParamName = "SG Status Material";

        /// <summary>True if any of the family's symbols exposes a writable material param.</summary>
        private bool FamilyHasMaterialParam(Document doc, Family fam)
        {
            foreach (var sid in fam.GetFamilySymbolIds())
            {
                if (!(doc.GetElement(sid) is FamilySymbol sym)) continue;
                foreach (Parameter p in sym.Parameters)
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId && IsMaterialParam(doc, p))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Opens the family, adds a Material type parameter, associates every
        /// solid's material parameter to it, and reloads the family (overwrite).
        /// After this, the family's symbols carry a writable material param so
        /// the normal symbol-duplicate path can color it per status. Returns true
        /// if at least one solid was bound. Non-destructive: discarded on
        /// close-without-saving like the rest of the command.
        /// </summary>
        private bool BindMaterialParamToFamily(Document doc, Family fam, out string note)
        {
            note = null;
            if (fam == null) { note = "null family"; return false; }
            if (!fam.IsEditable) { note = fam.Name + ": not editable (system/in-place)"; return false; }

            Document fdoc = null;
            try
            {
                fdoc = doc.EditFamily(fam);
                bool boundAny = false;
                using (var t = new Transaction(fdoc, "Bind status material"))
                {
                    t.Start();
                    FamilyManager fm = fdoc.FamilyManager;
                    FamilyParameter fp = fm.get_Parameter(StatusMatParamName)
                        ?? fm.AddParameter(StatusMatParamName, GroupTypeId.Materials, SpecTypeId.Reference.Material, false);

                    // Wire every solid-bearing element's material param to it
                    // (extrusions/blends/sweeps/free-forms, and nested instances).
                    foreach (Element el in new FilteredElementCollector(fdoc).WhereElementIsNotElementType())
                    {
                        Parameter mp;
                        try { mp = el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM); } catch { continue; }
                        if (mp == null || mp.IsReadOnly || mp.StorageType != StorageType.ElementId) continue;
                        try { fm.AssociateElementParameterToFamilyParameter(mp, fp); boundAny = true; } catch { }
                    }
                    t.Commit();
                }

                if (!boundAny)
                {
                    note = fam.Name + ": no bindable solids (imported/nested geometry?)";
                    try { fdoc.Close(false); } catch { }
                    return false;
                }
                fdoc.LoadFamily(doc, new FamLoadOpts());
                try { fdoc.Close(false); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                note = (fam.Name ?? "?") + ": " + Trunc(ex.Message);
                if (fdoc != null) { try { fdoc.Close(false); } catch { } }
                return false;
            }
        }

        /// <summary>Sets a category's By-Category material (null clears). Used for flex.</summary>
        private bool SetCategoryMaterial(Document doc, BuiltInCategory bic, ElementId matId)
        {
            try
            {
                Category cat = doc.Settings.Categories.get_Item(bic);
                if (cat == null) return false;
                cat.Material = (matId == ElementId.InvalidElementId) ? null : doc.GetElement(matId) as Material;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Overwrite-on-reload options for the in-memory family rebind.</summary>
        private class FamLoadOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = true; return true; }
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

        /// <summary>"Family : Type" label for a fitting/accessory, for the report.</summary>
        private static string FamilyLabel(FamilyInstance fi)
        {
            string fam = fi.Symbol?.Family?.Name ?? fi.Category?.Name ?? "?";
            string baseName = ColorizeStatusInfo.OriginalTypeName(fi.Symbol?.Name) ?? fi.Symbol?.Name;
            return string.IsNullOrEmpty(baseName) ? fam : $"{fam} : {baseName}";
        }
    }
}
