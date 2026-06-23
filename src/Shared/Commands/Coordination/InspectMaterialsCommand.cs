using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WinForms = System.Windows.Forms;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Read-only diagnostic for the Colorize-by-Workset work: pick one or more
    /// elements and dump how each resolves its material — type/instance material
    /// parameters AND the ACTUAL material(s) on the geometry faces (which is what
    /// Navisworks reads on NWC export). This tells us, per element, whether a
    /// flex pipe / fitting can be colored via a parameter or whether its solids
    /// are "By Category"/hardcoded (needing a .rfa edit).
    ///
    /// Nothing is modified. Output is shown in a copyable text box.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class InspectMaterialsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Use the current selection, else prompt to pick.
                var picked = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id))
                    .Where(e => e != null).ToList();
                if (picked.Count == 0)
                {
                    try
                    {
                        var refs = uidoc.Selection.PickObjects(ObjectType.Element,
                            "Pick elements to inspect (Esc/Finish when done).");
                        picked = refs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
                if (picked.Count == 0) { TaskDialog.Show("Inspect Materials", "Nothing selected."); return Result.Cancelled; }

                var sb = new StringBuilder();
                sb.AppendLine($"INSPECT MATERIALS — {picked.Count} element(s)");
                sb.AppendLine("(Geometry-face material = what Navisworks reads on NWC export.)");
                sb.AppendLine(new string('=', 70));

                int shown = 0;
                foreach (var e in picked)
                {
                    if (shown++ >= 12) { sb.AppendLine($"...and {picked.Count - 12} more (truncated)."); break; }
                    DumpElement(doc, e, sb);
                    sb.AppendLine(new string('-', 70));
                }

                ShowReport(sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void DumpElement(Document doc, Element e, StringBuilder sb)
        {
            string cat = e.Category?.Name ?? "(no category)";
            string ws = "";
            try { ws = doc.GetWorksetTable().GetWorkset(e.WorksetId)?.Name ?? ""; } catch { }

            sb.AppendLine($"{cat}  |  {e.GetType().Name}  |  id {e.Id.IntegerValue}");
            sb.AppendLine($"  Name: {SafeName(e)}");
            if (!string.IsNullOrEmpty(ws)) sb.AppendLine($"  Workset: {ws}");

            // Type element + its material params.
            Element typeElem = doc.GetElement(e.GetTypeId());
            if (typeElem != null)
            {
                sb.AppendLine($"  Type: {SafeName(typeElem)}  ({typeElem.GetType().Name})");
                var typeMats = MaterialParams(doc, typeElem);
                if (typeMats.Count > 0)
                {
                    sb.AppendLine("  Type material parameters:");
                    foreach (var s in typeMats) sb.AppendLine("    " + s);
                }
                else sb.AppendLine("  Type material parameters: (none)");
            }

            // Instance material params.
            var instMats = MaterialParams(doc, e);
            if (instMats.Count > 0)
            {
                sb.AppendLine("  Instance material parameters:");
                foreach (var s in instMats) sb.AppendLine("    " + s);
            }
            else sb.AppendLine("  Instance material parameters: (none)");

            // Ground truth: materials actually on the geometry faces.
            var geomMats = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
                CollectGeomMaterials(doc, e.get_Geometry(opt), geomMats, 0);
            }
            catch (Exception ex) { sb.AppendLine("  Geometry-face materials: <error: " + ex.Message + ">"); }
            if (geomMats.Count > 0)
            {
                sb.AppendLine("  Geometry-face materials (NWC reads these):");
                foreach (var kv in geomMats.OrderByDescending(k => k.Value))
                    sb.AppendLine($"    {kv.Key}   ({kv.Value} face(s))");
            }
            else sb.AppendLine("  Geometry-face materials: (no solids found at this detail level)");
        }

        /// <summary>Lists every Material-typed parameter (RW/RO + current value).</summary>
        private static List<string> MaterialParams(Document doc, Element e)
        {
            var outList = new List<string>();
            foreach (Parameter p in e.Parameters)
            {
                if (p == null || p.StorageType != StorageType.ElementId) continue;
                bool isMat = (BuiltInParameter)p.Id.IntegerValue == BuiltInParameter.MATERIAL_ID_PARAM;
                ElementId cur = p.AsElementId();
                if (!isMat && cur != ElementId.InvalidElementId && doc.GetElement(cur) is Material) isMat = true;
                if (!isMat && (p.Definition?.Name ?? "").IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0) isMat = true;
                if (!isMat) continue;

                string val = cur == ElementId.InvalidElementId
                    ? "<By Category / none>"
                    : ((doc.GetElement(cur) as Material)?.Name ?? cur.IntegerValue.ToString());
                outList.Add($"[{(p.IsReadOnly ? "RO" : "RW")}] {p.Definition?.Name} = {val}");
            }
            return outList;
        }

        private static void CollectGeomMaterials(Document doc, GeometryElement ge, Dictionary<string, int> outMats, int depth)
        {
            if (ge == null || depth > 6) return;
            foreach (GeometryObject go in ge)
            {
                if (go is Solid solid)
                {
                    if (solid.Faces.Size == 0) continue;
                    foreach (Face f in solid.Faces)
                    {
                        ElementId mid = f.MaterialElementId;
                        string nm = mid != ElementId.InvalidElementId
                            ? ((doc.GetElement(mid) as Material)?.Name ?? ("id " + mid.IntegerValue))
                            : "<By Category / none>";
                        outMats[nm] = (outMats.TryGetValue(nm, out int c) ? c : 0) + 1;
                    }
                }
                else if (go is GeometryInstance gi)
                {
                    CollectGeomMaterials(doc, gi.GetInstanceGeometry(), outMats, depth + 1);
                }
            }
        }

        private static string SafeName(Element e)
        {
            try { return string.IsNullOrEmpty(e.Name) ? "(unnamed)" : e.Name; } catch { return "(unnamed)"; }
        }

        private static void ShowReport(string text)
        {
            using (var f = new WinForms.Form())
            {
                f.Text = "Inspect Materials — copy this and paste it back";
                f.StartPosition = WinForms.FormStartPosition.CenterScreen;
                f.ClientSize = new System.Drawing.Size(760, 620);
                var tb = new WinForms.TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = WinForms.ScrollBars.Both,
                    WordWrap = false,
                    Dock = WinForms.DockStyle.Fill,
                    Font = new System.Drawing.Font("Consolas", 9f),
                    Text = text
                };
                var panel = new WinForms.Panel { Dock = WinForms.DockStyle.Bottom, Height = 44 };
                var btnCopy = new WinForms.Button { Text = "Copy to clipboard", Location = new System.Drawing.Point(10, 8), Size = new System.Drawing.Size(150, 28) };
                btnCopy.Click += (s, ev) => { try { WinForms.Clipboard.SetText(text); } catch { } };
                var btnClose = new WinForms.Button { Text = "Close", DialogResult = WinForms.DialogResult.OK, Location = new System.Drawing.Point(170, 8), Size = new System.Drawing.Size(90, 28) };
                panel.Controls.Add(btnCopy);
                panel.Controls.Add(btnClose);
                f.Controls.Add(tb);
                f.Controls.Add(panel);
                f.AcceptButton = btnClose;
                f.ShowDialog();
            }
        }
    }
}
