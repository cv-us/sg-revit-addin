# Icon Map — SG Revit Addin

All icons live in `src/Shared/UI/Resources/icons/` and are embedded into the DLL at build time.

Revit uses two sizes per button:
- **32x32** — large ribbon button (primary display)
- **16x16** — small ribbon button / dropdown

Format: PNG with transparent background. Replace any file and rebuild to update.

---

## Current Icons (shared placeholders per panel)

| File | Size | Used By |
|------|------|---------|
| sprinkler-layout-32.png | 32x32 | PlaceSprinklers |
| sprinkler-layout-16.png | 16x16 | PlaceSprinklers |
| pipe-routing-32.png | 32x32 | RouteBranchlines, ShortenFlexPipes |
| pipe-routing-16.png | 16x16 | RouteBranchlines, ShortenFlexPipes |
| hangers-32.png | 32x32 | HangCommand, HangerSectionIDs, SwapHydraCAD, SyncHangersToPipes, SyncHangersToRefPlane, SyncHangersToStructural, SyncStructSurface, SyncTrapeze, HangConcreteTee, FlipTrapeze |
| hangers-16.png | 16x16 | (same as above) |
| hang-cad-32.png | 32x32 | HangAtCADLines |
| hang-cad-16.png | 16x16 | HangAtCADLines |
| hang-struct-32.png | 32x32 | HangAtStructural |
| hang-struct-16.png | 16x16 | HangAtStructural |
| hang-downstream-32.png | 32x32 | HangDownstream |
| hang-downstream-16.png | 16x16 | HangDownstream |
| hang-spacing-32.png | 32x32 | HangTypicalSpacing |
| hang-spacing-16.png | 16x16 | HangTypicalSpacing |
| hang-parallel-32.png | 32x32 | HangParallelStructural |
| hang-parallel-16.png | 16x16 | HangParallelStructural |
| hang-userloc-32.png | 32x32 | HangUserLocations |
| hang-userloc-16.png | 16x16 | HangUserLocations |
| hang-trapeze-32.png | 32x32 | TrapezeHang |
| hang-trapeze-16.png | 16x16 | TrapezeHang |
| format-ticks-32.png | 32x32 | FormatHangerTicks |
| format-ticks-16.png | 16x16 | FormatHangerTicks |
| hydraulics-32.png | 32x32 | HydraulicCalc |
| hydraulics-16.png | 16x16 | HydraulicCalc |
| fabrication-32.png | 32x32 | PipeCutList |
| fabrication-16.png | 16x16 | PipeCutList |
| coordination-32.png | 32x32 | ColorCodePipes |
| coordination-16.png | 16x16 | ColorCodePipes |
| annotation-32.png | 32x32 | PipeElevations, FlexDropLengths, FlexDropDalmatian, GraphicScaleBars, SleeveElevations, SleevesAtBeams, SleevesAtDecks, SleevesAtWalls, RoomTextNotes, BeamPenetrations, SSBSymbols, DeleteDuplicateText, ClearAnnotations |
| annotation-16.png | 16x16 | (same as above) |
| views-32.png | 32x32 | DuplicateViews, CreatePlanViews, CreateDependentViews, RotateScopeBox, RemoveScopeBoxes |
| views-16.png | 16x16 | (same as above) |
| setup-32.png | 32x32 | LoadFamilies, CopyLinkLevelsGrids, SetupGlobalParams, ClearPipeElevationParams, ExportTrimblePoints, PlaceTrimbleMarkers, ImportASPipes, ImportASSprinklers |
| setup-16.png | 16x16 | (same as above) |
| modelcheck-32.png | 32x32 | SprinklerClearanceCheck, DeflectorDistanceCheck, PipesTooShort |
| modelcheck-16.png | 16x16 | (same as above) |

---

## How to add per-command icons

To give each command a unique icon:

1. Create a 32x32 and 16x16 PNG for the command
2. Save to this folder with a descriptive name, e.g.:
   - `color-pipes-32.png` / `color-pipes-16.png`
   - `trimble-markers-32.png` / `trimble-markers-16.png`
3. Update the icon filenames in both `src/SgRevit24/App.cs` and `src/SgRevit25/App.cs`
4. Rebuild: `dotnet build src/SgRevit24/SgRevit24.csproj -c Release` and SgRevit25
5. Redeploy: `powershell -File tools/deploy-addin.ps1 -RevitVersion {year}`

The .csproj automatically embeds all PNGs from this folder via:
```xml
<EmbeddedResource Include="..\Shared\UI\Resources\icons\*.png" LinkBase="Icons" />
```

