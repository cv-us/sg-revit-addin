# AutoSync Hangers to Reference Plane

**Command:** `SyncHangersToRefPlaneCommand`
**Domain:** Hangers
**Ribbon:** SG Revit Addin > Hangers > Sync to Ref Plane

## Purpose

Calculates and writes rod lengths for pipe hangers by measuring the vertical distance from each hanger to a user-selected reference plane. The reference plane typically represents the underside of a structural slab or deck above the hangers. Also stamps the reference plane name in the hanger's Comments parameter for traceability.

## Workflow

1. User selects pipe hangers (pre-selection or pick prompt filtered to OST_PipeAccessory)
2. Selection is filtered to valid hanger families
3. Dialog shows hanger count and dropdown of all named reference planes in the project
4. For each hanger:
   a. Get the hanger's position on its host pipe
   b. Project that position vertically onto the reference plane
   c. If the hanger is below the plane, compute rod length = vertical distance
   d. Compare with existing Rod Length — skip if unchanged
5. Write "Rod Length" and "Comments" only on hangers that changed

## Dialog Options

| Setting | Description |
|---------|-------------|
| Reference Plane | Dropdown of all named reference planes in the project, sorted alphabetically |

## Hanger Family Filtering

Selected elements must have a family name containing any of:
- `"-Pipe Hanger"` (SSG standard hangers)
- `"-Basic Adjustable"` (basic adjustable hangers)
- `"Adjustable Ring Hanger"` (HydraCAD hangers)

All matching is case-insensitive.

## Hanger Position Calculation

The hanger's actual position is determined by:
1. **Primary:** `LocationPoint.Point` from the family instance (works for most hosted instances)
2. **Fallback:** If LocationPoint is not available, computes position from the host pipe:
   - Gets the host pipe's centerline (Line)
   - Reads the hanger's `"Distance off End"` parameter
   - Translates the pipe's start point along its direction by that offset

## Rod Length Calculation

For each hanger position:
1. **Project vertically onto the reference plane** using parametric ray-plane intersection:
   - Ray: `hangerPoint + t × ZAxis`
   - Plane equation: `(P - planeOrigin) · planeNormal = 0`
   - Solve for `t`
2. **Direction check:** If the hanger is **above** the reference plane (`hangerZ ≥ projectedZ`), the hanger is skipped (would produce zero or negative rod length)
3. **Rod length** = `projectedZ - hangerZ` (vertical distance in feet)
4. **Change detection:** Compare with existing Rod Length parameter (tolerance: ~1/32 inch). Only write if the value actually changed

## Parameters Written

| Parameter | Value | Notes |
|-----------|-------|-------|
| `Rod Length` | Vertical distance from hanger to reference plane (feet) | Only written when value changed |
| `Comments` | `"Reference Plane: {planeName}"` | Stamps which plane was used for traceability |

## Summary Dialog

After processing, a summary dialog shows:
- Total hangers selected
- Number of hangers re-synced (rod length changed)
- Number of hangers that didn't require synchronizing (unchanged)
- Number of hangers above the plane (ignored)
- Number of hangers that failed

## Notes

- The reference plane should represent the structural underside (bottom of slab/deck) for meaningful rod length values
- This command does NOT move or rotate hangers — it only updates Rod Length and Comments
- For moving hangers to pipes, use "Sync to Pipes" (SyncHangersToPipesCommand)
- Rod lengths are written in Revit internal units (feet) — no conversion needed
- The projection math handles both horizontal and sloped reference planes correctly
- Vertical reference planes (parallel to Z-axis) cannot be projected onto and are skipped

## See Also

- **[Choosing a Command](choosing-a-command.md)** — full comparison of all sync commands
- **Sync Raybounce** or **Sync Surface** — better choice when structure varies (beams, sloped roofs, multiple levels)
- **Sync to Pipes** — run first to position hangers on pipes before calculating rod lengths
