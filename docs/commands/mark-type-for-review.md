# Mark Type for Review

**Ribbon:** SG → Hangers → Mark for Review
**Class:** `SgRevitAddin.Commands.Hangers.MarkTypeForReviewCommand`

## Purpose

Flags hangers of a chosen `Type Code (Hydratec)` for review by placing a
**tall vertical magenta cylinder** at each one. The cylinder extends a
configurable distance above *and* below the hanger's elevation, so it
crosses plan-view cut planes and stands out in 3D — making the flagged
hangers easy to find no matter which view you're in.

Same marker mechanism as [Hanger Gap Check](hanger-gap-check.md)
(DirectShape geometry tagged with an ApplicationId/ApplicationDataId for
clean re-runs), but filtered by Type Code instead of gap math, drawn as a
tall column instead of a short puck, and colored magenta with its own
ApplicationDataId so the two commands' markers never collide.

## Workflow

1. Pre-select hangers in the model.
2. Run **Hangers → Mark for Review**.
3. Dialog:
   - **Type Code** — dropdown, populated from codes in the selection.
   - **Reach above/below (ft)** — how far the cylinder extends each way
     from the hanger elevation (default 5 ft → a 10 ft tall column).
   - **Place Markers** / **Clear Markers Only** / **Cancel**.
4. A magenta cylinder is placed on every matching hanger, and the flagged
   hangers are added to the selection so you can tab through them.

## Marker geometry

- A vertical DirectShape cylinder, 6" diameter (3" radius), Generic Model
  category, bright magenta material (`SG_TypeReviewMarker`).
- Centered on the hanger's bounding-box center in plan; its Z spans
  `[center − reach, center + reach]`.
- Visible in both plan and 3D views automatically (no family file
  required).

## Re-running and clearing

- Each run clears any previous **Mark for Review** markers before placing
  new ones, so re-running with a different Type Code or reach won't
  accumulate stale markers.
- Running with **no hangers selected** offers to clear all existing
  review markers.
- **Clear Markers Only** in the dialog wipes them without placing new ones.

Markers are stamped with `ApplicationId = "SgRevitAddin"` and
`ApplicationDataId = "TypeReviewMarker"`, distinct from the Hanger Gap
Check (`HangerGapMarker`) and resize-drift (`MatchSizesDriftedMarker`)
markers, so each command manages only its own.

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Type Code (Hydratec)` (Text, instance) | Selection filter |

## See also

- [Hanger Gap Check](hanger-gap-check.md) — flags hangers whose
  top-of-pipe to structure gap exceeds a threshold (blue markers).
- [Change Type Code](change-type-code.md) — bulk-rewrite the Type Code
  once you've reviewed the flagged hangers.
