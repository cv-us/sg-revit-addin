# Match Hanger Sizes

**Ribbon:** SG → Hangers → Match Sizes
**Class:** `SSG_FP_Suite.Commands.Hangers.MatchHangerSizesCommand`

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
top stays fixed in space) while the pipe centerline = `rod_top −
rod_length − ring_radius`. When you change `Nominal Diameter` the ring
radius changes, which shifts the centerline up or down — the pipe stops
sitting at the visible center of the ring. To keep BOTH the rod top
AND the pipe centerline in their original positions, the command
adjusts rod length by half the OD delta:

```
new_rod_length = old_rod_length − (new_OD − old_OD) / 2
```

| Resize | Ring change | Rod change |
|---|---|---|
| Upsize (1" → 2") | Ring radius grows by 0.53" | Rod **shortens** by 0.53" |
| Downsize (2" → 1") | Ring radius shrinks by 0.53" | Rod **lengthens** by 0.53" |

Outside-diameter values are pulled from the standard NPS Schedule 40
table built into the command (covers 1/2" through 12"). For non-NPS
content (e.g. CPVC or imperial-pipe families with non-standard ODs)
the math falls back to nominal=OD which is less accurate but still in
the right direction.

If a hanger has no `Rod Length` parameter, the resize still happens but
the centerline will shift visibly. Affected hangers are noted in the
confirmation dialog and the report.

If the compensation would drive the rod shorter than 1/2", the rod
adjustment is skipped (the resize still happens) — that hanger's rod
is too short to lose any more length, and you'll need to look at it
manually.

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
