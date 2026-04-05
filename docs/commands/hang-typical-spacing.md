# Auto Hang — Typical Spaced Runs (Hangers to Decks)

**Ribbon location:** SSG FP Suite tab > Hangers panel > "Hang Spaced Runs"
**Command class:** `SSG_FP_Suite.Commands.Hangers.HangTypicalSpacingCommand`

## What It Does

Places pipe hangers at regular intervals along straight pipe runs using typical hanger spacing rules. Unlike the structural crossing or CAD line commands which place hangers at specific intersection points, this command **distributes hangers evenly** along each pipe based on a maximum spacing distance.

Rod length for each hanger is determined by raybounce (ReferenceIntersector) — shooting a ray straight up from each hanger location to find the nearest structural element (floor deck, roof, beam) above.

## How to Use

1. **Click "Hang Spaced Runs"** in the ribbon (Hangers panel)
2. **Select pipes** — Click pipe runs in the model, press Finish when done
   - Near-vertical pipes (slope > 60 degrees) and very short pipes (< 2') are automatically filtered out
3. **Fill in the dialog:**
   - **Pipe Type Filter** — "ALL Pipes" or select a specific pipe type to limit processing
   - **Hanger Family** — Pick from pipe accessory families loaded in the project
   - **Spacing Mode:**
     - *Evenly spaced along pipe run length* — divides the pipe into equal segments not exceeding the max spacing
     - *Use exact spacing distance* — places hangers at precisely the specified distance apart
   - **Maximum Hanger Spacing:**
     - 10'-6" (default per NFPA 13)
     - 12'-0"
     - 15'-0"
     - Custom (enter any distance in feet)
   - **Type Code (Hydratec)** — Hanger assembly type code (default: "01")
   - **Structural Link** — Select a linked Revit model containing structural elements, or "(None)" to skip raybounce
   - **Max Clash Height (ft)** — Maximum vertical distance to search for structure above (default: 10')
4. **Click "Place Hangers"** — The command runs and shows a summary when done

## Spacing Modes Explained

### Evenly Distributed (Recommended)
Divides the pipe's usable length (minus 6" from each end) by the max spacing to determine how many hangers are needed, then spaces them **equally** along the entire run. This ensures uniform spacing and avoids having one span much shorter than the rest.

**Example:** A 30-foot pipe with 10'-6" max spacing:
- Usable length: 29' (30' minus 6" from each end)
- Number of spans: ceil(29 / 10.5) = 3
- Actual spacing: 29 / 3 = 9.67' (4 hangers at 0.5', 10.17', 19.83', 29.5')

### Exact Spacing
Places hangers at precisely the specified distance apart, starting 6" from the pipe start. The last span may be shorter than the others.

**Example:** Same 30-foot pipe with 10'-6" exact spacing:
- Hangers at: 0.5', 11.0', 21.5' (3 hangers, last span is 8.5')

## What Gets Placed

For each pipe, N hangers are placed along its length. Each hanger gets:
- Rotated to align with the pipe direction
- Parameters set:
  - **Nominal Diameter** — matches the pipe's diameter
  - **Rod Length** — distance to structure above (via raybounce), or pipe diameter if no structure found
  - **Elevation from Level** — calculated from placement point and reference level
  - **Type Code (Hydratec)** — from dialog input
  - **Additional Stocklist Information (Hydratec)** — "CON1,{pipe ElementId}"
  - **C Clamp** — set to 0 (hidden)
  - **Comments** — hanger family name

## Tips

- **10'-6" is the NFPA 13 default** for most pipe sizes. Use this unless your AHJ or project specs require different spacing.
- **Use "Evenly Distributed"** for cleaner-looking results — it avoids the short last span that "Exact Spacing" produces.
- **Pipe Type Filter** — If you have a mix of mains and branchlines selected, use the type filter to process only specific pipe types (e.g., "Lines - Threaded" for branchlines only).
- **No structural link?** — If you skip the structural link selection, hangers are still placed at the correct spacing but rod length defaults to the pipe diameter. You'd need to manually adjust rod lengths.
- **Works with any pipe type** — Unlike the Downstream Ends command which filters for threaded lines only, this command works with any pipe type in the model.

## Changing the Icon

The button icon is a 32x32 PNG at `src/Shared/UI/Resources/icons/hang-spacing-32.png` (and 16x16 version). To change it:

1. Create a new 32x32 PNG (and optionally 16x16) in any image editor
2. Save it with the same filename in the icons folder
3. Rebuild and redeploy
