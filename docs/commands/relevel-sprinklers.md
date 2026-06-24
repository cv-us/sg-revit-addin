# Re-Level Sprinklers

**Command:** `SgRevitAddin.Commands.PipeRouting.RelevelSprinklersCommand`
**Domain:** Pipe Routing
**Ribbon:** SG ♈ > Pipe Routing > Re-Level Sprinklers

## Purpose

Moves selected sprinkler heads to a chosen **Level** while keeping each head in its **exact world location**. The "Elevation from Level" / "Offset from Host" value is recomputed automatically from the level-elevation difference.

> Example: 100 heads referenced to **Level 1** (floor 0'-0") at offset **72'-8"** → re-level to **Level 4** (floor 60'-0"). The heads stay put; their offset becomes **72'-8" − 60'-0" = 12'-8"**.

## How it works (per head, one transaction)

1. Capture the head's world point `P = (Location as LocationPoint).Point`.
2. Set `BuiltInParameter.FAMILY_LEVEL_PARAM` to the new level — this re-hosts the head and *jumps* it in Z (it keeps the old offset).
3. Re-fetch the Location and set `.Point = P` again — Revit recomputes the offset = `P.Z − newLevel.Elevation`, **exactly and family-agnostic**. The offset is never written by hand.

After processing, the document is regenerated and every re-leveled head's world position is **verified** against its captured point (tolerance ~0.001"). The summary reports `re-leveled / already-on-level / skipped / errors`, and flags any position drift (should always be zero).

## What gets skipped (and why not deleted)

Heads that **can't** be re-leveled in place are **skipped and reported**, not deleted:

- **Face- or work-plane-hosted** heads (`HostFace != null`, or a non-Level host) — pinned to a wall/ceiling/floor face; re-pinning the point fights the host.
- Heads whose **Level parameter is read-only** or missing.

Deleting + recreating would lose element IDs, schedule/tag/hydraulic-calc links, Trimble associations, SG marks, and pipe connections — so a head that can't be re-leveled in place is surfaced for the user to fix, not silently rebuilt.

## Usage

1. Select the sprinkler heads (or run the command and pick them).
2. Choose the **target level** from the dropdown (levels sorted by elevation; last choice remembered by name).
3. **Re-Level.** Read the summary.

## Notes

- Heads already on the target level are skipped (counted separately).
- Everything stays in internal feet; only the dialog/summary display converts to feet-inches.
- Connections (e.g. to a flex drop) are preserved — the head keeps its element identity and only its level/offset change.
