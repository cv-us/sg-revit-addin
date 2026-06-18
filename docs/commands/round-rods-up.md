# Round Rods Up

**Command:** `RoundRodLengthsCommand`
**Domain:** Hangers
**Ribbon:** SG ♈ > Hangers > Round Rods Up

## Purpose

Rounds the **Rod Length** of every selected hanger **UP** to the nearest half inch. Rods that already sit on a full or half inch are left untouched, and rods are **never** rounded down. This cleans up the odd fractional rod lengths that raybounce / surface sync produce so the fabrication stocklist reads in clean half-inch increments.

## Examples

| Current Rod Length | Result |
|--------------------|--------|
| 8 41/256" (8.160") | **8 1/2"** |
| 11 17/32" (11.531") | **12"** |
| 8 1/2" | unchanged |
| 9" | unchanged |

## Workflow

1. Select hangers (pre-selection — no pick prompt).
2. Run the command. It filters the selection to recognised hanger families and pre-counts how many rods are off a half-inch boundary.
3. Dialog confirms the count and offers one option:
   - **Also set Y Grip to match the new Rod Length** (default on, remembered between runs).
4. Each eligible rod is rounded up; the summary reports the counts.

## Rounding Logic

Rod Length is stored in feet (Revit internal units). The math is done in inches:

```
halves   = lengthInches * 2          // length in half-inch units
roundedUp = ceil(halves - tol)       // round up, tolerance absorbs float noise
targetIn = roundedUp / 2
```

A small tolerance (~0.0005") means a rod already on a half-inch boundary computes back to itself and is reported as "already on a half inch" rather than bumped to the next one.

## Parameters Written

| Parameter | Value | Condition |
|-----------|-------|-----------|
| `Rod Length` | Rounded-up half inch (feet) | When current length is off a half inch |
| `Y Grip` | Same as new Rod Length | Only when the "Also set Y Grip" option is checked |

## Hanger Identification

`OST_PipeAccessory` family instances whose family name contains any of:
`-Pipe Hanger`, `-Pipe Trapeze`, `-Basic Adjustable`, `Adjustable Ring Hanger`, `Ring Hanger`.

## Settings Memory

The "Also set Y Grip" checkbox is remembered between runs (and across Revit restarts) via the shared `DialogMemory` store (`%AppData%\SgRevitAddin\dialog-memory.json`).

## See Also

- **Uniform Rods** — set rod length to a single uniform value for a chosen Type Code under a cutoff
- **Sync Raybounce / Sync Surface** — produce the raw rod lengths this command cleans up
