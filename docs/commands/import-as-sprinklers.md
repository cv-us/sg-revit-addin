# Export: Import AS Sprinklers

**Command:** `ImportASSprinklersCommand`
**Domain:** Export
**Ribbon:** SG Revit Addin > Export > Import AS Sprinklers

## Purpose

Imports sprinkler head locations from an AutoSPRINK CSV export and places Revit sprinkler family instances at those coordinates. Bridges the AutoSPRINK hydraulic design workflow with Revit by batch-placing sprinklers without manual re-drafting.

## Workflow

1. User browses for an AutoSPRINK CSV file
2. Dialog: select level, sprinkler family type, and optional Z offset
3. CSV is parsed — header row skipped, XYZ read from columns 1–3
4. Rows with parse errors are skipped
5. Sprinkler instances placed in a single transaction using `doc.Create.NewFamilyInstance()`
6. Result dialog reports count of sprinklers placed and rows skipped

## CSV Format

AutoSPRINK export format (coordinates in inches):

| Col 0 | Col 1 | Col 2 | Col 3 |
|-------|-------|-------|-------|
| name  | X     | Y     | Z     |

Row 0 is the header and is always skipped. All coordinate values are divided by 12 to convert inches to Revit internal feet.

## Dialog Options

| Field | Description |
|-------|-------------|
| Associate with Level | Level used when placing instances |
| Sprinkler Family Type | Family and type shown as `FamilyName : TypeName`; lists all loaded sprinkler category families |
| Z Offset from Level (in) | Additional vertical offset in inches added to each CSV Z coordinate |

## Notes

- Only families in the `OST_Sprinklers` category are shown in the family type list
- The family symbol is activated before placement if not already active
- Placement failures are caught per-point and counted as skipped
- The Z offset field allows correcting for level datum differences between AutoSPRINK and Revit
