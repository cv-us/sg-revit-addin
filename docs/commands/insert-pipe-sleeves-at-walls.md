# Insert Pipe Sleeves at Intersecting Walls

**Command:** `InsertPipeSleevesAtWallsCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Sleeves at Walls
**Migrated from:** `AutoInsert - Pipe Sleeves at Intersecting Walls.dyn`

## Purpose

Automatically places pipe sleeve family instances at every intersection between user-selected pipes and walls from a linked Revit model. Sleeves are sized per NFPA annular clearance rules with separate seismic and non-seismic sizing tables. Wall types can be filtered by category for targeted processing.

## Workflow

1. Dialog opens to select linked model, seismic area, and wall type filters
2. User selects pipes in the model
3. For each pipe, command finds which linked walls it intersects (vertical face intersection)
4. At each intersection, a wall sleeve is placed:
   - Sized per NFPA seismic or non-seismic lookup table
   - Rotated to align with wall penetration direction
   - Length matches wall penetration distance
   - Metadata parameters populated

## Dialog Options

### Linked Model
Select the linked Revit model containing walls.

### Seismic Area
| Option | Description |
|--------|-------------|
| Non-Seismic (default) | Uses standard NFPA annular clearance table |
| Seismic | Uses larger NFPA seismic clearance table |

### Wall Types
| Mode | Description |
|------|-------------|
| All intersecting walls | No filtering — processes all walls |
| Filter by wall type | Enables search filter checkboxes |

### Search Filters (when filtering)
| Filter | Searches For |
|--------|-------------|
| Interior | Wall type name contains "INTERIOR" |
| Exterior | Wall type name contains "EXTERIOR" |
| Fire Rated | Wall type name contains "HR" or "HOUR" |
| Structural | Wall type name contains "CONCRETE" or "CMU" |

After checking desired filters and clicking **Apply Filters**, matching wall types appear in a checklist for final selection. All matching types are checked by default.

## Sleeve Family

- **Family:** `-Pipe Sleeve-Wall-EndJustified`
- **Type:** `Standard`
- This family must be loaded in the project before running the command

## NFPA Sizing Tables

### Non-Seismic

| Pipe Diameter | Sleeve Diameter |
|--------------|-----------------|
| 1" | 2" |
| 1.25" | 2" |
| 1.5" | 2.5" |
| 2" | 3" |
| 2.5" | 4" |
| 3" | 4" |
| 4" | 6" |
| 6" | 8" |
| 8" | 10" |
| Other | 12" |

### Seismic

| Pipe Diameter | Sleeve Diameter |
|--------------|-----------------|
| 1" | 3" |
| 1.25" | 4" |
| 1.5" | 4" |
| 2" | 4" |
| 2.5" | 5" |
| 3" | 5" |
| 4" | 8" |
| 6" | 10" |
| 8" | 12" |
| Other | 14" |

Pipe diameters are matched to the nearest table entry within 0.15" tolerance.

## Sleeve Length

Sleeve length equals the **wall penetration distance** — the geometric distance between the entry and exit points where the pipe centerline passes through the wall. For a horizontal pipe through a vertical wall, this equals the wall thickness. For angled penetrations, the length is proportionally longer.

## Parameters Written on Sleeves

| Parameter | Type | Description |
|-----------|------|-------------|
| `Sleeve Pipe Size` | Double/String | NFPA sleeve diameter from sizing table (inches) |
| `Sleeve Pipe Length` | Double | Wall penetration distance (feet) |
| `Comments` | String | Wall type name + fire rating (e.g., "Interior 5.5 CMU 2 HR") |
| `Elevation from Level` | Double | Intersection midpoint Z minus reference level elevation |

## Intersection Detection

### Slope Filtering
Steeply sloped pipes (> 60° from horizontal) are automatically excluded. Wall sleeves are only placed for roughly horizontal pipe penetrations. Vertical pipes are also excluded.

### Stage 1: Bounding Box Pre-Filter
Each pipe's bounding box is compared against each wall's bounding box (0.5 ft tolerance).

### Stage 2: Vertical Face Intersection
- Only **vertical faces** of the wall solid are tested (face normal Z component < 0.3)
- Pipe location curve is extended 1 ft each direction
- `Face.Intersect(Curve)` finds intersection points
- Requires 2+ hit points (entry and exit through wall faces)
- The two most distant points define entry/exit; midpoint is the sleeve placement location

### Rotation
Sleeve is rotated about Z-axis to align with the penetration direction vector (from entry to exit point), using `Math.Atan2(dir.Y, dir.X)`.

## Comparison: All Three Sleeve Commands

| Aspect | Walls | Beams | Decks |
|--------|-------|-------|-------|
| Family | -Pipe Sleeve-Wall-EndJustified | -Pipe Sleeve-Beam-MiddleJustified | -Pipe Sleeve-Deck |
| Target | OST_Walls | OST_StructuralFraming | OST_Floors/Stairs/Roofs |
| Sizing | Seismic/Non-seismic lookup tables | +2"/+4" formula | +2"/+4" formula |
| Length | Wall penetration distance | User-entered | Deck thickness ± wet area |
| Rotation | Penetration direction | Beam direction | None |
| Slope filter | > 60° excluded | None | None |
| Type filter | Interior/Exterior/Fire/Structural | None | None |
| Comments | Type name + fire rating | Beam type | Deck type |

## Notes

- The sleeve family must be loaded before running the command
- Linked model must be loaded (not unloaded) to access geometry
- Wall type filtering is case-insensitive
- Fire Rating parameter is read from the linked wall element (BuiltInParameter.FIRE_RATING)
- The command creates a single transaction for all sleeve placements
