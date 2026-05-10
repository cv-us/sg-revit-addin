# Auto Hang — Typical Spacing (Parallel to Structural Framing)

**Ribbon location:** SG Revit Addin tab > Hangers panel > "Hang Parallel Steel"
**Command class:** `SgRevitAddin.Commands.Hangers.HangParallelStructuralCommand`

## What It Does

Places pipe hangers at typical spacing along straight pipe runs, attaching them to structural framing members (beams, joists, girders) that run **parallel** to the pipes. Unlike the "Hangers to Decks" command which shoots rays straight up to find deck soffits, this command searches **perpendicular** to the pipe direction to find nearby parallel structural members.

Key additional features:
- **Widemouth detection** — if the structural member's flange is thicker than 0.75", the widemouth type code is used instead of the standard type
- **Top or bottom attachment** — choose whether hangers attach to the top or bottom flange
- **Clamp angle** — automatically calculates the C-clamp rotation toward the structural member's centerline
- **Top accessory offset** — set based on attachment mode and flange thickness

## How to Use

1. **Click "Hang Parallel Steel"** in the ribbon (Hangers panel)
2. **Select pipes** — Click pipe runs, press Finish
3. **Fill in the dialog:**
   - **Pipe Type Filter** — "ALL Pipes" or specific type
   - **Hanger Family** — Pipe accessory family
   - **Spacing Mode** — Evenly distributed or exact spacing
   - **Max Spacing** — 10'-6" (default), 12'-0", 15'-0", or custom
   - **Type Code (Hydratec)** — Standard hanger code (default: "01")
   - **Widemouth Type (Hydratec)** — Code for thick-flange steel (default: "01A")
   - **Attach Hangers To** — BOTTOM (default) or TOP of structural
   - **C-Clamp Visibility** — Hide or Show
   - **Structural Source** — Local framing or a linked Revit model
4. **Click "Place Hangers"**

## How Parallel Structural Detection Works

At each hanger point along the pipe:

1. The pipe's direction vector is rotated 90° about Z to get the **perpendicular direction**
2. The command searches up to **10 feet** in each perpendicular direction
3. Structural framing members within **5 feet** vertically are candidates
4. The **nearest** framing member (by perpendicular distance) is selected
5. The member's flange thickness is estimated from its bounding box depth
6. If flange > 0.75" → widemouth type code; otherwise → standard type code
7. Rod length = distance from pipe to the structural attachment point (top or bottom flange)
8. Clamp angle = corrective rotation from pipe direction toward the structural centerline

## What Gets Placed

For each pipe, N hangers at typical spacing, each with:
- **Nominal Diameter** — pipe diameter
- **Rod Length** — distance to parallel structural member
- **Elevation from Level** — from placement point
- **Top Accessory Offset** — 0.070 ft (top) or (0.847 - flangeThickness)/12 ft (bottom)
- **Clamp Angle** — toward structural centerline
- **Type Code (Hydratec)** — standard or widemouth based on flange thickness
- **Additional Stocklist Information (Hydratec)** — structural member name
- **C Clamp** — visibility toggle
- **Comments** — structural member name

## When to Use This vs "Hangers to Decks"

| Scenario | Use This Command | Use "Hangers to Decks" |
|----------|-----------------|----------------------|
| Pipes run between steel beams | ✅ Searches perpendicular for parallel beams | ❌ Shoots up, hits deck above beams |
| Pipes under concrete deck with no beams | ❌ No parallel framing to find | ✅ Finds deck soffit above |
| Pipes between joists in a joist field | ✅ Finds adjacent parallel joists | ✅ Also works (finds joist above) |
| Pipes parallel to a single beam | ✅ Designed for this exact case | ❌ May miss the parallel beam |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/hang-parallel-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.

## See Also

- **[Choosing a Command](choosing-a-command.md)** — full comparison of all similar commands
- **Hang Typical Spacing** — similar even spacing but without perpendicular beam search
- **Hang at Structural** — for hangers at beam crossing points (perpendicular pipes)
