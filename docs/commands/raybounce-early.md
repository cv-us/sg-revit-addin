# Raybounce Early

**Command:** `SgRevitAddin.Commands.Hangers.RaybounceEarlyCommand`
**Domain:** Hangers
**Ribbon:** SG ♈ > Hangers > Raybounce Early

## Purpose

The **stable, original** raybounce. Shoots a ray straight up from each selected pipe hanger and takes the first hit on a **native structural element** — floors, stairs, roofs, structural framing — including linked Revit models, via Revit's built-in `ReferenceIntersector`. Rod Length is set to the vertical distance to the hit.

This is the dependable fallback kept on the ribbon alongside **Raybounce Dev** (`SyncHangersRaybounceCommand`), which is still being refined for imported CAD / IFC mesh geometry. **If Raybounce Dev gives a result that looks wrong on imported steel, use Raybounce Early.**

## What it does (and doesn't)

- ✅ Native Revit structure (floors, roofs, framing, stairs), host or linked Revit model.
- ✅ **Geometry-verified distances** — every intersector hit is re-measured against the hit element's real triangulated geometry along the hanger's own vertical (via `StructureRayScanner`). This fixes rods stretching under **sloped decks in linked structural files**: the raw `Proximity` can be a phantom (element-extent / wrong-face) distance, and a candidate whose real geometry never touches the vertical is rejected in favor of the next-closest one.
- ✅ **Linked IFC DirectShapes** — a triangle index of DirectShapes (host + all links, structural categories) is ray-cast manually, so IFC beams that the native ray passes straight through are still found.
- ✅ Single straight-up ray — no multi-ray fan, predictable.
- ❌ Does **not** handle imported CAD `ImportInstance` geometry (linked DWG) or Generic Model / Mass imports — that's Raybounce Dev's job.

## Diagnostic mode

The dialog's diagnostic checkbox produces a copyable report instead of writing parameters (rod changes are rolled back). Columns now include **corr"** (raw Proximity minus verified distance, inches — how far the raw raycast was off) and **via** (`source:quality`):

- `native:centered` — intersector hit, re-measured on the element's real geometry at the hanger's XY (best).
- `mesh:centered` — found by the DirectShape triangle index (linked IFC path).
- `native:proximity` — element couldn't be triangulated; raw intersector value used (last resort).

## Workflow

1. Select pipe hangers (`-Pipe Hanger`, `Ring Hanger`, `-Basic Adjustable`).
2. Run **Raybounce Early**.
3. Dialog: set per-category type codes (Floors/Stairs/Roofs/Framing) and optionally "keep existing types".
4. Each hanger gets Rod Length + Y Grip set to the distance to the structure above; type code/comments assigned by hit category (unless "keep types").
5. Summary lists counts by category and highlights any misses.

## Parameters Written

`Rod Length`, `Y Grip` (always); `Type Code (Hydratec)`, `Comments` (unless "keep types").

## See Also

- **Raybounce Dev** ([sync-hangers-raybounce.md](sync-hangers-raybounce.md)) — under development; adds imported CAD/IFC mesh handling + a multi-ray fan.
- **Sync Surface**, **Sync Ref Plane** — alternative rod-length methods.
