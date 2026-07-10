# Riser Tags

**Ribbon:** Modify tab → SG panel → **Riser Tags**
**Class:** `SgRevitAddin.Commands.Modify.RiserTagsCommand`

## Purpose

Places a rotatable **annotation symbol** (your riser-nipple symbol, with its mask)
at the **top of vertical pipes**, centered on the pipe in plan and **auto-rotated**
so the line runs along the **horizontal pipe connected at the top** (the higher
pipe) and the solid semicircle marks the lower, vertical side.

> It places a **Generic Annotation / Sprinkler Tags family instance** (via
> `NewFamilyInstance`, like the Pretty Sprinklers head overlays), **not** a pipe
> tag — because Revit pipe tags (`IndependentTag`) can't be rotated to an arbitrary
> angle, but annotation-symbol instances can. Put the riser-nipple symbol into a
> **Generic Annotation** family (with its mask) and load it; the command places
> and rotates that.

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
- **Auto-rotate the tag to the branch direction** — rotate the tag so its line
  points along the horizontal pipe connected at the **top** of the vertical
  (the higher pipe), extending away from the vertical.
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
- Auto-rotation reads the horizontal pipe connected at the vertical's **top**
  (directly or through a fitting) and points the line along it, away from the
  vertical — the line marks the horizontal-and-higher pipe, the semicircle the
  lower side. For an armover (branch → riser up → over → drop down), both ends
  tag with the lines on the inside of the armover and the semicircles on the
  outsides. A rise straight off the branch line (head on top, no horizontal
  above) falls back to the branch at its bottom.
- **Sprigs are never tagged** — a vertical pipe off the TOP of the branch line
  that just feeds a single head above (head above + horizontal pipe below)
  needs no riser-nipple symbol; the report counts them separately.
- Pipes already tagged with the chosen family (in the view) are skipped.

## Notes

- Uses your existing masked pipe-tag family — the command doesn't create a family.
- Centering/rotation depend on the family's origin and default symbol direction;
  dial the **Center nudge** and **Rotate +** values once for your family and they
  are remembered.
- Runs through an `ExternalEvent` (`DeferredActionHandler`) since the Modify-tab
  button fires outside Revit's API context.
