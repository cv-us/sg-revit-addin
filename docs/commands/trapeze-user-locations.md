# Auto Trapeze Hang — Standard Pipe Trapeze — User Locations

**Ribbon location:** SG Revit Addin tab > Hangers panel > "Trapeze User Loc"
**Command class:** `SgRevitAddin.Commands.Hangers.TrapezeUserLocationsCommand`

## What It Does

Places standard pipe trapeze hangers at user-specified locations marked by detail lines. The user draws detail lines perpendicular to (or crossing) pipes where they want trapeze hangers. Each intersection of a detail line with a pipe becomes a trapeze location. Two rods anchor to the nearest structural beam above.

This is the user-locations variant of the Auto Trapeze Hang command — it replaces auto-spacing with precise manual control via detail lines.

## How to Use

1. **Draw detail lines** across pipes in plan view where you want trapeze hangers
2. **Click "Trapeze User Loc"** in the ribbon (Hangers panel)
3. **Select BOTH the pipes AND the detail lines**, then press Finish
4. **Fill in the dialog:**
   - **Pipe Type Filter** — "ALL Pipes" or specific pipe type
   - **Trapeze Family** — Pipe accessory family (pre-selects families with "Trapeze" in name)
   - **Rod Positioning** — Closest Side of Structural (default) or Middle of Structural
   - **Pipe Hanger Type (Hydratec)** — Type code for pipe hangers (default: "R3R")
   - **Trapeze Type (Hydratec)** — Type code for trapeze (default: "19A")
   - **Distance Down to Trapeze (in)** — Inches below supported pipe for cross-member (default: 7")
   - **Max Clash Height (ft)** — Vertical search range for structural above (default: 10')
   - **Structural Source** — Local framing or linked Revit model
5. **Click "Place Trapezes"**

## How It Works

1. User selection is split into pipes (OST_PipeCurves) and detail lines (OST_Lines)
2. Steep/vertical pipes (slope > 60 degrees) are filtered out
3. Each detail line is intersected with each pipe in 2D (plan view)
4. Intersection points are projected to the pipe's 3D centerline for correct Z
5. At each intersection, the nearest structural beam above is found
6. Rod anchor points are calculated on both sides of the beam centerline
7. Trapeze family is placed with two-rod offsets, top elevations, and rotation

## What Gets Placed

For each detail line x pipe intersection:

| Parameter | Value |
|-----------|-------|
| Rod 1 Top Elevation | Pipe-to-level + Rod 1 length (shorter rod) |
| Rod 1 Offset | Horizontal distance from pipe center to Rod 1 anchor |
| Rod 2 Top Elevation | Pipe-to-level + Rod 2 length (longer rod) |
| Rod 2 Offset | Horizontal distance from pipe center to Rod 2 anchor |
| Trapeze Pipe Elevation | Cross-member elevation (pipe Z - distance down) |
| Supported Pipe Elevation | Pipe centerline elevation |
| Supported Pipe Rotation Angle | Angle between pipe direction and trapeze direction |
| Nominal Diameter | Pipe diameter |
| Type Code (Hydratec) | Combined: "R3R;19A" (PipeHangerType;TrapezeType) |
| Additional Stocklist Information (Hydratec) | "CON1,{PipeElementId}" |
| Comments | Structural beam name |

## When to Use This vs Auto-Spaced Trapeze

| Scenario | This Command | Auto-Spaced Trapeze |
|----------|-------------|-------------------|
| Precise control over trapeze positions | Draw lines exactly where needed | Auto-calculated |
| Near valves, equipment, supports | Place around obstructions | May conflict with equipment |
| Regular spacing along long runs | Tedious to draw many lines | Designed for this |
| Custom irregular spacing | Draw lines at any interval | Fixed spacing pattern |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/hang-trapeze-ul-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.

