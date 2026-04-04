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
- [x] `InsertHangerSectionIDsCommand` - Populate Section_ID (Hydratec) with formatted rod length and type code for hanger tags (migrated from Dynamo)

## Seismic
- [x] `InsertSeismicBracesCommand` - Auto-place lateral and/or longitudinal seismic braces on welded mains with NFPA spacing and rod length from linked structure (migrated from Dynamo)

## Hydraulics
- [ ] `HydraulicCalcCommand` - Run/export hydraulic calculation data

## Fabrication
- [ ] `PipeCutListCommand` - Generate pipe cut list for fabrication shop

## Coordination
- [ ] `ColorCodePipesCommand` - Color-code pipes by size or system type

## Annotation
- [x] `InsertPipeElevationsCommand` - Calculate and write TOS/AFF elevation parameters on pipes and fittings with 4 reference methods including raybounce, slope classification (migrated from Dynamo)
- [x] `InsertFlexDropLengthsCommand` - Insert flexible drop length tags on sprinkler heads with standard pipe lengths (migrated from Dynamo)
- [x] `InsertGraphicScaleBarsCommand` - Insert graphic scale bar annotations on sheets based on view scales (migrated from Dynamo)
- [x] `InsertSleeveElevationsCommand` - Calculate AFF/BBD elevations on pipe sleeves from linked floor and deck geometry (migrated from Dynamo)
- [x] `InsertPipeSleevesAtBeamsCommand` - Auto-place NFPA-sized pipe sleeves at pipe-beam intersections with linked structural model (migrated from Dynamo)
- [x] `InsertPipeSleevesAtDecksCommand` - Auto-place NFPA-sized pipe sleeves at pipe-floor/roof intersections with wet area extension option (migrated from Dynamo)
- [x] `InsertPipeSleevesAtWallsCommand` - Auto-place pipe sleeves at pipe-wall intersections with seismic/non-seismic NFPA sizing and wall type filtering (migrated from Dynamo)

## ViewsAndSheets
- [ ] `DuplicateViewsCommand` - Duplicate fire protection plan views

## Setup
- [ ] `LoadFamiliesCommand` - Load standard FP families into project

## ModelCheck
- [ ] `SprinklerClearanceCheckCommand` - Verify upright sprinkler deflector clearances
