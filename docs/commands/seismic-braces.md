# Insert Seismic Braces on Welded Mains

**Command:** `SeismicBracesCommand`
**Domain:** Seismic / Annotation
**Ribbon:** SG Revit Addin > Seismic > Seismic Braces

## Purpose

Automatically places seismic brace family instances along selected welded fire protection pipe mains. Supports lateral braces, longitudinal braces, or both, with configurable NFPA 13 spacing. Calculates rod length to structure above via geometric intersection with linked architectural floor/roof geometry.

## Workflow

1. Dialog opens with brace type, family, spacing, orientation, and linked model settings
2. User selects welded main pipes
3. For each pipe, command calculates brace placement points at specified intervals
4. For each point, a vertical line is cast upward to find structure above (linked floors/roofs)
5. Brace family instances are placed, rotated to pipe direction, and parameters set

## Dialog Options

### Brace Types to Insert
| Option | Description |
|--------|-------------|
| Lateral Only | Places only lateral (perpendicular) braces |
| Longitudinal Only | Places only longitudinal (parallel) braces |
| Both (default) | Places both lateral and longitudinal braces |

### Lateral Brace Settings
| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| Family | Dropdown | — | Families containing "-SeismicBrace" and "Lateral" in name |
| Max Spacing | 1-40 ft | 40 ft | NFPA 13 max lateral brace spacing |
| Max Dist from End | 1-6 ft, step 0.5 | 6 ft | NFPA 13 max distance of first brace from pipe end |
| Orientation | Left/Above or Right/Below | Left/Above | Which side of pipe the brace attaches |

### Longitudinal Brace Settings
| Setting | Range | Default | Description |
|---------|-------|---------|-------------|
| Family | Dropdown | — | Families containing "-SeismicBrace" and "Longitudinal" in name |
| Max Spacing | 1-80 ft | 80 ft | NFPA 13 max longitudinal brace spacing |
| Orientation | Right/Upward or Left/Downward | Right/Upward | Brace direction along pipe |

### Linked Model
Select the architectural linked model containing floors and roofs (used to detect structure above for rod length calculation).

## Brace Families

Families are auto-discovered from the project by name pattern:
- **Lateral:** Family name contains both `-SeismicBrace` and `Lateral`
- **Longitudinal:** Family name contains both `-SeismicBrace` and `Longitudinal`

Examples: `-SeismicBrace-Lateral-Tolco1001-Tolco980-Deck`, `-SeismicBrace-Longitudinal-Tolco4LA-Tolco980-Deck`

## Spacing Rules (NFPA 13)

### Lateral Braces
- Distributed evenly along pipe using `ceiling(length / maxSpacing)` segments
- Actual spacing never exceeds the user-specified maximum
- A brace is ensured within the specified distance of each pipe end
- NFPA 13 allows up to 40 ft max spacing, first brace within 6 ft of end

### Longitudinal Braces
- Distributed evenly from start to end of pipe
- Spacing: `ceiling(length / maxSpacing)` segments
- NFPA 13 allows up to 80 ft max spacing

## Rod Length Calculation

For each brace point:
1. A vertical line is cast upward 20 ft from the brace point
2. The line is intersected with downward-facing faces (undersides) of linked floor/roof solids
3. The lowest hit point Z gives the structure elevation above
4. `BraceHeight` = structure Z - brace point Z
5. `XDistanceToAnchor` = BraceHeight rounded **up** to nearest 0.5 ft (6-inch increments)

## Parameters Written

| Parameter | Type | Description |
|-----------|------|-------------|
| `XDistanceToAnchor` | Double | Rod length, rounded up to nearest 6" (0.5 ft) |
| `BraceHeight` | Double | Raw vertical distance to structure above (feet) |
| `Nominal Diameter` | Double | Pipe diameter (feet, copied from pipe) |
| `Additional Stocklist Information (Hydratec)` | String | `"CON1," + pipeElementId` for stocklist tracking |

## Rotation Logic

### Lateral Braces (perpendicular to pipe)
- Rotated 90° from pipe direction
- Orientation option flips between +90° and -90° offset
- Quadrant-aware angle normalization ensures correct direction in all pipe orientations

### Longitudinal Braces (aligned with pipe)
- Rotated to match pipe direction
- Orientation option selects forward or reverse along pipe
- Angle adjusted by ±90° based on pipe quadrant

## Pipe Filtering

- **Vertical pipes:** Automatically excluded (vertical = no horizontal run to brace)
- **Steep pipes:** Pipes > 60° from horizontal are excluded
- **Short pipes:** Pipes < 0.5 ft length are skipped

## Notes

- Seismic brace families must be loaded before running the command (naming convention: `-SeismicBrace-Lateral-*` and `-SeismicBrace-Longitudinal-*`)
- Linked model must be loaded for rod length calculation; without it, BraceHeight defaults to 0
- The command creates a separate Seismic panel in the ribbon
- Rod lengths are rounded UP to nearest 6" per standard practice (field-cut to fit)
