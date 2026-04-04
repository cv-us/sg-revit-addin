# AutoSwap HydraCAD Hangers

**Command:** `AutoSwapHydraCADHangersCommand`
**Domain:** Hangers
**Ribbon:** SSG FP Suite > Hangers > Swap HydraCAD
**Migrated from:** `AutoSwap - HydraCAD Hangers.dyn`

## Purpose

Replaces HydraCAD pipe hanger family instances ("Adjustable Ring Hanger") with Shambaugh "-Pipe Hanger - Standard" family instances. Transfers all relevant parameters, computes correct position on the nearest pipe, sets rotation to match pipe direction, and adjusts rod length for any elevation difference between the old and new placement points.

## Workflow

1. User selects pipe accessories (pre-selection or pick prompt filtered to OST_PipeAccessory)
2. Selection is filtered to HydraCAD hangers (family name contains "Adjustable Ring Hanger")
3. Dialog shows count and delete option
4. For each hanger:
   a. Extract geometry midpoint from the hanger's line geometry
   b. Find the nearest intersecting pipe in the active view (bounding box search ±5 ft vertically)
   c. Compute closest point on the pipe centerline to the hanger
   d. Compute rotation angle from pipe direction (projected to XY plane)
   e. Read parameters from the HydraCAD hanger
5. Create new "-Pipe Hanger - Standard" instances with transferred parameters
6. Optionally delete original HydraCAD hangers

## Dialog Options

| Setting | Description |
|---------|-------------|
| Delete originals | If checked (default), deletes the original HydraCAD hangers after creating replacements |

## Parameters Transferred

| From HydraCAD | To New Hanger | Notes |
|---------------|---------------|-------|
| `Diameter` | `Nominal Diameter` | Rounded to 3 decimal places |
| `Rod Length` | `Rod Length` | Adjusted for elevation difference (see below) |
| `Type Code (Hydratec)` | `Type Code (Hydratec)` | Direct transfer |
| `HCAD-System` | `HCAD-System` | Direct transfer |
| — | `Additional Stocklist Information (Hydratec)` | Set to `"CON1," + pipeElementId` |
| — | `Elevation from Level` | Computed: pipe closest point Z - level elevation |

## Rod Length Adjustment

The new hanger is placed at the closest point on the pipe centerline, which may differ in Z from the original HydraCAD hanger's midpoint. The rod length is adjusted by this delta:

```
elevationAdjustment = originalMidPoint.Z - pipeClosestPoint.Z
adjustedRodLength = originalRodLength + elevationAdjustment
```

If the new placement is lower than the original, the rod length increases to compensate.

## Rotation Calculation

1. Get the pipe's centerline direction vector
2. Project to XY plane (ignore Z component)
3. Compute angle from X-axis: `atan2(dirY, dirX)` converted to degrees
4. Normalize to 0–360° range
5. Round to 2 decimal places
6. Apply rotation about Z-axis at the hanger placement point

## Pipe Finding Algorithm

For each hanger midpoint:
1. Create a search bounding box: ±0.5 ft horizontal, ±5 ft vertical from the hanger point
2. Check all pipes in the active view for bounding box overlap
3. Project the hanger point onto each overlapping pipe's centerline
4. Select the pipe with the shortest projection distance
5. Use the projected point as the new hanger placement location

## HydraCAD Identification

Elements are identified as HydraCAD hangers by checking if the family name contains `"Adjustable Ring Hanger"` (case-insensitive). The selection is also pre-filtered to the `OST_PipeAccessory` category.

## Replacement Family

The command looks for a family named exactly `"-Pipe Hanger - Standard"` in the project. The first available type of this family is used for all replacement instances. The family must be loaded before running the command.

## Not Adjusted

As noted in the original Dynamo script:
- **Hanger Types** are not mapped between HydraCAD and Shambaugh families
- **Top of Hanger** values are not adjusted

## Notes

- The replacement family type is activated automatically if not already active
- Hangers where no intersecting pipe is found within the search range are skipped
- The command operates in a single transaction for full undo support
- Pre-selection of pipe accessories is supported — if hangers are already selected, no pick prompt is shown
