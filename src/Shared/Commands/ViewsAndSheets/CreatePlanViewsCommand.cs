using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Creates floor and/or ceiling plan views, applies a view template, sets a
    /// Sub-Discipline, and names each view.
    ///
    /// Two source modes:
    ///   • THIS MODEL — create one floor/ceiling view per selected level (the
    ///     original behaviour). Name = "LEVEL NAME - SUFFIX".
    ///   • ANOTHER MODEL — replicate selected plan views from another open OR
    ///     linked document into this model: for each source view, create a
    ///     matching floor/ceiling view on the level of the same name (nearest
    ///     elevation if the name doesn't match), apply OUR template + scope box
    ///     (matched by name) + Sub-Discipline. The view is RECREATED, not copied
    ///     (plan views are level-bound and can't be copied across documents).
    ///
    /// Sub-Discipline is a project/shared parameter named "Sub-Discipline" on the
    /// view; it's how views are filed in the browser. It's set after the template
    /// is applied, and only if the template doesn't lock it.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreatePlanViewsCommand : IExternalCommand
    {
        private const string SubDisciplineParam = "Sub-Discipline";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Levels (this-model mode) ──
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderByDescending(l => l.Elevation)
                    .ToList();

                if (levels.Count == 0)
                {
                    TaskDialog.Show("Create Plan Views", "No levels found in the project.");
                    return Result.Failed;
                }

                var levelDisplayNames = levels
                    .Select(l => $"{l.Name}  (Elev: {FormatElevation(l.Elevation)})")
                    .ToList();
                var levelNames = levels.Select(l => l.Name).ToList();

                // ── View templates ──
                var allViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .ToList();

                var floorTemplates = allViews
                    .Where(v => v.ViewType == ViewType.FloorPlan)
                    .Select(v => v.Name).OrderBy(n => n).ToList();
                var ceilingTemplates = allViews
                    .Where(v => v.ViewType == ViewType.CeilingPlan)
                    .Select(v => v.Name).OrderBy(n => n).ToList();

                // ── Source models (another-model mode): open docs + linked docs ──
                var sourceModels = GatherSourceModels(uiapp, doc);
                var sourceModelDisplays = sourceModels.Select(m => m.Display).ToList();
                var sourceViewDisplays = sourceModels
                    .Select(m => (IList<string>)m.ViewDisplays).ToList();

                // ── Existing Sub-Discipline values in this model (for the dropdown) ──
                var subDisciplineValues = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => v.LookupParameter(SubDisciplineParam)?.AsString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // ── Dialog ──
                using (var dialog = new CreatePlanViewsDialog(
                    levelDisplayNames, levelNames, floorTemplates, ceilingTemplates,
                    sourceModelDisplays, sourceViewDisplays, subDisciplineValues))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    // Resolve ViewFamilyTypes once.
                    var vfts = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList();
                    ViewFamilyType floorVFT = vfts.FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
                    ViewFamilyType ceilingVFT = vfts.FirstOrDefault(v => v.ViewFamily == ViewFamily.CeilingPlan);

                    // Resolve chosen templates by name.
                    View floorTemplateView = string.IsNullOrEmpty(dialog.FloorTemplate) ? null :
                        allViews.FirstOrDefault(v => v.Name == dialog.FloorTemplate && v.ViewType == ViewType.FloorPlan);
                    View ceilingTemplateView = string.IsNullOrEmpty(dialog.CeilingTemplate) ? null :
                        allViews.FirstOrDefault(v => v.Name == dialog.CeilingTemplate && v.ViewType == ViewType.CeilingPlan);

                    string subDiscipline = dialog.SubDiscipline;

                    if (dialog.SourceMode == CreatePlanViewsDialog.SourceModeOption.AnotherModel)
                    {
                        return RunAnotherModel(doc, dialog, sourceModels, floorVFT, ceilingVFT,
                            floorTemplateView, ceilingTemplateView, subDiscipline);
                    }

                    return RunThisModel(doc, dialog, levels, floorVFT, ceilingVFT,
                        floorTemplateView, ceilingTemplateView, subDiscipline);
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Error", "Create Plan Views failed:\n" + ex.Message);
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  THIS-MODEL MODE  (original behaviour + Sub-Discipline)
        // ══════════════════════════════════════════════════════════════

        private Result RunThisModel(Document doc, CreatePlanViewsDialog dialog, List<Level> levels,
            ViewFamilyType floorVFT, ViewFamilyType ceilingVFT,
            View floorTemplateView, View ceilingTemplateView, string subDiscipline)
        {
            if (dialog.SelectedLevelNames.Count == 0)
            {
                TaskDialog.Show("Create Plan Views", "No levels selected.");
                return Result.Cancelled;
            }

            bool doFloor = dialog.SelectedViewType != CreatePlanViewsDialog.ViewTypeOption.CeilingOnly;
            bool doCeiling = dialog.SelectedViewType != CreatePlanViewsDialog.ViewTypeOption.FloorOnly;
            string suffix = dialog.ViewNameSuffix;

            if (doFloor && floorVFT == null)
            { TaskDialog.Show("Create Plan Views", "No Floor Plan view family type found."); return Result.Failed; }
            if (doCeiling && ceilingVFT == null)
            { TaskDialog.Show("Create Plan Views", "No Ceiling Plan view family type found."); return Result.Failed; }

            var selectedSet = new HashSet<string>(dialog.SelectedLevelNames);
            var selectedLevels = levels.Where(l => selectedSet.Contains(l.Name)).ToList();

            int floorCreated = 0, ceilingCreated = 0, skipped = 0, subDiscSet = 0;

            using (var tw = new TransactionWrapper(doc, "Create Plan Views"))
            {
                foreach (var level in selectedLevels)
                {
                    string viewName = level.Name.ToUpper();
                    if (!string.IsNullOrEmpty(suffix)) viewName += " - " + suffix;

                    if (doFloor)
                    {
                        try
                        {
                            ViewPlan v = ViewPlan.Create(doc, floorVFT.Id, level.Id);
                            if (v != null)
                            {
                                if (floorTemplateView != null) v.ViewTemplateId = floorTemplateView.Id;
                                try { v.Name = viewName; } catch { }
                                if (ApplySubDiscipline(v, subDiscipline)) subDiscSet++;
                                floorCreated++;
                            }
                        }
                        catch (Exception) { skipped++; }
                    }

                    if (doCeiling)
                    {
                        try
                        {
                            ViewPlan v = ViewPlan.Create(doc, ceilingVFT.Id, level.Id);
                            if (v != null)
                            {
                                if (ceilingTemplateView != null) v.ViewTemplateId = ceilingTemplateView.Id;
                                try { v.Name = viewName; } catch { }
                                if (ApplySubDiscipline(v, subDiscipline)) subDiscSet++;
                                ceilingCreated++;
                            }
                        }
                        catch (Exception) { skipped++; }
                    }
                }
                tw.Commit();
            }

            string summary = "Plan Views Created (this model):\n\n";
            if (doFloor) summary += $"Floor plans: {floorCreated}\n";
            if (doCeiling) summary += $"Ceiling plans: {ceilingCreated}\n";
            if (skipped > 0) summary += $"Skipped (errors): {skipped}\n";
            summary += $"\nName format: LEVEL NAME - {suffix}";
            if (floorTemplateView != null && doFloor) summary += $"\nFloor template: {dialog.FloorTemplate}";
            if (ceilingTemplateView != null && doCeiling) summary += $"\nCeiling template: {dialog.CeilingTemplate}";
            if (!string.IsNullOrEmpty(subDiscipline)) summary += $"\nSub-Discipline set on {subDiscSet} view(s): {subDiscipline}";

            TaskDialog.Show("Create Plan Views — Complete", summary);
            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════
        //  ANOTHER-MODEL MODE  (replicate views from open/linked doc)
        // ══════════════════════════════════════════════════════════════

        private Result RunAnotherModel(Document doc, CreatePlanViewsDialog dialog,
            List<SourceModel> sourceModels, ViewFamilyType floorVFT, ViewFamilyType ceilingVFT,
            View floorTemplateView, View ceilingTemplateView, string subDiscipline)
        {
            int mi = dialog.SelectedSourceModelIndex;
            if (mi < 0 || mi >= sourceModels.Count)
            { TaskDialog.Show("Create Plan Views", "No source model selected."); return Result.Cancelled; }

            SourceModel sm = sourceModels[mi];
            var picked = dialog.SelectedSourceViewIndices
                .Where(i => i >= 0 && i < sm.Views.Count)
                .Select(i => sm.Views[i])
                .ToList();

            if (picked.Count == 0)
            { TaskDialog.Show("Create Plan Views", "No source views selected."); return Result.Cancelled; }

            bool wantFloor = dialog.SelectedViewType != CreatePlanViewsDialog.ViewTypeOption.CeilingOnly;
            bool wantCeiling = dialog.SelectedViewType != CreatePlanViewsDialog.ViewTypeOption.FloorOnly;
            string suffix = dialog.ViewNameSuffix;

            // Destination matching data.
            var destLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            var destScopeBoxes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType().ToList();

            int created = 0, scopeSet = 0, subDiscSet = 0, skFilter = 0, skError = 0;
            var skNoLevel = new List<string>();

            using (var tw = new TransactionWrapper(doc, "Copy Plan Views From Model"))
            {
                foreach (var sv in picked)
                {
                    bool isCeiling = sv.ViewType == ViewType.CeilingPlan;
                    if (isCeiling && !wantCeiling) { skFilter++; continue; }
                    if (!isCeiling && !wantFloor) { skFilter++; continue; }

                    ViewFamilyType vft = isCeiling ? ceilingVFT : floorVFT;
                    if (vft == null) { skError++; continue; }

                    Level srcLevel = sv.GenLevel;
                    if (srcLevel == null) { skNoLevel.Add($"{sv.Name} (no level)"); continue; }

                    double srcElev = srcLevel.Elevation;
                    if (sm.Xf != null && !sm.Xf.IsIdentity)
                        srcElev += sm.Xf.Origin.Z;   // link Z offset (translation only)

                    Level dest = MatchLevel(destLevels, srcLevel.Name, srcElev);
                    if (dest == null) { skNoLevel.Add($"{sv.Name} → level \"{srcLevel.Name}\""); continue; }

                    try
                    {
                        ViewPlan nv = ViewPlan.Create(doc, vft.Id, dest.Id);
                        if (nv == null) { skError++; continue; }

                        // Name from the source view (collision-safe, illegal chars stripped).
                        string desired = SanitizeViewName(sv.Name);
                        if (!string.IsNullOrEmpty(suffix)) desired += " - " + suffix;
                        EnsureUniqueName(nv, desired);

                        // Scope box matched by name — set BEFORE the template so a template
                        // that does NOT manage the scope-box parameter leaves ours in place.
                        string srcScope = ScopeBoxName(sv);
                        if (!string.IsNullOrEmpty(srcScope))
                        {
                            var match = destScopeBoxes.FirstOrDefault(e =>
                                string.Equals(e.Name, srcScope, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                            {
                                var p = nv.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                                if (p != null && !p.IsReadOnly) { try { p.Set(match.Id); scopeSet++; } catch { } }
                            }
                        }

                        // Our template (governs V/G, scale, view range; may override scope box).
                        View tpl = isCeiling ? ceilingTemplateView : floorTemplateView;
                        if (tpl != null) nv.ViewTemplateId = tpl.Id;

                        if (ApplySubDiscipline(nv, subDiscipline)) subDiscSet++;
                        created++;
                    }
                    catch (Exception) { skError++; }
                }
                tw.Commit();
            }

            string summary = $"Copied views from \"{sm.Display}\":\n\n";
            summary += $"Views created: {created}\n";
            if (scopeSet > 0) summary += $"Scope box matched by name: {scopeSet}\n";
            if (!string.IsNullOrEmpty(subDiscipline)) summary += $"Sub-Discipline set: {subDiscSet}  ({subDiscipline})\n";
            if (skFilter > 0) summary += $"Skipped (view-type filter): {skFilter}\n";
            if (skNoLevel.Count > 0)
                summary += $"Skipped (no matching level): {skNoLevel.Count}\n  " +
                           string.Join("\n  ", skNoLevel.Take(12)) +
                           (skNoLevel.Count > 12 ? "\n  …" : "") +
                           "\n  (Run Setup → Import Link Levels/Grids first so level names match.)\n";
            if (skError > 0) summary += $"Skipped (errors): {skError}\n";
            if (floorTemplateView != null || ceilingTemplateView != null)
                summary += $"\nTemplates — Floor: {dialog.FloorTemplate}; Ceiling: {dialog.CeilingTemplate}";

            TaskDialog.Show("Create Plan Views — Complete", summary);
            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════
        //  SOURCE MODEL GATHERING
        // ══════════════════════════════════════════════════════════════

        private List<SourceModel> GatherSourceModels(UIApplication uiapp, Document active)
        {
            var models = new List<SourceModel>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Open documents (not the active one, not families, not link-backing docs).
            foreach (Document d in uiapp.Application.Documents)
            {
                if (d == null || d.IsFamilyDocument || d.IsLinked) continue;
                if (ReferenceEquals(d, active)) continue;
                var sm = BuildSourceModel(d, false, Transform.Identity, d.Title);
                if (sm.Views.Count > 0) { models.Add(sm); seenPaths.Add(SafePath(d)); }
            }

            // Linked documents.
            var links = new FilteredElementCollector(active)
                .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Where(li => li.GetLinkDocument() != null);
            foreach (var li in links)
            {
                Document ld = li.GetLinkDocument();
                string path = SafePath(ld);
                if (!seenPaths.Add(path)) continue;   // dedupe multiple instances / already-open
                var sm = BuildSourceModel(ld, true, li.GetTotalTransform(), ld.Title + "  (linked)");
                if (sm.Views.Count > 0) models.Add(sm);
            }

            return models;
        }

        private SourceModel BuildSourceModel(Document d, bool linked, Transform xf, string display)
        {
            var sm = new SourceModel { Doc = d, IsLinked = linked, Xf = xf, Display = display };
            sm.Views = new FilteredElementCollector(d)
                .OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate &&
                            (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.CeilingPlan))
                .OrderBy(v => v.Name)
                .ToList();
            sm.ViewDisplays = sm.Views.Select(BuildViewDisplay).ToList();
            return sm;
        }

        private string BuildViewDisplay(ViewPlan v)
        {
            try
            {
                string type = v.ViewType == ViewType.CeilingPlan ? "RCP" : "Floor";
                string lvl = v.GenLevel?.Name ?? "—";
                string scale = v.Scale > 0 ? $"1:{v.Scale}" : "?";
                string scope = ScopeBoxName(v);
                string tail = $"{type} · {lvl} · {scale}" + (string.IsNullOrEmpty(scope) ? "" : $" · ⬚{scope}");
                return $"{v.Name}   [{tail}]";
            }
            catch
            {
                try { return v.Name; } catch { return "(view)"; }
            }
        }

        private string ScopeBoxName(View v)
        {
            try
            {
                var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                var id = p?.AsElementId();
                if (id == null || id == ElementId.InvalidElementId) return null;
                return v.Document.GetElement(id)?.Name;
            }
            catch { return null; }
        }

        /// <summary>Strips characters Revit forbids in view names so EnsureUniqueName doesn't churn.</summary>
        private static string SanitizeViewName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "View";
            var invalid = new[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
            string clean = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '-' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(clean) ? "View" : clean;
        }

        private static string SafePath(Document d)
        {
            try { return string.IsNullOrEmpty(d.PathName) ? d.Title : d.PathName; }
            catch { return d.Title; }
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        private Level MatchLevel(List<Level> destLevels, string srcName, double srcElevHost)
        {
            var byName = destLevels.FirstOrDefault(l =>
                string.Equals(l.Name, srcName, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;

            // Nearest within 1 ft, else give up (avoid wild mismatches).
            Level nearest = destLevels.OrderBy(l => Math.Abs(l.Elevation - srcElevHost)).FirstOrDefault();
            if (nearest != null && Math.Abs(nearest.Elevation - srcElevHost) <= 1.0) return nearest;
            return null;
        }

        private void EnsureUniqueName(View v, string desired)
        {
            string name = desired;
            for (int n = 2; n <= 60; n++)
            {
                try { v.Name = name; return; }
                catch { name = desired + " (" + n + ")"; }
            }
            // give up — keep the auto-generated name
        }

        private bool ApplySubDiscipline(View v, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var p = v.LookupParameter(SubDisciplineParam);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
            try { p.Set(value); return true; } catch { return false; }
        }

        private string FormatElevation(double elevFeet)
        {
            if (Math.Abs(elevFeet - Math.Round(elevFeet)) < 0.001)
                return ((int)Math.Round(elevFeet)).ToString();
            return elevFeet.ToString("F4").TrimEnd('0').TrimEnd('.');
        }

        /// <summary>A candidate source document (open or linked) and its plan views.</summary>
        private class SourceModel
        {
            public Document Doc;
            public bool IsLinked;
            public Transform Xf;
            public string Display;
            public List<ViewPlan> Views = new List<ViewPlan>();
            public List<string> ViewDisplays = new List<string>();
        }
    }
}
