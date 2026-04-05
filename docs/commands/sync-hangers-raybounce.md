# AutoSync Hangers to Structural Elements (RayBounce)

**Command:** `SyncHangersRaybounceCommand`
**Domain:** Hangers
**Ribbon:** SSG FP Suite > Hangers > Sync to Structural

## Purpose

Calculates rod lengths for pipe hangers by shooting a ray straight up from each hanger to find the structural element above (floors, stairs, roofs, structural framing). The vertical distance from the hanger to the structural hit becomes the rod length. Works with both host model and linked model structural elements.

## Workflow

1. User selects pipe hangers (pre-selection or pick prompt)
2. Command finds or creates a "3D-Raybounce" view for the ReferenceIntersector
3. Dialog shows hanger count, type code settings, and keep-types option
4. For each hanger:
   a. Get the hanger's location point
   b. Shoot a ray straight up (Z-axis direction)
   c. Find the nearest structural element hit
   d. Calculate rod length as the proximity distance
5. Write Rod Length, Y Grip, and optionally Type Code + Comments
6. Highlight any hangers that missed (no structure above)

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| Floors type code | "05" | Type Code (Hydratec) for hangers hitting floors |
| Stairs type code | "02" | Type Code for hangers hitting stairs |
| Roofs type code | "03" | Type Code for hangers hitting roofs |
| Structural Framing type code | "02" | Type Code for hangers hitting framing |
| Keep Hanger Types | unchecked | If checked, only adjusts Rod Length/Y Grip without touching Type Code or Comments |

## RayBounce Implementation

### Revit API Method
Uses `ReferenceIntersector`:
- **Origin:** Hanger location point
- **Direction:** `XYZ.BasisZ` (straight up, 0,0,1)
- **Target:** `FindReferenceTarget.Face` — finds the first face hit
- **Linked models:** `FindReferencesInRevitLinks = true`
- **View:** Uses a dedicated "3D-Raybounce" isometric view

### Target Categories
- `OST_Floors` — Floor slabs
- `OST_Stairs` — Stair elements
- `OST_Roofs` — Roof elements
- `OST_StructuralFraming` — Beams, joists, etc.

### 3D View Setup
The command finds or creates a 3D isometric view named "3D-Raybounce":
- Detail level: Fine
- Visual style: Hidden Line
- This view is required by the `ReferenceIntersector` API

## Parameters Written

| Parameter | Value | Condition |
|-----------|-------|-----------|
| `Rod Length` | Distance from hanger to structural hit (feet) | Always |
| `Y Grip` | Same as Rod Length | Always |
| `Type Code (Hydratec)` | User-specified code per structural category | Only when "Keep Hanger Types" is unchecked |
| `Comments` | Same as Type Code | Only when "Keep Hanger Types" is unchecked |

## Hanger Identification

Elements must be `OST_PipeAccessory` with family name containing any of:
- `"-Pipe Hanger"`
- `"Ring Hanger"` (catches "Adjustable Ring Hanger")
- `"-Basic Adjustable"`

## Line-Based Hangers

For hangers whose location is a curve (line-based families), the midpoint of the curve is used as the ray origin instead of the location point.

## Missed Hangers

Hangers where the ray doesn't hit any structural element are:
- Collected and counted separately
- **Highlighted in the Revit selection** after the command completes (so the user can see which ones need attention)
- Reported in the summary dialog

## Summary Dialog

Reports:
- Total hangers re-synced
- Breakdown by structural category (Floors: N, Roofs: N, etc.)
- Number of hangers that missed (no structure above)

## Comparison with Reference Plane Sync

| Feature | This Command (Structural) | Reference Plane Sync |
|---------|--------------------------|---------------------|
| Method | RayBounce upward | Vertical projection onto plane |
| Target | Actual structural geometry | Named reference plane |
| Linked models | Yes | No |
| Category detection | Yes (Floors/Stairs/Roofs/Framing) | No |
| Type code assignment | Yes, per category | No |
| Best for | General use, varied structure | Uniform slab at known elevation |

## Notes

- Rod lengths are written in Revit internal units (feet), no rounding applied
- The "3D-Raybounce" view is a permanent artifact in the project — it can be deleted manually if desired
- Type code defaults are per-session dialog values (enhancement opportunity: persist to project parameters)
- Linked model structural elements are found via `ReferenceIntersector.FindReferencesInRevitLinks = true`

## See Also

- **[Choosing a Command](choosing-a-command.md)** — full comparison of all sync commands
- **Sync Surface** — alternative method with top/bottom framing choice and global parameter persistence
- **Sync to Ref Plane** — simpler option when structure is a flat slab at known elevation
- **Sync to Pipes** — run first to position hangers on pipes before calculating rod lengths
