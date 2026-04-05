# Export: Import AS Pipes

**Command:** `ImportASPipesCommand`
**Domain:** Export
**Ribbon:** SSG FP Suite > Export > Import AS Pipes

## Purpose

Imports pipe geometry from an AutoSPRINK CSV export and creates Revit pipes. Bridges the AutoSPRINK hydraulic design workflow with Revit by batch-creating pipes from exported coordinates without manual re-drafting.

## Workflow

1. User browses for an AutoSPRINK CSV file
2. Dialog: select level, pipe type, and piping system type
3. CSV is parsed — header row skipped, start/end XYZ read from columns 1–6
4. Degenerate rows (zero-length, parse errors) are skipped
5. Revit pipes created in a single transaction
6. Result dialog reports count of pipes created and rows skipped

## CSV Format

AutoSPRINK export format (coordinates in inches):

| Col 0 | Col 1 | Col 2 | Col 3 | Col 4 | Col 5 | Col 6 |
|-------|-------|-------|-------|-------|-------|-------|
| name  | X1    | Y1    | Z1    | X2    | Y2    | Z2    |

Row 0 is the header and is always skipped. All coordinate values are divided by 12 to convert inches to Revit internal feet.

## Dialog Options

| Field | Description |
|-------|-------------|
| Associate with Level | Level used when creating pipes |
| Pipe Type | Pipe type family to assign; defaults to name containing `-SS FP Mains` |
| Piping System | MEP system type; defaults to name containing `Fire Protection Wet` |

Only pipe-compatible system classifications are shown (supply/return hydronic, fire protection wet/dry/preaction/other, other pipe).

## Notes

- Zero-length segments (start and end within 0.001 ft) are silently skipped
- Pipe creation failures are caught per-segment and counted as skipped
- Pipes are created with `Pipe.Create()` at the exact CSV coordinates; no diameter is set from the CSV (use the pipe type default)
