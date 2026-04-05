# Insert Pipe Sleeves at Intersecting Beams

**Command:** `PipeSleevesAtBeamsCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Sleeves at Beams

## Purpose

Automatically places pipe sleeve family instances at every intersection between user-selected pipes (in the host model) and structural beams (in a linked Revit model). Sleeves are sized per NFPA annular clearance rules, rotated to match beam direction, and stamped with metadata parameters for scheduling.

## Workflow

1. Dialog opens to select structural link and enter sleeve length
2. User selects pipes in the model
3. For each pipe, command finds which linked beams it intersects (bounding box pre-filter + precise face intersection)
4. At each intersection point, a sleeve family instance is placed:
   - Sized per NFPA clearance rules
   - Rotated to match beam direction
   - Placed on the pipe's reference level
   - Metadata parameters populated

## Dialog Options

| Setting | Description |
|---------|-------------|
| Structural Link (Beams) | Linked Revit model containing structural framing (beams) |
| Sleeve Length (inches) | Length of the sleeve to place (default: 6") |

## Sleeve Family

- **Family:** `-Pipe Sleeve-Beam-MiddleJustified`
- **Type:** `Standard`
- This family must be loaded in the project before running the command

## NFPA Annular Clearance Sizing

Sleeve diameter is computed from pipe diameter:

| Pipe Diameter | Clearance | Sleeve Diameter |
|--------------|-----------|-----------------|
| < 3.5" | +2" | pipe + 2" |
| >= 3.5" | +4" | pipe + 4" |

Standard size snapping:
- 3.25" → 3.5" (nearest standard)
- 4.5" → 5" (nearest standard)

### Examples

| Pipe Size | Clearance | Raw Sleeve | Snapped |
|-----------|-----------|------------|---------|
| 1" | +2" | 3" | 3" |
| 1.25" | +2" | 3.25" | 3.5" |
| 1.5" | +2" | 3.5" | 3.5" |
| 2" | +2" | 4" | 4" |
| 2.5" | +2" | 4.5" | 5" |
| 3" | +2" | 5" | 5" |
| 4" | +4" | 8" | 8" |
| 6" | +4" | 10" | 10" |

## Parameters Written on Sleeves

| Parameter | Type | Description |
|-----------|------|-------------|
| `Sleeve Pipe Size` | Double/String | Computed NFPA sleeve diameter (inches) |
| `Sleeve Pipe Length` | Double | User-entered sleeve length (stored in feet) |
| `Comments` | String | Beam type name from linked model (for scheduling) |
| `Elevation from Level` | Double | Intersection point Z minus reference level elevation |

## Intersection Detection

### Stage 1: Bounding Box Coarse Filter
- Each pipe's bounding box is compared against each beam's bounding box (with 0.5 ft tolerance)
- Beams with no bounding box overlap are skipped (performance optimization)

### Stage 2: Precise Face Intersection
- The pipe's location curve is extended 1 ft in each direction
- Each face of the beam's solid geometry is tested with `Face.Intersect(Curve)`
- All intersection points are collected
- If 2+ points found: midpoint between the most distant pair (entry/exit through beam) is used
- If 1 point found: that point is used directly

### Placement
- Sleeve is placed at the intersection midpoint on the pipe's reference level
- Rotated about Z-axis to align with beam direction (from beam's location curve)
- Elevation offset from level is computed and set as a parameter

## Element Selection

- **Category:** OST_PipeCurves (Pipes only)
- **Multi-select:** Yes, processes all selected pipes against all beams

## Linked Model Handling

- Beam solid geometry is extracted from the linked document and transformed to host coordinates using `SolidUtils.CreateTransformed`
- Beam direction vectors are transformed from link coordinates to host coordinates
- Only loaded (not unloaded) link instances appear in the dialog

## Notes

- The sleeve family must be loaded before running the command — use the Load Families command if needed
- For large models with many beams, the bounding box pre-filter ensures performance stays reasonable
- Each pipe can generate multiple sleeves if it crosses multiple beams
- The command creates a single transaction for all sleeve placements (all-or-nothing)
