# Raybounce Early

**Command:** `SgRevitAddin.Commands.Hangers.RaybounceEarlyCommand`
**Domain:** Hangers
**Ribbon:** SG ♈ > Hangers > Raybounce Early

## Purpose

The **stable, original** raybounce. Shoots a ray straight up from each selected pipe hanger and takes the first hit on a **native structural element** — floors, stairs, roofs, structural framing — including linked Revit models, via Revit's built-in `ReferenceIntersector`. Rod Length is set to the vertical distance to the hit.

This is the dependable fallback kept on the ribbon alongside **Raybounce Dev** (`SyncHangersRaybounceCommand`), which is still being refined for imported CAD / IFC mesh geometry. **If Raybounce Dev gives a result that looks wrong on imported steel, use Raybounce Early.**

## What it does (and doesn't)

- ✅ Native Revit structure (floors, roofs, framing, stairs), host or linked Revit model.
- ✅ Simple, fast, predictable — no mesh triangulation, no diagnostics.
- ❌ Does **not** specially handle imported CAD `ImportInstance` geometry (linked DWG). Against a raw CAD import the native intersector returns bounding-box proximity, so the rod can overshoot — that's exactly the case Raybounce Dev exists to solve.

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
