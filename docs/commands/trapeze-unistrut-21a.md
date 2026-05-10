# Auto Trapeze Hang — Unistrut 21A — Auto Spaced

**Ribbon location:** SG Revit Addin tab > Hangers panel > "Unistrut 21A"
**Command class:** `SgRevitAddin.Commands.Hangers.TrapezeUnistrut21ACommand`

## What It Does

Places Unistrut 21A pipe trapeze hangers at auto-spaced intervals along pipe runs. This is a variant of the regular Unistrut trapeze command with 21A-specific defaults. It offers the **full set of user options** — rod position, type codes, distance-down, extension settings — just with 21A-appropriate default values pre-filled.

## How It Differs from Regular Unistrut Trapeze

| Feature | Regular Unistrut | Unistrut 21A |
|---------|-----------------|--------------|
| Family pre-selection | Families with "Unistrut" in name | Families with "21A" in name |
| Pipe hanger type code default | "04" | "04" |
| Trapeze type code default | "21F" | "21A" |
| Distance to unistrut default | 6" | 6" |
| Extension distance default | 1" | 1" |
| All other options | Full user control | Full user control (same options) |

Both commands share the same full set of user-configurable inputs.

## How to Use

1. **Click "Unistrut 21A"** in the ribbon (Hangers panel)
2. **Select pipes** — Click pipe runs, press Finish
3. **Fill in the dialog:**
   - **Pipe Type Filter** — "ALL Pipes" or specific type
   - **Unistrut 21A Family** — Pre-selects families with "21A" in name
   - **Trapeze Rod Positioning** — Closest side of structural (default) or middle
   - **Spacing Mode** — Evenly distributed (default) or exact spacing
   - **Max Spacing** — 10'-6" (default), 12'-0", 15'-0", or custom
   - **Pipe Hanger Type (Hydratec)** — Default: "04"
   - **Trapeze Type (Hydratec)** — Default: "21A"
   - **Distance to Top of Unistrut (in)** — Default: 6"
   - **Unistrut Extension** — Measured from framing (default) or rod, with distance (default: 1")
   - **Max Clash Height (ft)** — Vertical search range (default: 10')
   - **Structural Source** — Local framing or linked Revit model
4. **Click "Place Trapezes"**

## Hydratec Type Code Format

The "Type Code (Hydratec)" parameter on placed instances is a **combined value**:

```
PipeHangerTypeCode;TrapezeTypeCode
```

Example: `"04;21A"` — where "04" is the pipe hanger type and "21A" is the trapeze type. This combined format is used for stocklist processing.

## What Gets Placed

For each pipe at each spacing point:

| Parameter | Value |
|-----------|-------|
| Rod 1 Offset | Horizontal distance from pipe center to Rod 1 |
| Rod 2 Offset | Horizontal distance from pipe center to Rod 2 |
| Top of Unistrut Elevation | Bottom of structural beam minus distance-down (relative to level) |
| Rod 1 Top Elevation | Top of structural beam (relative to level) |
| Rod 2 Top Elevation | Top of structural beam (relative to level) |
| Rod 1 Unistrut Extension | Calculated from extension settings |
| Rod 2 Unistrut Extension | Calculated from extension settings |
| Supported Pipe Elevation | Pipe centerline elevation |
| Supported Pipe Rotation Angle | Angle between pipe and trapeze directions |
| Nominal Diameter | Pipe diameter |
| Type Code (Hydratec) | Combined: "04;21A" (PipeHangerType;TrapezeType) |
| Additional Stocklist Information (Hydratec) | "CON1,{PipeElementId}" |
| Comments | Structural beam name |

## Extension Calculation

Extension distance depends on the "Measured From" setting:

- **From Framing (F):** `(distToBeamCenter + extensionDist) * 12` inches
- **From Rod (R):** `extensionDist * 12` inches

The same extension value is applied to both Rod 1 and Rod 2 sides.

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/hang-uni21a-32.png` (and 16x16). Replace with same filename, rebuild, redeploy.

