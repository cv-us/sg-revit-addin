using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WinColor = System.Drawing.Color;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// "Pretty Sprinklers" — places an opaque head-symbol overlay family (category
    /// Sprinkler Tags, family named "Head1".."Head29") coincident with each
    /// SELECTED sprinkler head, so the plan head symbol reads solid instead of
    /// having pipe/branch lines run through it.
    ///
    /// Which overlay is used is driven by the sprinkler TYPE's graphics params:
    /// the checked "Symbol - HeadN" Yes/No param (falling back to parsing the
    /// "HeadSymbol" text param, e.g. "Head02.png").
    ///
    /// Run with NO sprinklers selected → removes ALL head overlays in the view.
    ///
    /// Invoked from the Modify-tab SG button via <see cref="DeferredActionHandler"/>,
    /// and also usable as a normal command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PrettySprinklersCommand : IExternalCommand
    {
        // Overlay family names: "Head" + number (Head1 .. Head29). Accept an
        // optional zero-pad ("Head02") too.
        private static readonly Regex HeadOverlayName = new Regex(@"^Head0*(\d+)$", RegexOptions.IgnoreCase);
        private const double LocTolFt = 0.15;   // overlay-at-head proximity

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try { Run(commandData.Application); return Result.Succeeded; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        public static void Run(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Selected sprinklers (instances of OST_Sprinklers).
            var sprinklers = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as FamilyInstance)
                .Where(fi => fi != null && fi.Category != null &&
                             fi.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers)
                .ToList();

            // ── No selection → clear all head overlays in the view ──
            if (sprinklers.Count == 0)
            {
                int removed = 0;
                using (var tw = new TransactionWrapper(doc, "Pretty Sprinklers — Clear"))
                {
                    foreach (var id in CollectHeadOverlays(doc, view).Select(e => e.Id).ToList())
                    {
                        try { doc.Delete(id); removed++; } catch { }
                    }
                    tw.Commit();
                }
                TaskDialog.Show("Pretty Sprinklers",
                    removed > 0
                        ? $"Removed {removed} head overlay{(removed != 1 ? "s" : "")} from this view."
                        : "No sprinklers selected and no head overlays in this view to remove.\n\n" +
                          "Select sprinklers first to add overlays.");
                return;
            }

            // ── Options: color each overlay by its head's workset ──
            bool colorByWorkset = false;
            Dictionary<int, WinColor> colorMap = null;

            if (doc.IsWorkshared)
            {
                var wsNames = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToDictionary(w => w.Id.IntegerValue, w => w.Name);
                var counts = new Dictionary<int, int>();
                foreach (var s in sprinklers)
                {
                    int w = s.WorksetId.IntegerValue;
                    counts[w] = (counts.TryGetValue(w, out int c) ? c : 0) + 1;
                }
                var worksets = counts.Keys
                    .Where(id => wsNames.ContainsKey(id))
                    .Select(id => (id, name: wsNames[id]))
                    .OrderBy(t => t.name)
                    .ToList();

                using (var dlg = new PrettySprinklersDialog(worksets, counts))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    if (dlg.Action == PrettySprinklersDialog.PrettyAction.ClearColoring)
                    {
                        ClearColoring(doc, view);
                        return;
                    }
                    colorByWorkset = dlg.ColorByWorkset && worksets.Count > 0;
                    colorMap = dlg.WorksetColors;
                }
            }
            else
            {
                TaskDialog.Show("Pretty Sprinklers",
                    "This model isn't workshared, so there are no worksets to color by.\n\n" +
                    "Placing head overlays without coloring.");
            }

            // ── Place overlays for the selection (+ color by workset) ──
            var symbolCache = new Dictionary<int, FamilySymbol>();
            var existingOverlays = CollectHeadOverlays(doc, view)
                .Select(e => new { e.Id, Loc = GetLoc(e) })
                .Where(x => x.Loc != null)
                .ToList();

            int placed = 0, refreshed = 0, unresolved = 0, colored = 0;
            var missingFamilies = new SortedSet<string>();
            ElementId solidFillId = GetSolidFillPatternId(doc);

            using (var tw = new TransactionWrapper(doc, "Pretty Sprinklers"))
            {
                foreach (var spk in sprinklers)
                {
                    XYZ loc = GetLoc(spk);
                    if (loc == null) { unresolved++; continue; }

                    int headNum = ResolveHeadNumber(spk);
                    if (headNum <= 0) { unresolved++; continue; }

                    FamilySymbol sym = ResolveOverlaySymbol(doc, headNum, symbolCache);
                    if (sym == null) { unresolved++; missingFamilies.Add("Head" + headNum); continue; }

                    // Refresh: delete any existing overlay already at this head.
                    foreach (var ov in existingOverlays.Where(x => x.Loc.DistanceTo(loc) <= LocTolFt).ToList())
                    {
                        try { doc.Delete(ov.Id); refreshed++; } catch { }
                        existingOverlays.Remove(ov);
                    }

                    if (!sym.IsActive) sym.Activate();
                    try
                    {
                        var inst = doc.Create.NewFamilyInstance(loc, sym, view);
                        placed++;
                        if (colorByWorkset && inst != null && colorMap != null &&
                            colorMap.TryGetValue(spk.WorksetId.IntegerValue, out WinColor wc))
                        {
                            ApplyColor(view, inst.Id, wc, solidFillId);
                            colored++;
                        }
                    }
                    catch { unresolved++; }
                }
                tw.Commit();
            }

            string report = $"Pretty Sprinklers\n\n" +
                            $"Overlays placed: {placed}\n";
            if (colored > 0) report += $"Colored by workset: {colored}\n";
            if (refreshed > 0) report += $"Replaced existing overlays: {refreshed}\n";
            if (unresolved > 0) report += $"Sprinklers skipped (no head symbol / family): {unresolved}\n";
            if (missingFamilies.Count > 0)
                report += "\nMissing overlay families (load them):\n  " +
                          string.Join(", ", missingFamilies);
            TaskDialog.Show("Pretty Sprinklers", report);
        }

        // ── Coloring (per-instance view graphic override on the overlay) ──

        /// <summary>
        /// Comprehensive per-element override so the head symbol recolors whether its
        /// glyph is drawn as lines or a filled/masking region: projection line color +
        /// surface/cut foreground pattern color (solid fill). Same recipe as Colorize
        /// by Workset's view-override path.
        /// </summary>
        private static void ApplyColor(View view, ElementId id, WinColor wc, ElementId solidFillId)
        {
            var rc = new Color(wc.R, wc.G, wc.B);
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(rc);
            ogs.SetSurfaceForegroundPatternColor(rc);
            ogs.SetCutForegroundPatternColor(rc);
            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetCutForegroundPatternId(solidFillId);
            }
            try { view.SetElementOverrides(id, ogs); } catch { }
        }

        /// <summary>Removes graphic overrides from every head overlay in the view.</summary>
        private static void ClearColoring(Document doc, View view)
        {
            var overlays = CollectHeadOverlays(doc, view);
            int cleared = 0;
            using (var tw = new TransactionWrapper(doc, "Pretty Sprinklers — Clear Coloring"))
            {
                var empty = new OverrideGraphicSettings();
                foreach (var e in overlays)
                {
                    try { view.SetElementOverrides(e.Id, empty); cleared++; } catch { }
                }
                tw.Commit();
            }
            TaskDialog.Show("Pretty Sprinklers",
                cleared > 0
                    ? $"Removed coloring from {cleared} head overlay{(cleared != 1 ? "s" : "")} in this view."
                    : "No head overlays in this view to clear coloring from.");
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern() != null && f.GetFillPattern().IsSolidFill);
            return solid?.Id ?? ElementId.InvalidElementId;
        }

        // ── Head-symbol resolution from the sprinkler TYPE ──

        private static int ResolveHeadNumber(FamilyInstance spk)
        {
            FamilySymbol sym = spk.Symbol;
            if (sym == null) return -1;

            // 1) The checked "Symbol - HeadN" Yes/No param.
            for (int n = 1; n <= 29; n++)
            {
                var p = sym.LookupParameter($"Symbol - Head{n}");
                if (p != null && p.StorageType == StorageType.Integer && p.AsInteger() == 1)
                    return n;
            }

            // 2) Fall back to the "HeadSymbol" text param, e.g. "Head02.png".
            var hs = sym.LookupParameter("HeadSymbol")?.AsString();
            if (!string.IsNullOrEmpty(hs))
            {
                var m = Regex.Match(hs, @"(\d+)");
                if (m.Success && int.TryParse(m.Value, out int n) && n > 0) return n;
            }
            return -1;
        }

        private static FamilySymbol ResolveOverlaySymbol(Document doc, int headNum, Dictionary<int, FamilySymbol> cache)
        {
            if (cache.TryGetValue(headNum, out var cached)) return cached;

            FamilySymbol found = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_SprinklerTags)
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                {
                    var m = HeadOverlayName.Match(fs.Family?.Name ?? "");
                    return m.Success && int.TryParse(m.Groups[1].Value, out int n) && n == headNum;
                });

            cache[headNum] = found;
            return found;
        }

        // ── Overlay collection / geometry helpers ──

        private static List<Element> CollectHeadOverlays(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_SprinklerTags)
                .WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance fi &&
                            HeadOverlayName.IsMatch(fi.Symbol?.Family?.Name ?? ""))
                .ToList();
        }

        private static XYZ GetLoc(Element e)
        {
            if (e.Location is LocationPoint lp) return lp.Point;
            var bb = e.get_BoundingBox(null);
            return bb != null ? (bb.Min + bb.Max) / 2.0 : null;
        }
    }
}
