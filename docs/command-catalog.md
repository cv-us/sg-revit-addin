# Command Catalog

Master list of all SSG FP Suite commands. Update this as commands are implemented.

## Status Legend
- [ ] Not started
- [~] In progress
- [x] Complete

## SprinklerLayout
- [ ] `PlaceSprinklersCommand` - Place sprinkler heads in rooms based on coverage rules

## PipeRouting
- [ ] `RouteBranchlinesCommand` - Auto-route branchlines from mains to sprinkler heads
- [x] `ShortenFlexPipesCommand` - Replace selected flex pipes with shortest-length connections between the same endpoints

## Hangers
- [ ] `HangCommand` - Auto-place hangers at typical spacing along pipes
- [x] `HangAtCADLinesCommand` - Place hangers where pipes cross linked CAD structural lines
- [x] `HangAtStructuralCommand` - Place hangers where pipes cross structural framing members
- [x] `HangDownstreamCommand` - Place hangers at downstream ends of threaded branchline pipes with raybounce rod length
- [x] `HangTypicalSpacingCommand` - Place hangers at typical spacing along straight pipe runs with raybounce rod length to decks
- [x] `HangParallelStructuralCommand` - Place hangers at typical spacing, attached to parallel structural framing with clamp angle and widemouth detection
- [x] `HangUserLocationsCommand` - Place hangers at user-marked detail line locations with raybounce rod length to structure above
- [x] `TrapezeHangCommand` - Place standard pipe trapeze hangers at auto-spaced intervals with two-rod structural attachment
- [x] `TrapezeUserLocationsCommand` - Place trapeze hangers at user-marked detail line locations with two-rod structural attachment
- [x] `TrapezeUnistrutCommand` - Place unistrut pipe trapeze hangers at auto-spaced intervals with channel extensions
- [x] `TrapezeUnistrut21ACommand` - Place Unistrut 21A trapeze hangers with auto-calculated extensions, simplified variant
- [x] `FormatHangerTicksCommand` - Format pipe hanger tick symbols to consistent direction (/ or \) accounting for pipe rotation
- [x] `HangerSectionIDsCommand` - Populate Section_ID (Hydratec) with formatted rod length and type code for hanger tags
- [x] `SwapHydraCADHangersCommand` - Replace HydraCAD Adjustable Ring Hangers with Shambaugh -Pipe Hanger - Standard with parameter transfer
- [x] `SyncHangersToPipesCommand` - Move hangers to closest pipe, rotate to match direction, set ring size and stocklist info
- [x] `SyncHangersToRefPlaneCommand` - Calculate rod lengths from hangers to a named reference plane representing structural underside
- [x] `SyncHangersRaybounceCommand` - Calculate rod lengths via raybounce to structural elements above including linked models, with per-category type codes
- [x] `SyncHangersSurfaceCommand` - Calculate rod lengths via bounding box search and surface intersection to structural elements above, with framing top/bottom option
- [x] `SyncTrapezeHangersCommand` - Sync trapeze hanger rod lengths, offsets, rotation, and pipe diameter to closest pipe and structural elements above
- [x] `HangConcreteTeeCommand` - Place hangers on sides of concrete double tee stems at user-marked detail line locations with linked structural model

## Seismic
- [x] `SeismicBracesCommand` - Auto-place lateral and/or longitudinal seismic braces on welded mains with NFPA spacing and rod length from linked structure

## Hydraulics
- [ ] `HydraulicCalcCommand` - Run/export hydraulic calculation data

## Fabrication
- [ ] `PipeCutListCommand` - Generate pipe cut list for fabrication shop

## Export
- [x] `ExportTrimblePointsCommand` - Export hanger locations as Trimble-compatible CSV point files for field layout of inserts before concrete pours

## Coordination
- [ ] `ColorCodePipesCommand` - Color-code pipes by size or system type

## Annotation
- [x] `PipeElevationsCommand` - Calculate and write TOS/AFF elevation parameters on pipes and fittings with 4 reference methods including raybounce, slope classification
- [x] `FlexDropLengthsCommand` - Insert flexible drop length tags on sprinkler heads with standard pipe lengths
- [x] `FlexDropLengthsDalmatianCommand` - Auto-populate flex drop lengths from actual connected pipe lengths with Wet/Dry system thresholds and dynamic tag families
- [x] `GraphicScaleBarsCommand` - Insert graphic scale bar annotations on sheets based on view scales
- [x] `SleeveElevationsCommand` - Calculate AFF/BBD elevations on pipe sleeves from linked floor and deck geometry
- [x] `PipeSleevesAtBeamsCommand` - Auto-place NFPA-sized pipe sleeves at pipe-beam intersections with linked structural model
- [x] `PipeSleevesAtDecksCommand` - Auto-place NFPA-sized pipe sleeves at pipe-floor/roof intersections with wet area extension option
- [x] `PipeSleevesAtWallsCommand` - Auto-place pipe sleeves at pipe-wall intersections with seismic/non-seismic NFPA sizing and wall type filtering
- [x] `RoomTextNotesCommand` - Place stacked room name/number text notes from linked model rooms with crop region filtering

## ViewsAndSheets
- [ ] `DuplicateViewsCommand` - Duplicate fire protection plan views
- [x] `CreateDependentViewsCommand` - Create dependent views from parent floor/ceiling plans with scope box assignment or blank copies
- [x] `CreatePlanViewsCommand` - Create floor and/or ceiling plan views for selected levels with view templates and naming
- [x] `RotateScopeBoxCommand` - Rotate a scope box to match the angle of a local or linked grid line

## Setup
- [x] `LoadFamiliesCommand` - Load .rfa families from a folder into the project, skipping already-loaded families
- [x] `SetupGlobalParamsCommand` - Create all 86 configuration global parameters with defaults, fix legacy seismic Int64 types
- [x] `ClearPipeElevationParamsCommand` - Remove pipe elevation shared parameters (TOS/AFF) from the project with confirmation
- [x] `CopyLinkLevelsGridsCommand` - Copy levels and/or grids from a linked model with duplicate detection, grid type assignment, and pinning

## ModelCheck
- [ ] `SprinklerClearanceCheckCommand` - Verify upright sprinkler deflector clearances
