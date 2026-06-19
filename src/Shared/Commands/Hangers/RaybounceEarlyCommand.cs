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

                var categoryFilter = new ElementMulticategoryFilter(
                    new List<BuiltInCategory>(TargetCategories));

                foreach (var hanger in hangers)
                {
                    XYZ hangerPoint = GetHangerPoint(hanger);
                    if (hangerPoint == null) { misses.Add(hanger); continue; }

                    var hitResult = ShootRayUp(doc, raybounceView, hangerPoint, categoryFilter);
                    if (hitResult == null)
                    {
                        misses.Add(hanger);
                        continue;
                    }

                    hits.Add(new RayHitResult
                    {
                        Hanger = hanger,
                        HangerPoint = hangerPoint,
                        HitPoint = hitResult.Value.hitPoint,
                        Distance = hitResult.Value.distance,
                        HitCategory = hitResult.Value.category,
                        HitCategoryLabel = hitResult.Value.categoryLabel
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

                TaskDialog.Show("Raybounce Early — Summary", string.Join("\n", summaryLines));

                return Result.Succeeded;
            }
        }

        private (XYZ hitPoint, double distance, BuiltInCategory category, string categoryLabel)?
            ShootRayUp(Document doc, View3D view3D, XYZ origin, ElementFilter categoryFilter)
        {
            try
            {
                var intersector = new ReferenceIntersector(categoryFilter, FindReferenceTarget.Face, view3D)
                {
                    FindReferencesInRevitLinks = true
                };

                ReferenceWithContext result = intersector.FindNearest(origin, XYZ.BasisZ);
                if (result == null) return null;

                Reference reference = result.GetReference();
                if (reference == null) return null;

                double distance = result.Proximity;
                XYZ hitPoint = origin + XYZ.BasisZ * distance;

                Element hitElement = null;
                BuiltInCategory hitCat = BuiltInCategory.INVALID;

                if (reference.LinkedElementId != ElementId.InvalidElementId)
                {
                    RevitLinkInstance linkInst = doc.GetElement(reference.ElementId) as RevitLinkInstance;
                    Document linkDoc = linkInst?.GetLinkDocument();
                    if (linkDoc != null)
                        hitElement = linkDoc.GetElement(reference.LinkedElementId);
                }
                else
                {
                    hitElement = doc.GetElement(reference.ElementId);
                }

                if (hitElement?.Category != null)
                    hitCat = (BuiltInCategory)hitElement.Category.Id.IntegerValue;

                string label = GetCategoryLabel(hitCat);
                return (hitPoint, distance, hitCat, label);
            }
            catch
            {
                return null;
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

        private string GetCategoryLabel(BuiltInCategory cat)
        {
            switch (cat)
            {
                case BuiltInCategory.OST_Floors: return "Floors";
                case BuiltInCategory.OST_Stairs: return "Stairs";
                case BuiltInCategory.OST_Roofs: return "Roofs";
                case BuiltInCategory.OST_StructuralFraming: return "Structural Framing";
                default: return "Other";
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
