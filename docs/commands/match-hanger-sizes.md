# Match Hanger Sizes

**Ribbon:** SG → Hangers → Match Sizes
**Class:** `SSG_FP_Suite.Commands.Hangers.MatchHangerSizesCommand`

> **See also:** [Replace Hanger Sizes](replace-hanger-sizes.md) is the
> sibling command that solves the same problem by deleting and
> recreating each hanger instead of mutating its parameters. Replace
> Sizes is the recommended approach because it sidesteps the family-
> level centerline-drift bug. Match Sizes (this command) is kept as a
> backup for cases where the delete+recreate path doesn't work for a
> particular family.

## Purpose

Resize selected pipe hangers to match the nominal diameter of the pipes
they're attached to. Useful after a pipe-resize because Revit doesn't
automatically propagate diameter changes to connected pipe-accessory
hangers — this command catches the drift in one shot.

## Workflow

1. Select pipe hangers in the model.
2. Run **Hangers → Match Sizes**.
3. The command analyzes each hanger:
   - Finds the closest near-horizontal pipe (XY-distance match using
     bounding-box-center, same approach as Hanger Gap Check).
   - Reads the pipe's nominal diameter.
   - Reads the hanger's `Nominal Diameter` parameter.
   - Compares them, captures the current `Rod Length` for compensation.
4. A confirmation dialog summarizes:
   - How many were already matching
   - How many are mismatched (with a preview of the first 10:
     `ID 12345: 1" → 1-1/4"  (rod −0.17")`)
   - How many were skipped (no nearby pipe, no `Nominal Diameter`
     parameter, or read-only diameter)
5. Click **Yes** to apply, or **Cancel** to bail.
6. The command sets each mismatched hanger's `Nominal Diameter` to the
   pipe's diameter **and** adjusts the `Rod Length` to compensate (see
   below). Reports rod adjustments separately from resizes.

## Rod-length compensation

Hydratec hanger families anchor the rod top to the structure (the rod
top stays fixed in space). When you change `Nominal Diameter`, the ring
geometry resizes around the pipe — and depending on how the family is
built, this typically shifts the ring center up (downsize) or down
(upsize), so the pipe stops sitting at the visible center of the ring.

To keep BOTH the rod top AND the pipe centerline in their original
positions, the command:

1. Snapshots the bottom of each hanger's bounding box (Z) **before**
   any change.
2. Sets the new `Nominal Diameter` for every mismatched hanger.
3. Forces a Revit regenerate so the bounding boxes reflect the new
   geometry.
4. Reads each hanger's new bounding-box bottom and computes the actual
   centerline shift:
   ```
   bb_shift = bb_min_after − bb_min_before
   ring_radius_change = (new_OD − old_OD) / 2
   centerline_shift = bb_shift + ring_radius_change
   ```
   (`ring_radius_change` is the predictable geometric component;
   anything left over is the actual displacement to undo.)
5. Adjusts rod length by exactly `centerline_shift` to bring the ring
   center back to the pipe's centerline:
   ```
   new_rod_length = old_rod_length + centerline_shift
   ```

The empirical measurement makes the compensation robust against
family-specific geometry quirks — fixed offsets, strap thicknesses,
non-standard ring sizing, etc. all wash out automatically because we
read the actual after-resize bounding box rather than calculating a
predicted one.

Outside-diameter values for the `ring_radius_change` term come from
the standard NPS Schedule 40 table built into the command (covers
1/2" through 12"). For non-NPS content, the math falls back to
nominal=OD, which biases the compensation slightly but still reduces
displacement.

The preview shows estimated rod adjustments (e.g. `rod ~−0.17"`); the
actual amount applied is whatever the empirical measurement reads,
which may differ a bit if the family has any internal offset.

If a hanger has no `Rod Length` parameter, the resize still happens but
the centerline will shift visibly. Affected hangers are noted in the
confirmation dialog and the report.

If the compensation would drive the rod shorter than 1/2", the rod
adjustment is skipped (the resize still happens) — that hanger's rod
is too short to lose any more length, and you'll need to look at it
manually.

## Review markers (resized + drifted hangers)

After the resize phase, the command offers to place orange review
markers above two categories of hangers:

| Category | Why it needs review |
|---|---|
| **Resized** | The empirical rod compensation isn't perfect for every family geometry — even after the auto-adjustment, the centerline may still need a small manual nudge in section view. Marking every resized hanger lets you tab through them and verify. |
| **Drifted** | Hanger has no near-horizontal pipe within 6", so it likely became disconnected or its host pipe was deleted. Needs to be re-attached to a pipe. |

A single prompt covers both:

> *N hangers need review:*
> *  • X resized — centerline may need manual adjustment after Revit re-renders*
> *  • Y drifted — no nearby pipe found, may need re-attaching*
>
> *Place orange location markers above them so you can find and fix them in section views?*

If you click **Yes**, the command places an orange DirectShape cylinder
6" above each flagged hanger's bounding-box center (visible in plan
and 3D) and selects all flagged hangers so you can immediately tab
through them.

The orange markers use a different material and ApplicationDataId from
the red Hanger Gap Check markers, so they won't interfere with each
other and can be cleaned up independently. Re-running this command
with **Yes** on the marker prompt always wipes any prior review
markers in the project before placing fresh ones, so they don't
accumulate across runs.

## Sister command

[Sync Hangers to Pipes](sync-hangers-to-pipes.md) does the same diameter
sync **plus** moves the hanger to the closest pipe and rotates it to
match the pipe direction. Use that command for fresh placement; use
**Match Sizes** when the hangers are already correctly placed and you
just need to fix size drift after a pipe resize.

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Nominal Diameter` (Length, instance, writable) | The value the command sets |
| Hanger family | `Rod Length` (Length, instance, writable) | Adjusted to compensate for centerline shift |
| Pipe | Built-in `RBS_PIPE_DIAMETER_PARAM` | The target diameter |

If a hanger family encodes diameter as a **type parameter** (different
family types per size, e.g. one type for 1", one for 1-1/4"), the
command can't resize it via the instance parameter — it'll be reported
under "Failed" with an explanation. In that case either switch the
family type manually, or use Sync Hangers to Pipes (which handles type
swaps in some cases).

## Limitations

- Only matches against **near-horizontal** pipes (within 30° of
  horizontal). Sprigs and risers are excluded — hangers don't go on
  those.
- Hanger must be within 6" XY of a pipe centerline to match.
- Pipes are matched purely by XY proximity to the hanger's bounding-box
  center; if a hanger is somehow midway between two pipes in plan, the
  closer one wins.

## Re-running

Idempotent — running again on already-matched hangers reports
`Already matching: N` and exits with no changes.

## See also

- [Sync Hangers to Pipes](sync-hangers-to-pipes.md) — full sync (move +
  rotate + resize) for fresh placement
- [Hanger Gap Check](hanger-gap-check.md) — flags hangers whose
  top-of-pipe to structure gap exceeds a threshold; uses the same
  pipe-finding logic
