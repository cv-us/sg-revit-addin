# Hangers: AutoSync Hangers to Structural Elements (Surface Intersection)

**Command:** `SyncHangersSurfaceCommand`
**Domain:** Hangers
**Ribbon:** SG Revit Addin > Hangers > Sync Struct Surface

## Purpose

Synchronizes pipe hanger rod lengths to the structural elements above using bounding box spatial search and surface intersection. This is the non-raybounce variant — it extracts geometry faces from structural elements and intersects vertical lines through them to compute rod lengths. Handles floors, roofs, structural framing, and stairs from both the host model and linked models.

## Comparison with RayBounce Version

| Aspect | This Command (Surface) | RayBounce Version |
|--------|----------------------|-------------------|
| Method | BBox pre-filter + face intersection | `ReferenceIntersector` ray shooting |
| 3D View required | No | Yes (creates "3D-Raybounce" view) |
| Framing Top/Bottom | User choice via dialog | Always uses closest hit |
| Framing filtering | Excludes angles, hollows, C-channels | Accepts all framing |
| Clash Height | Configurable search distance | Unlimited ray distance |
| Global params | Reads and writes AutoSync settings | Does not persist settings |

## Workflow

1. User selects pipe hangers (pre-selection or pick)
2. Dialog: configure search parameters, type codes, framing sync direction
3. Collect structural elements from host document and all linked models
4. Filter out angle/hollow/C-channel framing families
5. Extract horizontal planar faces from structural element geometry
6. For each hanger, find structural elements whose bounding boxes overlap
7. Intersect vertical line from hanger with structural faces
8. Take closest face above → compute rod length
9. Set Rod Length, Y Grip, Type Code, Comments
10. Write changed settings back to global parameters

## Dialog Options

| Setting | Default | Source | Description |
|---------|---------|--------|-------------|
| Clash Height (feet) | 10 | GP: AutoSync Clash Height Distance | Vertical search range above hangers |
| Framing Offset (inches) | 1 | GP: AutoSync Framing Offset Distance | XY search radius for framing proximity |
| Framing Sync To | Bottom | GP: AutoSync Framing Hangers Sync'd To | Use bottom or top surface of framing |
| Type Code - Floors | 02 | GP: AutoSync Hanger Type - Floors | Hydratec type code for floor-attached hangers |
| Type Code - Stairs | 04 | GP: AutoSync Hanger Type - Stairs | Hydratec type code for stair-attached hangers |
| Type Code - Roofs | 01 | GP: AutoSync Hanger Type - Roofs | Hydratec type code for roof-attached hangers |
| Type Code - Framing | 03 | GP: AutoSync Hanger Type - Framing | Hydratec type code for framing-attached hangers |
| Keep Hanger Types | true | GP: AutoSync Keep Hanger Types | Only update rod lengths, leave type codes unchanged |

## Structural Element Search

### Bounding Box Pre-Filter
For each hanger, a search volume is defined:
- XY: hanger point +/- framing offset distance (min 6")
- Z: hanger elevation to hanger elevation + clash height

Structural elements whose bounding boxes overlap this volume are candidates.

### Face Extraction
From each candidate structural element:
- Extract solid geometry with `get_Geometry()`
- Find all `PlanarFace` instances
- Keep only horizontal faces (normal Z > 0.9 or < -0.9)
- Tessellate edge loops for point-in-polygon testing

### Face Selection by Category
- **Floors, Roofs, Stairs**: use down-facing faces (structural underside)
- **Structural Framing**: use down-facing (Bottom) or up-facing (Top) based on user setting

### Point-in-Polygon
A 2D ray-casting test in the XY plane determines if the hanger projects inside a face boundary.

## Framing Exclusions

Structural framing families containing these substrings (case-insensitive) are excluded:
- ANGLE
- HOLLOW
- C-CHANNEL


## Parameters Written

| Parameter | Value |
|-----------|-------|
| Rod Length | Vertical distance from hanger to structural face (feet) |
| Y Grip | Same as Rod Length |
| Type Code (Hydratec) | Category-based code (unless Keep Types enabled) |
| Comments | Same as Type Code (unless Keep Types enabled) |

## Global Parameters Read/Written

All prefixed with "Dynamo Setting - ":
- AutoSync Hanger Type - Floors / Roofs / Framing / Stairs
- AutoSync Clash Height Distance
- AutoSync Framing Offset Distance
- AutoSync Framing Hangers Sync'd To
- AutoSync Keep Hanger Types

Changed values are written back after the dialog is confirmed.

## Summary Dialog

Reports:
- Total hangers re-synced with counts per structural category
- Hangers with unchanged rod lengths
- Hangers that couldn't be synchronized (highlighted in view)
- Failed hangers count

## Notes

- A unified family name exclusion filter handles both standard Revit and IFC/Tekla framing member types
- Rod length change detection uses a tolerance of ~0.004" (1/256 foot) to avoid unnecessary parameter writes

## See Also

- **[Choosing a Command](choosing-a-command.md)** — full comparison of all sync commands
- **Sync Raybounce** — simpler alternative that doesn't need top/bottom choice; requires a 3D view
- **Sync to Ref Plane** — fastest option when structure is a flat slab at known elevation
- **Sync to Pipes** — run first to position hangers on pipes before calculating rod lengths

