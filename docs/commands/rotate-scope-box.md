# Rotate Scope Box

**Command:** `RotateScopeBoxCommand`
**Domain:** ViewsAndSheets
**Ribbon:** SSG FP Suite > Views & Sheets > Rotate Scope Box

## Purpose

Rotates a scope box to match the angle of a grid line. Supports local grids, linked model grids, or a manual angle entry. This is useful when fire protection plans need to be oriented to match a building grid that isn't aligned with the project's cardinal directions (e.g., angled buildings, parking structures).

## Workflow

1. User selects a scope box (pre-selection or pick prompt)
2. Dialog: choose angle source — local grid, linked grid, or manual angle
3. Command computes the current scope box orientation from its geometry
4. Computes the target angle from the selected grid line direction
5. Rotates the scope box by the delta angle around its center Z-axis

## Dialog Options

| Setting | Description |
|---------|-------------|
| Match local grid | Select a grid from the host model (dropdown) |
| Match linked grid | Pick a grid element from a linked model after clicking OK |
| Manual angle | Enter a rotation angle in degrees |

## Scope Box Angle Detection

The current scope box orientation is determined by:
1. Extracting geometry lines from the scope box element
2. Filtering to horizontal lines only (direction Z ≈ 0)
3. Sorting by Z elevation, taking the bottom face edges
4. Using the longest bottom edge's direction as the orientation
5. Computing `atan2(dir.Y, dir.X)` to get the angle from X-axis

The algorithm:
1. Extract geometry lines, filter to horizontal (direction Z = 0)
2. Sort by Z, take bottom face edges
3. Use longest bottom edge direction
4. Compute `atan2(dir.Y, dir.X)` for the angle from X-axis

## Grid Angle Detection

### Local Grids
- `Grid.Curve` → get endpoints → flatten to XY → `atan2(dir.Y, dir.X)`

### Linked Grids
- User picks via `ObjectType.LinkedElement`
- `RevitLinkInstance.GetTotalTransform()` transforms grid endpoints to host coordinates
- Same angle calculation as local grids

## Rotation

- **Axis:** Vertical line through the scope box bounding box center
- **Method:** `ElementTransformUtils.RotateElement(doc, scopeBox.Id, axis, deltaRadians)`
- **Delta:** Target angle − current angle, normalized to ±180°
- **Skip:** If delta < 0.001°, reports "already aligned" without modifying

## Summary Dialog

Reports:
- Previous scope box angle
- Target angle (from grid or manual)
- Rotation applied (delta)

## Notes

- Scope box category is `OST_VolumeOfInterest`
- Grid angles are computed in the XY plane only (Z component ignored)
- Linked grid coordinates are properly transformed using the link instance transform
- Delta angle is normalized to +/-180 degrees to ensure the shortest rotation path
