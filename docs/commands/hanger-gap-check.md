# Hanger Gap Check

**Ribbon:** SG → Seismic → Hanger Gap Check
**Class:** `SSG_FP_Suite.Commands.ModelCheck.HangerGapCheckCommand`

## Purpose

Identify pipe hangers whose vertical gap between top-of-pipe and the structure
above exceeds a configurable threshold (default 6"). Hangers exceeding the gap
are visually flagged with a marker family so they're easy to spot in plan and
3D views — useful for catching hangers that may need additional bracing or
restraint per Hydratec / NFPA convention.

## Workflow

1. Select pipe hangers in the model (any combination of `-Pipe Hanger`,
   `-Pipe Trapeze`, `-Basic Adjustable`, or `Ring Hanger` family instances).
2. Run **Seismic → Hanger Gap Check**.
3. In the dialog:
   - Tick which **Type Code (Hydratec)** values to check (pre-populated from
     the selection — only codes actually present are shown).
   - Tick which **pipe sizes** to include (also pulled from the selection).
   - Set the **gap threshold** in inches (default 6").
4. Click one of:
   - **Check** — runs the check, clears any prior markers, and places new
     ones on flagged hangers.
   - **Clear Markers Only** — wipes every existing `-Hanger Gap Marker`
     instance from the project and exits without checking. No selection
     filters apply.
   - **Cancel** — exits without changes.
5. The command reports how many hangers were flagged, places markers on
   them, and adds them to the active selection.

### Clearing markers without a selection

If you run the command with **no hangers selected** and there are existing
markers in the project, you'll get a confirmation dialog asking whether to
clear them. Use this as a quick "wipe everything" path without having to
select hangers first.

## Math

For each matching hanger, the command finds the closest pipe centerline,
reads the pipe's outside diameter (actual, not nominal), and computes:

| Type Code | Formula |
|---|---|
| `02` (adjustable ring + hardware) | `gap = rod_length − 1.5" − (pipe_OD ÷ 2)` |
| All others (e.g. `03A`, `04`) | `gap = rod_length − (pipe_OD ÷ 2)` |

The 1.5" subtraction for Type 02 accounts for the hardware between the rod
end and the pipe top that isn't captured in the **Rod Length** parameter.

If `gap > threshold`, the hanger is flagged.

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Type Code (Hydratec)` (Text) | Filter + math branch |
| Hanger family | `Rod Length` (Length, feet internal) | Gap math |
| Pipe | `Outside Diameter` (BIP `RBS_PIPE_OUTER_DIAMETER`) | Gap math (falls back to nominal `Diameter` if missing) |

If a hanger has no `Type Code (Hydratec)` value, it's silently skipped — the
dialog already filters available codes from the selection. If a hanger has no
`Rod Length` value, it's reported in the "skipped" count.

## Marker geometry

The command places a Revit `DirectShape` at each flagged hanger — a small
vertical cylinder (4" diameter × 4" tall, Generic Model category) created
directly in the project, no family file required. It's visible in plan and
3D views automatically.

Markers are stamped with `ApplicationId = "SSG_FP_Suite"` and
`ApplicationDataId = "HangerGapMarker"` so the command can find and delete
its own markers without touching unrelated DirectShape elements created by
other tools or addins.

## Re-running

Each run clears all existing `-Hanger Gap Marker` instances from the project
before placing new ones, so re-running with different filters or thresholds
won't accumulate stale markers.

## Limitations

- Only checks straight (non-vertical) pipes; relies on each hanger's nearest
  pipe being within 1 ft of the hanger location point.
- Does not measure the actual structure-to-rod-top distance via raybounce —
  it trusts the `Rod Length` parameter. Use `Sync Hangers Raybounce` first
  to ensure rod lengths are accurate.
- Only Type 02 has a hardcoded hardware offset; if you need additional
  type-specific math (e.g. Type 04 has a 0.75" offset), add a branch in
  `ComputeGap()` in the command file.

## See also

- [Sync Hangers (Raybounce)](sync-hangers-raybounce.md) — populate accurate
  rod lengths before running this check.
- [Sprinkler Clearance Check](sprinkler-clearance-check.md) — same general
  pattern (clearance violation + marker family) for sprinkler heads.
