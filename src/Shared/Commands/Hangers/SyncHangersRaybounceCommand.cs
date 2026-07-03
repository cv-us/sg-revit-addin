using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Synchronizes pipe hanger rod lengths to the structural elements above
    /// using RayBounce (ReferenceIntersector). Shoots a ray straight up from
    /// each hanger, finds the first structural hit (floors, stairs, roofs,
    /// structural framing — including linked models), and sets Rod Length
    /// to the vertical distance.
    ///
    /// WORKFLOW:
    ///   1. User selects pipe hangers (pre-selection or pick)
    ///   2. Find or create a 3D view for raybounce
    ///   3. Dialog: set type codes per structural category, keep-types option
    ///   4. Shoot ray UP from each hanger, find structural hit
    ///   5. Set Rod Length, Y Grip, and optionally Type Code + Comments
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SyncHangersRaybounceCommand : IExternalCommand
    {
        private const string RaybounceViewName = "3D-Raybounce";

        /// <summary>
        /// Family name patterns that identify valid pipe hangers.
        /// </summary>
        private static readonly string[] HangerFamilyPatterns = new[]
        {
            "-Pipe Hanger",
            "Ring Hanger",
            "-Basic Adjustable"
        };

        /// <summary>Target structural categories for raybounce.</summary>
        private static readonly BuiltInCategory[] TargetCategories = new[]
        {
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralTruss,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Roofs
        };

        /// <summary>
        /// Result from raybounce for a single hanger.
        /// </summary>
        private class RayHitResult
        {
            public FamilyInstance Hanger { get; set; }
            public XYZ HangerPoint { get; set; }
            public XYZ HitPoint { get; set; }
            public double Distance { get; set; }
            public BuiltInCategory HitCategory { get; set; }
            public string HitCategoryLabel { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Get selected pipe accessories ──
            List<FamilyInstance> selectedAccessories = GetSelectedPipeAccessories(uidoc);
            if (selectedAccessories == null)
                return Result.Cancelled;

            // ── Filter to valid pipe hangers ──
            var hangers = selectedAccessories
                .Where(fi => IsValidHanger(fi))
                .ToList();

            if (hangers.Count == 0)
            {
                TaskDialog.Show("Sync Hangers to Structural",
                    "No valid pipe hangers found in the selection.\n\n" +
                    "Select elements whose family name contains \"-Pipe Hanger\", " +
                    "\"Ring Hanger\", or \"-Basic Adjustable\".");
                return Result.Failed;
            }

            // ── Show dialog ──
            using (var dlg = new SyncHangersRaybounceDialog(hangers.Count))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                bool includeGeneric = dlg.IncludeGenericGeometry;
                bool includeImportedCAD = dlg.IncludeImportedCAD;

                // ── Find or create 3D view for raybounce ──
                View3D raybounceView = null;

                using (var tw = new TransactionWrapper(doc, "Setup Raybounce View"))
                {
                    try
                    {
                        raybounceView = FindOrCreate3DView(doc);
                        if (raybounceView == null)
                        {
                            TaskDialog.Show("Sync Hangers to Structural",
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

                // ── Perform raybounce for each hanger ──
                var hits = new List<RayHitResult>();
                var misses = new List<FamilyInstance>();

                // Linked CAD geometry (DWG / DGN / SAT — covers STEP and
                // IFC routed through AutoCAD) is handled by a dedicated
                // CadMeshRaycaster: ReferenceIntersector returns
                // element-extent/bbox proximity for ImportInstance, not
                // triangle proximity, which caused rods to overshoot the
                // real geometry by feet.
                CadMeshRaycaster cadRaycaster = null;
                if (includeImportedCAD)
                {
                    cadRaycaster = new CadMeshRaycaster(doc, raybounceView);
                    cadRaycaster.Build();
                }

                // Resolve hanger points up front — they also bound the
                // scanner's DirectShape/IFC mesh index spatially.
                var hangerPoints = new List<(FamilyInstance hanger, XYZ pt)>();
                foreach (var hanger in hangers)
                {
                    XYZ pt = GetHangerPoint(hanger);
                    if (pt == null) misses.Add(hanger);
                    else hangerPoints.Add((hanger, pt));
                }

                // The scanner does the heavy lifting: native raybounce with
                // geometry-VERIFIED distances (fixes rods stretching under
                // sloped decks in linked files), a center-priority ray fan
                // (narrow steel a couple inches off in plan), a DirectShape
                // triangle index (linked IFC beams that native rays pass
                // through), and the CAD import raycaster. Closest verified
                // hit wins.
                var scanner = new StructureRayScanner(doc, raybounceView, TargetCategories)
                {
                    UseFan = true,
                    VerifyWithGeometry = true,
                    IncludeGenericCategories = includeGeneric,
                    ImportRaycaster = cadRaycaster
                };
                scanner.Build(hangerPoints.Select(hp => hp.pt).ToList());

                // Capture the first few hanger points for the CAD spatial
                // probe (diagnostics only).
                var probePoints = new List<XYZ>();

                foreach (var (hanger, hangerPoint) in hangerPoints)
                {
                    if (cadRaycaster != null && probePoints.Count < 3)
                        probePoints.Add(hangerPoint);

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
                        HitCategoryLabel = scan.CategoryLabel
                    });
                }

                if (hits.Count == 0 && misses.Count > 0)
                {
                    var noHitLines = new List<string>
                    {
                        $"No structural elements found above any of the {misses.Count} selected hangers.",
                        "",
                        "Make sure structural elements (floors, roofs, framing) exist above the hangers " +
                        "in the model or linked models."
                    };
                    // Always surface the CAD probe here too — this is the
                    // total-miss case we most need to diagnose.
                    if (cadRaycaster != null)
                    {
                        noHitLines.Add("");
                        noHitLines.Add("── Linked CAD detection ──");
                        noHitLines.Add(cadRaycaster.Diagnostics);
                        foreach (var pt in probePoints)
                        {
                            noHitLines.Add("");
                            noHitLines.Add(cadRaycaster.ProbeColumn(pt, XYZ.BasisZ));
                        }
                    }
                    TaskDialog.Show("Sync Hangers to Structural", string.Join("\n", noHitLines));
                    return Result.Failed;
                }

                // ── Write parameters ──
                int syncedCount = 0;
                int failedCount = 0;
                var categoryCounts = new Dictionary<string, int>();

                using (var tw = new TransactionWrapper(doc, "Sync Hangers to Structural"))
                {
                    try
                    {
                        foreach (var hit in hits)
                        {
                            try
                            {
                                // Set Rod Length
                                SetParameter(hit.Hanger, "Rod Length", hit.Distance);
                                // Set Y Grip (same value)
                                SetParameter(hit.Hanger, "Y Grip", hit.Distance);

                                // Set Type Code and Comments unless keeping existing types
                                if (!dlg.KeepHangerTypes)
                                {
                                    string typeCode = GetTypeCode(hit.HitCategory, dlg);
                                    SetParameter(hit.Hanger, "Type Code (Hydratec)", typeCode);
                                    SetParameter(hit.Hanger, "Comments", typeCode);
                                }

                                syncedCount++;

                                // Track category counts for summary
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

                // ── Highlight misses ──
                if (misses.Count > 0)
                {
                    var missIds = misses.Select(m => m.Id).ToList();
                    uidoc.Selection.SetElementIds(missIds);
                }

                // ── Summary ──
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

                // ── Verification diagnostics ──
                summaryLines.Add("");
                summaryLines.Add("── Geometry verification ──");
                summaryLines.Add(scanner.DiagnosticsSummary);

                // ── CAD diagnostics ── (only when linked-CAD detection is on)
                if (cadRaycaster != null)
                {
                    int cadWins = hits.Count(h => h.HitCategoryLabel == "Imported CAD");
                    summaryLines.Add("");
                    summaryLines.Add("── Linked CAD detection ──");
                    summaryLines.Add($"{cadWins} hanger{(cadWins != 1 ? "s" : "")} measured to CAD geometry.");
                    summaryLines.Add(cadRaycaster.Diagnostics);

                    // Per-hanger spatial probe — reveals XY/Z alignment of
                    // the cached CAD geometry relative to the hanger.
                    foreach (var pt in probePoints)
                    {
                        summaryLines.Add("");
                        summaryLines.Add(cadRaycaster.ProbeColumn(pt, XYZ.BasisZ));
                    }

                    if (cadRaycaster.TriangleCount == 0)
                    {
                        summaryLines.Add("");
                        summaryLines.Add("⚠ No CAD triangles were found. The DWG likely came in as " +
                                         "2D curves / wireframe (nothing solid to bounce off), or it " +
                                         "isn't an ImportInstance. Check that the STEP imported into " +
                                         "AutoCAD as 3D solids before saving the DWG.");
                    }
                    else if (cadWins == 0)
                    {
                        summaryLines.Add("");
                        summaryLines.Add("⚠ CAD triangles were cached but no ray hit one. The geometry " +
                                         "may be offset from the hangers (transform), or it sits below " +
                                         "the hanger rather than above.");
                    }
                }

                TaskDialog.Show("Hanger Sync To Structural Summary:",
                    string.Join("\n", summaryLines));

                return Result.Succeeded;
            }
        }

        /// <summary>
        /// Gets the hanger's location point. For hangers with Line geometry,
        /// uses the midpoint of the line.
        /// </summary>
        private XYZ GetHangerPoint(FamilyInstance hanger)
        {
            LocationPoint locPt = hanger.Location as LocationPoint;
            if (locPt != null)
                return locPt.Point;

            // Fallback: check if location is a curve (line-based hanger)
            LocationCurve locCurve = hanger.Location as LocationCurve;
            if (locCurve?.Curve != null)
                return locCurve.Curve.Evaluate(0.5, true);

            return null;
        }

        /// <summary>
        /// Finds an existing 3D view named "3D-Raybounce" or creates one.
        /// </summary>
        private View3D FindOrCreate3DView(Document doc)
        {
            // Look for existing view
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate &&
                    v.Name.Equals(RaybounceViewName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return existing;

            // Create a new 3D view
            ViewFamilyType vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null) return null;

            View3D newView = View3D.CreateIsometric(doc, vft.Id);
            newView.Name = RaybounceViewName;

            // Set detail level to Fine
            Parameter detailParam = newView.get_Parameter(BuiltInParameter.VIEW_DETAIL_LEVEL);
            if (detailParam != null && !detailParam.IsReadOnly)
                detailParam.Set(3); // Fine

            // Set visual style to Hidden Line
            Parameter styleParam = newView.get_Parameter(BuiltInParameter.MODEL_GRAPHICS_STYLE);
            if (styleParam != null && !styleParam.IsReadOnly)
                styleParam.Set(2); // Hidden Line

            return newView;
        }

        /// <summary>
        /// Maps a hit category to the user-specified type code.
        /// </summary>
        private string GetTypeCode(BuiltInCategory hitCat, SyncHangersRaybounceDialog dlg)
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

        /// <summary>
        /// Checks if a family instance is a valid pipe hanger.
        /// </summary>
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

        /// <summary>
        /// Gets selected pipe accessories from pre-selection or prompts to pick.
        /// </summary>
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

                return refs
                    .Select(r => doc.GetElement(r.ElementId))
                    .OfType<FamilyInstance>()
                    .ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Sets a parameter by name on an element.
        /// </summary>
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

