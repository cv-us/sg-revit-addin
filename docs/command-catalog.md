# Command Catalog

Master list of all SSG FP Suite commands. Update this as commands are implemented.

## Status Legend
- [ ] Not started
- [~] In progress
- [x] Complete

## SprinklerLayout
- [ ] `PlaceSprinklersCommand` - Place sprinkler heads in rooms based on coverage rules

## PipeRouting
- [ ] `AutoRouteBranchlinesCommand` - Auto-route branchlines from mains to sprinkler heads

## Hangers
- [ ] `AutoHangCommand` - Auto-place hangers at typical spacing along pipes
- [x] `AutoHangAtCADLinesCommand` - Place hangers where pipes cross linked CAD structural lines (migrated from Dynamo)
- [x] `AutoHangAtStructuralCommand` - Place hangers where pipes cross structural framing members (migrated from Dynamo)
- [x] `AutoHangDownstreamCommand` - Place hangers at downstream ends of threaded branchline pipes with raybounce rod length (migrated from Dynamo)
- [x] `AutoHangTypicalSpacingCommand` - Place hangers at typical spacing along straight pipe runs with raybounce rod length to decks (migrated from Dynamo)
- [x] `AutoHangParallelStructuralCommand` - Place hangers at typical spacing, attached to parallel structural framing with clamp angle and widemouth detection (migrated from Dynamo)
- [x] `AutoHangUserLocationsCommand` - Place hangers at user-marked detail line locations with raybounce rod length to structure above (migrated from Dynamo)
- [x] `AutoTrapezeHangCommand` - Place standard pipe trapeze hangers at auto-spaced intervals with two-rod structural attachment (migrated from Dynamo)
- [x] `AutoTrapezeUserLocationsCommand` - Place trapeze hangers at user-marked detail line locations with two-rod structural attachment (migrated from Dynamo)
- [x] `AutoTrapezeUnistrutCommand` - Place unistrut pipe trapeze hangers at auto-spaced intervals with channel extensions (migrated from Dynamo)
- [x] `AutoTrapezeUnistrut21ACommand` - Place Unistrut 21A trapeze hangers with auto-calculated extensions, simplified variant (migrated from Dynamo)
- [x] `FormatHangerTicksCommand` - Format pipe hanger tick symbols to consistent direction (/ or \) accounting for pipe rotation (migrated from Dynamo)

## Hydraulics
- [ ] `HydraulicCalcCommand` - Run/export hydraulic calculation data

## Fabrication
- [ ] `PipeCutListCommand` - Generate pipe cut list for fabrication shop

## Coordination
- [ ] `ColorCodePipesCommand` - Color-code pipes by size or system type

## Annotation
- [ ] `InsertElevationsCommand` - Insert pipe/fitting elevation annotations

## ViewsAndSheets
- [ ] `DuplicateViewsCommand` - Duplicate fire protection plan views

## Setup
- [ ] `LoadFamiliesCommand` - Load standard FP families into project

## ModelCheck
- [ ] `SprinklerClearanceCheckCommand` - Verify upright sprinkler deflector clearances
