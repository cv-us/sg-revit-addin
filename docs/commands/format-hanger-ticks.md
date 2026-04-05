# AutoFormat — Hanger Ticks

**Ribbon location:** SSG FP Suite tab > Hangers panel > "Format Ticks"
**Command class:** `SSG_FP_Suite.Commands.Hangers.FormatHangerTicksCommand`

## What It Does

Formats all selected pipe hanger tick symbols so they face a consistent direction — either forward slash `/` or backslash `\`. Accounts for the pipe's rotation angle to ensure correct visual appearance regardless of pipe direction.

## How to Use

1. **Click "Format Ticks"** in the ribbon (Hangers panel)
2. **Select pipe accessories** — Click hanger symbols in the view, press Finish
3. **Choose direction** in the dialog:
   - **/ Forward Slash** — All ticks oriented as `/` (accounting for pipe angle)
   - **\ Backslash** — All ticks oriented as `\` (accounting for pipe angle)
   - **Default** — Reset all to unflipped (value 0)
4. **Click "Format Ticks"**

## How It Works

The command reads each hanger's rotation angle and calculates the correct "Flip Symbol" parameter value to achieve the desired visual direction:

| Pipe Angle Range | Base Flip Value | Forward Result | Back Result |
|-----------------|-----------------|----------------|-------------|
| 0° – 45°       | 0               | 1              | 0           |
| 45° – 135°     | 1               | 0              | 1           |
| 135° – 225°    | 0               | 1              | 0           |
| 225° – 315°    | 1               | 0              | 1           |
| 315° – 360°    | 0               | 1              | 0           |

- **Forward** = inverted base flip (ensures `/` appearance)
- **Back** = direct base flip (ensures `\` appearance)
- **Default** = always 0 (unflipped)

## Element Filtering

Only processes elements whose family name contains `"-Pipe Hanger"`. Other pipe accessories (valves, fittings, etc.) in the selection are silently skipped.

## Parameter Modified

| Parameter | Type | Values |
|-----------|------|--------|
| Flip Symbol | Integer | 0 (unflipped) or 1 (flipped) |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/format-ticks-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.
