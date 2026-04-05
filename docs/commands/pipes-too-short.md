# Model Check: Pipes Too Short

**Command:** `PipesTooShortCommand`
**Domain:** ModelCheck
**Ribbon:** SSG FP Suite > Model Check > Pipes Too Short

## Purpose

Flags pipes shorter than the minimum fabricable nipple length for their diameter and connection type (threaded vs welded). Prevents short-pipe errors from reaching the fabrication shop.

## Workflow

1. Collects all pipes in the active view (or project, if no active view filter applies)
2. Classifies each pipe as threaded or welded based on pipe type name
3. Compares actual pipe length against the minimum for that diameter
4. Highlights or reports pipes that are too short
5. Reports total count of violations

## Minimum Length Table

### Threaded Pipes

| Nominal Diameter | Min Length |
|-----------------|------------|
| ½"  | 1.5" |
| ¾"  | 1.5" |
| 1"  | 1.5" |
| 1¼" | 2.0" |
| 1½" | 2.0" |
| 2"  | 2.5" |
| 2½" | 3.0" |
| 3"  | 3.5" |
| 4"  | 4.0" |
| 6"  | 4.5" |

### Welded Pipes

All sizes: 16" minimum (standard spool minimum for welded fabrication).

## Classification

- Pipe type name containing `Threaded` → threaded minimum lengths apply
- All other pipe types → welded minimum (16") applies

## Notes

- Uses `[Transaction(TransactionMode.ReadOnly)]` — no model changes are made
- Pipe outer diameter is read from the `Diameter` parameter
- Length is the pipe's actual curve length in Revit internal feet
