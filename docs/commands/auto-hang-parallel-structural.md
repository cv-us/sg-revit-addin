# Auto Hang — Typical Spacing (Parallel to Structural Framing)

**Ribbon location:** SSG FP Suite tab > Hangers panel > "Hang Parallel Steel"
**Command class:** `SSG_FP_Suite.Commands.Hangers.AutoHangParallelStructuralCommand`
**Migrated from:** `Auto Hang - Typical Spaced Runs - Parallel To Structural Framing.dyn` (V33)

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

## Differences from the Dynamo Version

| Feature | Dynamo Script (V33) | Plugin Command |
|---------|-------------------|----------------|
| Speed | Very slow — BimorphNodes bounding box + surface analysis | Fast — analytical perpendicular distance calculation |
| Dependencies | Data-Shapes + BimorphNodes + Clockwork + 12 custom .dyf nodes | Zero external dependencies |
| UI | 11-input Data-Shapes form | 1 consolidated WinForms dialog |
| Transactions | Multiple | Single transaction |
| Structural detection | BimorphNodes BoundingBox.GetElementsIntersect + surface filtering | Direct perpendicular distance to FramingInfo centerlines |
| Flange thickness | Full surface geometry analysis via custom .dyf nodes | Estimated from bounding box depth |
| IFC/Tekla support | Separate filter path for IFC/Tekla member types | Uses same IsBeamLikeType filter (handles both) |
| Global Parameters | 9 Revit Global Parameters | Not yet (planned: settings file) |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/hang-parallel-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.
