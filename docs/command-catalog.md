# Command Catalog

Master list of all SG Revit Addin commands.

## SprinklerLayout
- `LayoutCommand` - Lay out branch lines with sprinklers at VARIABLE spacings to fill an area (unlike fixed-spacing array tools). Numbered slots (1-6) hold line spacings, lettered slots (A-F) hold head spacings; the sequence strings (line 112112, head AABA) REPEAT/tile to span the picked rectangle. Three pick modes: **Fill area** (two corners), **Area + central main** (two corners + a main line — branches run perpendicular to the main as shallow Vs that slope down to it from both sides and tie in with a riser nipple + tee at each crossing, the main sloping toward its riser end; for draining dry/pre-action systems). Options: pipe/system/line size, level, start elevation, branch + main slope (in/10 ft), main + riser size, main elevation, selectable branch-outlet-on-main (GOL default) and riser-top tee (Firelock default) fittings forced via a temporary routing-preference rule, a **Branch tie-in** style (riser nipple above the main, or **side outlet** where the branch sits at the main's elevation and taps its side directly — 4-way outlet at interior crossings, tee at ends/two-mains — no nipple), lines-run direction (clickable arrows toggle); heads at outlets or on sprigs (common or fixed elevation); branch ends capped with the pipe type's routing-preference cap a settable distance past the last head. In main mode the cross-main is one continuous pipe (GOL taps, no cut) with a capped 6" high-end stub and an open riser end. **Two mains** (4-pt) mode places two parallel mains (primary + secondary/floater), each flat branch tying into BOTH via riser nipples — with a **tailback** (Firelock tee + short capped stub past each main) or an **elbow** into the riser (option); heads tiled between the mains, mains left open. Execute button reduced to **Place** with the pick guidance beside it; a clickable main image shows HIGH/LOW slope ends (click to flip) and reorients with the branch-direction toggle. All settings remembered. **Under development — needs field testing**

## PipeRouting
- `ShortenFlexPipesCommand` - Replace selected flex pipes with shortest-length connections between the same endpoints
- `TraceCadPipeCommand` - Trace the pipe in a linked/imported CAD (e.g. a coordination model round-tripped NWC -> FBX -> 3ds Max -> DWG) and build real Revit pipe from it. The pipe arrives as a Mesh of coarse triangulated tubes (fittings arrive separately as Solids, sitting within 0.8 ft of the run ends); each connected component is welded, its principal axis fitted as the centerline, and its median radial distance taken as the radius, then snapped to nominal steel OD. Dialog reports run count, total length and the fitted size histogram BEFORE placing. Options: pipe type/system/level, snap-to-nominal vs measured vs forced size, minimum run length, flatten-to-level. Pipes are placed but NOT connected - no fittings inserted. **Under development - needs field testing**
- `SprinklerDropCommand` - Place hard-pipe up-over-down drops to pendent heads ending in a REAL elbow at the drop base (a short stub — settable to 0 — forces a 90° turn so the BOM lists an elbow, not a union), then a flex hose from the elbow to the head. Free-pipe segments + explicit NewElbowFitting at every joint; plan-perpendicular tap so a sloped branch keeps the armover straight; flex built head-first with mandatory above-head + in-front-of-elbow guide vertices and an optional whip length. Options: pipe/flex sizes (default 1"), drop offset toward branch, no-stub checkbox. **Under development — needs field testing**
- `RelevelSprinklersCommand` - **(DISABLED — ribbon button hidden, under rework)** Move selected sprinkler heads to a chosen Level while keeping each head in its EXACT world location; sets FAMILY_LEVEL_PARAM then re-pins the captured world point so Revit recomputes Offset-from-Level = worldZ − newLevelElevation automatically. Verifies position held; skips + reports face/work-plane-hosted or read-only-level heads (never deletes). Level picker remembers last choice

## Hangers
- `PlaceHangersCommand` - Unified auto-placement: one dialog with a method dropdown (auto-spaced to decks, auto-spaced to parallel framing, downstream ends, or at structural steel). Settings remembered per method. **Replaces the four commands below on the ribbon** (their classes remain for the RunPlacement logic)
- `HangAtCADLinesCommand` - Place hangers where pipes cross linked CAD structural lines
- `HangAtStructuralCommand` - (merged into Place Hangers) Place hangers where pipes cross structural framing members
- `HangDownstreamCommand` - (merged into Place Hangers) Place hangers at downstream ends of threaded branchline pipes with raybounce rod length
- `HangTypicalSpacingCommand` - (merged into Place Hangers) Place hangers at typical spacing along straight pipe runs with raybounce rod length to decks
- `HangParallelStructuralCommand` - (merged into Place Hangers) Place hangers at typical spacing, attached to parallel structural framing with clamp angle and widemouth detection
- `HangUserLocationsCommand` - Place hangers at user-marked detail line locations with raybounce rod length to structure above
- `RaybounceEarlyCommand` - **STABLE** raybounce: rod lengths straight up to native structural elements (floors/roofs/framing) incl. linked models. Geometry-verified distances (fixes rods stretching under sloped linked decks) + a DirectShape triangle index for linked IFC
- `SyncHangersRaybounceCommand` - "Raybounce Dev" — **UNDER DEVELOPMENT**: same verified engine (`StructureRayScanner`) plus imported CAD (DWG/STEP) mesh detection, Generic Model/Mass (STEP-in-family) indexing, and a center-priority multi-ray fan
- `TrapezeHangCommand` - Place standard pipe trapeze hangers at auto-spaced intervals with two-rod structural attachment
- `TrapezeUserLocationsCommand` - Place trapeze hangers at user-marked detail line locations with two-rod structural attachment
- `TrapezeUnistrutCommand` - Place unistrut pipe trapeze hangers at auto-spaced intervals with channel extensions
- `TrapezeUnistrut21ACommand` - Place Unistrut 21A trapeze hangers with auto-calculated extensions, simplified variant
- `FormatHangerTicksCommand` - Format pipe hanger tick symbols to consistent direction (/ or \) accounting for pipe rotation
- `HangerSectionIDsCommand` - Populate Section_ID (Hydratec) with formatted rod length and type code for hanger tags
- `RingHangerSectionIDsCommand` - Adjustable-ring-hanger variant: subtracts a nominal-diameter-based ring takeout from Rod Length before writing Section_ID (Hydratec)
- `ChangeTypeCodeCommand` - Bulk-change Type Code (Hydratec) on selected hangers from a chosen From code (dropdown of codes in selection) to a typed To code
- `UniformRodLengthsCommand` - Sweep Rod Length on hangers of a chosen Type Code to a uniform target, only on hangers under a max-length cutoff (so longer rods on lower pipes are left alone)
- `RoundRodLengthsCommand` - Round each selected hanger's Rod Length UP to the nearest half inch (never down). Rods already on a full/half inch are left alone; optionally keeps Y Grip in sync
- `StripSectionIDTypeCodeCommand` - For hangers matching a chosen Type Code, strip the prefix before the first '(' in Section_ID (Hydratec). E.g. "#11T(5)" becomes "(5)"
- `StripSectionIDHashesCommand` - Remove every '#' character from Section_ID (Hydratec) on all selected hangers. E.g. "#11T(5)" becomes "11T(5)"
- `RingSectionIDsHardwareCommand` - Ring takeout like Ring Section IDs, then adds 1.5" back for Type Codes starting with 01 or 02; writes type(length) with no leading #. E.g. 02D rod 4" becomes 02D(4)
- `MarkTypeForReviewCommand` - Flag hangers of a chosen Type Code with a tall magenta cylinder extending above + below the hanger, visible in plan and 3D (review/QA marker)
- `MarkFamilyInstancesCommand` - Coordination panel. Searchable family picker + workset checklist; places orange 12" spheres at every instance of the chosen family. Scope: active view or whole project. Workset filter updates the family list dynamically. Separate Delete All Markers button (Place does not auto-clear)
- `LegendTransferCommand` - Views & Sheets panel. WPF + MVVM dialog. Copies Legend views from one open document into another. Searchable + checkable legend list, source/target dropdowns, progress bar, skips existing names. Wraps everything in a single-undo TransactionGroup. Requires the target to already have at least one Legend view (Revit API can't create Legends from scratch — uses View.Duplicate as a seed)
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

## Hydraulics
- `FluidDeliveryCommand` - Estimate WATER-DELIVERY TIME for a dry / (double-interlock) preaction system. Pick the source valve; draw a region (rectangle/polygon) to flag the flowing heads `Flowing (Hydratec)=1` (or use existing flowing); Dijkstra-traces the connector graph from source to the most-remote flowing head and runs a two-phase air-displacement model (Heskestad-Kung): air-trip blowdown `≈1.12·V/(K·N)` for dry-pipe differential (or detection latency for electric preaction) + water transit where each segment's fill is the min of air-vent-limited (`A_eff=0.0263·K·N` orifice venting) and supply-limited (Hazen-Williams friction + lift + air back-pressure). Path volume drives the time, whole-system volume is the code gate (`Volume Hydratec` param or computed from `Inside Diameter` bore). Hazard-class picker sets the NFPA 13 Table 8.2.3.6.1 target (Light 60/Ord 50/Extra 45/HP 40/Dwelling 15 s). Clean PDF export via a zero-dependency writer. **Documented engineering estimate (~±25-40%), NOT a listed calc / not for code compliance — verify with a listed program or trip test. Under development — needs field testing**

## Export
- `ExportTrimblePointsCommand` - Export hanger locations as Trimble-compatible CSV point files for field layout of inserts before concrete pours
- `ImportASPipesCommand` - Import pipe geometry from an AutoSPRINK CSV export and create Revit pipes (coordinates in inches)
- `PlaceTrimbleMarkersCommand` - Place Trimble symbol families at hanger (3/8" and 1/2") and seismic brace locations for field layout
- `ImportASSprinklersCommand` - Import sprinkler head locations from an AutoSPRINK CSV export and place Revit sprinkler family instances

## Coordination
- `ColorCodePipesCommand` - Color-code pipes in the active view by diameter (8 size buckets), type name (substring match), or reset all overrides
- `ColorizeByWorksetCommand` - Colorize pipes & fittings by construction status carried on their workset (Existing/Demo/Modify/New), so it EXPORTS to NWC. Pipes get a colored per-status duplicate pipe type (e.g. "Welded - New", preserving the system) swapped via ChangeTypeId; flex gets a colored FlexPipeType (type material param); fittings/sprinklers/accessories get an instance/type material. (Face paint does NOT export — that was the v1 dead end.) Workset→status grid w/ keyword auto-suggest (remembered per workset), color pickers, scope, preview, view-override (Revit-only) option, and Clear All Coloring (pipes revert by type-name suffix). Summary names the families it colored (✓) and couldn't (✗, By-Category solids). Tip: export then close-without-saving to keep colored types out of the fab model
- `InspectMaterialsCommand` - Read-only diagnostic. Pick elements to dump their type/instance material parameters and the ACTUAL material(s) on their geometry faces (what Navisworks reads on NWC export). Copyable text report. Used to find why a flex pipe / By-Category fitting won't colorize
- `InspectCadGeometryCommand` - Read-only diagnostic for tracing a linked CAD / coordination model into real pipe. Reports whether the link arrives as solids with analytic `CylindricalFace` (exact axis + radius + slope, no fitting needed) or as triangle mesh, how it's segmented (pre-separated pipes vs one merged blob), every cylinder radius matched to nominal steel OD in mils, per-run slope in in/10 ft, surviving layer names, and the float32 precision risk from the model's distance to the origin. Copyable text report with a verdict block. An NWC itself is unreadable by the Revit API — round-trip it NWC → FBX → 3ds Max → DWG first

## Annotation
- `PipeElevationsCommand` - Calculate and write TOS/AFF elevation parameters on pipes and fittings with 4 reference methods including raybounce, slope classification
- `FlexDropLengthsCommand` - Insert flexible drop length tags on sprinkler heads with standard pipe lengths
- `FlexDropLengthsAutoCommand` - Auto-size flex drop tags by reading each sprinkler's connected flex pipe length against Wet/Dry threshold tables; flags pipes exceeding the system max. Companion to FlexDropLengthsCommand ("Flex Drops Set")
- `SprigTagsCommand` - Re-types the Hydratec vertical-pipe direction tag (UP/DN/RN) to a SPRIG tag on small sprigs. A candidate's host pipe must be vertical, ≤ a chosen size (default 1"), with a sprinkler reachable at its UPPER end (geometric sprig-vs-drop test that walks through a reducing coupling/nipple). Drops (sprinkler at the bottom) and riser nipples (no sprinkler) are left alone. Optional "only this tag family" guard. Works on selected tags/pipes, else scans the active view
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
- `CreatePlanViewsCommand` - Create floor and/or ceiling plan views with view templates, a Sub-Discipline, and naming. Two source modes: from this model's selected levels, OR replicate selected plan views from another open/linked model (recreated on the matching level with your template + scope-box-by-name + sub-discipline)
- `RotateScopeBoxCommand` - Rotate a scope box to match the angle of a local or linked grid line
- `RemoveScopeBoxesCommand` - Delete selected scope boxes, or all scope boxes in the project if none are selected

## Setup
- `LoadFamiliesCommand` - Load .rfa families from a folder into the project, skipping already-loaded families
- `SetupGlobalParamsCommand` - Create all 86 configuration global parameters with defaults, fix legacy seismic Int64 types
- `ClearPipeElevationParamsCommand` - Remove pipe elevation shared parameters (TOS/AFF) from the project with confirmation
- `CopyLinkLevelsGridsCommand` - Copy levels and/or grids from a linked model with duplicate detection, grid type assignment, and pinning. Optional "Set up Copy/Monitor instead" checkbox skips the recreate-import and launches Revit's native Copy/Monitor tool (the API can't create monitor relationships)

## ModelCheck
- `SprinklerClearanceCheckCommand` - Check upright sprinklers for NFPA 3" clearance violations from pipes and hangers with annotation placement
- `DeflectorDistanceCheckCommand` - Measure upright deflector-to-structure distance via raybounce and check against NFPA 13 limits (unobstructed/obstructed/custom)
- `PipesTooShortCommand` - Flag pipes shorter than the minimum fabricable nipple length for their size and type (threaded vs welded)
- `HangerGapCheckCommand` - Flag selected hangers whose top-of-pipe to structure gap exceeds a threshold (per-Type-Code math, default 6"); appears on the Seismic panel

## Modify (Modify-tab SG panel)
Injected onto Revit's built-in Modify tab via AdWindows; these buttons fire outside the API context, so document changes route through `DeferredActionHandler` (an ExternalEvent). The panel holds Tag Pipes, Pretty Sprinklers, and two placeholder slots.
- `TagPipesCommand` - Place pipe length/stocklist tags (HydraCAD-style) using your loaded tag families. SG-blue custom-titlebar dialog: 4 tag types each with a family dropdown, User vs System-Walker selection, drops handling, and options (Homogenize, Transparent -T variant, Reset Take-Out/Cut arithmetic on HydraCAD params, first-pass anti-overlap Cleanup). Remembers all settings
- `PrettySprinklersCommand` - Place opaque `HeadN` head-symbol overlays (category Sprinkler Tags) coincident with each selected sprinkler, chosen from the type's `Symbol - HeadN` / `HeadSymbol` params. Run with nothing selected to remove all overlays in the view
- `DeferredActionHandler` - Generic IExternalEventHandler that lets the AdWindows-injected Modify-tab buttons run document-modifying work in a valid API context
- `ChromeDpiAwareForm` - DpiAwareForm variant with a borderless SG-blue (#085990) custom title bar (white title + close, draggable), used by the Tag Pipes dialog

