# Auto Hang — User Locations (Underside of Structural — Raybounce)

**Ribbon location:** SSG FP Suite tab > Hangers panel > "Hang User Loc"
**Command class:** `SSG_FP_Suite.Commands.Hangers.HangUserLocationsCommand`

## What It Does

Places pipe hangers at user-specified locations marked by detail lines in plan view. The user draws detail lines perpendicular to (or crossing) pipes wherever they want hangers. Each intersection of a detail line with a pipe becomes a hanger location. Rod length is determined by raybounce upward to the nearest structural element.

This gives precise manual control over hanger placement — useful near supports, valves, equipment, or anywhere auto-spacing is inappropriate.

## How to Use

1. **Draw detail lines** across pipes in plan view where you want hangers placed
2. **Click "Hang User Loc"** in the ribbon (Hangers panel)
3. **Select BOTH the pipes AND the detail lines**, then press Finish
4. **Fill in the dialog:**
   - **Pipe Type Filter** — "ALL Pipes" or specific pipe type
   - **Hanger Family** — Pipe accessory family to place
   - **Type Code (Hydratec)** — Hanger type code (default: "01")
5. **Click "Place Hangers"**

## How It Works

1. User selection is split into pipes (OST_PipeCurves) and detail lines (OST_Lines)
2. Steep/vertical pipes (slope > 60°) are filtered out
3. Each detail line is intersected with each pipe in 2D (plan view)
4. Intersection points are projected to the pipe's 3D centerline for correct Z
5. At each intersection, a ray is shot upward to find the nearest structural element
6. Rod length = distance from pipe to structure above
7. Hanger is placed at the intersection, rotated to match pipe direction

## What Gets Placed

For each detail line × pipe intersection:
- **Nominal Diameter** — pipe diameter
- **Rod Length** — raybounce distance to structure above (defaults to pipe diameter if no hit)
- **Elevation from Level** — from placement point to reference level
- **Type Code (Hydratec)** — user-specified type code
- **Additional Stocklist Information (Hydratec)** — "CON1,{PipeElementId}"
- **C Clamp** — off (0.0) by default
- **Comments** — name of the structural element above (from raybounce)

## When to Use This vs Other Hanger Commands

| Scenario | This Command | Typical Spacing | Downstream |
|----------|-------------|----------------|------------|
| Precise control over hanger positions | ✅ Draw lines exactly where needed | ❌ Auto-spaced | ❌ End-of-pipe only |
| Near valves, equipment, supports | ✅ Place around obstructions | ❌ May conflict with equipment | ❌ Not applicable |
| Regular spacing along long runs | ❌ Tedious to draw many lines | ✅ Designed for this | ❌ Not applicable |
| Branchline downstream ends | ❌ Overkill | ❌ Wrong purpose | ✅ Designed for this |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/hang-userloc-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.
