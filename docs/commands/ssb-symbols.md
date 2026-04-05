# Annotation: SSB Symbols

**Command:** `SSBSymbolsCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > SSB Symbols

## Purpose

Places SSB hanger annotation symbols 1 ft from each end of selected pipe runs. Used to mark the ends of pipe segments that require SSB (seismic sway brace) installation for documentation and field coordination.

## Workflow

1. User selects pipes, or is prompted to pick them
2. For each pipe, two symbol instances are placed: one 1 ft from each end
3. Each symbol is rotated to align with the pipe's direction
4. Reports count of symbols placed

## Selection

- Accepts pre-selection of pipes
- If nothing is pre-selected, prompts the user to select pipes
- Searches for a family whose name contains `-Generic Annotation - SSB Hanger`

## Notes

- Symbols are placed as face-hosted annotation instances in the active view
- Rotation is applied via `ElementTransformUtils.RotateElement` to match pipe direction
- Missing or non-loaded SSB family will produce an error message
- Offset of 1 ft from pipe end is in Revit internal units (feet)
