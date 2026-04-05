# Auto Hang — Concrete Tee Stems (User Locations)

**Command:** `HangConcreteTeeCommand`
**Domain:** Hangers
**Ribbon:** SSG FP Suite > Hangers > Hang Tee Stems

## Purpose

Places pipe hangers on the **sides of concrete double tee stems** at user-specified locations. The user draws detail lines across pipe runs, parallel to and near the tee stem locations. The command finds the closest double tee stem to each detail line × pipe intersection, then places a hanger at the pipe with rod length calculated to reach the stem face.

This is a specialized command for buildings with precast concrete double-tee structural members, where hangers must attach to the vertical stems rather than the underside of the deck above.

## Workflow

1. User draws detail lines in plan view across pipe runs, near the tee stem locations where hangers are desired
2. User selects BOTH the pipes AND the detail lines (single selection, then Finish)
3. Dialog collects: pipe filter, hanger family/type, stem offset, anchor distance, linked model keyword
4. Command finds the linked structural model containing double tees
5. Extracts vertical stem surfaces from double tee elements
6. For each detail line × pipe intersection:
   a. Finds the closest stem surface
   b. Calculates rod length as horizontal distance from pipe to stem face + offset
7. Places hangers at intersection points, rotated to pipe direction
8. Writes parameters: diameter, rod length, elevation, type code, stocklist info

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| Pipe type filter | ALL Pipes | Filter to specific pipe types or include all |
| Hanger family | (first with "-Pipe Hanger") | Pipe accessory family to place |
| Type code | "01" | Type Code (Hydratec) value |
| Rod offset from stem (in) | 0.5 | Gap between rod and stem face |
| Anchor above stem bottom (in) | 4.0 | How far above the bottom of the stem the anchor point sits |
| Linked model keyword | "DOUBLE_TEE" | Keyword to identify the structural link containing double tees |

## Linked Model — Double Tee Detection

The command searches all loaded Revit link instances for:
1. **Link file name** containing the keyword (default "DOUBLE_TEE", case-insensitive)
2. **Structural framing elements** within that link whose family name matches any of:
   - `"DOUBLE_TEE"`, `"DOUBLE TEE"`, `"DBL_TEE"`, `"DBL TEE"`

## Stem Surface Extraction

For each double tee element, the command:
1. Retrieves the element's `Solid` geometry at Fine detail level
2. Iterates all faces, keeping only **vertical faces** (normal Z component < 0.1)
3. These vertical faces are the tee stems
5. All geometry is transformed from link coordinates to host coordinates via `RevitLinkInstance.GetTotalTransform()`

### "Warped" Stems

Double tees in parking structures often have non-planar ("warped") stems — the stems follow the slope of the driving surface. The command handles this by:
- Working with actual face geometry rather than assuming planar stems
- Sampling two points (20% and 80%) along the face to create a best-fit center line
- Using closest-point projection rather than simple plane intersection

## Detail Line × Pipe Intersections

1. Each detail line and pipe is flattened to 2D (XY plane)
2. Parametric line-line intersection test finds crossing points
3. The intersection is projected back onto the pipe curve to get the correct Z elevation
4. Small tolerance (0.01) allows intersections near pipe endpoints

## Stem Matching

For each pipe intersection point:
1. Search all stem surfaces within 10' horizontal distance
2. Filter by Z overlap (stem must be at or above pipe elevation ± 5')
3. Select the closest stem by horizontal distance to its center line
4. Compute the closest point on the stem center line

## Rod Length Calculation

Rod length = horizontal distance from pipe center to closest point on stem center line + rod offset from stem face

This represents the horizontal rod extending from the hanger (at the pipe) outward to the anchor point on the stem.

## Hanger Placement

- **Location:** At the pipe intersection point (the hanger sits on the pipe)
- **Rotation:** Aligned to pipe direction via `Math.Atan2(dir.Y, dir.X)` using `ElementTransformUtils.RotateElement`
- **Level:** From the pipe's "Reference Level" parameter

## Parameters Written

| Parameter | Value | Notes |
|-----------|-------|-------|
| `Nominal Diameter` | Pipe outer diameter (feet) | From closest pipe |
| `Rod Length` | Horizontal distance to stem + offset (feet) | Rounded to 4 decimal places |
| `Elevation from Level` | Hanger Z - level elevation (feet) | |
| `Type Code (Hydratec)` | User-specified code | Default "01" |
| `Additional Stocklist Information (Hydratec)` | `"CON1,{pipeElementId}"` | Links hanger to pipe |
| `C Clamp` | `"CON1,{diameter in inches}"` | Pipe diameter reference |
| `Comments` | `"Concrete Tee Stem"` | Identifies placement method |

## Hanger Family Identification

Families must be `OST_PipeAccessory` with name containing any of:
- `"-Pipe Hanger"`
- `"Ring Hanger"`
- `"-Basic Adjustable"`

Default selection: `"-Basic Adjustable Ring Hanger"` if available.

## Comparison with Standard User Locations Hang

| Feature | This Command (Tee Stems) | Standard User Locations |
|---------|-------------------------|------------------------|
| Rod direction | Horizontal (to stem side) | Vertical (raybounce up) |
| Structure source | Linked double tee stems | Any structure above |
| Rod length method | Horizontal distance to stem | Vertical raybounce distance |
| Target surface | Vertical stem faces | Horizontal structure underside |
| Best for | Precast concrete tee buildings | Standard steel/concrete structures |

## Notes

- All internal calculations use Revit internal units (feet)
- The "warped" handling uses face UV sampling rather than assuming planar geometry
- If no stems are found, the command fails gracefully with a descriptive message including the search keyword used
