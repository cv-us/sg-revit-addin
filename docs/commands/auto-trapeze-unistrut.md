# Auto Trapeze Hang — Unistrut — Auto Spaced

**Ribbon location:** SSG FP Suite tab > Hangers panel > "Unistrut Trapeze"
**Command class:** `SSG_FP_Suite.Commands.Hangers.AutoTrapezeUnistrutCommand`
**Migrated from:** `Auto Trapeze Hang - Unistrut - Auto Spaced.dyn` (V28)

## What It Does

Places unistrut pipe trapeze hangers at auto-spaced intervals along pipe runs. Like the standard pipe trapeze, each hanger has two rods anchoring to structural framing above. The key difference is the **unistrut channel** that spans across the rods, with configurable extension distance beyond each rod.

## How It Differs from Standard Pipe Trapeze

| Feature | Standard Pipe Trapeze | Unistrut Trapeze |
|---------|----------------------|-----------------|
| Family | "Pipe Trapeze Hanger - Single Pipe - Standard" | "Pipe Trapeze Hanger - Unistrut" |
| Channel extension params | None | Rod 1/2 Unistrut Extension |
| Top elevation param | Trapeze Pipe Elevation | Top of Unistrut Elevation |
| Extension measured from | N/A | Framing center or hanger rod |
| Default type codes | Pipe "R3R", Trapeze "19A" | Pipe "04", Trapeze "21F" |

## How to Use

1. **Click "Unistrut Trapeze"** in the ribbon (Hangers panel)
2. **Select pipes** — Click pipe runs, press Finish
3. **Fill in the dialog:**
   - **Pipe Type Filter** — "ALL Pipes" or specific type
   - **Unistrut Trapeze Family** — Pre-selects families with "Unistrut" in name
   - **Rod Positioning** — Closest Side of Structural (default) or Middle
   - **Spacing Mode** — Evenly distributed (default) or exact spacing
   - **Max Spacing** — 10'-6" (default), 12'-0", 15'-0", or custom
   - **Pipe Hanger Type (Hydratec)** — default "04"
   - **Trapeze Type (Hydratec)** — default "21F"
   - **Distance to Top of Unistrut (in)** — Inches below pipe to unistrut top (default: 6")
   - **Extension Measured From** — Middle of Framing (default) or Hanger Rod
   - **Extension Distance (in)** — Channel overhang beyond rod (default: 1")
   - **Max Clash Height (ft)** — Vertical search range (default: 10')
   - **Structural Source** — Local framing or linked Revit model
4. **Click "Place Trapezes"**

## Unistrut Extension Calculation

The unistrut channel extends beyond each rod. How far depends on the "measured from" setting:

### From Middle of Framing (default)
```
extension = (distance_from_rod_to_framing_center + user_extension) * 12
```
The channel extends from the rod, past the beam centerline, plus the user-specified extra distance.

### From Hanger Rod
```
extension = user_extension * 12
```
The channel extends a fixed distance beyond each rod, regardless of beam position.

## What Gets Placed

For each pipe at each spacing point:

| Parameter | Value |
|-----------|-------|
| Rod 1 Top Elevation | Pipe-to-level + Rod 1 length (shorter rod) |
| Rod 1 Offset | Horizontal distance from pipe center to Rod 1 |
| Rod 2 Top Elevation | Pipe-to-level + Rod 2 length (longer rod) |
| Rod 2 Offset | Horizontal distance from pipe center to Rod 2 |
| **Top of Unistrut Elevation** | Pipe Z - distance down to unistrut |
| **Rod 1 Unistrut Extension** | Calculated channel overhang (Rod 1 side) |
| **Rod 2 Unistrut Extension** | Calculated channel overhang (Rod 2 side) |
| Supported Pipe Elevation | Pipe centerline elevation |
| Supported Pipe Rotation Angle | Angle between pipe and trapeze directions |
| Nominal Diameter | Pipe diameter |
| Type Code (Hydratec) | Combined: "04;21F" (PipeHangerType;TrapezeType) |
| Additional Stocklist Information (Hydratec) | "CON1,{PipeElementId}" |
| Comments | Structural beam name |

## Differences from the Dynamo Version

| Feature | Dynamo Script (V28) | Plugin Command |
|---------|-------------------|----------------|
| Speed | Very slow — 16+ UI inputs, BimorphNodes, Clockwork | Fast — native C# |
| Dependencies | Data-Shapes + BimorphNodes + Clockwork + 18 custom .dyf | Zero external dependencies |
| UI | 16-input Data-Shapes form with input groups | Single WinForms dialog |
| Grid-based spacing | Supported (between grid lines) | Not yet (planned) |
| Two-bays toggle | Supported | Not yet (planned) |
| Settings persistence | Revit Global Parameters | Not yet (planned) |
| Structural detection | BimorphNodes surface analysis | Direct distance calculation |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/hang-unistrut-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.
