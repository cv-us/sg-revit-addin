using Autodesk.Revit.UI;
using SgRevitAddin.Utils;
using System;
using System.Reflection;

namespace SgRevitAddin
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string tabName = "SG ♈";
            string asmPath = Assembly.GetExecutingAssembly().Location;

            try { application.CreateRibbonTab(tabName); }
            catch (Exception) { }

            // ── Pipe Routing panel ──
            RibbonPanel pipingPanel = application.CreateRibbonPanel(tabName, "Pipe Routing");
            AddLargeButton(pipingPanel, "AutoShortenFlexPipes", "Shorten\nFlex Pipes", asmPath,
                "SgRevitAddin.Commands.PipeRouting.ShortenFlexPipesCommand",
                "shorten-flex-32.png", "shorten-flex-16.png",
                "Replace selected flex pipes with shortest-length connections between the same endpoints.");

            // ── Hangers panel ──
            RibbonPanel hangersPanel = application.CreateRibbonPanel(tabName, "Hangers");

            // Small stack: alternate placement methods
            hangersPanel.AddStackedItems(
                MakeButton("AutoHangCAD", "Hang at CAD", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangAtCADLinesCommand",
                    "hang-cad-16.png", "Place hangers where pipes cross linked CAD structural lines."),
                MakeButton("AutoHangStructural", "Hang at Steel", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangAtStructuralCommand",
                    "hang-struct-16.png", "Place hangers where pipes cross structural framing members."),
                MakeButton("AutoHangDownstream", "Hang Downstream", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangDownstreamCommand",
                    "hang-downstream-16.png", "Place hangers at downstream ends of threaded branchline pipes (raybounce)."));
            // Small stack: spacing-based placement
            hangersPanel.AddStackedItems(
                MakeButton("AutoHangTypicalSpacing", "Hang Spaced", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangTypicalSpacingCommand",
                    "hang-spacing-16.png", "Place hangers at typical spacing along straight pipe runs (raybounce to decks)."),
                MakeButton("AutoHangParallelStructural", "Hang Parallel", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangParallelStructuralCommand",
                    "hang-parallel-16.png", "Place hangers at typical spacing, attached to parallel structural framing."),
                MakeButton("AutoHangUserLocations", "Hang User Loc", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangUserLocationsCommand",
                    "hang-userloc-16.png", "Place hangers at user-marked detail line locations with raybounce rod length."));
            // Small stack: special placement
            hangersPanel.AddStackedItems(
                MakeButton("HangConcreteTee", "Hang Tee Stems", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangConcreteTeeCommand",
                    "hang-tee-16.png", "Place hangers on sides of concrete double tee stems at user-marked detail line locations."),
                MakeButton("FormatHangerTicks", "Format Ticks", asmPath,
                    "SgRevitAddin.Commands.Hangers.FormatHangerTicksCommand",
                    "format-ticks-16.png", "Format all selected pipe hanger tick symbols to face the same direction (/ or \\)."));
            // Small stack: Section ID variants
            // Small stack: Section_ID writers
            hangersPanel.AddStackedItems(
                MakeButton("InsertHangerSectionIDs", "Section IDs", asmPath,
                    "SgRevitAddin.Commands.Hangers.HangerSectionIDsCommand",
                    "section-ids-16.png", "Populate Section_ID (Hydratec) with formatted hanger type and rod length for tags."),
                MakeButton("RingHangerSectionIDs", "Ring Section IDs", asmPath,
                    "SgRevitAddin.Commands.Hangers.RingHangerSectionIDsCommand",
                    "section-ids-16.png", "Adjustable Ring Hanger variant of Section IDs: subtracts a nominal-diameter-based ring takeout from Rod Length before writing Section_ID (Hydratec)."),
                MakeButton("RingSectionIDsHardware", "Ring IDs (+Hardware)", asmPath,
                    "SgRevitAddin.Commands.Hangers.RingSectionIDsHardwareCommand",
                    "section-ids-16.png", "Ring takeout like Ring Section IDs, then adds 1.5\" back for Type Codes starting with 01 or 02. Writes type(length) with no leading #. e.g. 02D rod 4\" becomes 02D(4)."));
            // Small stack: Type Code / Section_ID edits
            hangersPanel.AddStackedItems(
                MakeButton("ChangeTypeCode", "Change Type Code", asmPath,
                    "SgRevitAddin.Commands.Hangers.ChangeTypeCodeCommand",
                    "section-ids-16.png", "Bulk-change Type Code (Hydratec) on selected hangers: pick a From code from the selection, type the To code, only matching hangers are updated."),
                MakeButton("StripSectionIDTypeCode", "Strip Section ID Code", asmPath,
                    "SgRevitAddin.Commands.Hangers.StripSectionIDTypeCodeCommand",
                    "section-ids-16.png", "Strip the Type Code prefix from Section_ID (Hydratec) on selected hangers matching a chosen Type Code. \"#11T(5)\" becomes \"(5)\"."),
                MakeButton("StripSectionIDHashes", "Strip # From IDs", asmPath,
                    "SgRevitAddin.Commands.Hangers.StripSectionIDHashesCommand",
                    "section-ids-16.png", "Remove every '#' character from Section_ID (Hydratec) on all selected hangers. \"#11T(5)\" becomes \"11T(5)\"."));

            // Large: primary trapeze command
            AddLargeButton(hangersPanel, "AutoTrapezeHang", "Trapeze\nHang", asmPath,
                "SgRevitAddin.Commands.Hangers.TrapezeHangCommand",
                "hang-trapeze-32.png", "hang-trapeze-16.png",
                "Place standard pipe trapeze hangers at auto-spaced intervals with two-rod structural attachment.");
            // Small stack: trapeze variants
            hangersPanel.AddStackedItems(
                MakeButton("AutoTrapezeUserLoc", "Trapeze User Loc", asmPath,
                    "SgRevitAddin.Commands.Hangers.TrapezeUserLocationsCommand",
                    "hang-trapeze-ul-16.png", "Place trapeze hangers at user-marked detail line locations with two-rod structural attachment."),
                MakeButton("AutoTrapezeUnistrut", "Unistrut Trapeze", asmPath,
                    "SgRevitAddin.Commands.Hangers.TrapezeUnistrutCommand",
                    "hang-unistrut-16.png", "Place unistrut pipe trapeze hangers at auto-spaced intervals with channel extensions."),
                MakeButton("AutoTrapezeUnistrut21A", "Unistrut 21A", asmPath,
                    "SgRevitAddin.Commands.Hangers.TrapezeUnistrut21ACommand",
                    "hang-uni21a-16.png", "Place Unistrut 21A trapeze hangers with auto-calculated extensions (simplified)."));

            // Large: primary sync command
            AddLargeButton(hangersPanel, "SyncHangersToPipes", "Sync to\nPipes", asmPath,
                "SgRevitAddin.Commands.Hangers.SyncHangersToPipesCommand",
                "sync-pipes-32.png", "sync-pipes-16.png",
                "Move hangers to closest pipe, set rotation and ring size to match.");
            // Small stack: resize variants (parameter-set vs delete+recreate)
            hangersPanel.AddStackedItems(
                MakeButton("MatchHangerSizes", "Match Sizes", asmPath,
                    "SgRevitAddin.Commands.Hangers.MatchHangerSizesCommand",
                    "match-sizes-16.png", "Resize hangers to match pipe diameter via parameter set + rod-length compensation. Backup approach if Replace Sizes has issues."),
                MakeButton("ReplaceHangerSizes", "Replace Sizes", asmPath,
                    "SgRevitAddin.Commands.Hangers.ReplaceHangerSizesCommand",
                    "replace-sizes-16.png", "Resize hangers by deleting + recreating them at the new size with parameters preserved. Avoids the centerline-drift bug from parametric resize."));
            // Small stack: family swap + parameter diagnostic + rod-length sweep
            hangersPanel.AddStackedItems(
                MakeButton("AutoSwapHydraCAD", "Swap HydraCAD", asmPath,
                    "SgRevitAddin.Commands.Hangers.SwapHydraCADHangersCommand",
                    "swap-hydracad-16.png", "Replace HydraCAD hangers with SG -Pipe Hanger - Standard family instances."),
                MakeButton("InspectElementParameters", "Inspect Params", asmPath,
                    "SgRevitAddin.Commands.Hangers.InspectElementParametersCommand",
                    "inspect-params-16.png", "Diagnostic: dump every parameter of a selected element to a dialog + clipboard for debugging."),
                MakeButton("UniformRodLengths", "Uniform Rods", asmPath,
                    "SgRevitAddin.Commands.Hangers.UniformRodLengthsCommand",
                    "match-sizes-16.png", "Sweep Rod Length on hangers of a chosen Type Code to a uniform target — only those under a max-length cutoff (longer rods, likely on lower pipe, are left alone)."));
            // Large: review markers by type code
            AddLargeButton(hangersPanel, "MarkTypeForReview", "Mark for\nReview", asmPath,
                "SgRevitAddin.Commands.Hangers.MarkTypeForReviewCommand",
                "modelcheck-32.png", "modelcheck-16.png",
                "Flag hangers of a chosen Type Code with a tall magenta cylinder that extends above and below the hanger, visible in plan and 3D.");
            // Small stack: rod-length syncs
            hangersPanel.AddStackedItems(
                MakeButton("SyncHangersToRefPlane", "Sync Ref Plane", asmPath,
                    "SgRevitAddin.Commands.Hangers.SyncHangersToRefPlaneCommand",
                    "sync-refplane-16.png", "Calculate rod lengths from hangers to a named reference plane (slab underside)."),
                MakeButton("SyncHangersToStructural", "Sync Raybounce", asmPath,
                    "SgRevitAddin.Commands.Hangers.SyncHangersRaybounceCommand",
                    "sync-raybounce-16.png", "Calculate rod lengths via raybounce to structural elements above (floors, roofs, framing)."),
                MakeButton("SyncHangersToStructuralSurface", "Sync Surface", asmPath,
                    "SgRevitAddin.Commands.Hangers.SyncHangersSurfaceCommand",
                    "sync-surface-16.png", "Calculate rod lengths via surface intersection to structural elements above (no raybounce)."));
            // Small stack: trapeze utilities
            hangersPanel.AddStackedItems(
                MakeButton("SyncTrapeze", "Sync Trapeze", asmPath,
                    "SgRevitAddin.Commands.Hangers.SyncTrapezeHangersCommand",
                    "sync-trapeze-16.png", "Sync trapeze hanger rod lengths, offsets, and rotation to closest pipe and structure above."),
                MakeButton("FlipTrapezeHangers", "Flip Trapeze", asmPath,
                    "SgRevitAddin.Commands.Hangers.FlipTrapezeHangersCommand",
                    "flip-trapeze-16.png", "Rotate selected trapeze hangers 180° and swap Rod 1/Rod 2 parameter values."));

            // ── Seismic panel ──
            RibbonPanel seismicPanel = application.CreateRibbonPanel(tabName, "Seismic");
            AddLargeButton(seismicPanel, "InsertSeismicBraces", "Seismic\nBraces", asmPath,
                "SgRevitAddin.Commands.Annotation.SeismicBracesCommand",
                "seismic-braces-32.png", "seismic-braces-16.png",
                "Auto-place seismic braces on welded mains with NFPA spacing and rod length calculation.");
            AddLargeButton(seismicPanel, "HangerGapCheck", "Hanger Gap\nCheck", asmPath,
                "SgRevitAddin.Commands.ModelCheck.HangerGapCheckCommand",
                "hanger-gap-32.png", "hanger-gap-16.png",
                "Flag selected hangers whose top-of-pipe to structure gap exceeds a threshold (default 6\").");

            // ── Coordination panel ──
            RibbonPanel coordPanel = application.CreateRibbonPanel(tabName, "Coordination");
            AddLargeButton(coordPanel, "ColorCodePipes", "Color Code\nPipes", asmPath,
                "SgRevitAddin.Commands.Coordination.ColorCodePipesCommand",
                "color-pipes-32.png", "color-pipes-16.png",
                "Color-code pipes in the active view by diameter, type name, or reset overrides.");
            AddLargeButton(coordPanel, "MarkFamilyInstances", "Mark Family\nInstances", asmPath,
                "SgRevitAddin.Commands.Coordination.MarkFamilyInstancesCommand",
                "modelcheck-32.png", "modelcheck-16.png",
                "Place orange spheres at every instance of a chosen family. Searchable family list; scope is active view or whole project. Separate Delete All Markers button.");

            // ── Annotation panel ──
            RibbonPanel annotPanel = application.CreateRibbonPanel(tabName, "Annotation");

            // Large: most-used annotation command
            AddLargeButton(annotPanel, "InsertElevations", "Pipe\nElevations", asmPath,
                "SgRevitAddin.Commands.Annotation.PipeElevationsCommand",
                "pipe-elevations-32.png", "pipe-elevations-16.png",
                "Calculate and write TOS/AFF elevation parameters on pipes and fittings.");
            // Small stack: flex drops + scale bars
            annotPanel.AddStackedItems(
                MakeButton("InsertFlexDropLengths", "Flex Drops Set", asmPath,
                    "SgRevitAddin.Commands.Annotation.FlexDropLengthsCommand",
                    "flex-drop-16.png", "Insert flex drop length tags on selected sprinklers using ONE user-picked standard length (31\"/36\"/48\"/60\"/72\") applied uniformly to every sprinkler."),
                MakeButton("InsertFlexDropAuto", "Flex Drops Auto", asmPath,
                    "SgRevitAddin.Commands.Annotation.FlexDropLengthsAutoCommand",
                    "flex-auto-16.png", "Auto-size flex drop tags by reading each sprinkler's connected flex pipe and assigning the matching Wet or Dry standard. Flags pipes that exceed the system max."),
                MakeButton("InsertScaleBars", "Scale Bars", asmPath,
                    "SgRevitAddin.Commands.Annotation.GraphicScaleBarsCommand",
                    "scale-bars-16.png", "Insert graphic scale bar annotations on sheets based on view scales."));
            // Small stack: sleeve elevations + placement
            annotPanel.AddStackedItems(
                MakeButton("InsertSleeveElevations", "Sleeve Elevations", asmPath,
                    "SgRevitAddin.Commands.Annotation.SleeveElevationsCommand",
                    "sleeve-elevations-16.png", "Calculate AFF/BBD elevations on pipe sleeves from linked floor and deck geometry."),
                MakeButton("InsertSleevesAtBeams", "Sleeves at Beams", asmPath,
                    "SgRevitAddin.Commands.Annotation.PipeSleevesAtBeamsCommand",
                    "sleeves-beams-16.png", "Auto-place pipe sleeves at intersections with linked structural beams (NFPA sized)."),
                MakeButton("InsertSleevesAtDecks", "Sleeves at Decks", asmPath,
                    "SgRevitAddin.Commands.Annotation.PipeSleevesAtDecksCommand",
                    "sleeves-decks-16.png", "Auto-place pipe sleeves at intersections with linked floors/roofs (NFPA sized)."));
            // Small stack: walls, rooms, beams
            annotPanel.AddStackedItems(
                MakeButton("InsertSleevesAtWalls", "Sleeves at Walls", asmPath,
                    "SgRevitAddin.Commands.Annotation.PipeSleevesAtWallsCommand",
                    "sleeves-walls-16.png", "Auto-place pipe sleeves at intersections with linked walls (NFPA seismic/non-seismic)."),
                MakeButton("InsertRoomTextNotes", "Room Text Notes", asmPath,
                    "SgRevitAddin.Commands.Annotation.RoomTextNotesCommand",
                    "room-text-16.png", "Place stacked room name/number text notes from linked model rooms in the active view."),
                MakeButton("BeamPenetrationSymbols", "Beam Penetrations", asmPath,
                    "SgRevitAddin.Commands.Annotation.BeamPenetrationSymbolsCommand",
                    "beam-penetration-16.png", "Place beam penetration annotation symbols at pipe-grid or pipe-detail line crossing points."));
            // Small stack: SSB + cleanup
            annotPanel.AddStackedItems(
                MakeButton("SSBSymbols", "SSB Symbols", asmPath,
                    "SgRevitAddin.Commands.Annotation.SSBSymbolsCommand",
                    "ssb-symbols-16.png", "Place SSB hanger annotation symbols 1 ft from each end of selected pipe runs."),
                MakeButton("DeleteDuplicateText", "Delete Dup Text", asmPath,
                    "SgRevitAddin.Commands.Annotation.DeleteDuplicateTextCommand",
                    "delete-dupe-text-16.png", "Delete duplicate text notes at the same location in the active view."),
                MakeButton("ClearAnnotations", "Clear Annotations", asmPath,
                    "SgRevitAddin.Commands.Annotation.ClearAnnotationsCommand",
                    "clear-annotations-16.png", "Delete all generic annotation family instances from the active view."));

            // ── Views & Sheets panel ──
            RibbonPanel viewsPanel = application.CreateRibbonPanel(tabName, "Views & Sheets");

            // Large: primary view creation command
            AddLargeButton(viewsPanel, "CreatePlanViews", "Create\nPlan Views", asmPath,
                "SgRevitAddin.Commands.ViewsAndSheets.CreatePlanViewsCommand",
                "plan-views-32.png", "plan-views-16.png",
                "Create floor and/or ceiling plan views for selected levels with templates and naming.");
            // Large: legend transfer between open documents
            AddLargeButton(viewsPanel, "LegendTransfer", "Legend\nTransfer", asmPath,
                "SgRevitAddin.Commands.ViewsAndSheets.LegendTransferCommand",
                "dependent-views-32.png", "dependent-views-16.png",
                "Copy Legend views from one open document into another. Searchable list, per-legend selection, skips existing names, single-undo TransactionGroup.");
            // Small stack: dependent views + scope boxes
            viewsPanel.AddStackedItems(
                MakeButton("CreateDependentViews", "Dependent Views", asmPath,
                    "SgRevitAddin.Commands.ViewsAndSheets.CreateDependentViewsCommand",
                    "dependent-views-16.png", "Create dependent views from parent plans with optional scope box assignment."),
                MakeButton("RotateScopeBox", "Rotate Scope Box", asmPath,
                    "SgRevitAddin.Commands.ViewsAndSheets.RotateScopeBoxCommand",
                    "rotate-scopebox-16.png", "Rotate a scope box to match the angle of a local or linked grid line."),
                MakeButton("RemoveScopeBoxes", "Remove Scope Boxes", asmPath,
                    "SgRevitAddin.Commands.ViewsAndSheets.RemoveScopeBoxesCommand",
                    "remove-scopebox-16.png", "Delete selected scope boxes, or all scope boxes in the project if none are selected."));

            // ── Setup panel ──
            RibbonPanel setupPanel = application.CreateRibbonPanel(tabName, "Setup");

            // Large: load families is primary setup action
            AddLargeButton(setupPanel, "LoadFamilies", "Load\nFamilies", asmPath,
                "SgRevitAddin.Commands.Setup.LoadFamiliesCommand",
                "load-families-32.png", "load-families-16.png",
                "Load standard FP families into the project.");
            // Small stack: other setup commands
            setupPanel.AddStackedItems(
                MakeButton("CopyLinkLevelsGrids", "Copy Levels/Grids", asmPath,
                    "SgRevitAddin.Commands.Setup.CopyLinkLevelsGridsCommand",
                    "copy-levels-16.png", "Copy levels and/or grids from a linked model into the host project."),
                MakeButton("SetupGlobalParams", "Global Params", asmPath,
                    "SgRevitAddin.Commands.Setup.SetupGlobalParamsCommand",
                    "global-params-16.png", "Create all configuration global parameters with defaults (idempotent)."),
                MakeButton("ClearPipeElevationParams", "Clear Elev Params", asmPath,
                    "SgRevitAddin.Commands.Setup.ClearPipeElevationParamsCommand",
                    "clear-params-16.png", "Remove pipe elevation shared parameters (TOS/AFF) from the project."));

            // ── Export panel ──
            RibbonPanel exportPanel = application.CreateRibbonPanel(tabName, "Export");

            // Large: primary export command
            AddLargeButton(exportPanel, "ExportTrimblePoints", "Trimble\nPoints", asmPath,
                "SgRevitAddin.Commands.Export.ExportTrimblePointsCommand",
                "trimble-points-32.png", "trimble-points-16.png",
                "Export hanger locations as Trimble CSV point files for field layout of inserts.");
            // Small stack: markers + imports
            exportPanel.AddStackedItems(
                MakeButton("PlaceTrimbleMarkers", "Trimble Markers", asmPath,
                    "SgRevitAddin.Commands.Export.PlaceTrimbleMarkersCommand",
                    "trimble-markers-16.png", "Place or clear Trimble marker families at hanger and seismic brace locations for field layout."),
                MakeButton("ImportASPipes", "Import AS Pipes", asmPath,
                    "SgRevitAddin.Commands.Export.ImportASPipesCommand",
                    "import-pipes-16.png", "Import pipe geometry from an AutoSPRINK CSV export and create Revit pipes."),
                MakeButton("ImportASSprinklers", "Import AS Sprinklers", asmPath,
                    "SgRevitAddin.Commands.Export.ImportASSprinklersCommand",
                    "import-sprinklers-16.png", "Import sprinkler head locations from an AutoSPRINK CSV export and place family instances."));

            // ── Model Check panel ──
            RibbonPanel checkPanel = application.CreateRibbonPanel(tabName, "Model Check");
            checkPanel.AddStackedItems(
                MakeButton("SprinklerClearanceCheck", "Sprinkler Clearance", asmPath,
                    "SgRevitAddin.Commands.ModelCheck.SprinklerClearanceCheckCommand",
                    "sprinkler-clearance-16.png", "Check upright sprinklers for NFPA 3\" clearance violations from pipes and hangers."),
                MakeButton("DeflectorDistanceCheck", "Deflector Distance", asmPath,
                    "SgRevitAddin.Commands.ModelCheck.DeflectorDistanceCheckCommand",
                    "deflector-distance-16.png", "Measure upright deflector-to-structure distance and check against NFPA limits."),
                MakeButton("PipesTooShort", "Pipes Too Short", asmPath,
                    "SgRevitAddin.Commands.ModelCheck.PipesTooShortCommand",
                    "pipes-too-short-16.png", "Flag pipes shorter than the minimum fabricable nipple length for their size."));

            // Apply the SG-brand color border around the tab title. Best-effort
            // hook into AdWindows — no-op if Revit's WPF tree shape changes.
            RibbonStyling.ApplyTabAccent(tabName);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        /// <summary>
        /// Add a large (32×32) push button directly to a panel.
        /// </summary>
        private void AddLargeButton(RibbonPanel panel, string internalName, string displayName,
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

        /// <summary>
        /// Create a PushButtonData for use in stacked (small 16×16) layouts.
        /// </summary>
        private PushButtonData MakeButton(string internalName, string displayName,
            string assemblyPath, string className, string icon, string tooltip)
        {
            var btnData = new PushButtonData(internalName, displayName, assemblyPath, className);
            btnData.ToolTip = tooltip;

            var img = IconHelper.LoadIcon(icon);
            if (img != null) btnData.Image = img;

            return btnData;
        }
    }
}

