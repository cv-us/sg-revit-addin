# Auto Hang — Threaded Branchlines (Downstream Ends)

**Ribbon location:** SG Revit Addin tab > Hangers panel > "Hang Downstream"
**Command class:** `SgRevitAddin.Commands.Hangers.HangDownstreamCommand`

## What It Does

Places pipe hangers at the downstream ends of threaded branchline pipes — the end opposite from the POL (pipe-o-let) connection to the main. Uses raybounce (ReferenceIntersector) to shoot a ray straight up from each hanger point, find the nearest structural element above, and set the rod length to that distance. Hanger type codes are automatically assigned based on what structural category the ray hits (roof, floor deck, structural framing, or stairs).

For small-diameter pipes (< 1.5") longer than 12 feet, an additional midpoint hanger is automatically placed.

## How to Use

1. **Click "Hang Downstream"** in the ribbon (Hangers panel)
2. **Select threaded line pipes** — Click pipes in the model, press Finish when done
   - Only pipes with type name containing "Lines - Threaded" or "Sched 40 Line" are processed
   - Near-vertical pipes (slope > 60 degrees) are automatically filtered out
3. **Fill in the dialog:**
   - **Hanger Family** — Pick from pipe accessory families loaded in the project
   - **Type Codes** — One per structural category:
     - **Roofs** — default "03A"
     - **Floor Decks** — default "05"
     - **Structural Framing** — default "01"
     - **Stairs** — default empty
   - **Distance from End of Pipe (inches)** — How far from the downstream end to place the hanger (default: 12")
   - **Min Pipe Length to Hang (inches)** — Pipes shorter than this are skipped (default: 18")
   - **C-Clamp Visibility** — Hide (default) or Show
4. **Click "Place Hangers"** — The command runs and shows a summary when done

## What Gets Placed

For each qualifying pipe:
- A hanger family instance at the downstream end (offset by the specified distance)
- For small pipes (< 1.5" diameter) longer than 12': an additional midpoint hanger
- Each hanger is rotated to align with the pipe direction
- Parameters set:
  - **Nominal Diameter** — matches the pipe's diameter
  - **Rod Length** — distance from hanger to the nearest structure above (via raybounce), or pipe diameter if no structure found
  - **Elevation from Level** — calculated from the placement point and reference level
  - **Type Code** — assigned based on what structural category the raybounce hits
  - **C Clamp** — visibility toggle from dialog
  - **Comments** — name of the structural element above (if raybounce hit)

## How the Raybounce Works

The command creates (or reuses) a 3D view called "3D-RayBounce" with specific category visibility:
- **Visible:** Pipes, Pipe Accessories, Pipe Fittings, Floors, Stairs, Structural Framing, Roofs
- **Hidden:** Everything else (to avoid false hits on ductwork, furniture, etc.)

From each hanger location, a ray is shot straight up (Z direction). The nearest element hit determines:
1. **Rod length** — the vertical distance to the structure
2. **Type code** — mapped from the hit element's category

## How Downstream End is Detected

The command uses the Revit ConnectorManager API to inspect what's connected to each end of the pipe:
- If a **POL fitting** (pipe-o-let, O-LET, OLET) is found at one end, that end is "upstream" (connected to the main)
- The **opposite end** is "downstream" — where the hanger goes
- If no POL is found, the pipe's endpoint (index 1) is used as the default downstream end

## Tips

- **Pre-select carefully** — Only threaded line pipes are processed, but selecting the right pipes upfront saves time
- **Adjust distance from end** — 12" is standard, but you may want shorter (6-8") for tight spaces or longer for specific hanger requirements
- **Min pipe length** — Set this to avoid placing hangers on nipples and very short stubs. 18" is a safe default.
- **Check the 3D-RayBounce view** — If hangers are getting wrong rod lengths, open the "3D-RayBounce" view and verify the right categories are visible. The command creates this view automatically on first run.
- **No structure above?** — If the raybounce doesn't hit anything (e.g., exposed to sky), rod length defaults to the pipe's diameter value. You'll want to manually adjust these.

## Changing the Icon

The button icon is a 32x32 PNG at `src/Shared/UI/Resources/icons/hang-downstream-32.png` (and 16x16 version). To change it:

1. Create a new 32x32 PNG (and optionally 16x16) in any image editor
2. Save it with the same filename in the icons folder
3. Rebuild and redeploy

## See Also

- **[Choosing a Command](choosing-a-command.md)** — full comparison of all similar commands
- **Hang Typical Spacing** — for full-run hanger spacing (not just branchline ends)
- **Hang at Structural** — for hangers at beam crossings on any pipe type
