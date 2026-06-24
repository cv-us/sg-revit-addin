using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>
    /// Re-levels selected sprinkler heads to a chosen Level while keeping each
    /// head in its EXACT world location. Changing the Level re-hosts the head;
    /// re-pinning the captured world point makes Revit recompute "Elevation from
    /// Level" / "Offset from Host" automatically — so a head on Level 1 (0'-0")
    /// at 72'-8" moved to Level 4 (60'-0") ends up at 12'-8" offset, same spot.
    ///
    /// MECHANISM (per head, one shared transaction):
    ///   1. capture world point P = (Location as LocationPoint).Point
    ///   2. set BuiltInParameter.FAMILY_LEVEL_PARAM to the new level's id
    ///      (this jumps the head in Z, keeping the old offset)
    ///   3. re-fetch Location and set .Point = P  → Revit recomputes the offset
    ///      = P.Z - newLevel.Elevation, exactly, family-agnostic.
    ///
    /// Heads that can't be re-leveled in place (read-only Level param, or
    /// face/work-plane-hosted) are skipped and reported, not deleted — deleting
    /// would lose element IDs, schedule/tag/calc links, and connections.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RelevelSprinklersCommand : IExternalCommand
    {
        private const double TolFt = 1e-4; // ~0.0012" — tighter than any real precision

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Selected sprinklers, else prompt to pick.
                var heads = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id))
                    .OfType<FamilyInstance>()
                    .Where(fi => fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers)
                    .ToList();
                if (heads.Count == 0)
                {
                    try
                    {
                        var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                            new SprinklerFilter(), "Select sprinkler heads to re-level, then Finish.");
                        heads = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
                if (heads.Count == 0) { TaskDialog.Show("Re-Level Sprinklers", "No sprinklers selected."); return Result.Cancelled; }

                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => (id: l.Id.IntegerValue, name: l.Name, elev: l.Elevation))
                    .ToList();
                if (levels.Count == 0) { TaskDialog.Show("Re-Level Sprinklers", "No levels in the project."); return Result.Cancelled; }

                Level newLevel;
                using (var dlg = new RelevelSprinklersDialog(levels, heads.Count))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;
                    newLevel = doc.GetElement(new ElementId(dlg.SelectedLevelId)) as Level;
                }
                if (newLevel == null) { TaskDialog.Show("Re-Level Sprinklers", "Target level not found."); return Result.Cancelled; }

                int releveled = 0, alreadyThere = 0;
                var skipped = new List<string>();
                var failed = new List<string>();
                var toVerify = new List<(FamilyInstance fi, XYZ p)>();

                using (var tx = new Transaction(doc, "Re-Level Sprinklers"))
                {
                    tx.Start();
                    foreach (var fi in heads)
                    {
                        try
                        {
                            if (fi.LevelId == newLevel.Id) { alreadyThere++; continue; }

                            if (IsFaceOrWorkPlaneHosted(fi)) { skipped.Add($"{fi.Id.IntegerValue}: face/work-plane hosted"); continue; }
                            var lp = fi.Location as LocationPoint;
                            if (lp == null) { skipped.Add($"{fi.Id.IntegerValue}: no location point"); continue; }
                            Parameter lvlParam = fi.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                            if (lvlParam == null || lvlParam.IsReadOnly || lvlParam.StorageType != StorageType.ElementId)
                            { skipped.Add($"{fi.Id.IntegerValue}: Level not settable"); continue; }

                            XYZ p = lp.Point;          // world XYZ (Z is world Z)
                            lvlParam.Set(newLevel.Id);  // re-host — jumps Z
                            // Re-fetch Location (can be stale after the param change) and re-pin.
                            var lp2 = fi.Location as LocationPoint;
                            if (lp2 != null) lp2.Point = p; // Revit recomputes offset = p.Z - newLevel.Elevation
                            toVerify.Add((fi, p));
                            releveled++;
                        }
                        catch (Exception ex)
                        {
                            failed.Add($"{fi.Id.IntegerValue}: {Trunc(ex.Message)}");
                        }
                    }

                    // Regenerate so LocationPoint reflects the final state, then verify.
                    doc.Regenerate();
                    tx.Commit();
                }

                // Verify world position held (read after commit = fully regenerated).
                var mismatches = new List<string>();
                foreach (var (fi, p) in toVerify)
                {
                    var lp = fi.Location as LocationPoint;
                    if (lp == null) continue;
                    XYZ q = lp.Point;
                    double dz = Math.Abs(q.Z - p.Z);
                    double dxy = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                    if (dz > TolFt || dxy > TolFt)
                        mismatches.Add($"{fi.Id.IntegerValue}: moved {UnitConversion.FormatFeetInches(Math.Max(dz, dxy))}");
                }

                var lines = new List<string>
                {
                    $"Re-Level Sprinklers → {newLevel.Name} ({UnitConversion.FormatFeetInches(newLevel.Elevation)}).",
                    "",
                    $"Re-leveled (world position kept): {releveled}",
                };
                if (alreadyThere > 0) lines.Add($"Already on that level (skipped):  {alreadyThere}");
                if (skipped.Count > 0)
                {
                    lines.Add($"Couldn't re-level (skipped):      {skipped.Count}");
                    foreach (var s in skipped.Take(12)) lines.Add("   • " + s);
                    if (skipped.Count > 12) lines.Add($"   …and {skipped.Count - 12} more.");
                }
                if (failed.Count > 0)
                {
                    lines.Add($"Errors: {failed.Count}");
                    foreach (var s in failed.Take(8)) lines.Add("   • " + s);
                }
                if (mismatches.Count > 0)
                {
                    lines.Add("");
                    lines.Add($"⚠ Position check FAILED on {mismatches.Count} (should be 0):");
                    foreach (var s in mismatches.Take(8)) lines.Add("   • " + s);
                }
                else if (releveled > 0)
                {
                    lines.Add("");
                    lines.Add("Position check: all held their exact world location ✓");
                }

                TaskDialog.Show("Re-Level Sprinklers", string.Join("\n", lines));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>True if the head is hosted to a face / work plane / non-Level host.</summary>
        private static bool IsFaceOrWorkPlaneHosted(FamilyInstance fi)
        {
            try
            {
                if (fi.HostFace != null) return true;
                if (fi.Host != null && !(fi.Host is Level)) return true;
            }
            catch { }
            return false;
        }

        private static string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 80 ? s.Substring(0, 80) : s);

        private class SprinklerFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Sprinklers;
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
