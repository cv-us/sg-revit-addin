# Migration Queue

Track which Dynamo scripts from the older folders have been evaluated and their disposition.

## Status Legend
- [ ] Not yet evaluated
- [=] Duplicate of existing command — skipped
- [x] New command created
- [-] Not applicable (utility script, etc.)

## C:\dev\Dynamo Revit\2.3\ (98 scripts)

| # | Script | Status | Notes |
|---|--------|--------|-------|
| 1 | ! Clear Dynamo !.dyn | [-] | File cleanup utility, not a command |
| 2 | ! Copy Link Levels and Grids.dyn | [=] | Duplicate of CopyLinkLevelsGridsCommand |
| 3 | ! Create Floor and Ceiling Plan Views.dyn | [=] | Duplicate of CreatePlanViewsCommand |
| 4 | ! Setup - Clear Pipe Elevation Shared Parameters.dyn | [x] | New command: ClearPipeElevationParamsCommand |
| 5 | ! Setup - Copy Link Levels and Grids.dyn | [=] | Duplicate of CopyLinkLevelsGridsCommand |
| 6 | ! Setup - Create Dependent Views.dyn | [x] | New command: CreateDependentViewsCommand |
| 7 | ! Setup - Global Parameters.dyn | [=] | Duplicate of SetupGlobalParamsCommand |
| 8 | ! Setup - Load Custom Families.dyn | [=] | Duplicate of LoadFamiliesCommand |
| 9 | Auto Hang - Pipes Crossing Linked CAD File Lines.dyn | [=] | Duplicate of HangAtCADLinesCommand |
| 10 | Auto Hang - Pipes Crossing Structural Framing.dyn | [=] | Duplicate of HangAtStructuralCommand |
| 11 | Auto Hang - Threaded Branchlines - Downstream Ends.dyn | [=] | Duplicate of HangDownstreamCommand |
| 12 | Auto Hang - Threaded Branchlines - Under Stairs.dyn | [=] | Duplicate of HangDownstreamCommand — under-stairs context is pipe selection, not a different algorithm |
| 13 | Auto Hang - Typical Spaced Runs-Crossing Structural Framing.dyn | [=] | Duplicate of HangAtStructuralCommand |
| 14 | Auto Hang - Typical Spaced Runs-Hangers to Decks.dyn | [=] | Duplicate of HangTypicalSpacingCommand |
| 15 | Auto Hang - Typical Spaced Runs-Parallel To Structural Framing.dyn | [=] | Duplicate of HangParallelStructuralCommand |
| 16 | Auto Hang - Underside of Structural - Typical Spaced Runs.dyn | [=] | Duplicate of HangTypicalSpacingCommand |
| 17 | Auto Hang - Underside of Structural - User Locations.dyn | [=] | Duplicate of HangUserLocationsCommand |
| 18 | Auto Trapeze Hang - Single Pipe - Auto Spaced.dyn | [=] | Duplicate of TrapezeHangCommand — single-pipe family selected via dialog |
| 19 | Auto Trapeze Hang - Single Pipe - User Locations.dyn | [=] | Duplicate of TrapezeUserLocationsCommand |
| 20 | Auto Trapeze Hang - Standard Pipe Trapeze - Auto Spaced.dyn | [=] | Duplicate of TrapezeHangCommand |
| 21 | Auto Trapeze Hang - Standard Pipe Trapeze - User Locations.dyn | [=] | Duplicate of TrapezeUserLocationsCommand |
| 22 | Auto Trapeze Hang - Two Pipes - User Locations.dyn | [=] | Duplicate of TrapezeUserLocationsCommand — two-pipe family selected via dialog |
| 23 | Auto Trapeze Hang - Unistrut - Auto Spaced.dyn | [=] | Duplicate of TrapezeUnistrutCommand |
| 24 | Auto Trapeze Hang - Unistrut 21A - Auto Spaced.dyn | [=] | Duplicate of TrapezeUnistrut21ACommand |
| 25 | Auto Trapeze Hang - Unistrut 21A Single Pipe - Auto Spaced.dyn | [=] | Duplicate of TrapezeUnistrut21ACommand |
| 26 | AutoFlip Trapeze Hangers.dyn | [x] | New command: FlipTrapezeHangersCommand |
| 27 | AutoFormat - Hanger Ticks.dyn | [=] | Duplicate of FormatHangerTicksCommand |
| 28 | AutoInsert - Flexible Drop Lengths.dyn | [=] | Duplicate of FlexDropLengthsCommand |
| 29 | AutoInsert - Graphic Scale Bars To Sheets.dyn | [=] | Duplicate of GraphicScaleBarsCommand |
| 30 | AutoInsert - Hanger Section ID's.dyn | [=] | Duplicate of HangerSectionIDsCommand |
| 31 | AutoInsert - Pipe Elevations OVERALL.dyn | [=] | Duplicate of PipeElevationsCommand |
| 32 | AutoInsert - Pipe Elevations PIPE ENDS.dyn | [=] | Duplicate of PipeElevationsCommand |
| 33 | AutoInsert - Pipe Elevations-3DRayBounce.dyn | [=] | Duplicate of PipeElevationsCommand |
| 34 | AutoInsert - Pipe Elevations-Reference Level.dyn | [=] | Duplicate of PipeElevationsCommand |
| 35 | AutoInsert - Pipe Elevations.dyn | [=] | Duplicate of PipeElevationsCommand |
| 36 | AutoInsert - Pipe Fitting Elevations.dyn | [=] | Duplicate of PipeElevationsCommand |
| 37 | AutoInsert - Pipe Sleeve Elevations.dyn | [=] | Duplicate of SleeveElevationsCommand |
| 38 | AutoInsert - Pipe Sleeves at Intersecting Beams.dyn | [=] | Duplicate of PipeSleevesAtBeamsCommand |
| 39 | AutoInsert - Pipe Sleeves at Intersecting Decks.dyn | [=] | Duplicate of PipeSleevesAtDecksCommand |
| 40 | AutoInsert - Pipe Sleeves at Intersecting Walls.dyn | [=] | Duplicate of PipeSleevesAtWallsCommand |
| 41 | AutoInsert - Pipe and Fitting Elevations.dyn | [=] | Duplicate of PipeElevationsCommand |
| 42 | AutoInsert - Seismic Braces On Welded Mains.dyn | [=] | Duplicate of SeismicBracesCommand |
| 43 | AutoInsert - Text Notes - Room Names and Numbers.dyn | [=] | Duplicate of RoomTextNotesCommand |
| 44 | AutoInsert - Text Notes - Room Names.dyn | [=] | Duplicate of RoomTextNotesCommand |
| 45 | AutoShorten - Flex Pipes.dyn | [=] | Duplicate of ShortenFlexPipesCommand |
| 46 | AutoSwap - HydraCAD Hangers.dyn | [=] | Duplicate of SwapHydraCADHangersCommand |
| 47 | AutoSync - Hangers To Pipes.dyn | [=] | Duplicate of SyncHangersToPipesCommand |
| 48 | AutoSync - Hangers To Reference Plane.dyn | [=] | Duplicate of SyncHangersToRefPlaneCommand |
| 49 | AutoSync - Hangers To Structural Elements-3DRayBounce.dyn | [=] | Duplicate of SyncHangersRaybounceCommand |
| 50 | AutoSync - Hangers To Structural Elements.dyn | [=] | Duplicate of SyncHangersSurfaceCommand |
| 51 | AutoSync - Trapeze Hanger - Raybounce.dyn | [=] | Duplicate of SyncTrapezeHangersCommand |
| 52 | AutoSync - Trapeze Hanger.dyn | [=] | Duplicate of SyncTrapezeHangersCommand |
| 53 | Custom - Import AS Pipes From CSV.dyn | [x] | New command: ImportASPipesCommand |
| 54 | Custom - Import AS Sprinklers From CSV.dyn | [x] | New command: ImportASSprinklersCommand |
| 55 | Custom Auto Hang - Pipes Crossing Structural Framing - IFC Joists.dyn | [=] | Duplicate of HangAtStructuralCommand — IFC joists are a structural category selection, not a different algorithm |
| 56 | Custom AutoHang - C-Channel.dyn | [=] | Duplicate of HangAtStructuralCommand — C-channel is a structural framing family selection |
| 57 | Custom AutoHang - Center Loaded Hangers at Beams.dyn | [=] | Duplicate of HangAtStructuralCommand — beam center loading is a placement option, not a new algorithm |
| 58 | Custom AutoHang - GM Mechanical Plenum Hangers.dyn | [=] | Duplicate of HangTypicalSpacingCommand — project-specific family name, same algorithm |
| 59 | Custom AutoHang - Linked C-Channel.dyn | [=] | Duplicate of HangAtStructuralCommand — linked model variant, same algorithm |
| 60 | Custom AutoHang - Phoenix - Pipes Crossing Structural Framing.dyn | [=] | Duplicate of HangAtStructuralCommand — project-specific family, same algorithm |
| 61 | Custom AutoHang - Phoenix - Typical Spaced Runs.dyn | [=] | Duplicate of HangTypicalSpacingCommand — project-specific family, same algorithm |
| 62 | Custom AutoHang - Plenum Pipe Hangers.dyn | [=] | Duplicate of HangTypicalSpacingCommand — project-specific family, same algorithm |
| 63 | Custom AutoHang - Plenum Trapeze Hangers - User Locations.dyn | [=] | Duplicate of TrapezeUserLocationsCommand — project-specific family, same algorithm |
| 64 | Custom AutoHang - Threaded Armovers.dyn | [=] | Duplicate of HangDownstreamCommand — armover ends are downstream ends, same algorithm |
| 65 | Custom AutoHang - Threaded Branchlines-Nurbs Curve Roof.dyn | [=] | Duplicate of HangDownstreamCommand — Nurbs roof is a raybounce target, not a new algorithm |
| 66 | Custom AutoHang - Typical Spaced Straight Runs-Nurbs Curve Roof.dyn | [=] | Duplicate of HangTypicalSpacingCommand — Nurbs roof is a raybounce target, not a new algorithm |
| 67 | Custom AutoHang - Underside of Concrete Tees (Warped) - Typical Spaced Runs.dyn | [=] | Duplicate of HangConcreteTeeCommand — warped variant uses same side-of-stem algorithm |
| 68 | Custom AutoHang - Underside of Concrete Tees (Warped) - User Locations.dyn | [=] | Duplicate of HangConcreteTeeCommand — user location variant of same algorithm |
| 69 | Custom AutoHang - Z Purlins - Pipes Crossing.dyn | [=] | Duplicate of HangAtStructuralCommand — Z purlins are a structural framing category |
| 70 | Custom AutoHang - Z Purlins - Pipes Parallel.dyn | [=] | Duplicate of HangParallelStructuralCommand — Z purlins are a structural framing category |
| 71 | Custom AutoInsert - Beam Penetration Symbols at Grids.dyn | [x] | New command: BeamPenetrationSymbolsCommand |
| 72 | Custom AutoInsert - Flexible Drop Lengths-Dalmatian Fire Style.dyn | [=] | Duplicate of FlexDropLengthsDalmatianCommand |
| 73 | Custom AutoInsert - Flexible Drop Lengths-Northstar Fire Style.dyn | [=] | Duplicate of FlexDropLengthsDalmatianCommand — different company name, same algorithm |
| 74 | Custom AutoInsert - Pipe Sleeves at Intersecting Walls - META LCO.dyn | [=] | Duplicate of PipeSleevesAtWallsCommand — project-specific wall type filter, same algorithm |
| 75 | Custom AutoInsert - Pipe and Fitting Elevations - Structural Framing.dyn | [=] | Duplicate of PipeElevationsCommand — structural framing reference is an existing method option |
| 76 | Custom AutoInsert - SSB Symbols Along Pipe Runs.dyn | [x] | New command: SSBSymbolsCommand |
| 77 | Custom AutoSync - Hangers To Structural IFC Joists.dyn | [=] | Duplicate of SyncHangersSurfaceCommand — IFC joists are a structural category, same surface intersection algorithm |
| 78 | Custom Trapeze Hang - 17P Hanger - Auto Spaced.dyn | [=] | Duplicate of TrapezeHangCommand — project-specific family name, same algorithm |
| 79 | Custom Trapeze Hang - Single Pipe - Auto Spaced - Z Purlins.dyn | [=] | Duplicate of TrapezeHangCommand — Z purlin attachment, same algorithm |
| 80 | Custom TrapezeHang - Two Pipes - User Locations.dyn | [=] | Duplicate of TrapezeUserLocationsCommand — two-pipe family selected via dialog |
| 81 | Delete Duplicate Text Notes.dyn | [x] | New command: DeleteDuplicateTextCommand |
| 82 | Delete Duplicate Text.dyn | [=] | Duplicate of DeleteDuplicateTextCommand — same algorithm, slightly different name |
| 83 | Design Manage - Color Code Pipes By Size.dyn | [=] | Duplicate of ColorCodePipesCommand |
| 84 | Design Manage - Color Code Pipes By Type.dyn | [=] | Duplicate of ColorCodePipesCommand |
| 85 | Design Manage - Color Overrides Reset.dyn | [=] | Duplicate of ColorCodePipesCommand — reset is a mode of the same command |
| 86 | Design Manage - Hangers vs Upright Sprinkler Clearances V2.dyn | [=] | Duplicate of SprinklerClearanceCheckCommand |
| 87 | Design Manage - Hangers vs Upright Sprinkler Clearances.dyn | [=] | Duplicate of SprinklerClearanceCheckCommand |
| 88 | Model Check - Clear Annotations From Current View.dyn | [x] | New command: ClearAnnotationsCommand |
| 89 | Model Check - Clear Short Pipe Annotations From Current View.dyn | [=] | Duplicate of ClearAnnotationsCommand — short pipe annotations are a subset of generic annotations |
| 90 | Model Check - Clear Upright Clearance Annotations From Current View.dyn | [=] | Duplicate of ClearAnnotationsCommand — clearance annotations are a subset of generic annotations |
| 91 | Model Check - Pipes Too Short To Fab.dyn | [x] | New command: PipesTooShortCommand |
| 92 | Model Check - Upright Sprinkler Clearances.dyn | [=] | Duplicate of SprinklerClearanceCheckCommand |
| 93 | Model Check - Upright Sprinkler Deflector Distances.dyn | [=] | Duplicate of DeflectorDistanceCheckCommand |
| 94 | RotateScopeBox.dyn | [=] | Duplicate of RotateScopeBoxCommand |
| 95 | Scope Box Remover.dyn | [x] | New command: RemoveScopeBoxesCommand |
| 96 | Trimble - Add Trimble Families.dyn | [=] | Duplicate of LoadFamiliesCommand — Trimble families loaded from standard folder |
| 97 | Trimble - Clear Trimble Families In Active View.dyn | [=] | Covered by PlaceTrimbleMarkersCommand — clears existing markers before placing new ones |
| 98 | Trimble - Clear TrimbleFieldPoints In Active View.dyn | [=] | Covered by PlaceTrimbleMarkersCommand — clears existing markers before placing new ones |

