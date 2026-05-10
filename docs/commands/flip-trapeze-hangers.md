# Hangers: Flip Trapeze Hangers

**Command:** `FlipTrapezeHangersCommand`
**Domain:** Hangers
**Ribbon:** SG Revit Addin > Hangers > Flip Trapeze

## Purpose

Rotates selected trapeze hanger family instances 180° around their vertical Z-axis and swaps the Rod 1 / Rod 2 parameter values to match the new physical orientation.

When a trapeze hanger is flipped, the physical rods swap sides — what was "Rod 1" is now on the "Rod 2" side. This command corrects the stored elevation and offset parameters so the data remains accurate after the flip.

## Workflow

1. User pre-selects trapeze hangers, or is prompted to pick them
2. Each hanger is rotated 180° around a vertical axis through its location point
3. Rod 1 and Rod 2 parameter values are swapped to reflect the new orientation

No dialog is shown — the operation runs immediately on the selection.

## Selection

- Accepts pre-selection: if trapeze hangers are already selected when the command runs, they are used directly
- If nothing matching is pre-selected, prompts the user to pick pipe accessories
- Filters to family instances whose family name contains `-Pipe Trapeze` (case-insensitive)

## Parameters Swapped

| Before Flip | After Flip |
|-------------|------------|
| Rod 1 Top Elevation | Rod 2 Top Elevation |
| Rod 2 Top Elevation | Rod 1 Top Elevation |
| Rod 1 Offset | Rod 2 Offset |
| Rod 2 Offset | Rod 1 Offset |

Parameters are only written if they exist and are not read-only. Missing parameters are silently skipped.

## Notes

- Rotation is applied as a delta of π radians (180°) via `ElementTransformUtils.RotateElement`
- The axis of rotation is a vertical line through the hanger's `LocationPoint`
- Rod parameter values are read before rotation and written after, so the swap reflects the geometry correctly
- Hangers without a `LocationPoint` location (e.g. face-hosted instances) are skipped and counted as failed

