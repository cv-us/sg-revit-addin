# Command Catalog

Master list of all SG Revit Addin commands.

## PipeRouting
- `ShortenFlexPipesCommand` - Replace selected flex pipes with shortest-length connections between the same endpoints

## Hangers
- `HangAtCADLinesCommand` - Place hangers where pipes cross linked CAD structural lines
- `HangAtStructuralCommand` - Place hangers where pipes cross structural framing members
- `HangDownstreamCommand` - Place hangers at downstream ends of threaded branchline pipes with raybounce rod length
- `HangTypicalSpacingCommand` - Place hangers at typical spacing along straight pipe runs with raybounce rod length to decks
- `HangParallelStructuralCommand` - Place hangers at typical spacing, attached to parallel structural framing with clamp angle and widemouth detection
- `HangUserLocationsCommand` - Place hangers at user-marked detail line locations with raybounce rod length to structure above
- `TrapezeHangCommand` - Place standard pipe trapeze hangers at auto-spaced intervals with two-rod structural attachment
- `TrapezeUserLocationsCommand` - Place trapeze hangers at user-marked detail line locations with two-rod structural attachment
- `TrapezeUnistrutCommand` - Place unistrut pipe trapeze hangers at auto-spaced intervals with channel extensions
- `TrapezeUnistrut21ACommand` - Place Unistrut 21A trapeze hangers with auto-calculated extensions, simplified variant
- `FormatHangerTicksCommand` - Format pipe hanger tick symbols to consistent direction (/ or \) accounting for pipe rotation
- `HangerSectionIDsCommand` - Populate Section_ID (Hydratec) with formatted rod length and type code for hanger tags
- `RingHangerSectionIDsCommand` - Adjustable-ring-hanger variant: subtracts a nominal-diameter-based ring takeout from Rod Length before writing Section_ID (Hydratec)
- `ChangeTypeCodeCommand` - Bulk-change Type Code (Hydratec) on selected hangers from a chosen From code (dropdown of codes in selection) to a typed To code
- `UniformRodLengthsCommand` - Sweep Rod Length on hangers of a chosen Type Code to a uniform target, only on hangers under a max-length cutoff (so longer rods on lower pipes are left alone)
- `StripSectionIDTypeCodeCommand` - For hangers matching a chosen Type Code, strip the prefix before the first '(' in Section_ID (Hydratec). E.g. "#11T(5)" becomes "(5)"
- `StripSectionIDHashesCommand` - Remove every '#' character from Section_ID (Hydratec) on all selected hangers. E.g. "#11T(5)" becomes "11T(5)"
- `RingSectionIDsHardwareCommand` - Ring takeout like Ring Section IDs, then adds 1.5" back for Type Codes starting with 01 or 02; writes type(length) with no leading #. E.g. 02D rod 4" becomes 02D(4)
- `MarkTypeForReviewCommand` - Flag hangers of a chosen Type Code with a tall magenta cylinder extending above + below the hanger, visible in plan and 3D (review/QA marker)
- `MarkFamilyInstancesCommand` - Coordination panel. Searchable family picker; places orange 12" spheres at every instance of the chosen family. Scope: active view or whole project. Separate Delete All Markers button (Place does not auto-clear)
- `SwapHydraCADHangersCommand` - Replace HydraCAD Adjustable Ring Hangers with SG -Pipe Hanger - Standard with parameter transfer
- `MatchHangerSizesCommand` - Resize selected hangers via parameter set + rod-length compensation, with orange review markers on resized + drifted hangers (kept as a backup for the delete+recreate ReplaceHangerSizes path)
- `ReplaceHangerSizesCommand` - Resize selected hangers by deleting + recreating each at the new size (preserves all writable parameters; sidesteps the family-level centerline-drift bug)
- `InspectElementParametersCommand` - Diagnostic: dumps every parameter on a selected element (instance + type + connectors) to a TaskDialog and clipboard for debugging family behavior
- `SyncHangersToPipesCommand` - Move hangers to closest pipe, rotate to match direction, set ring size and stocklist info
- `SyncHangersToRefPlaneCommand` - Calculate rod lengths from hangers to a named reference plane representing structural underside
- `SyncHangersRaybounceCommand` - Calculate rod lengths via raybounce to structural elements above including linked models, with per-category type codes
- `SyncHangersSurfaceCommand` - Calculate rod lengths via bounding box search and surface intersection to structural elements above, with framing top/bottom option
- `SyncTrapezeHangersCommand` - Sync trapeze hanger rod lengths, offsets, rotation, and pipe diameter to closest pipe and structural elements above
- `HangConcreteTeeCommand` - Place hangers on sides of concrete double tee stems at user-marked detail line locations with linked structural model
- `FlipTrapezeHangersCommand` - Rotate selected trapeze hangers 180° around Z-axis and swap Rod 1/Rod 2 Top Elevation and Offset parameter values

