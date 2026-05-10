using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Diagnostic command. Dumps every parameter on a selected element
    /// (instance + type, with read-only flags, storage types, and current
    /// values) to a TaskDialog and copies the full output to the clipboard
    /// for easy pasting into bug reports / chat / documentation.
    ///
    /// Originally added to investigate why connector-hosted hanger families
    /// don't recenter on their host pipe after a Nominal Diameter change,
    /// but it's element-generic — works on any FamilyInstance, pipe, fitting,
    /// view, etc.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class InspectElementParametersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Resolve target element ──
            var preSelected = uidoc.Selection.GetElementIds().ToList();
            Element target = null;

            if (preSelected.Count == 1)
            {
                target = doc.GetElement(preSelected[0]);
            }
            else if (preSelected.Count == 0)
            {
                try
                {
                    var refX = uidoc.Selection.PickObject(ObjectType.Element,
                        "Pick the element you want to inspect.");
                    target = doc.GetElement(refX.ElementId);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }
            else
            {
                TaskDialog.Show("Inspect Element Parameters",
                    $"Select exactly one element (you have {preSelected.Count} selected), " +
                    "or run with no selection and you'll be prompted to pick one.");
                return Result.Cancelled;
            }

            if (target == null)
            {
                TaskDialog.Show("Inspect Element Parameters", "Couldn't resolve the element.");
                return Result.Failed;
            }

            // ── Build the dump ──
            var sb = new StringBuilder();
            sb.AppendLine("=== Element Parameter Inspection ===");
            sb.AppendLine($"ElementId:    {target.Id.IntegerValue}");
            sb.AppendLine($"Category:     {target.Category?.Name ?? "(no category)"}");
            sb.AppendLine($"Element name: {target.Name ?? "(no name)"}");

            FamilyInstance fi = target as FamilyInstance;
            FamilySymbol symbol = fi?.Symbol;
            if (fi != null && symbol != null)
            {
                sb.AppendLine($"Family:       {symbol.Family?.Name ?? "(no family)"}");
                sb.AppendLine($"Type:         {symbol.Name ?? "(no type)"}");
                sb.AppendLine($"Host element: {DescribeHost(fi, doc)}");
                sb.AppendLine($"Location:     {DescribeLocation(target)}");
            }
            else
            {
                sb.AppendLine($"Location:     {DescribeLocation(target)}");
            }
            sb.AppendLine();

            // Instance parameters
            sb.AppendLine("=== INSTANCE PARAMETERS ===");
            int dumpedInst = DumpParameters(target, doc, sb);
            sb.AppendLine($"({dumpedInst} instance parameters)");
            sb.AppendLine();

            // Type parameters (only for FamilyInstance)
            if (symbol != null)
            {
                sb.AppendLine("=== TYPE PARAMETERS ===");
                int dumpedType = DumpParameters(symbol, doc, sb);
                sb.AppendLine($"({dumpedType} type parameters)");
                sb.AppendLine();
            }

            // Connector summary (relevant for hangers)
            if (fi != null && fi.MEPModel?.ConnectorManager != null)
            {
                sb.AppendLine("=== MEP CONNECTORS ===");
                int connCount = 0;
                foreach (Connector c in fi.MEPModel.ConnectorManager.Connectors)
                {
                    connCount++;
                    string connInfo = $"  Connector {c.Id}: ";
                    try { connInfo += $"domain={c.Domain}, "; } catch { }
                    try { connInfo += $"isConnected={c.IsConnected}, "; } catch { }
                    try { connInfo += $"origin=({c.Origin.X:F3},{c.Origin.Y:F3},{c.Origin.Z:F3})"; }
                    catch { }
                    sb.AppendLine(connInfo);

                    if (c.IsConnected)
                    {
                        try
                        {
                            foreach (Connector other in c.AllRefs)
                            {
                                if (other.Owner.Id == fi.Id) continue;
                                sb.AppendLine($"    -> connected to {other.Owner.Category?.Name} " +
                                              $"id={other.Owner.Id.IntegerValue}");
                            }
                        }
                        catch { }
                    }
                }
                if (connCount == 0) sb.AppendLine("  (no connectors)");
                sb.AppendLine();
            }

            string fullDump = sb.ToString();

            // ── Copy to clipboard ──
            bool clipOk = false;
            try
            {
                System.Windows.Forms.Clipboard.SetText(fullDump);
                clipOk = true;
            }
            catch { /* clipboard can fail; fall back to dialog only */ }

            // ── Show dialog ──
            string preview = Truncate(fullDump, 2000);
            var td = new TaskDialog("Inspect Element Parameters")
            {
                MainInstruction = $"Inspected {target.Category?.Name ?? "element"} " +
                                  $"id={target.Id.IntegerValue}",
                MainContent = clipOk
                    ? "Full parameter dump copied to clipboard. Paste it where needed.\n\n" +
                      "Preview below:"
                    : "(Clipboard copy failed — full dump shown below.)",
                ExpandedContent = preview,
                CommonButtons = TaskDialogCommonButtons.Close,
                AllowCancellation = true
            };
            td.Show();

            return Result.Succeeded;
        }

        // ── Parameter dumping ──

        private int DumpParameters(Element element, Document doc, StringBuilder sb)
        {
            var paramList = element.Parameters
                .Cast<Parameter>()
                .Where(p => p.Definition != null)
                .OrderBy(p => p.Definition.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paramList.Count == 0)
            {
                sb.AppendLine("  (no parameters)");
                return 0;
            }

            int nameWidth = Math.Min(50, paramList.Max(p => (p.Definition.Name ?? "").Length));
            nameWidth = Math.Max(nameWidth, 20);

            sb.AppendLine($"  {"Name".PadRight(nameWidth)}  Flags  Storage    Value");
            sb.AppendLine($"  {new string('-', nameWidth)}  -----  ---------  " +
                          new string('-', 40));

            foreach (var p in paramList)
            {
                string name = TruncateField(p.Definition.Name ?? "(no name)", nameWidth);
                string flags = BuildFlags(p);
                string storage = p.StorageType.ToString();
                string value = FormatValue(p, doc);

                sb.AppendLine($"  {name.PadRight(nameWidth)}  {flags}  " +
                              $"{storage.PadRight(9)}  {value}");
            }

            return paramList.Count;
        }

        /// <summary>
        /// Builds a 5-character flag string showing key parameter properties:
        /// position 1: 'R' = read-only
        /// position 2: 'S' = shared parameter
        /// position 3: 'B' = built-in (has BuiltInParameter id)
        /// position 4: 'V' = has value
        /// position 5: ' ' (reserved)
        /// </summary>
        private string BuildFlags(Parameter p)
        {
            var flags = new char[5];
            for (int i = 0; i < flags.Length; i++) flags[i] = ' ';

            if (p.IsReadOnly) flags[0] = 'R';
            if (p.IsShared) flags[1] = 'S';

            try
            {
                // BuiltInParameter check via the parameter's own Id
                ElementId pid = p.Id;
                if (pid != null && pid.IntegerValue < 0)
                    flags[2] = 'B';
            }
            catch { }

            if (p.HasValue) flags[3] = 'V';

            return new string(flags);
        }

        private string FormatValue(Parameter p, Document doc)
        {
            if (!p.HasValue) return "(no value)";
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        string vs = p.AsValueString();
                        if (!string.IsNullOrEmpty(vs))
                            return $"{vs}  (raw: {p.AsDouble():F4})";
                        return $"{p.AsDouble():F4}";

                    case StorageType.Integer:
                        return p.AsInteger().ToString();

                    case StorageType.String:
                        string s = p.AsString();
                        return s != null ? $"\"{s}\"" : "(null)";

                    case StorageType.ElementId:
                        ElementId id = p.AsElementId();
                        if (id == null || id.IntegerValue == -1)
                            return "(invalid id -1)";
                        Element refElem = doc.GetElement(id);
                        if (refElem == null)
                            return $"id={id.IntegerValue}  (not found)";
                        string refName = refElem.Name ?? "(no name)";
                        string refCat = refElem.Category?.Name ?? "(no cat)";
                        return $"id={id.IntegerValue}  ({refCat}: \"{refName}\")";

                    default:
                        return "(unknown storage)";
                }
            }
            catch (Exception ex)
            {
                return $"(error: {ex.Message})";
            }
        }

        // ── Helpers ──

        private string DescribeHost(FamilyInstance fi, Document doc)
        {
            try
            {
                if (fi.Host != null)
                {
                    Element host = fi.Host;
                    return $"id={host.Id.IntegerValue}  ({host.Category?.Name}: \"{host.Name}\")";
                }
            }
            catch { }

            try
            {
                if (fi.SuperComponent != null)
                {
                    Element sc = fi.SuperComponent as Element;
                    return $"(super) id={sc.Id.IntegerValue}  ({sc.Category?.Name})";
                }
            }
            catch { }

            return "(none)";
        }

        private string DescribeLocation(Element e)
        {
            switch (e.Location)
            {
                case LocationPoint lp:
                    return $"Point ({lp.Point.X:F3}, {lp.Point.Y:F3}, {lp.Point.Z:F3}); " +
                           $"rotation={lp.Rotation:F4} rad";
                case LocationCurve lc:
                    var c = lc.Curve;
                    if (c == null) return "Curve (null)";
                    var s = c.GetEndPoint(0);
                    var ee = c.GetEndPoint(1);
                    return $"Curve from ({s.X:F3},{s.Y:F3},{s.Z:F3}) to " +
                           $"({ee.X:F3},{ee.Y:F3},{ee.Z:F3})";
                default:
                    return "(none / non-spatial)";
            }
        }

        private string TruncateField(string s, int width)
        {
            if (s.Length <= width) return s;
            return s.Substring(0, width - 1) + "…";
        }

        private string Truncate(string s, int maxLen)
        {
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) +
                   "\n\n... (truncated for display, full dump is in clipboard)";
        }
    }
}

