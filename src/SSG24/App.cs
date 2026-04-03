using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Reflection;

namespace SSG_FP_Suite
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "SSG FP Suite";
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            try { application.CreateRibbonTab(tabName); }
            catch (Exception) { }

            // ── Sprinkler Layout panel ──
            RibbonPanel layoutPanel = application.CreateRibbonPanel(tabName, "Sprinkler Layout");
            AddButton(layoutPanel, "PlaceSprinklers", "Place\nSprinklers", assemblyPath,
                "SSG_FP_Suite.Commands.SprinklerLayout.PlaceSprinklersCommand",
                "sprinkler-layout-32.png", "sprinkler-layout-16.png",
                "Place sprinkler heads in rooms based on coverage rules.");

            // ── Pipe Routing panel ──
            RibbonPanel pipingPanel = application.CreateRibbonPanel(tabName, "Pipe Routing");
            AddButton(pipingPanel, "AutoRouteBranchlines", "Auto Route\nBranchlines", assemblyPath,
                "SSG_FP_Suite.Commands.PipeRouting.AutoRouteBranchlinesCommand",
                "pipe-routing-32.png", "pipe-routing-16.png",
                "Auto-route branchlines from mains to sprinkler heads.");

            // ── Hangers panel ──
            RibbonPanel hangersPanel = application.CreateRibbonPanel(tabName, "Hangers");
            AddButton(hangersPanel, "AutoHang", "Auto\nHang", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangCommand",
                "hangers-32.png", "hangers-16.png",
                "Auto-place hangers at typical spacing along pipes.");
            AddButton(hangersPanel, "AutoHangCAD", "Hang at\nCAD Lines", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangAtCADLinesCommand",
                "hang-cad-32.png", "hang-cad-16.png",
                "Place hangers where pipes cross linked CAD structural lines.");
            AddButton(hangersPanel, "AutoHangStructural", "Hang at\nStructural", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangAtStructuralCommand",
                "hang-struct-32.png", "hang-struct-16.png",
                "Place hangers where pipes cross structural framing members.");
            AddButton(hangersPanel, "AutoHangDownstream", "Hang\nDownstream", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangDownstreamCommand",
                "hang-downstream-32.png", "hang-downstream-16.png",
                "Place hangers at downstream ends of threaded branchline pipes (raybounce).");
            AddButton(hangersPanel, "AutoHangTypicalSpacing", "Hang\nSpaced Runs", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangTypicalSpacingCommand",
                "hang-spacing-32.png", "hang-spacing-16.png",
                "Place hangers at typical spacing along straight pipe runs (raybounce to decks).");
            AddButton(hangersPanel, "AutoHangParallelStructural", "Hang\nParallel Steel", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangParallelStructuralCommand",
                "hang-parallel-32.png", "hang-parallel-16.png",
                "Place hangers at typical spacing, attached to parallel structural framing.");
            AddButton(hangersPanel, "AutoHangUserLocations", "Hang\nUser Loc", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangUserLocationsCommand",
                "hang-userloc-32.png", "hang-userloc-16.png",
                "Place hangers at user-marked detail line locations with raybounce rod length.");
            AddButton(hangersPanel, "AutoTrapezeHang", "Trapeze\nHang", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoTrapezeHangCommand",
                "hang-trapeze-32.png", "hang-trapeze-16.png",
                "Place standard pipe trapeze hangers at auto-spaced intervals with two-rod structural attachment.");
            AddButton(hangersPanel, "AutoTrapezeUserLoc", "Trapeze\nUser Loc", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoTrapezeUserLocationsCommand",
                "hang-trapeze-ul-32.png", "hang-trapeze-ul-16.png",
                "Place trapeze hangers at user-marked detail line locations with two-rod structural attachment.");
            AddButton(hangersPanel, "AutoTrapezeUnistrut", "Unistrut\nTrapeze", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoTrapezeUnistrutCommand",
                "hang-unistrut-32.png", "hang-unistrut-16.png",
                "Place unistrut pipe trapeze hangers at auto-spaced intervals with channel extensions.");
            AddButton(hangersPanel, "AutoTrapezeUnistrut21A", "Unistrut\n21A", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoTrapezeUnistrut21ACommand",
                "hang-uni21a-32.png", "hang-uni21a-16.png",
                "Place Unistrut 21A trapeze hangers with auto-calculated extensions (simplified).");
            AddButton(hangersPanel, "FormatHangerTicks", "Format\nTicks", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.FormatHangerTicksCommand",
                "format-ticks-32.png", "format-ticks-16.png",
                "Format all selected pipe hanger tick symbols to face the same direction (/ or \\).");

            // ── Hydraulics panel ──
            RibbonPanel hydraulicsPanel = application.CreateRibbonPanel(tabName, "Hydraulics");
            AddButton(hydraulicsPanel, "HydraulicCalc", "Hydraulic\nCalc", assemblyPath,
                "SSG_FP_Suite.Commands.Hydraulics.HydraulicCalcCommand",
                "hydraulics-32.png", "hydraulics-16.png",
                "Run or export hydraulic calculation data.");

            // ── Fabrication panel ──
            RibbonPanel fabPanel = application.CreateRibbonPanel(tabName, "Fabrication");
            AddButton(fabPanel, "PipeCutList", "Pipe\nCut List", assemblyPath,
                "SSG_FP_Suite.Commands.Fabrication.PipeCutListCommand",
                "fabrication-32.png", "fabrication-16.png",
                "Generate pipe cut list for fabrication shop.");

            // ── Coordination panel ──
            RibbonPanel coordPanel = application.CreateRibbonPanel(tabName, "Coordination");
            AddButton(coordPanel, "ColorCodePipes", "Color Code\nPipes", assemblyPath,
                "SSG_FP_Suite.Commands.Coordination.ColorCodePipesCommand",
                "coordination-32.png", "coordination-16.png",
                "Color-code pipes by size or system type.");

            // ── Annotation panel ──
            RibbonPanel annotPanel = application.CreateRibbonPanel(tabName, "Annotation");
            AddButton(annotPanel, "InsertElevations", "Insert\nElevations", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertElevationsCommand",
                "annotation-32.png", "annotation-16.png",
                "Insert pipe and fitting elevation annotations.");

            // ── Views & Sheets panel ──
            RibbonPanel viewsPanel = application.CreateRibbonPanel(tabName, "Views & Sheets");
            AddButton(viewsPanel, "DuplicateViews", "Duplicate\nViews", assemblyPath,
                "SSG_FP_Suite.Commands.ViewsAndSheets.DuplicateViewsCommand",
                "views-32.png", "views-16.png",
                "Duplicate fire protection plan views.");

            // ── Setup panel ──
            RibbonPanel setupPanel = application.CreateRibbonPanel(tabName, "Setup");
            AddButton(setupPanel, "LoadFamilies", "Load\nFamilies", assemblyPath,
                "SSG_FP_Suite.Commands.Setup.LoadFamiliesCommand",
                "setup-32.png", "setup-16.png",
                "Load standard FP families into the project.");

            // ── Model Check panel ──
            RibbonPanel checkPanel = application.CreateRibbonPanel(tabName, "Model Check");
            AddButton(checkPanel, "SprinklerClearanceCheck", "Sprinkler\nClearance", assemblyPath,
                "SSG_FP_Suite.Commands.ModelCheck.SprinklerClearanceCheckCommand",
                "modelcheck-32.png", "modelcheck-16.png",
                "Verify upright sprinkler deflector clearances.");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        /// <summary>
        /// Helper to add a push button with icons and tooltip.
        /// </summary>
        private void AddButton(RibbonPanel panel, string internalName, string displayName,
            string assemblyPath, string className, string largeIcon, string smallIcon, string tooltip)
        {
            var btnData = new PushButtonData(internalName, displayName, assemblyPath, className);
            btnData.ToolTip = tooltip;

            var large = IconHelper.LoadIcon(largeIcon);
            var small = IconHelper.LoadIcon(smallIcon);
            if (large != null) btnData.LargeImage = large;
            if (small != null) btnData.Image = small;

            panel.AddItem(btnData);
        }
    }
}
