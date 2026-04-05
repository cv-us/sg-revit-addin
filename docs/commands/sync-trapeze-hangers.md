# AutoSync Trapeze Hangers

**Command:** `SyncTrapezeHangersCommand`
**Domain:** Hangers
**Ribbon:** SSG FP Suite > Hangers > Sync Trapeze

## Purpose

Synchronizes trapeze hanger parameters to the closest pipe and the structural elements directly above. For each trapeze hanger, the command determines pipe rotation, calculates two rod positions perpendicular to the pipe, raybounces from each rod position to find structural elements, and writes rod lengths, offsets, elevations, pipe diameter, and stocklist info.

## Workflow

1. User selects trapeze hangers (pre-selection or pick prompt)
2. Dialog collects minimum clearance distance and rod position preference
3. For each hanger:
   a. Find the closest pipe (by Curve.Project distance)
   b. Compute rotation angle from pipe direction
   c. Compute two rod positions offset perpendicular to pipe direction
   d. Shoot rays upward from each rod position to find structural elements
   e. Calculate rod top elevations based on structural hit minus clearance
   f. Write all parameters (rotation, rod elevations, offsets, diameter, stocklist info)
4. Summary dialog reports results and missed hangers

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| Minimum Clearance (inches) | 7.0 | Minimum vertical distance from structural underside to top of pipe |
| Rod Position | Closest side | Whether rods land on the closest side or middle of structural elements |

## Hanger Identification

Trapeze hangers are identified by family name containing `"-Pipe Trapeze"`.

## Pipe Matching

- All `OST_PipeCurves` in the model are candidates
- Uses `Curve.Project()` to find the closest point on each pipe curve to the hanger location
- Pipe direction is extracted from the pipe's `LocationCurve` for rotation calculation

## Rod Position Calculation

Each trapeze has two rods, positioned perpendicular to the pipe:
- **Rod spread:** `(pipeDiameter + 8") / 2` from the hanger center
- **Perpendicular vectors:** Computed from pipe angle ±90°
- **Mirror handling:** If `FamilyInstance.Mirrored` is true, rod directions and offsets are inverted

## RayBounce (Structural Detection)

Uses `ReferenceIntersector` with:
- **Origin:** Each rod position (not the hanger center)
- **Direction:** `XYZ.BasisZ` (straight up)
- **Target:** `FindReferenceTarget.Face`
- **Linked models:** `FindReferencesInRevitLinks = true`
- **View:** Dedicated "3D-Raybounce" isometric view (auto-created if missing)

### Target Categories
- `OST_Floors` — Floor slabs
- `OST_StructuralFraming` — W-flange beams, bar joists

### Structural Family Filtering
Only structural framing with family names matching these patterns:
- `"W-Wide Flange"`
- `"Bar Joist"`

## Rod Top Elevation Calculation

For each rod:
1. Ray hits structural element at some Z elevation
2. Rod top elevation = structural hit Z − minimum clearance − 0.5" adjustment
3. If "closest side" mode: uses the nearest face of the structural element
4. If "middle" mode: uses the center of the structural element

## Parameters Written

| Parameter | Value | Notes |
|-----------|-------|-------|
| `Supported Pipe Rotation Angle` | Pipe direction angle (degrees, 0-360) | From `atan2(dir.Y, dir.X)` |
| `Rod 1 Top Elevation` | Structural hit Z for rod 1 (feet) | Adjusted for clearance |
| `Rod 2 Top Elevation` | Structural hit Z for rod 2 (feet) | Adjusted for clearance |
| `Rod 1 Offset` | Horizontal distance from hanger center to rod 1 (feet) | Half of rod spread |
| `Rod 2 Offset` | Horizontal distance from hanger center to rod 2 (feet) | Half of rod spread |
| `Diameter` | Pipe outer diameter (feet) | From closest pipe |
| `Nominal Diameter` | Pipe nominal diameter (inches, 3 decimal places) | From closest pipe |
| `Supported Pipe Elevation` | Pipe centerline Z elevation (feet) | From closest point on pipe |
| `Stocklist Info` | `"CON1,{pipeElementId}"` | Links hanger to its pipe |
| `Comments` | Structural category name | e.g., "Floors", "Structural Framing" |

## Mirror Handling

For mirrored trapeze instances (`FamilyInstance.Mirrored == true`):
- Rod 1 and Rod 2 perpendicular directions are swapped
- Offset signs are inverted
- This ensures the physical rod positions match the family's mirrored geometry

## Missed Hangers

Hangers where either rod fails to hit structure are:
- Counted separately
- Highlighted in the Revit selection after command completes
- Reported in the summary dialog

## Summary Dialog

Reports:
- Total trapeze hangers processed
- Number successfully synced
- Number that missed (no structure above one or both rods)

## Comparison with Single-Rod Structural Sync

| Feature | This Command (Trapeze) | Single-Rod Structural Sync |
|---------|----------------------|---------------------------|
| Rod count | 2 per hanger | 1 per hanger |
| Ray origin | Two positions perpendicular to pipe | Hanger center point |
| Rotation | Writes pipe rotation angle | Not applicable |
| Pipe matching | Auto-finds closest pipe | Not applicable |
| Offsets | Writes Rod 1/2 offsets | Not applicable |
| Best for | Trapeze hangers with dual rods | Standard ring hangers |

## Notes

- All lengths are written in Revit internal units (feet)
- The "3D-Raybounce" view is shared with `SyncHangersRaybounceCommand`
- All logic is consolidated into a single `ProcessHanger()` method
