# Auto Hang at CAD Lines

**Ribbon location:** SSG FP Suite tab → Hangers panel → "Hang at CAD Lines"
**Command class:** `SSG_FP_Suite.Commands.Hangers.HangAtCADLinesCommand`

## What It Does

Places a pipe hanger family instance at every point where a selected pipe crosses a line from a linked CAD file. The CAD lines typically represent structural steel members (beams, joists, bar joists) that pipes need to be hung from.

## How to Use

1. **Click "Hang at CAD Lines"** in the ribbon (Hangers panel)
2. **Select pipes** — Click pipes in the model, press Finish when done
   - Near-vertical pipes (slope > 60°) are automatically filtered out
3. **Select the linked CAD file** — Click the CAD link that contains structural lines
4. **Fill in the dialog:**
   - **Hanger Family** — Pick from pipe accessory families loaded in the project
   - **Type Code** — Code for the hanger type (default: "01")
   - **Rod Length (inches)** — Hanger rod length (default: 12")
   - **Min Line Length (ft)** — Ignore CAD lines shorter than this (default: 4'). Filters out dimension ticks, short segments, etc.
   - **CAD Layers** — Check which layers contain the structural lines. Layer names are shown with line counts.
5. **Click "Place Hangers"** — The command runs and shows a summary when done

## What Gets Placed

For each pipe/CAD-line intersection:
- A hanger family instance at the crossing point
- Rotated to align with the pipe direction
- Parameters set:
  - **Nominal Diameter** — matches the pipe's diameter
  - **Rod Length** — from your input (converted to feet)
  - **Type Code** — from your input
  - **Elevation from Level** — calculated from the placement point and reference level

## Tips

- **Select pipes by window** — You can window-select a large group of pipes. The command handles filtering.
- **Choose layers carefully** — If the CAD file has many layers, only check the ones with actual structural lines. This avoids placing hangers at random CAD geometry.
- **Min line length filter** — Set this higher (6-8') if you're getting false positives from short CAD fragments. Set to 0 to process all lines.
- **Performance** — This command uses analytical 2D math for intersections instead of Revit geometry operations. It handles hundreds of pipes × thousands of CAD lines in seconds.

## Changing the Icon

The button icon is a 32x32 PNG at `src/Shared/UI/Resources/icons/hang-cad-32.png` (and 16x16 version). To change it:

1. Create a new 32x32 PNG (and optionally 16x16) in any image editor
2. Save it with the same filename in the icons folder
3. Rebuild and redeploy
