using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Raybounce EARLY — the original, simple raybounce that shoots a ray
    /// straight up from each hanger and takes the first hit on a structural
    /// category (floors, stairs, roofs, structural framing), including linked
    /// models, via the native ReferenceIntersector. No CAD/IFC mesh handling,
    /// no diagnostics. This is the stable fallback kept on the ribbon while
    /// "Raybounce Dev" (SyncHangersRaybounceCommand) is being refined for
    /// imported CAD / IFC geometry.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RaybounceEarlyCommand : IExternalCommand
    {
        private const string RaybounceViewName = "3D-Raybounce";

        private static readonly string[] HangerFamilyPatterns = new[]
        {
            "-Pipe Hanger",
            "Ring Hanger",
            "-Basic Adjustable"
        };

        private static readonly BuiltInCategory[] TargetCategories = new[]
        {
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Roofs
        };

        private class RayHitResult
        {
            public FamilyInstance Hanger { get; set; }
            public XYZ HangerPoint { get; set; }
            public XYZ HitPoint { get; set; }
            public double Distance { get; set; }
            public BuiltInCategory HitCategory { get; set; }
            public string HitCategoryLabel { get; set; }
            public string HitElementName { get; set; }
            public string HitLinkName { get; set; }
            public string Source { get; set; }
            public string Quality { get; set; }
            public double CorrectionFt { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<FamilyInstance> selectedAccessories = GetSelectedPipeAccessories(uidoc);
            if (selectedAccessories == null)
                return Result.Cancelled;

            var hangers = selectedAccessories.Where(IsValidHanger).ToList();

            if (hangers.Count == 0)
            {
                TaskDialog.Show("Raybounce Early",
                    "No valid pipe hangers found in the selection.\n\n" +
                    "Select elements whose family name contains \"-Pipe Hanger\", " +
                    "\"Ring Hanger\", or \"-Basic Adjustable\".");
                return Result.Failed;
            }

            using (var dlg = new RaybounceEarlyDialog(hangers.Count))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                View3D raybounceView = null;
                using (var tw = new TransactionWrapper(doc, "Setup Raybounce View"))
                {
                    try
                    {
                        raybounceView = FindOrCreate3DView(doc);
                        if (raybounceView == null)
                        {
                            TaskDialog.Show("Raybounce Early",
                                "Could not find or create a 3D view for raybounce.");
                            return Result.Failed;
                        }
                        tw.Commit();
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                        return Result.Failed;
                    }
                }

                var hits = new List<RayHitResult>();
                var misses = new List<FamilyInstance>();

                // Resolve hanger points up front — they bound the scanner's
                // DirectShape mesh index spatially.
                var hangerPoints = new List<(FamilyInstance hanger, XYZ pt)>();
                foreach (var hanger in hangers)
                {
                    XYZ pt = GetHangerPoint(hanger);
                    if (pt == null) misses.Add(hanger);
                    else hangerPoints.Add((hanger, pt));
                }

                // Early = single straight-up ray, native structural categories
                // only (no fan, no CAD imports, no generic models). It still
                // gets the two reliability fixes:
                //  • every hit is RE-MEASURED against the hit element's real
                //    triangulated geometry (fixes rods stretching under sloped
                //    decks in linked files — phantom Proximity), and
                //  • a DirectShape triangle index (host + links) so linked IFC
                //    beams the native ray passes through are still found.
                var scanner = new StructureRayScanner(doc, raybounceView, TargetCategories)
                {
                    UseFan = false,
                    VerifyWithGeometry = true,
                    IncludeGenericCategories = false
                };
                scanner.Build(hangerPoints.Select(hp => hp.pt).ToList());

                foreach (var (hanger, hangerPoint) in hangerPoints)
                {
                    StructureRayScanner.ScanHit scan = null;
                    try { scan = scanner.Scan(hangerPoint); } catch { }
                    if (scan == null)
                    {
                        misses.Add(hanger);
                        continue;
                    }

                    hits.Add(new RayHitResult
                    {
                        Hanger = hanger,
                        HangerPoint = hangerPoint,
                        HitPoint = hangerPoint + XYZ.BasisZ * scan.Distance,
                        Distance = scan.Distance,
                        HitCategory = scan.Category,
                        HitCategoryLabel = scan.CategoryLabel,
                        HitElementName = scan.ElementName,
                        HitLinkName = scan.LinkName,
                        Source = scan.Source,
                        Quality = scan.Quality,
                        CorrectionFt = scan.CorrectionFt
                    });
                }

                if (hits.Count == 0 && misses.Count > 0)
                {
                    TaskDialog.Show("Raybounce Early",
                        $"No structural elements found above any of the {misses.Count} selected hangers.\n\n" +
                        "Make sure structural elements (floors, roofs, framing) exist above the hangers " +
                        "in the model or linked models.");
                    return Result.Failed;
                }

                // ── DIAGNOSTIC: report each hanger's hit, non-destructively ──
                // Set the rods inside a transaction we DON'T commit (TransactionWrapper
                // auto-rolls back), regenerate, read each hanger's actual rod-top, then
                // discard. Lets us compare: hitZ (the raycast target) vs rodTopZ (where
                // the family actually puts the rod top). If rodTopZ ≈ hitZ but the rod
                // visually overshoots the deck, the HIT is above the underside (raycast /
                // wrong face). If rodTopZ > hitZ, the FAMILY extends beyond Rod Length.
                if (dlg.Diagnostic)
                {
                    var rep = new StringBuilder();
                    rep.AppendLine($"RAYBOUNCE EARLY — DIAGNOSTIC   ({hits.Count} hits, {misses.Count} misses)   [units: decimal feet]");
                    rep.AppendLine("hitZ = hangerZ + rodLen (the raycast target).  rodTopZ = actual top of the hanger after setting Rod Length.");
                    rep.AppendLine("If rodTopZ ≈ hitZ but the rod still pokes past the deck → the HIT is above the underside (raycast/wrong face).");
                    rep.AppendLine("If rodTopZ > hitZ → the hanger FAMILY extends beyond Rod Length.  Compare hitZ to the deck underside in a section.");
                    rep.AppendLine("via = source:quality — native:centered = intersector hit re-measured on the element's real geometry;");
                    rep.AppendLine("      mesh:centered = DirectShape triangle index (linked IFC); native:proximity = unverifiable, raw intersector value.");
                    rep.AppendLine("corr = raw Proximity minus verified distance, in inches (how far the raw raycast was off — phantom-face error).");
                    rep.AppendLine(scanner.DiagnosticsSummary);
                    rep.AppendLine(new string('-', 130));
                    rep.AppendLine(string.Format("{0,-4}{1,9}{2,9}{3,10}{4,9}{5,10}{6,10}{7,8}{8,8}  {9}",
                        "#", "X", "Y", "hangerZ", "rodLen", "hitZ", "rodTopZ", "dTop", "corr\"", "hit (cat / element / link / via)"));

                    using (var tw = new TransactionWrapper(doc, "Raybounce Diagnostic (rolled back)"))
                    {
                        foreach (var h in hits)
                        {
                            try { SetParameter(h.Hanger, "Rod Length", h.Distance); SetParameter(h.Hanger, "Y Grip", h.Distance); } catch { }
                        }
                        doc.Regenerate();

                        int i = 1;
                        foreach (var h in hits.OrderBy(x => x.HangerPoint.X).ThenBy(x => x.HangerPoint.Y))
                        {
                            double hz = h.HangerPoint.Z + h.Distance;
                            double rodTop = double.NaN;
                            try { var bb = h.Hanger.get_BoundingBox(null); if (bb != null) rodTop = bb.Max.Z; } catch { }
                            double dTop = double.IsNaN(rodTop) ? double.NaN : rodTop - hz;
                            rep.AppendLine(string.Format("{0,-4}{1,9:F3}{2,9:F3}{3,10:F3}{4,9:F3}{5,10:F3}{6,10:F3}{7,8:F3}{8,8:F1}  {9}",
                                i++, h.HangerPoint.X, h.HangerPoint.Y, h.HangerPoint.Z, h.Distance, hz, rodTop, dTop,
                                h.CorrectionFt * 12.0,
                                $"{h.HitCategoryLabel} / {h.HitElementName} / {h.HitLinkName} / {h.Source}:{h.Quality}"));
                        }
                        // NOT committed → rolled back. Nothing changes.
                    }

                    ShowCopyableReport(rep.ToString());
                    return Result.Succeeded;
                }

                int syncedCount = 0;
                int failedCount = 0;
                var categoryCounts = new Dictionary<string, int>();

                using (var tw = new TransactionWrapper(doc, "Raybounce Early"))
                {
                    try
                    {
                        foreach (var hit in hits)
                        {
                            try
                            {
                                SetParameter(hit.Hanger, "Rod Length", hit.Distance);
                                SetParameter(hit.Hanger, "Y Grip", hit.Distance);

                                if (!dlg.KeepHangerTypes)
                                {
                                    string typeCode = GetTypeCode(hit.HitCategory, dlg);
                                    SetParameter(hit.Hanger, "Type Code (Hydratec)", typeCode);
                                    SetParameter(hit.Hanger, "Comments", typeCode);
                                }

                                syncedCount++;

                                string label = hit.HitCategoryLabel;
                                if (categoryCounts.ContainsKey(label))
                                    categoryCounts[label]++;
                                else
                                    categoryCounts[label] = 1;
                            }
                            catch
                            {
                                failedCount++;
                            }
                        }

                        tw.Commit();
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                        return Result.Failed;
                    }
                }

                if (misses.Count > 0)
                    uidoc.Selection.SetElementIds(misses.Select(m => m.Id).ToList());

                var summaryLines = new List<string>();
                if (syncedCount > 0)
                {
                    summaryLines.Add($"A Total Of {syncedCount} Hangers Have Been Re-Sync'd!");
                    summaryLines.Add("");
                    foreach (var kvp in categoryCounts.OrderBy(k => k.Key))
                        summaryLines.Add($"  {kvp.Key}: {kvp.Value}");
                }
                else
                {
                    summaryLines.Add("No Hangers Required Synchronizing.");
                }

                if (misses.Count > 0)
                {
                    summaryLines.Add("");
                    summaryLines.Add($"{misses.Count} hanger{(misses.Count != 1 ? "s" : "")} couldn't be synchronized " +
                                    "(no structural element found above). These are now highlighted.");
                }

                if (failedCount > 0)
                    summaryLines.Add($"{failedCount} hanger{(failedCount != 1 ? "s" : "")} failed.");

                summaryLines.Add("");
                summaryLines.Add("── Geometry verification ──");
                summaryLines.Add(scanner.DiagnosticsSummary);

                TaskDialog.Show("Raybounce Early — Summary", string.Join("\n", summaryLines));

                return Result.Succeeded;
            }
        }

        /// <summary>Shows a copyable, monospaced text report (for the diagnostic).</summary>
        private static void ShowCopyableReport(string text)
        {
            using (var f = new DpiAwareForm())
            {
                f.Text = "Raybounce Diagnostic — copy this and paste it back";
                // DPI self-check: after the handle exists, DeviceDpi is final.
                // 144 on a 150% machine = the DPI fix engaged; 96 = still broken.
                f.Shown += (s, ev) => { try { f.Text = $"Raybounce Diagnostic — DeviceDpi={f.DeviceDpi} (expect 144 @150%) — copy & paste back"; } catch { } };
                f.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                f.ClientSize = new System.Drawing.Size(900, 600);
                var tb = new System.Windows.Forms.TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = System.Windows.Forms.ScrollBars.Both,
                    WordWrap = false,
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    Font = new System.Drawing.Font("Consolas", 9f),
                    Text = text
                };
                var panel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Bottom, Height = 44 };
                var btnCopy = new System.Windows.Forms.Button { Text = "Copy to clipboard", Location = new System.Drawing.Point(10, 8), Size = new System.Drawing.Size(150, 28) };
                btnCopy.Click += (s, ev) => { try { System.Windows.Forms.Clipboard.SetText(text); } catch { } };
                var btnClose = new System.Windows.Forms.Button { Text = "Close", DialogResult = System.Windows.Forms.DialogResult.OK, Location = new System.Drawing.Point(170, 8), Size = new System.Drawing.Size(90, 28) };
                panel.Controls.Add(btnCopy);
                panel.Controls.Add(btnClose);
                f.Controls.Add(tb);
                f.Controls.Add(panel);
                f.AcceptButton = btnClose;
                f.ShowDialog();
            }
        }

        private XYZ GetHangerPoint(FamilyInstance hanger)
        {
            LocationPoint locPt = hanger.Location as LocationPoint;
            if (locPt != null)
                return locPt.Point;

            LocationCurve locCurve = hanger.Location as LocationCurve;
            if (locCurve?.Curve != null)
                return locCurve.Curve.Evaluate(0.5, true);

            return null;
        }

        private View3D FindOrCreate3DView(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate &&
                    v.Name.Equals(RaybounceViewName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            ViewFamilyType vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null) return null;

            View3D newView = View3D.CreateIsometric(doc, vft.Id);
            newView.Name = RaybounceViewName;

            Parameter detailParam = newView.get_Parameter(BuiltInParameter.VIEW_DETAIL_LEVEL);
            if (detailParam != null && !detailParam.IsReadOnly)
                detailParam.Set(3);

            Parameter styleParam = newView.get_Parameter(BuiltInParameter.MODEL_GRAPHICS_STYLE);
            if (styleParam != null && !styleParam.IsReadOnly)
                styleParam.Set(2);

            return newView;
        }

        private string GetTypeCode(BuiltInCategory hitCat, RaybounceEarlyDialog dlg)
        {
            switch (hitCat)
            {
                case BuiltInCategory.OST_Floors: return dlg.TypeCodeFloors;
                case BuiltInCategory.OST_Stairs: return dlg.TypeCodeStairs;
                case BuiltInCategory.OST_Roofs: return dlg.TypeCodeRoofs;
                case BuiltInCategory.OST_StructuralFraming: return dlg.TypeCodeFraming;
                default: return "";
            }
        }

        private bool IsValidHanger(FamilyInstance fi)
        {
            if (fi.Category?.Id.IntegerValue != (int)BuiltInCategory.OST_PipeAccessory)
                return false;

            string familyName = fi.Symbol?.Family?.Name ?? "";
            foreach (var pattern in HangerFamilyPatterns)
            {
                if (familyName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private List<FamilyInstance> GetSelectedPipeAccessories(UIDocument uidoc)
        {
            Document doc = uidoc.Document;

            var preSelected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilyInstance>()
                .Where(fi => fi.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                .ToList();

            if (preSelected.Count > 0)
                return preSelected;

            try
            {
                var refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new PipeAccessoryFilter(),
                    "Select PIPE HANGERS to sync to structural, then press Finish.");

                return refs.Select(r => doc.GetElement(r.ElementId)).OfType<FamilyInstance>().ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private void SetParameter(Element elem, string paramName, object value)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (value is double dVal)
                param.Set(dVal);
            else if (value is string sVal)
                param.Set(sVal);
        }

        private class PipeAccessoryFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
                => elem.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory;

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
