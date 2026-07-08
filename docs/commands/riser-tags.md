# Riser Tags

**Ribbon:** Modify tab → SG panel → **Riser Tags**
**Class:** `SgRevitAddin.Commands.Modify.RiserTagsCommand`

## Purpose

Places a chosen **pipe-tag family** (your riser-nipple symbol, with its mask) at
the **top of vertical pipes**, centered on the pipe in plan and **auto-rotated to
the branch** it comes from.

Revit's intrinsic rise/drop symbol on a pipe stops showing once the run is sloped
off-plumb, so drops/risers on a sloped system often go unmarked. This drops the
tag in as a reliable stand-in — without the manual centering and rotation the tag
otherwise needs.

## Dialog

- **Tag family / Type** — the loaded Pipe Tag family + type to place (your
  riser-nipple tag). Remembered.
- **Scope** — *Selected pipes* (default), *Active view*, or *Whole model*.
- **Vertical pipes only** — restrict to near-vertical pipes (within 30° of plumb).
- **Only drops (reach a sprinkler)** — further restrict to vertical pipes that
  connect down to a sprinkler within a couple of hops.
- **Auto-rotate the tag to the branch direction** — rotate the tag so its symbol
  points along the horizontal branch the riser connects to.
- **Fine-tune** — per-family calibration:
  - **Center nudge X / Y** (inches) — offset the tag head if your family's origin
    isn't at the symbol center.
  - **Rotate +** (degrees) — a constant added to the auto-rotation, to match your
    family's default symbol direction.

**Remove Riser Tags** deletes every tag of the chosen family that hosts a pipe in
the active view.

## Placement

- The tag head lands at the pipe's **top endpoint** in plan (not the 3-D midpoint,
  which drifts off the riser when the pipe is sloped).
- Auto-rotation reads the connected horizontal branch pipe (directly or through a
  fitting) and rotates the tag to point along it.
- Pipes already tagged with the chosen family (in the view) are skipped.

## Notes

- Uses your existing masked pipe-tag family — the command doesn't create a family.
- Centering/rotation depend on the family's origin and default symbol direction;
  dial the **Center nudge** and **Rotate +** values once for your family and they
  are remembered.
- Runs through an `ExternalEvent` (`DeferredActionHandler`) since the Modify-tab
  button fires outside Revit's API context.
