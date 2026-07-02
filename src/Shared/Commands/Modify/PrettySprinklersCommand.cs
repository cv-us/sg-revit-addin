using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

            // ── Place overlays for the selection ──
            var symbolCache = new Dictionary<int, FamilySymbol>();
            var existingOverlays = CollectHeadOverlays(doc, view)
                .Select(e => new { e.Id, Loc = GetLoc(e) })
                .Where(x => x.Loc != null)
                .ToList();

            int placed = 0, refreshed = 0, unresolved = 0;
            var missingFamilies = new SortedSet<string>();

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
                        doc.Create.NewFamilyInstance(loc, sym, view);
                        placed++;
                    }
                    catch { unresolved++; }
                }
                tw.Commit();
            }

            string report = $"Pretty Sprinklers\n\n" +
                            $"Overlays placed: {placed}\n";
            if (refreshed > 0) report += $"Replaced existing overlays: {refreshed}\n";
            if (unresolved > 0) report += $"Sprinklers skipped (no head symbol / family): {unresolved}\n";
            if (missingFamilies.Count > 0)
                report += "\nMissing overlay families (load them):\n  " +
                          string.Join(", ", missingFamilies);
            TaskDialog.Show("Pretty Sprinklers", report);
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
