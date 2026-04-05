# Insert Pipe & Fitting Elevations

**Command:** `PipeElevationsCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Pipe Elevations

## Purpose

Calculates and populates elevation parameters on selected pipes and fittings relative to two independent reference systems: TOS (Top of Steel/Deck) and AFF (Above Finished Floor).

## Workflow

1. Dialog opens with TOS and AFF reference method selection
2. User selects pipes and/or fittings in the model
3. For each element:
   - Gets element center Z elevation (midpoint for pipes, location point for fittings)
   - Resolves reference elevation based on selected method
   - Calculates offset = element Z - reference Z
   - Formats as feet-inches-fraction string (e.g., `+42'-3 1/2" TOS`)
   - Writes `PipeElevationTOS` and `PipeElevationAFF` parameters
4. For pipes only: calculates and writes `Slope` parameter

## Dialog Options

### TOS / AFF Reference Method (independent for each)

| Method | Description |
|--------|-------------|
| Structural Decks (RayBounce) | Casts a ray upward (TOS) or downward (AFF) from each element to find the nearest structural deck/floor |
| Named Reference Plane | Uses a named reference plane's Z elevation as the datum |
| User-Defined Z Elevation | Uses a manually entered Z value in feet |
| Reference Level | Uses a project level's elevation as the datum |

### Element Types

- **Pipes** - OST_PipeCurves
- **Fittings & Accessories** - OST_PipeFitting and OST_PipeAccessory

At least one element type must be selected.

## Parameters Written

| Parameter | Elements | Description |
|-----------|----------|-------------|
| `PipeElevationTOS` | Pipes, Fittings | Formatted elevation relative to TOS reference (e.g., `+8'-6 1/4" TOS`) |
| `PipeElevationAFF` | Pipes, Fittings | Formatted elevation relative to AFF reference (e.g., `+10'-2" AFF`) |
| `Slope` | Pipes only | Slope classification: `1/4" / 10 Ft`, `1/2" / 10 Ft`, `3/4" / 10 Ft`, `1" / 10 Ft`, or `Varies` |

## Elevation Format

- Sign prefix: `+` for above reference, `-` for below
- Feet and inches with quarter-inch fraction precision
- Suffix indicates reference type (TOS or AFF)
- Example: `+42'-3 1/2" TOS`

## Slope Classification

Rise per 10 feet of horizontal run is calculated and categorized:

| Rise per 10 ft | Classification |
|----------------|---------------|
| < 0.15" | Varies |
| 0.15" - 0.45" | 1/4" / 10 Ft |
| 0.45" - 0.75" | 1/2" / 10 Ft |
| 0.75" - 1.05" | 3/4" / 10 Ft |
| > 1.05" | 1" / 10 Ft |

Vertical pipes (run < 0.001 ft) are classified as "Varies".

## RayBounce Details

When using the Structural Decks method:
- **TOS**: Ray cast upward from element center to find underside of nearest floor/deck/roof above
- **AFF**: Ray cast downward from element center to find top of nearest floor/stairs below
- Target categories: Floors, Stairs, Structural Framing, Structural Foundation, Roofs
- Uses the first available non-template 3D view in the project
- Falls back to Z=0 reference if no intersection found

## Notes

- Parameters must exist on the elements as shared/project parameters (string type)
- All parameters are written silently — if a parameter doesn't exist on an element, it is skipped
- Elevation rounding is to the nearest 1/4 inch

## See Also

- **[Choosing a Command](choosing-a-command.md)** — comparison of elevation commands
- **Sleeve Elevations** — for pipe sleeve AFF/BBD elevations (different element type, different parameters)
- **Clear Pipe Elevation Params** — removes the 6 shared parameters this command writes (cleanup utility)
