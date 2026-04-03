# Auto Hang at Structural Framing

**Ribbon location:** SSG FP Suite tab > Hangers panel > "Hang at Structural"
**Command class:** `SSG_FP_Suite.Commands.Hangers.AutoHangAtStructuralCommand`
**Migrated from:** `Auto Hang - Pipes Crossing Structural Framing.dyn` (V25)

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

## Differences from the Dynamo Version

| Feature | Dynamo Script (V25) | Plugin Command |
|---------|-------------------|----------------|
| Speed | Slow — geometry projection per pair | Fast — analytical 2D intersection with vertical pre-filter |
| Dependencies | Data-Shapes + Genius Loci packages | Zero external dependencies |
| UI | 4+ sequential dialogs | 1 consolidated dialog |
| Transactions | Multiple (per Dynamo node) | Single transaction (all-or-nothing) |
| Linked model support | Yes | Yes (same approach, cleaner implementation) |
| Local model support | Limited | Full support with checkbox toggle |
| Clamp angle calc | Complex node chain | Single helper method |
| Flange offsets | Hardcoded in nodes | Named constants, easy to adjust |
| Hydratec-specific params | Yes (stocklist info) | Removed (generic, configurable) |

## Changing the Icon

The button icon is a 32x32 PNG at `src/Shared/UI/Resources/icons/hang-struct-32.png` (and 16x16 version). To change it:

1. Create a new 32x32 PNG (and optionally 16x16) in any image editor
2. Save it with the same filename in the icons folder
3. Rebuild and redeploy
