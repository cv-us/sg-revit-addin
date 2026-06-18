using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Commands.Hangers.PlaceHangers
{
    /// <summary>
    /// Unified hanger-placement command. One dialog with a method dropdown
    /// replaces the four separate auto-placement commands:
    ///   • Auto-spaced (decks / raybounce)   → HangTypicalSpacingCommand
    ///   • Auto-spaced (parallel framing)     → HangParallelStructuralCommand
    ///   • Downstream ends (threaded lines)   → HangDownstreamCommand
    ///   • At structural steel                → HangAtStructuralCommand
    ///
    /// Each method's placement algorithm is unchanged — this command just
    /// gathers the config from the unified dialog and calls the relevant
    /// command's RunPlacement(uidoc, config, pipes).
    ///
    /// All four methods select pipes the same way (pick pipe curves), so the
    /// pick happens once here after the dialog returns.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceHangersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // ── Dialog feed data ──
                var families = GetHangerFamilyNames(doc);
                if (families.Count == 0)
                {
                    TaskDialog.Show("Place Hangers",
                        "No pipe accessory (hanger) families are loaded. Load a hanger family first.");
                    return Result.Failed;
                }
                var pipeTypes = GetPipeTypeNames(doc);
                var linkNames = StructuralFramingHelpers.GetRevitLinks(doc).Select(l => l.Name).ToList();

                // ── Show unified dialog ──
                PlacementMethod method;
                TypicalSpacingConfig tsCfg = null;
                ParallelStructuralConfig psCfg = null;
                DownstreamConfig dsCfg = null;
                AtStructuralConfig asCfg = null;

                using (var dlg = new PlaceHangersDialog(families, pipeTypes, linkNames))
                {
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return Result.Cancelled;

                    method = dlg.Method;
                    switch (method)
                    {
                        case PlacementMethod.TypicalSpacing: tsCfg = dlg.BuildTypicalSpacing(); break;
                        case PlacementMethod.ParallelStructural: psCfg = dlg.BuildParallel(); break;
                        case PlacementMethod.Downstream: dsCfg = dlg.BuildDownstream(); break;
                        case PlacementMethod.AtStructural: asCfg = dlg.BuildAtStructural(); break;
                    }
                }

                // ── Pick pipes (all methods select pipe curves) ──
                string prompt = method == PlacementMethod.Downstream
                    ? "Select THREADED LINE pipes to hang, then press Finish"
                    : "Select pipe runs to hang, then press Finish";

                IList<Reference> pipeRefs;
                try
                {
                    pipeRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new CategorySelectionFilter(BuiltInCategory.OST_PipeCurves),
                        prompt);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (pipeRefs == null || pipeRefs.Count == 0)
                {
                    TaskDialog.Show("Place Hangers", "No pipes selected.");
                    return Result.Cancelled;
                }

                var pipes = pipeRefs.Select(r => doc.GetElement(r)).Where(e => e != null).ToList();

                // ── Dispatch to the chosen method's placement ──
                switch (method)
                {
                    case PlacementMethod.TypicalSpacing:
                        return new HangTypicalSpacingCommand().RunPlacement(uidoc, tsCfg, pipes, ref message);
                    case PlacementMethod.ParallelStructural:
                        return new HangParallelStructuralCommand().RunPlacement(uidoc, psCfg, pipes, ref message);
                    case PlacementMethod.Downstream:
                        return new HangDownstreamCommand().RunPlacement(uidoc, dsCfg, pipes, ref message);
                    case PlacementMethod.AtStructural:
                        return new HangAtStructuralCommand().RunPlacement(uidoc, asCfg, pipes, ref message);
                    default:
                        return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private IList<string> GetHangerFamilyNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null
                    && f.FamilyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_PipeAccessory)
                .Select(f => f.Name)
                .OrderBy(n => n)
                .ToList();
        }

        private IList<string> GetPipeTypeNames(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType))
                .Cast<Autodesk.Revit.DB.Plumbing.PipeType>()
                .Select(pt => pt.Name)
                .OrderBy(n => n)
                .ToList();
        }
    }
}
