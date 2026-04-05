# Insert Pipe Sleeve Elevations

**Command:** `SleeveElevationsCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Sleeve Elevations

## Purpose

Calculates and populates AFF (Above Finished Floor) and BBD (Below Bottom of Deck) elevation parameters on pipe sleeve family instances by finding geometric intersections with linked architectural floors and structural decks.

## Workflow

1. Dialog opens to select linked models and elevation format
2. User selects pipe sleeves in the model (filtered by family name containing "Sleeve")
3. For each sleeve:
   - Gets sleeve location point
   - Casts a vertical line downward through linked architectural model floor geometry to find the floor top surface below (AFF)
   - Casts a vertical line upward through linked structural model deck geometry to find the deck bottom surface above (BBD)
   - Calculates AFF = sleeve Z - floor top Z
   - Calculates BBD = deck bottom Z - sleeve Z
   - Formats elevation strings and writes parameters

## Dialog Options

### Linked Model References

| Setting | Description |
|---------|-------------|
| AFF Reference (Architectural Link) | Linked Revit model containing floors for AFF calculation |
| BBD Reference (Structural Link) | Linked Revit model containing structural decks for BBD calculation |

Both dropdowns list all loaded RevitLinkInstance elements. The same link can be used for both if floors and structure are in the same model.

### Elevation Display Format

| Format | Example |
|--------|---------|
| Decimal Feet (default) | `+10.50' AFF` |
| Feet and Inches | `+10'-6 1/2" AFF` |

Feet-and-inches format uses 1/4" precision rounding.

## Parameters Written

| Parameter | Description |
|-----------|-------------|
| `PipeElevationAFF` | AFF elevation string (distance above floor below) |
| `PipeElevationTOS` | BBD elevation string (distance below deck above) |
| `Sleeve Elevation AFF` | Duplicate AFF string for sleeve-specific tag display |
| `Sleeve Elevation DATUM` | Duplicate BBD string for sleeve-specific tag display |

## Element Selection

- **Category:** OST_PipeAccessory (Pipe Accessories)
- **Family filter:** Family name must contain "Sleeve" (matches Sleeve-Wall, Sleeve-Beam, and other sleeve variants)
- **Location:** Uses FamilyInstance LocationPoint; falls back to bounding box center

## Geometric Intersection Method

The command uses direct solid geometry intersection with linked model elements rather than raybounce:

1. Collects all Floor and Roof elements from the selected linked model(s)
2. Extracts Solid geometry and transforms to host document coordinates
3. For each sleeve, creates vertical Line segments extending 50 feet up and down
4. Iterates solid faces, filtering by normal direction:
   - Up-facing faces (normal Z > 0.9) → floor tops for AFF
   - Down-facing faces (normal Z < -0.9) → deck bottoms for BBD
5. Uses `Face.Intersect(Curve)` to find exact intersection points
6. Selects the nearest surface (highest floor below, lowest deck above)

### Performance Optimization

- Bounding box pre-filter skips solids that can't possibly intersect the sleeve's vertical search line
- All linked geometry is collected and transformed once upfront, not per-sleeve

## Edge Cases

- **No intersection found:** Writes "None" for that elevation parameter
- **Unloaded links:** Filtered out of the dialog dropdown
- **No links in project:** Warning dialog shown before command runs
- **Vertical sleeves:** Uses LocationPoint regardless of orientation

## Notes

- Linked models must be loaded (not unloaded) to access their geometry
- The 50-foot search distance covers typical multi-story floor-to-floor heights
- All four parameters are written as strings — they must exist on the sleeve family as shared/project parameters

## See Also

- **[Choosing a Command](choosing-a-command.md)** — comparison of elevation commands
- **Pipe Elevations** — for pipe and fitting TOS/AFF elevations (different element type, different reference methods)
