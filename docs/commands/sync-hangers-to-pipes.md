# AutoSync Hangers to Pipes

**Command:** `SyncHangersToPipesCommand`
**Domain:** Hangers
**Ribbon:** SG Revit Addin > Hangers > Sync to Pipes

## Purpose

Synchronizes existing pipe hanger positions, rotations, and parameters to their closest pipes. For each selected hanger, the command finds the nearest pipe, moves the hanger to the closest point on that pipe's centerline, rotates it to match the pipe direction, and updates the ring size and stocklist tracking parameters.

This command does **not** adjust rod lengths. Use "AutoSync Hangers to Structural Elements" for rod length updates.

## Workflow

1. User selects both pipes and hangers (pre-selection or pick prompt)
2. Elements are separated: family name contains "-Pipe Hanger" = hangers, OST_PipeCurves = pipes
3. Steep/vertical pipes are filtered out (>60° from horizontal)
4. For each hanger:
   a. Find the closest pipe by perpendicular distance to centerline
   b. Move hanger to the closest point on that pipe
   c. Rotate hanger to match pipe direction in plan view
   d. Set Nominal Diameter from pipe's Diameter parameter
   e. Set stocklist info = "CON1," + pipe element ID

## Selection

Both pipes and hangers are selected together in a single selection operation:

- **Pre-selection:** If pipe accessories and pipe curves are already selected (≥2 elements), they are used directly
- **Pick mode:** User is prompted to select pipes and hangers together, filtered to `OST_PipeAccessory` and `OST_PipeCurves` categories

### Element Separation
- **Hangers:** `FamilyInstance` elements whose family name contains `"-Pipe Hanger"` (case-insensitive)
- **Pipes:** Remaining elements in the `OST_PipeCurves` category

## Pipe Filtering

Two filters remove invalid pipes before matching:

### Filter 1: Vertical Pipes
Pipes whose `"Slope"` parameter is empty or null are excluded (indicates a vertical pipe).

### Filter 2: Steep Pipes (>60°)
For each remaining pipe:
1. Get the pipe's centerline direction vector
2. Project to XY plane: `(dirX, dirY, 0)`
3. Compute angle between the 3D direction and the flattened direction
4. Exclude pipes where this angle exceeds 60°

## Closest Pipe Matching

For each hanger:
1. Project the hanger's location point onto every valid pipe's centerline using `Curve.Project()`
2. The pipe with the shortest projection distance is selected as the match
3. The projection point becomes the hanger's new location

## Modifications Made

### 1. Location (Move)
The hanger's `LocationPoint.Point` is set to the closest point on the matched pipe's centerline.

### 2. Rotation
1. Get the matched pipe's direction vector
2. Project to XY plane: `atan2(dirY, dirX)` → degrees
3. Normalize to 0–360° range, round to 3 decimal places
4. Compute delta from hanger's current rotation
5. Apply rotation via `ElementTransformUtils.RotateElement` about the Z-axis

### 3. Nominal Diameter
The pipe's `"Diameter"` parameter value (in feet) is rounded to 3 decimal places and written to the hanger's `"Nominal Diameter"` parameter.

### 4. Stocklist Information
`"Additional Stocklist Information (Hydratec)"` is set to `"CON1," + pipeElementId` for Hydratec stocklist tracking.

## Parameters Summary

| Parameter | Read From | Written To | Notes |
|-----------|-----------|------------|-------|
| `Diameter` | Pipe | — | Source for ring size |
| `Nominal Diameter` | — | Hanger | Pipe diameter rounded to 3dp |
| `Additional Stocklist Information (Hydratec)` | — | Hanger | `"CON1," + pipeElementId` |
| `Slope` | Pipe | — | Used to filter vertical pipes |

## Notes

- All operations happen in a single transaction for full undo support
- The command modifies existing hangers — no elements are created or deleted
- Rod lengths are NOT adjusted by this command (use the structural sync command for that)
- Pre-selection is supported and preferred for large selections

## See Also

- **[Choosing a Command](choosing-a-command.md)** — full comparison of all sync commands
- **Sync Raybounce** or **Sync Surface** — run after this command to calculate rod lengths from structure above
- **Sync to Ref Plane** — simpler rod length calculation when structure is a flat slab at known elevation

