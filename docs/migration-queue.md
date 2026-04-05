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
| 53 | Custom - Import AS Pipes From CSV.dyn | [ ] | |
| 54 | Custom - Import AS Sprinklers From CSV.dyn | [ ] | |
| 55 | Custom Auto Hang - Pipes Crossing Structural Framing - IFC Joists.dyn | [ ] | |
| 56 | Custom AutoHang - C-Channel.dyn | [ ] | |
| 57 | Custom AutoHang - Center Loaded Hangers at Beams.dyn | [ ] | |
| 58 | Custom AutoHang - GM Mechanical Plenum Hangers.dyn | [ ] | |
| 59 | Custom AutoHang - Linked C-Channel.dyn | [ ] | |
| 60 | Custom AutoHang - Phoenix - Pipes Crossing Structural Framing.dyn | [ ] | |
| 61 | Custom AutoHang - Phoenix - Typical Spaced Runs.dyn | [ ] | |
| 62 | Custom AutoHang - Plenum Pipe Hangers.dyn | [ ] | |
| 63 | Custom AutoHang - Plenum Trapeze Hangers - User Locations.dyn | [ ] | |
| 64 | Custom AutoHang - Threaded Armovers.dyn | [ ] | |
| 65 | Custom AutoHang - Threaded Branchlines-Nurbs Curve Roof.dyn | [ ] | |
| 66 | Custom AutoHang - Typical Spaced Straight Runs-Nurbs Curve Roof.dyn | [ ] | |
| 67 | Custom AutoHang - Underside of Concrete Tees (Warped) - Typical Spaced Runs.dyn | [ ] | |
| 68 | Custom AutoHang - Underside of Concrete Tees (Warped) - User Locations.dyn | [ ] | |
| 69 | Custom AutoHang - Z Purlins - Pipes Crossing.dyn | [ ] | |
| 70 | Custom AutoHang - Z Purlins - Pipes Parallel.dyn | [ ] | |
| 71 | Custom AutoInsert - Beam Penetration Symbols at Grids.dyn | [ ] | |
| 72 | Custom AutoInsert - Flexible Drop Lengths-Dalmatian Fire Style.dyn | [ ] | |
| 73 | Custom AutoInsert - Flexible Drop Lengths-Northstar Fire Style.dyn | [ ] | |
| 74 | Custom AutoInsert - Pipe Sleeves at Intersecting Walls - META LCO.dyn | [ ] | |
| 75 | Custom AutoInsert - Pipe and Fitting Elevations - Structural Framing.dyn | [ ] | |
| 76 | Custom AutoInsert - SSB Symbols Along Pipe Runs.dyn | [ ] | |
| 77 | Custom AutoSync - Hangers To Structural IFC Joists.dyn | [ ] | |
| 78 | Custom Trapeze Hang - 17P Hanger - Auto Spaced.dyn | [ ] | |
| 79 | Custom Trapeze Hang - Single Pipe - Auto Spaced - Z Purlins.dyn | [ ] | |
| 80 | Custom TrapezeHang - Two Pipes - User Locations.dyn | [ ] | |
| 81 | Delete Duplicate Text Notes.dyn | [ ] | |
| 82 | Delete Duplicate Text.dyn | [ ] | |
| 83 | Design Manage - Color Code Pipes By Size.dyn | [ ] | |
| 84 | Design Manage - Color Code Pipes By Type.dyn | [ ] | |
| 85 | Design Manage - Color Overrides Reset.dyn | [ ] | |
| 86 | Design Manage - Hangers vs Upright Sprinkler Clearances V2.dyn | [ ] | |
| 87 | Design Manage - Hangers vs Upright Sprinkler Clearances.dyn | [ ] | |
| 88 | Model Check - Clear Annotations From Current View.dyn | [ ] | |
| 89 | Model Check - Clear Short Pipe Annotations From Current View.dyn | [ ] | |
| 90 | Model Check - Clear Upright Clearance Annotations From Current View.dyn | [ ] | |
| 91 | Model Check - Pipes Too Short To Fab.dyn | [ ] | |
| 92 | Model Check - Upright Sprinkler Clearances.dyn | [ ] | |
| 93 | Model Check - Upright Sprinkler Deflector Distances.dyn | [ ] | |
| 94 | RotateScopeBox.dyn | [ ] | |
| 95 | Scope Box Remover.dyn | [ ] | |
| 96 | Trimble - Add Trimble Families.dyn | [ ] | |
| 97 | Trimble - Clear Trimble Families In Active View.dyn | [ ] | |
| 98 | Trimble - Clear TrimbleFieldPoints In Active View.dyn | [ ] | |

## C:\dev\Dynamo Revit\Revit 2023\ (46 scripts)

Will be evaluated after 2.3 folder is complete. Many are likely duplicates of Revit 2024 scripts (already migrated).
