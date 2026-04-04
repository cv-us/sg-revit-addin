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
            AddButton(pipingPanel, "AutoShortenFlexPipes", "Shorten\nFlex Pipes", assemblyPath,
                "SSG_FP_Suite.Commands.PipeRouting.AutoShortenFlexPipesCommand",
                "pipe-routing-32.png", "pipe-routing-16.png",
                "Replace selected flex pipes with shortest-length connections between the same endpoints.");

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
            AddButton(hangersPanel, "InsertHangerSectionIDs", "Section\nIDs", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.InsertHangerSectionIDsCommand",
                "hangers-32.png", "hangers-16.png",
                "Populate Section_ID (Hydratec) with formatted hanger type and rod length for tags.");
            AddButton(hangersPanel, "AutoSwapHydraCAD", "Swap\nHydraCAD", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoSwapHydraCADHangersCommand",
                "hangers-32.png", "hangers-16.png",
                "Replace HydraCAD hangers with Shambaugh -Pipe Hanger - Standard family instances.");
            AddButton(hangersPanel, "SyncHangersToPipes", "Sync to\nPipes", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoSyncHangersToPipesCommand",
                "hangers-32.png", "hangers-16.png",
                "Move hangers to closest pipe, set rotation and ring size to match.");
            AddButton(hangersPanel, "SyncHangersToRefPlane", "Sync to\nRef Plane", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoSyncHangersToRefPlaneCommand",
                "hangers-32.png", "hangers-16.png",
                "Calculate rod lengths from hangers to a named reference plane (slab underside).");
            AddButton(hangersPanel, "SyncHangersToStructural", "Sync to\nStructural", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoSyncHangersToStructuralCommand",
                "hangers-32.png", "hangers-16.png",
                "Calculate rod lengths via raybounce to structural elements above (floors, roofs, framing).");
            AddButton(hangersPanel, "SyncHangersToStructuralSurface", "Sync Struct\nSurface", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoSyncHangersToStructuralSurfaceCommand",
                "hangers-32.png", "hangers-16.png",
                "Calculate rod lengths via surface intersection to structural elements above (no raybounce).");
            AddButton(hangersPanel, "SyncTrapeze", "Sync\nTrapeze", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoSyncTrapezeHangersCommand",
                "hangers-32.png", "hangers-16.png",
                "Sync trapeze hanger rod lengths, offsets, and rotation to closest pipe and structure above.");
            AddButton(hangersPanel, "HangConcreteTee", "Hang\nTee Stems", assemblyPath,
                "SSG_FP_Suite.Commands.Hangers.AutoHangConcreteTeeCommand",
                "hangers-32.png", "hangers-16.png",
                "Place hangers on sides of concrete double tee stems at user-marked detail line locations.");

            // ── Seismic panel ──
            RibbonPanel seismicPanel = application.CreateRibbonPanel(tabName, "Seismic");
            AddButton(seismicPanel, "InsertSeismicBraces", "Seismic\nBraces", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertSeismicBracesCommand",
                "hangers-32.png", "hangers-16.png",
                "Auto-place seismic braces on welded mains with NFPA spacing and rod length calculation.");

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
            AddButton(annotPanel, "InsertElevations", "Pipe\nElevations", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertPipeElevationsCommand",
                "annotation-32.png", "annotation-16.png",
                "Calculate and write TOS/AFF elevation parameters on pipes and fittings.");
            AddButton(annotPanel, "InsertFlexDropLengths", "Flex Drop\nLengths", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertFlexDropLengthsCommand",
                "annotation-32.png", "annotation-16.png",
                "Insert flexible drop length tags on sprinkler heads with standard pipe lengths.");
            AddButton(annotPanel, "InsertFlexDropDalmatian", "Flex Drop\nDalmatian", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertFlexDropLengthsDalmatianCommand",
                "annotation-32.png", "annotation-16.png",
                "Auto-populate flex drop lengths from actual pipe lengths with Wet/Dry thresholds (Dalmatian style).");
            AddButton(annotPanel, "InsertScaleBars", "Scale\nBars", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertGraphicScaleBarsCommand",
                "annotation-32.png", "annotation-16.png",
                "Insert graphic scale bar annotations on sheets based on view scales.");
            AddButton(annotPanel, "InsertSleeveElevations", "Sleeve\nElevations", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertSleeveElevationsCommand",
                "annotation-32.png", "annotation-16.png",
                "Calculate AFF/BBD elevations on pipe sleeves from linked floor and deck geometry.");
            AddButton(annotPanel, "InsertSleevesAtBeams", "Sleeves at\nBeams", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertPipeSleevesAtBeamsCommand",
                "annotation-32.png", "annotation-16.png",
                "Auto-place pipe sleeves at intersections with linked structural beams (NFPA sized).");
            AddButton(annotPanel, "InsertSleevesAtDecks", "Sleeves at\nDecks", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertPipeSleevesAtDecksCommand",
                "annotation-32.png", "annotation-16.png",
                "Auto-place pipe sleeves at intersections with linked floors/roofs (NFPA sized).");
            AddButton(annotPanel, "InsertSleevesAtWalls", "Sleeves at\nWalls", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertPipeSleevesAtWallsCommand",
                "annotation-32.png", "annotation-16.png",
                "Auto-place pipe sleeves at intersections with linked walls (NFPA seismic/non-seismic).");
            AddButton(annotPanel, "InsertRoomTextNotes", "Room\nText Notes", assemblyPath,
                "SSG_FP_Suite.Commands.Annotation.InsertRoomTextNotesCommand",
                "annotation-32.png", "annotation-16.png",
                "Place stacked room name/number text notes from linked model rooms in the active view.");

            // ── Views & Sheets panel ──
            RibbonPanel viewsPanel = application.CreateRibbonPanel(tabName, "Views & Sheets");
            AddButton(viewsPanel, "DuplicateViews", "Duplicate\nViews", assemblyPath,
                "SSG_FP_Suite.Commands.ViewsAndSheets.DuplicateViewsCommand",
                "views-32.png", "views-16.png",
                "Duplicate fire protection plan views.");
            AddButton(viewsPanel, "CreatePlanViews", "Create\nPlan Views", assemblyPath,
                "SSG_FP_Suite.Commands.ViewsAndSheets.CreatePlanViewsCommand",
                "views-32.png", "views-16.png",
                "Create floor and/or ceiling plan views for selected levels with templates and naming.");
            AddButton(viewsPanel, "RotateScopeBox", "Rotate\nScope Box", assemblyPath,
                "SSG_FP_Suite.Commands.ViewsAndSheets.RotateScopeBoxCommand",
                "views-32.png", "views-16.png",
                "Rotate a scope box to match the angle of a local or linked grid line.");

            // ── Setup panel ──
            RibbonPanel setupPanel = application.CreateRibbonPanel(tabName, "Setup");
            AddButton(setupPanel, "LoadFamilies", "Load\nFamilies", assemblyPath,
                "SSG_FP_Suite.Commands.Setup.LoadFamiliesCommand",
                "setup-32.png", "setup-16.png",
                "Load standard FP families into the project.");
            AddButton(setupPanel, "CopyLinkLevelsGrids", "Copy Link\nLevels/Grids", assemblyPath,
                "SSG_FP_Suite.Commands.Setup.CopyLinkLevelsAndGridsCommand",
                "setup-32.png", "setup-16.png",
                "Copy levels and/or grids from a linked model into the host project.");
            AddButton(setupPanel, "SetupGlobalParams", "Setup Global\nParameters", assemblyPath,
                "SSG_FP_Suite.Commands.Setup.SetupGlobalParametersCommand",
                "setup-32.png", "setup-16.png",
                "Create all Dynamo Setting global parameters with defaults (idempotent).");

            // ── Export panel ──
            RibbonPanel exportPanel = application.CreateRibbonPanel(tabName, "Export");
            AddButton(exportPanel, "ExportTrimblePoints", "Trimble\nPoints", assemblyPath,
                "SSG_FP_Suite.Commands.Export.ExportTrimblePointsCommand",
                "setup-32.png", "setup-16.png",
                "Export hanger locations as Trimble CSV point files for field layout of inserts.");

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
