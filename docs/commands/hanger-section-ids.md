# AutoInsert — Hanger Section IDs

**Ribbon location:** SG Revit Addin tab > Hangers panel > "Section IDs"
**Command class:** `SgRevitAddin.Commands.Hangers.HangerSectionIDsCommand`

## What It Does

Populates the "Section_ID (Hydratec)" parameter on selected pipe hangers with a formatted string combining the rod length and type code. This value displays in hanger tags and auto-updates with cut lengths when the AutoList process is run.

## How to Use

1. **Click "Section IDs"** in the ribbon (Hangers panel)
2. **Select pipe accessories** — Click hangers in the model, press Finish
3. Command runs automatically — no additional dialog needed

## Section ID Format

```
(WHOLE_INCHES#TYPE_CODE FRACTION)
```

**Examples:**
- `(12#R3R)` — 12" rod, type code R3R
- `(12#R3R¼)` — 12¼" rod, type code R3R
- `(8#04½)` — 8½" rod, type code 04
- `(15#19A¾)` — 15¾" rod, type code 19A

## Parameters

| Action | Parameter | Source |
|--------|-----------|--------|
| Read | Rod Length | Hanger instance (feet, internal units) |
| Read | Type Code (Hydratec) | Hanger instance |
| Read | Family Name | Used to filter valid hanger families |
| Write | Section_ID (Hydratec) | Formatted string |

## Rod Length Conversion

- Rod Length is stored in Revit internal units (feet)
- Converted to inches: `feet × 12`
- Rounded to nearest quarter-inch: `Round(inches / 0.25) × 0.25`
- Fraction displayed as Unicode: ¼ (U+00BC), ½ (U+00BD), ¾ (U+00BE)

## Recognized Hanger Families

Elements are filtered by family name containing any of:
- `-Pipe Hanger`
- `-Pipe Trapeze`
- `Adjustable Ring Hanger`

Other pipe accessories (valves, etc.) in the selection are silently skipped.

## Changing the Icon

The button shares `hangers-32.png`. Replace with a unique icon if desired.
