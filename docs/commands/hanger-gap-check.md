# Hanger Gap Check

**Ribbon:** SG → Seismic → Hanger Gap Check
**Class:** `SgRevitAddin.Commands.ModelCheck.HangerGapCheckCommand`

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
reads the pipe's outside diameter (actual, not nominal), and computes a single
formula for **all** Type Codes:

```
gap = rod_length − (pipe_OD ÷ 2)
```

> **Note:** Earlier versions subtracted per-type hardware offsets — 1.0" for
> `01*`, 1.5" for `02*`, 0.5" for `05S*` — to account for takeout lengths that
> were being added in the drawing. Those lengths are no longer added, so the
> adjustments were removed and the gap math is now uniform.

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

## Marker geometry and color

The command places a Revit `DirectShape` at each flagged hanger — a vertical
cylinder (8" diameter × 8" tall, Generic Model category) created directly in
the project. No family file required, and visible in both plan and 3D views
automatically.

The marker **color** flags how badly the hanger failed:

| Color | Meaning | Condition |
|---|---|---|
| **Blue** | Clear failure | gap exceeds the threshold by **≥ 0.5"** |
| **Green** | Near miss / possible false positive | gap exceeds the threshold by **< 0.5"** |

Green markers are worth a manual look — a rod length that's off by a fraction
of an inch (placement variables, rounding) can tip a hanger just over the 6"
line without being a real problem.

Two project-wide materials back the markers: `SG_HangerGapMarker` (blue) and
`SG_HangerGapMarkerNear` (green). They're created on first run and reused (with
their color refreshed) afterward.

Markers of **both** colors are stamped with `ApplicationId = "SgRevitAddin"`
and `ApplicationDataId = "HangerGapMarker"`, so "Clear Markers Only" and the
automatic re-run cleanup remove them together, without touching unrelated
DirectShape elements created by other tools.

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
- The gap math is uniform across all Type Codes (`gap = rod_length − pipe_OD/2`).
  If a project ever needs type-specific offsets again, add a branch in
  `ComputeGap()` in the command file.

## See also

- [Sync Hangers (Raybounce)](sync-hangers-raybounce.md) — populate accurate
  rod lengths before running this check.
- [Sprinkler Clearance Check](sprinkler-clearance-check.md) — same general
  pattern (clearance violation + marker family) for sprinkler heads.

