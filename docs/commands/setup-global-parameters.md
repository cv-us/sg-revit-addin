# Setup: Global Parameters

**Command:** `SetupGlobalParamsCommand`
**Domain:** Setup
**Ribbon:** SSG FP Suite > Setup > Setup Global Parameters

## Purpose

Creates and initializes the full set of 86 "Dynamo Setting - " global parameters used as a configuration store by all SSG FP Suite commands (and other SSG FP Suite commands). Parameters that already exist are left untouched; only missing ones are created with their default values. This is a one-time (or run-anytime-safe) project setup step.

## Workflow

1. Scan for legacy seismic parameters stored as Int64 — delete if found (will be recreated as String)
2. Regenerate document if any deletions occurred
3. Iterate all 86 parameter definitions; skip any that already exist
4. Create missing parameters as String type with default values
5. Report summary: created count, existing count, fixed count

## Parameters Created (86 total)

All parameters use the prefix `Dynamo Setting - ` followed by the base name.

| Group | Count | Examples |
|-------|-------|---------|
| AutoSync | 12 | Categories - Floors, Clash Height Distance, Hanger Type - Floors |
| C-Channel / Z-Purlin | 2 | Hanger Type - >= 6 Inch, Hanger Type - <= 4 Inch |
| Flexible Drops | 2 | Standard Lengths, Tag Orientation |
| Linked Models | 4 | Architectural, Structural, Use All Links, ID's |
| Pipe Elevations | 14 | TOS Distance, AFF Distance, Skip Short Pipes, TOS Method, etc. |
| Pipe Hangers | 14 | Family, Height, Maximum Spacing, Position, Symbol, Type, etc. |
| Pipe Sleeves | 3 | Filters, Seismic, Wall Types |
| Reference Level | 1 | Reference Level |
| Seismic Bracing | 9 | Brace Types, Lateral/Longitudinal Family/Spacing/Orientation, Pipe Filter |
| Threaded Lines | 7 | Hanger Assembly (4 categories), Distance From End, Minimum Length, Categories |
| Trapeze Hangers | 12 | DualPipe Family/Type, Hanger Family/Type/Spacing, Position, etc. |
| Trimble / Unistrut | 4 | Trimble Hanger Type, Two Bays, Unistrut Extension From/Distance |
| Z-Purlin | 2 | Hanger Type - >= 6 Inch, Hanger Type - <= 4 Inch |

## Seismic Int64 Fix

Three seismic parameters may have been created as Int64 (integer) type by older scripts:
- Seismic Lateral Brace Max Distance From End Of Main
- Seismic Lateral Brace Maximum Spacing
- Seismic Longitudinal Brace Maximum Spacing

If detected as `IntegerParameterValue`, they are deleted and recreated as String type to match the expected configuration format.

## Idempotent Behavior

- Safe to run multiple times — existing parameters are never modified
- Only missing parameters are created
- Legacy Int64 parameters are fixed on each run if still present

## No Dialog

This is a one-click command with no user inputs. The summary dialog reports results after execution.

## Summary Dialog

Reports:
- Number of new parameters created
- Number of parameters that already existed
- Total expected (86)
- Number of legacy seismic parameters fixed (if any)

## Notes

- The C# version uses `GlobalParametersManager.FindByName()` and `GlobalParameter.Create()` directly
- Parameter type is `SpecTypeId.String.Text` for both Revit 2024 and 2025+
