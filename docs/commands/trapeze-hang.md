# Auto Trapeze Hang — Standard Pipe Trapeze — Auto Spaced

**Ribbon location:** SSG FP Suite tab > Hangers panel > "Trapeze Hang"
**Command class:** `SSG_FP_Suite.Commands.Hangers.TrapezeHangCommand`

## What It Does

Places standard pipe trapeze hangers at auto-spaced intervals along pipe runs. Each trapeze has **two threaded rods** that anchor to structural framing above the pipe. The rods are positioned on either side of the nearest structural beam, with configurable placement at the closest side or middle of the structural member.

Key features:
- **Two rods per hanger** — Rod 1 and Rod 2 each have independent offset and top elevation parameters
- **Structural beam detection** — searches above each hanger point for nearest beam/joist/girder
- **Rod anchor calculation** — rods offset by beam-width/2 + 0.5" clearance on each side of beam centerline
- **Closest side vs middle** — choose whether rods attach at beam edges or beam center
- **Clevis variant** — automatic family switch for pipes larger than 8" diameter
- **Three spacing modes** — evenly distributed, exact spacing, or between grid lines
- **Distance down to trapeze pipe** — configurable offset below the supported pipe

## How to Use

1. **Click "Trapeze Hang"** in the ribbon (Hangers panel)
2. **Select pipes** — Click pipe runs, press Finish
3. **Fill in the dialog:**
   - **Pipe Type Filter** — "ALL Pipes" or specific type
   - **Trapeze Family** — Pipe accessory family (e.g. "Pipe Trapeze Hanger - Single Pipe - Standard")
   - **Rod Positioning** — Closest Side of Structural (default) or Middle of Structural
   - **Spacing Mode** — Evenly distributed (default) or exact spacing
   - **Max Spacing** — 10'-6" (default), 12'-0", 15'-0", or custom
   - **Pipe Hanger Type (Hydratec)** — Type code for pipe hangers (default: "R3R")
   - **Trapeze Type (Hydratec)** — Type code for trapeze (default: "19A")
   - **Distance Down to Trapeze (in)** — Inches below supported pipe (default: 7")
   - **Max Clash Height (ft)** — Vertical search range for structural above (default: 10')
   - **Structural Source** — Local framing or linked Revit model
4. **Click "Place Trapezes"**

## How Structural Detection Works

At each hanger point along the pipe:

1. The command searches upward (within Max Clash Height) for structural framing members
2. It filters to beam-like types (excluding angles, channels, misc. steel)
3. The **nearest** beam above (by combined horizontal + vertical distance) is selected
4. The beam's width is estimated from its bounding box (the smaller horizontal dimension)
5. Rod anchor points are placed at `beam-width/2 + 0.5"` clearance on each side of the beam centerline
6. Rod lengths = vertical distance from pipe to anchor points
7. Rod offsets = horizontal distance from pipe center to each anchor

### Closest Side vs Middle

| Mode | Rod Placement | Use When |
|------|--------------|----------|
| Closest Side (default) | Rods offset on both sides of beam | Standard trapeze — cross-member bridges the beam |
| Middle | Both rods at beam centerline | Beam is directly above pipe center |

## What Gets Placed

For each pipe at each spacing point, a trapeze family instance with:

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

## Family Type Selection

| Pipe Diameter | Family Used |
|--------------|-------------|
| <= 8" | Standard trapeze family (user-selected) |
| > 8" | Clevis variant (auto-detected by name containing "Clevis" + "Trapeze") |

If no Clevis variant is found in the project, the standard family is used for all sizes.

## Differences from Single-Pipe Hanger Commands

| Feature | Trapeze | Single-Pipe Hangers |
|---------|---------|-------------------|
| Rods per hanger | 2 (Rod 1 + Rod 2) | 1 (Rod Length) |
| Structural attachment | Both sides of beam | Top of deck/beam |
| Rod offset parameters | Yes (horizontal distance) | No |
| Trapeze pipe elevation | Yes (cross-member below pipe) | No |
| Rotation angle | Trapeze + supported pipe angles | Pipe direction only |
| Beam width calculation | Yes (for rod offset) | No |
| Clevis variant | Yes (>8" pipe) | No |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/hang-trapeze-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.
