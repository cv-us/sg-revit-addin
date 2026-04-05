# Insert Pipe Sleeves at Intersecting Decks

**Command:** `PipeSleevesAtDecksCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Sleeves at Decks

## Purpose

Automatically places pipe sleeve family instances at every intersection between user-selected pipes and floors/stairs/roofs from a linked Revit model. Sleeves are sized per NFPA annular clearance rules. Sleeve length matches the penetrated deck thickness, with an option to extend 2" above for wet areas.

## Workflow

1. Dialog opens to select linked model and sleeve length behavior
2. User selects pipes in the model
3. For each pipe, command finds which linked floors/roofs/stairs it intersects
4. At each intersection, a deck sleeve is placed with:
   - NFPA-sized diameter
   - Length matching deck thickness (or +2" for wet areas)
   - Metadata parameters for scheduling

## Dialog Options

| Setting | Description |
|---------|-------------|
| Structural/Arch Link | Linked Revit model containing floors, stairs, and/or roofs |
| Sleeve Lengths | **Same thickness as floor** (non-wet areas) or **Extend 2" above floor** (wet areas, default) |

## Sleeve Family

- **Family:** `-Pipe Sleeve-Deck`
- **Type:** `Standard`
- This family must be loaded in the project before running the command

## NFPA Annular Clearance Sizing

Pipe diameter is first snapped to standard sizes, then clearance is added:

### Step 1: Snap Pipe Diameter
| Non-Standard | Snapped To |
|-------------|------------|
| 3.25" | 3.5" |
| 4.5" | 5" |

### Step 2: Add Clearance
| Pipe Diameter | Clearance | Annular Space |
|--------------|-----------|---------------|
| < 3.5" | +2" | 1" per side |
| >= 3.5" | +4" | 2" per side |

## Sleeve Length

| Option | Length |
|--------|--------|
| Same as floor (non-wet) | Deck thickness (distance pipe travels through slab) |
| Extend for wet areas | Deck thickness + 2" |

Deck thickness is determined geometrically from the vertical distance between the top and bottom face intersection points.

## Parameters Written on Sleeves

| Parameter | Type | Description |
|-----------|------|-------------|
| `Sleeve Pipe Size` | Double/String | Computed NFPA sleeve diameter (inches) |
| `Sleeve Pipe Length` | Double | Sleeve length (feet) |
| `Comments` | String | Deck/floor type name from linked model |
| `Deck Thickness` | Double/String | Thickness of penetrated deck (inches) |
| `Elevation from Level` | Double | Intersection midpoint Z minus reference level elevation |
| `Sleeve Elevation DATUM` | String | Formatted elevation (e.g., `+10.50'`) |
| `Reference Level` | String | Name of nearest level at or below sleeve |

## Linked Categories Searched

| Category | Description |
|----------|-------------|
| OST_Floors | Floor slabs |
| OST_Stairs | Stair elements |
| OST_Roofs | Roof decks |

## Intersection Detection

### Stage 1: Bounding Box Pre-Filter
- Each pipe's bounding box is compared against each deck element's bounding box (0.5 ft tolerance)
- Non-overlapping pairs are skipped for performance

### Stage 2: Precise Face Intersection
- Pipe location curve is extended 1 ft each direction
- All faces of the deck solid are tested with `Face.Intersect(Curve)`
- Intersection points are collected and analyzed:
  - **Multiple hits:** Entry/exit through slab give deck thickness (Z difference) and midpoint for placement
  - **Single hit:** Uses nominal 6" thickness as fallback

### Reference Level Assignment
- All host model levels are collected and sorted by elevation
- For each sleeve, the nearest level at or below the intersection Z is assigned

## Differences from Sleeves at Beams

| Aspect | Beams | Decks |
|--------|-------|-------|
| Target categories | OST_StructuralFraming | OST_Floors, OST_Stairs, OST_Roofs |
| Sleeve family | -Pipe Sleeve-Beam-MiddleJustified | -Pipe Sleeve-Deck |
| Sleeve rotation | Rotated to beam direction | No rotation (vertical) |
| Sleeve length | User-entered fixed value | Deck thickness (geometric) |
| Wet area option | No | Yes (+2" extension) |
| Extra parameters | — | Deck Thickness, Reference Level, Sleeve Elevation DATUM |

## Notes

- The sleeve family must be loaded before running the command
- Linked model must be loaded (not unloaded) to access geometry
- Deck thickness is measured geometrically — it accounts for sloped decks and varying slab thickness
- For nearly-horizontal pipe-deck intersections, the command uses the full distance through the slab rather than just Z difference
