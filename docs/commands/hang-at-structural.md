# Auto Hang at Structural Framing

**Ribbon location:** SG Revit Addin tab > Hangers panel > "Hang at Structural"
**Command class:** `SgRevitAddin.Commands.Hangers.HangAtStructuralCommand`

## What It Does

Places a pipe hanger family instance at every point where a selected pipe crosses a structural framing member (beam, joist, girder). Works with both local structural elements and elements from linked Revit models. Automatically calculates clamp angles, rod lengths, and flange offsets.

## How to Use

1. **Click "Hang at Structural"** in the ribbon (Hangers panel)
2. **Select pipes** — Click pipes in the model, press Finish when done
   - Near-vertical pipes (slope > 60 degrees) are automatically filtered out
3. **Fill in the dialog:**
   - **Hanger Family** — Pick from pipe accessory families loaded in the project
   - **Hanger Type Code** — Code for the hanger type (default: "01")
   - **Widemouth Type Code** — Code for widemouth type (default: "01A")
   - **Attach Hangers To** — BOTTOM (default) or TOP of structural member
   - **C-Clamp Visibility** — Hide (default) or Show the C-clamp component
   - **Max Clash Height (ft)** — Vertical proximity filter; pipes farther than this from a structural member are skipped (default: 10 ft)
   - **Structural Source** — Either check "Use LOCAL structural framing" or select a linked Revit model from the dropdown
4. **Click "Place Hangers"** — The command runs and shows a summary when done

## What Gets Placed

For each pipe/structural-member intersection:
- A hanger family instance at the crossing point (at the pipe's Z elevation)
- Rotated to align with the pipe direction
- Parameters set:
  - **Nominal Diameter** — matches the pipe's diameter
  - **Rod Length** — set to the pipe's diameter value
  - **Elevation from Level** — calculated from the placement point and reference level
  - **Top Accessory Offset** — flange offset (0.069 ft for top, 0.033 ft for bottom)
  - **Type Code** — widemouth type code from your input
  - **C Clamp** — visibility toggle (0 = hide, 1 = show)
  - **Clamp Angle** — calculated angle from hanger to structural member centerline
  - **Comments** — name of the structural member (e.g., "W-Wide Flange : W12x26")

## Structural Member Filtering

The command automatically filters structural framing to relevant beam-like members:

**Included types:** BEAM, GIRDER, JOIST, TOP_CHORD, PURLIN, RIGID FRAME, W-WIDE, and any W-shape (W12x26, W8x10, etc.)

**Excluded types:** L-ANGLE, LL-DOUBLE ANGLE, CHANNEL, DOOR, FLAT, GRADE, HP-BEARING PILE, ROUND BARS, SQUARE BARS, TEE

## Tips

- **Local vs. Linked** — If structural steel is in the same Revit model, check "Use LOCAL structural framing." If it's in a linked structural model (common in multi-discipline projects), select the link from the dropdown.
- **Max Clash Height** — This is a vertical proximity filter in feet. A pipe must be within this distance of a structural member's top or bottom flange to be considered a crossing. Increase it if you're missing intersections on deep beams; decrease it to reduce false positives.
- **Attach to Bottom vs. Top** — "Bottom" places the hanger reference point just below the bottom flange (typical for overhead pipes). "Top" places it just below the top flange (for pipes running through steel).
- **Clamp angle** — The command automatically calculates the angle from the hanger's pipe-aligned direction to the structural member's centerline. This ensures C-clamps are oriented correctly toward the steel.
- **Performance** — Uses analytical 2D line-line intersection math with vertical proximity pre-filtering. Handles hundreds of pipes crossed with thousands of structural members in seconds.

## Changing the Icon

The button icon is a 32x32 PNG at `src/Shared/UI/Resources/icons/hang-struct-32.png` (and 16x16 version). To change it:

1. Create a new 32x32 PNG (and optionally 16x16) in any image editor
2. Save it with the same filename in the icons folder
3. Rebuild and redeploy

## See Also

- **[Choosing a Command](choosing-a-command.md)** — full comparison of all similar commands
- **Hang at CAD Lines** — same concept but uses linked CAD linework instead of Revit structural elements
- **Hang Typical Spacing** — for evenly-spaced hangers along runs (not just at beam crossings)
- **Hang Parallel Structural** — for pipes running parallel to beams where there are no crossings
- **Hang User Locations** — for manual placement at detail-line locations
- **Hang Downstream** — for branchline end hangers only