## C:\dev\Dynamo Revit\Revit 2023\ (46 scripts)

**All evaluated — no new commands.** Every script is a duplicate of an existing command or a skip.

| # | Script | Status | Notes |
|---|--------|--------|-------|
| 1 | ! Clear Dynamo !.dyn | [-] | Cleanup utility, not a command |
| 2 | ! Setup - Copy Link Levels and Grids.dyn | [=] | Duplicate of CopyLinkLevelsGridsCommand |
| 3 | ! Setup - Create Dependent Views.dyn | [=] | Duplicate of CreateDependentViewsCommand |
| 4 | ! Setup - Create Plan Views.dyn | [=] | Duplicate of CreatePlanViewsCommand |
| 5 | ! Setup - Global Parameters.dyn | [=] | Duplicate of SetupGlobalParamsCommand |
| 6 | ! Setup - Load Custom Families.dyn | [=] | Duplicate of LoadFamiliesCommand |
| 7 | Auto Hang - Pipes Crossing Linked CAD File Lines.dyn | [=] | Duplicate of HangAtCADLinesCommand |
| 8 | Auto Hang - Pipes Crossing Structural Framing.dyn | [=] | Duplicate of HangAtStructuralCommand |
| 9 | Auto Hang - Threaded Branchlines - On Downstream Ends.dyn | [=] | Duplicate of HangDownstreamCommand |
| 10 | Auto Hang - Typical Spaced Runs - Hangers to Floor or Roof Decks.dyn | [=] | Duplicate of HangTypicalSpacingCommand |
| 11 | Auto Hang - Typical Spaced Runs - Parallel To Structural Framing.dyn | [=] | Duplicate of HangParallelStructuralCommand |
| 12 | Auto Hang - Typical Spaced Runs - Underside of Structure.dyn | [=] | Duplicate of HangTypicalSpacingCommand |
| 13 | Auto Hang - User Locations - Underside of Structural - RayBounce.dyn | [=] | Duplicate of HangUserLocationsCommand |
| 14 | Auto Hang - User Locations - Underside of Structure - RayBounce.dyn | [=] | Duplicate of HangUserLocationsCommand |
| 15 | Auto Trapeze Hang - Standard Pipe Trapeze - Auto Spaced.dyn | [=] | Duplicate of TrapezeHangCommand |
| 16 | Auto Trapeze Hang - Standard Pipe Trapeze - User Locations.dyn | [=] | Duplicate of TrapezeUserLocationsCommand |
| 17 | Auto Trapeze Hang - Unistrut - Auto Spaced.dyn | [=] | Duplicate of TrapezeUnistrutCommand |
| 18 | Auto Trapeze Hang - Unistrut 21A - Auto Spaced.dyn | [=] | Duplicate of TrapezeUnistrut21ACommand |
| 19 | AutoFormat - Hanger Ticks.dyn | [=] | Duplicate of FormatHangerTicksCommand |
| 20 | AutoInsert - Flexible Drop Lengths.dyn | [=] | Duplicate of FlexDropLengthsCommand |
| 21 | AutoInsert - Graphic Scale Bars To Sheets.dyn | [=] | Duplicate of GraphicScaleBarsCommand |
| 22 | AutoInsert - Hanger Section ID's.dyn | [=] | Duplicate of HangerSectionIDsCommand |
| 23 | AutoInsert - Pipe Sleeve Elevations.dyn | [=] | Duplicate of SleeveElevationsCommand |
| 24 | AutoInsert - Pipe Sleeves at Intersecting Beams.dyn | [=] | Duplicate of PipeSleevesAtBeamsCommand |
| 25 | AutoInsert - Pipe Sleeves at Intersecting Decks.dyn | [=] | Duplicate of PipeSleevesAtDecksCommand |
| 26 | AutoInsert - Pipe Sleeves at Intersecting Walls.dyn | [=] | Duplicate of PipeSleevesAtWallsCommand |
| 27 | AutoInsert - Pipe and Fitting Elevations.dyn | [=] | Duplicate of PipeElevationsCommand |
| 28 | AutoInsert - Seismic Braces On Welded Mains.dyn | [=] | Duplicate of SeismicBracesCommand |
| 29 | AutoInsert - Text Notes - Room Names and Numbers.dyn | [=] | Duplicate of RoomTextNotesCommand |
| 30 | AutoShorten - Flex Pipes.dyn | [=] | Duplicate of ShortenFlexPipesCommand |
| 31 | AutoSwap - HydraCAD Hangers.dyn | [=] | Duplicate of SwapHydraCADHangersCommand |
| 32 | AutoSync - Hangers To Pipes.dyn | [=] | Duplicate of SyncHangersToPipesCommand |
| 33 | AutoSync - Hangers To Reference Plane.dyn | [=] | Duplicate of SyncHangersToRefPlaneCommand |
| 34 | AutoSync - Hangers To Structural Elements-3DRayBounce.dyn | [=] | Duplicate of SyncHangersRaybounceCommand |
| 35 | AutoSync - Hangers To Structural Elements.dyn | [=] | Duplicate of SyncHangersSurfaceCommand |
| 36 | Custom Auto Hang - Concrete Tees (Warped) - Side of Stems - User Locations.dyn | [=] | Duplicate of HangConcreteTeeCommand |
| 37 | Custom Auto Hang - Concrete Tees (Warped) - Underside of Flange - Typical Spaced Runs.dyn | [=] | Duplicate of HangTypicalSpacingCommand — flange underside is vertical raybounce target, same algorithm |
| 38 | Custom Auto Hang - Concrete Tees (Warped) - Underside of Flange - User Locations.dyn | [=] | Duplicate of HangUserLocationsCommand — flange underside is vertical raybounce target, same algorithm |
| 39 | Custom Auto Hang - Z Purlins - Pipes Parallel.dyn | [=] | Duplicate of HangParallelStructuralCommand |
| 40 | Custom AutoInsert - Beam Penetration Symbols at Grids.dyn | [=] | Duplicate of BeamPenetrationSymbolsCommand |
| 41 | Custom AutoInsert - Flexible Drop Lengths-Dalmatian Fire Style.dyn | [=] | Duplicate of FlexDropLengthsDalmatianCommand |
| 42 | Custom AutoInsert - Pipe and Fitting Elevations - Structural Framing.dyn | [=] | Duplicate of PipeElevationsCommand |
| 43 | Custom AutoInsert - SSB Symbols Along Pipe Runs.dyn | [=] | Duplicate of SSBSymbolsCommand |
| 44 | Custom Trapeze Hang - Dual Pipe - User Locations.dyn | [=] | Duplicate of TrapezeUserLocationsCommand |
| 45 | Model Check - Upright Sprinkler Deflector Distances.dyn | [=] | Duplicate of DeflectorDistanceCheckCommand |
| 46 | RotateScopeBox.dyn | [=] | Duplicate of RotateScopeBoxCommand |