## Seismic
- `SeismicBracesCommand` - Auto-place lateral and/or longitudinal seismic braces on welded mains with NFPA spacing and rod length from linked structure

## Export
- `ExportTrimblePointsCommand` - Export hanger locations as Trimble-compatible CSV point files for field layout of inserts before concrete pours
- `ImportASPipesCommand` - Import pipe geometry from an AutoSPRINK CSV export and create Revit pipes (coordinates in inches)
- `PlaceTrimbleMarkersCommand` - Place Trimble symbol families at hanger (3/8" and 1/2") and seismic brace locations for field layout
- `ImportASSprinklersCommand` - Import sprinkler head locations from an AutoSPRINK CSV export and place Revit sprinkler family instances

## Coordination
- `ColorCodePipesCommand` - Color-code pipes in the active view by diameter (8 size buckets), type name (substring match), or reset all overrides

## Annotation
- `PipeElevationsCommand` - Calculate and write TOS/AFF elevation parameters on pipes and fittings with 4 reference methods including raybounce, slope classification
- `FlexDropLengthsCommand` - Insert flexible drop length tags on sprinkler heads with standard pipe lengths
- `FlexDropLengthsDalmatianCommand` - Auto-populate flex drop lengths from actual connected pipe lengths with Wet/Dry system thresholds and dynamic tag families
- `GraphicScaleBarsCommand` - Insert graphic scale bar annotations on sheets based on view scales
- `SleeveElevationsCommand` - Calculate AFF/BBD elevations on pipe sleeves from linked floor and deck geometry
- `PipeSleevesAtBeamsCommand` - Auto-place NFPA-sized pipe sleeves at pipe-beam intersections with linked structural model
- `PipeSleevesAtDecksCommand` - Auto-place NFPA-sized pipe sleeves at pipe-floor/roof intersections with wet area extension option
- `PipeSleevesAtWallsCommand` - Auto-place pipe sleeves at pipe-wall intersections with seismic/non-seismic NFPA sizing and wall type filtering
- `RoomTextNotesCommand` - Place stacked room name/number text notes from linked model rooms with crop region filtering
- `BeamPenetrationSymbolsCommand` - Place beam penetration annotation symbols at pipe-grid or pipe-detail line intersection points in the active view
- `SSBSymbolsCommand` - Place SSB hanger annotation symbols 1 ft from each end of selected pipe runs aligned to pipe direction
- `DeleteDuplicateTextCommand` - Delete duplicate text notes at the same location in the active view
- `ClearAnnotationsCommand` - Delete all generic annotation family instances from the active view

## ViewsAndSheets
- `CreateDependentViewsCommand` - Create dependent views from parent floor/ceiling plans with scope box assignment or blank copies
- `CreatePlanViewsCommand` - Create floor and/or ceiling plan views for selected levels with view templates and naming
- `RotateScopeBoxCommand` - Rotate a scope box to match the angle of a local or linked grid line
- `RemoveScopeBoxesCommand` - Delete selected scope boxes, or all scope boxes in the project if none are selected

## Setup
- `LoadFamiliesCommand` - Load .rfa families from a folder into the project, skipping already-loaded families
- `SetupGlobalParamsCommand` - Create all 86 configuration global parameters with defaults, fix legacy seismic Int64 types
- `ClearPipeElevationParamsCommand` - Remove pipe elevation shared parameters (TOS/AFF) from the project with confirmation
- `CopyLinkLevelsGridsCommand` - Copy levels and/or grids from a linked model with duplicate detection, grid type assignment, and pinning

## ModelCheck
- `SprinklerClearanceCheckCommand` - Check upright sprinklers for NFPA 3" clearance violations from pipes and hangers with annotation placement
- `DeflectorDistanceCheckCommand` - Measure upright deflector-to-structure distance via raybounce and check against NFPA 13 limits (unobstructed/obstructed/custom)
- `PipesTooShortCommand` - Flag pipes shorter than the minimum fabricable nipple length for their size and type (threaded vs welded)
- `HangerGapCheckCommand` - Flag selected hangers whose top-of-pipe to structure gap exceeds a threshold (per-Type-Code math, default 6"); appears on the Seismic panel

