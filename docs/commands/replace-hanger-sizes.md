# Replace Hanger Sizes

**Ribbon:** SG → Hangers → Replace Sizes
**Class:** `SgRevitAddin.Commands.Hangers.ReplaceHangerSizesCommand`

> **Sister command:** [Match Hanger Sizes](match-hanger-sizes.md) does
> the same job using a parameter-set approach (no delete) and orange
> review markers. It's kept around as a backup if Replace Sizes
> misbehaves on a particular family. Both buttons live in the same
> small stack on the Hangers panel.

## Purpose

Resize selected pipe hangers to match the nominal diameter of the pipes
they're attached to. Useful after a pipe-resize because Revit doesn't
automatically propagate diameter changes to connected pipe-accessory
hangers — this command catches the drift in one shot.

## Workflow

1. Select pipe hangers in the model.
2. Run **Hangers → Replace Sizes**.
3. The command analyzes each hanger:
   - Finds the closest near-horizontal pipe (XY-distance match using
     bounding-box-center).
   - Reads the pipe's nominal diameter and the hanger's `Nominal Diameter`.
   - Compares them.
4. A confirmation dialog summarizes:
   - How many were already matching
   - How many are mismatched, with a per-hanger preview of the size change
   - How many were skipped (no nearby pipe → drifted, no `Nominal Diameter`
     parameter, or read-only diameter)
5. Click **Yes** to apply, **No** to skip resize but still mark drifted
   hangers, or **Cancel** to bail entirely.
6. If you confirmed, each mismatched hanger is **deleted and recreated**
   at the same point and rotation with the new size, preserving every
   writable instance parameter (Type Code, Rod Length, Distance off End,
   Comments, Hydratec fields, etc.).
7. After resize, if any drifted hangers were detected, you're prompted
   whether to place orange location markers above them.

## Why delete + recreate, not parameter-set

The naive approach — set `Nominal Diameter` on the existing hanger
instance — has a family-level bug: the ring center drifts off the host
pipe (the ring shrinks toward its top anchor instead of staying centered).
This happens both via our API and via Revit's native Properties palette,
so it's not something we can work around just by being clever with the
parameter setter.

Earlier versions of this command tried to compensate by adjusting `Rod
Length` after the resize (using empirical bounding-box measurement to
detect the actual ring shift). That helped but couldn't fully eliminate
the drift on all family geometries.

The current approach sidesteps the bug entirely: a freshly placed
hanger has no prior parametric state, so the family draws its geometry
correctly for the target size with the ring properly centered on the
pipe. We use the same pattern as `SwapHydraCADHangersCommand` — capture
parameters, place a new instance via `doc.Create.NewFamilyInstance`,
restore the parameters.

## Trade-off: ElementId changes

Each recreated hanger gets a **new ElementId**. Implications:

- **In-project references survive**: schedules, view filters, tags,
  3D-view filters all use category and parameter rules, not specific
  IDs.
- **External references break**: anything outside the project that
  references the old hanger IDs (saved Trimble exports, manually
  recorded clash reports, etc.) will not find them after recreation.
- **`Additional Stocklist Information (Hydratec)` survives** because it
  contains the *pipe* ID, not the hanger ID — the value is captured and
  restored verbatim.

For the workflow this command targets — hangers got out of sync with
pipes — losing the old hanger IDs is almost always fine.

## Parameters preserved on recreation

The command copies every **writable instance parameter** from the old
hanger to the new one, except a small set of Revit-managed built-ins
(Family, Type, Host Id, Phase, Level, IfcGUID, etc.) that Revit sets
automatically based on the new instance's identity.

For typical Hydratec / SG hangers this includes:

- `Rod Length`, `Distance off End`, `Rod Tilt`
- `Type Code (Hydratec)`, `Type Code Description (Hydratec)`
- `Comments`, `Mark`
- `Additional Stocklist Information (Hydratec)`, `HCAD-System`,
  `Hydraulic Group`, `PipeElevationAFF`, `PipeElevationTOS`,
  `Section_ID (Hydratec)`, `Section_ID_Override (Hydratec)`,
  `SubSystem`, `Material Group`, `Install Room`, `Takeout`, …
- Any other writable instance param the user has set

Duplicate parameter names (some Hydratec families have a built-in *and*
a shared parameter with the same name, e.g. two `Rod Length` entries)
are handled — the captured value is restored to **all** writable
parameters with that name on the new instance.

`Nominal Diameter` itself is set explicitly to the pipe's diameter
during recreation; it is *not* restored from the captured (old) value.

## Marking drifted hangers

If any selected hangers have **no near-horizontal pipe within 6"**,
they're tracked separately as "drifted" and offered an optional
location marker:

> *N hangers have no near-horizontal pipe within 6" — they may have
> drifted off their host pipes and could not be matched to a pipe
> diameter.*
>
> *Place orange location markers above them so you can find and
> re-attach them?*

If you click **Yes**, the command places an orange DirectShape cylinder
6" above each drifted hanger's bounding-box center (visible in plan
and 3D) and selects all drifted hangers so you can tab through them.

Re-running with **Yes** on the marker prompt wipes any prior drift
markers before placing fresh ones, so they don't accumulate across
runs. Drift markers use a distinct ApplicationDataId from the blue
Hanger Gap Check markers, so the two commands' markers can be cleaned
up independently.

## Sister command

[Sync Hangers to Pipes](sync-hangers-to-pipes.md) does the same diameter
sync **plus** moves the hanger to the closest pipe and rotates it to
match the pipe direction. Use that command when hangers need full
re-alignment to their pipes; use **Replace Sizes** when the hangers are
already correctly positioned and you just need diameter to match after
a pipe resize.

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Nominal Diameter` (Length, instance, writable) | Set to the pipe's diameter on the new instance |
| Pipe | Built-in `RBS_PIPE_DIAMETER_PARAM` | The target diameter |

If a hanger family encodes diameter as a **type parameter** (different
family types per size), `Nominal Diameter` won't be writable on the
instance. The recreate will still happen, but the new instance's size
will be whatever the FamilySymbol's default is — you may need to switch
the family type manually after.

## Limitations

- Only matches against **near-horizontal** pipes (within 30° of
  horizontal). Sprigs and risers are excluded — hangers don't go on
  those.
- Hanger must be within 6" XY of a pipe centerline to match.
- Pipes are matched purely by XY proximity to the hanger's bounding-box
  center; if a hanger is somehow midway between two pipes in plan, the
  closer one wins.
- Hangers without a `LocationPoint`, without a resolvable level, or
  whose family symbol can't be activated will be reported as failures.

## Re-running

Idempotent on already-matched hangers — they report `Already matching:
N` and aren't touched.

## See also

- [Sync Hangers to Pipes](sync-hangers-to-pipes.md) — full sync (move +
  rotate + resize) for fresh placement; uses the same delete+recreate
  pattern internally for diameter changes
- [Hanger Gap Check](hanger-gap-check.md) — flags hangers whose
  top-of-pipe to structure gap exceeds a threshold
- [Inspect Element Parameters](#) — diagnostic command in the same
  ribbon stack; dumps every parameter on a selected element to help
  debug family behavior