No .csproj edits needed — just drop PNGs here and rebuild.

---

## Recommended unique icon names (for when you're ready)

### Hangers panel (currently sharing hangers-32/16)
- `section-ids-32.png` / `section-ids-16.png` — HangerSectionIDs
- `swap-hydracad-32.png` / `swap-hydracad-16.png` — SwapHydraCAD
- `sync-pipes-32.png` / `sync-pipes-16.png` — SyncHangersToPipes
- `sync-refplane-32.png` / `sync-refplane-16.png` — SyncHangersToRefPlane
- `sync-raybounce-32.png` / `sync-raybounce-16.png` — SyncHangersToStructural
- `sync-surface-32.png` / `sync-surface-16.png` — SyncStructSurface
- `sync-trapeze-32.png` / `sync-trapeze-16.png` — SyncTrapeze
- `hang-tee-32.png` / `hang-tee-16.png` — HangConcreteTee
- `flip-trapeze-32.png` / `flip-trapeze-16.png` — FlipTrapeze
- `trapeze-userloc-32.png` / `trapeze-userloc-16.png` — TrapezeUserLocations
- `trapeze-unistrut-32.png` / `trapeze-unistrut-16.png` — TrapezeUnistrut
- `trapeze-uni21a-32.png` / `trapeze-uni21a-16.png` — TrapezeUnistrut21A

### Annotation panel (currently sharing annotation-32/16)
- `pipe-elevations-32.png` / `pipe-elevations-16.png` — PipeElevations
- `flex-drop-32.png` / `flex-drop-16.png` — FlexDropLengths
- `flex-dalmatian-32.png` / `flex-dalmatian-16.png` — FlexDropDalmatian
- `scale-bars-32.png` / `scale-bars-16.png` — GraphicScaleBars
- `sleeve-elevations-32.png` / `sleeve-elevations-16.png` — SleeveElevations
- `sleeves-beams-32.png` / `sleeves-beams-16.png` — SleevesAtBeams
- `sleeves-decks-32.png` / `sleeves-decks-16.png` — SleevesAtDecks
- `sleeves-walls-32.png` / `sleeves-walls-16.png` — SleevesAtWalls
- `room-text-32.png` / `room-text-16.png` — RoomTextNotes
- `beam-penetration-32.png` / `beam-penetration-16.png` — BeamPenetrations
- `ssb-symbols-32.png` / `ssb-symbols-16.png` — SSBSymbols
- `delete-dupe-text-32.png` / `delete-dupe-text-16.png` — DeleteDuplicateText
- `clear-annotations-32.png` / `clear-annotations-16.png` — ClearAnnotations
- `seismic-braces-32.png` / `seismic-braces-16.png` — SeismicBraces

### Export panel (currently sharing setup-32/16)
- `trimble-points-32.png` / `trimble-points-16.png` — ExportTrimblePoints
- `trimble-markers-32.png` / `trimble-markers-16.png` — PlaceTrimbleMarkers
- `import-pipes-32.png` / `import-pipes-16.png` — ImportASPipes
- `import-sprinklers-32.png` / `import-sprinklers-16.png` — ImportASSprinklers

### Views panel (currently sharing views-32/16)
- `duplicate-views-32.png` / `duplicate-views-16.png` — DuplicateViews
- `plan-views-32.png` / `plan-views-16.png` — CreatePlanViews
- `dependent-views-32.png` / `dependent-views-16.png` — CreateDependentViews
- `rotate-scopebox-32.png` / `rotate-scopebox-16.png` — RotateScopeBox
- `remove-scopebox-32.png` / `remove-scopebox-16.png` — RemoveScopeBoxes

### Setup panel (currently sharing setup-32/16)
- `load-families-32.png` / `load-families-16.png` — LoadFamilies
- `copy-levels-32.png` / `copy-levels-16.png` — CopyLinkLevelsGrids
- `global-params-32.png` / `global-params-16.png` — SetupGlobalParams
- `clear-params-32.png` / `clear-params-16.png` — ClearPipeElevationParams

### Model Check panel (currently sharing modelcheck-32/16)
- `sprinkler-clearance-32.png` / `sprinkler-clearance-16.png` — SprinklerClearanceCheck
- `deflector-distance-32.png` / `deflector-distance-16.png` — DeflectorDistanceCheck
- `pipes-too-short-32.png` / `pipes-too-short-16.png` — PipesTooShort

### Coordination panel
- `color-pipes-32.png` / `color-pipes-16.png` — ColorCodePipes

