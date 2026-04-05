# Annotation: Beam Penetration Symbols

**Command:** `BeamPenetrationSymbolsCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Beam Penetrations

## Purpose

Places beam penetration annotation symbols at pipe-grid or pipe-detail line crossing points in the active view. Used to mark where pipes pass through structural beams for documentation and coordination.

## Workflow

1. User selects pipes and grid lines (or detail lines) in the active view, or is prompted to pick them
2. Command finds 2D crossing points between selected pipe lines and reference lines
3. Places a beam penetration annotation symbol at each crossing
4. Reports count of symbols placed

## Selection

- Accepts pre-selection of pipes plus grid lines or detail lines
- If nothing matching is pre-selected, prompts the user to pick elements
- Searches for a family whose name contains `Beam Penetration`

## Notes

- Crossing detection is performed in 2D (plan view XY plane)
- The annotation is placed at the intersection point of the pipe centerline and the reference line
- Symbols are oriented to align with the pipe direction
- Missing or non-loaded beam penetration family will produce an error message
