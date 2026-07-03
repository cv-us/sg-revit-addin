# Raybounce Dev (AutoSync Hangers to Structural — RayBounce)

**Command:** `SyncHangersRaybounceCommand`
**Domain:** Hangers
**Ribbon:** SG ♈ > Hangers > Raybounce Dev

> ⚠️ **UNDER DEVELOPMENT.** This is the experimental raybounce that also tries
> imported CAD / IFC mesh geometry (via a custom triangle raycaster) and a
> multi-ray fan to tolerate small plan misalignment. If a result looks wrong,
> use **Raybounce Early** ([raybounce-early.md](raybounce-early.md)), the
> stable fallback (which shares the same verified core engine, minus the fan
> and CAD handling).

## Engine (StructureRayScanner)

Both raybounce commands now run on `Utils/StructureRayScanner`, which merges
three sources per hanger and takes the **closest verified** hit:

1. **Native** `ReferenceIntersector` (`Find()` + proximity sort — never
   `FindNearest`, which has returned farther faces). Every candidate is
   **re-measured against the hit element's real triangulated geometry along
   the hanger's own vertical**. This fixes rods stretching under **sloped
   decks in linked structural files** — raw `Proximity` can be a phantom
   (element-extent / wrong-face) distance. A candidate whose real geometry
   never touches the vertical is rejected and the next-closest candidate is
   tried. The intersector filter also ORs in the `RevitLinkInstance` class
   and re-checks the resolved element's category, so link hits behave the
   same across Revit versions.
2. **DirectShape mesh index** — DirectShapes from the host doc and every
   linked doc (structural categories, + Generic Models/Masses when that
   option is on, including STEP-in-a-family `FamilyInstance`s) are
   triangulated once (spatially bounded by the hangers' footprint) and
   ray-cast manually. This covers **linked IFC**, where the native
   intersector has been observed passing straight through beams.
3. **CAD import raycaster** (`CadMeshRaycaster`) — `ImportInstance` DWG /
   DGN / SAT geometry, when the linked-CAD option is on.

**Ray fan, center-priority:** the fan (8-spoke rings at 2" and 4") exists to
catch narrow steel a couple of inches off in plan. Fan hits are re-measured
along the **center** line, so on a sloped deck all rays converge to the exact
rod length at the hanger's XY (the old fan took the minimum across rays — the
downhill sample). A genuinely-offset hit only wins when it's **shorter than
the centered hit by more than 6"** (nearby lower steel / deck-joint guard).
Hits beyond **120 ft** are ignored.

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
| Detect non-structural geometry | unchecked | If checked, the `ReferenceIntersector` category filter also matches **Generic Models** and **Masses** — covers IFC imports, STEP / SAT / Inventor imports, and other simple geometry that isn't categorized as structure. Linked Revit models are always searched regardless of this option |
| Detect linked CAD geometry | unchecked | If checked, **every `ImportInstance` in the host doc and in linked Revit docs is triangulated and ray-cast manually** (Möller-Trumbore). `ReferenceIntersector`'s `Proximity` on `ImportInstance` is element-extent / bbox distance — not triangle distance — which caused rods to overshoot the real geometry by feet (30 ft on a STEP-in-DWG test, 1'6" past an IFC beam). The custom raycaster runs alongside the native intersector and the closer hit wins per hanger. The DWG must be **visible in the `3D-Raybounce` view** for the hit to land; if its import subcategory is hidden in V/G the ray sails right through |

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
- **Geometry verification stats** — mesh-index size, how many rods the
  verification pass corrected (and the largest correction), and how many
  phantom references were rejected
- CAD detection diagnostics + spatial probes (when linked-CAD is on)

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

