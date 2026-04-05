# Export Trimble Layout Points

**Command:** `ExportTrimblePointsCommand`
**Domain:** Export
**Ribbon:** SSG FP Suite > Export > Trimble Points
## Purpose

Exports pipe hanger locations as Trimble-compatible CSV point files for field layout of hanger inserts before concrete pours. The output file can be imported directly into Trimble FieldLink, Trimble Access (RTS total stations), Trimble Layout Manager, or any equipment that accepts CSV point files.

## Workflow

1. Dialog opens with scope, coordinate, and naming options
2. Hangers are collected from the selected scope
3. Coordinates are transformed to shared/project basis
4. Units are converted and elevation offset applied
5. SaveFileDialog prompts for output location
6. CSV is written with one row per hanger

## Output Format

Plain CSV, no header row, comma-delimited:

**Northing-Easting order (default, most common in US construction):**
```
PointName,Northing,Easting,Elevation,Code
H-001,5000.0000,3000.0000,102.5000,HANGER-INSERT 1"
H-002,5010.2500,3005.1000,102.5000,HANGER-INSERT 2"
```

**Easting-Northing order (alternative):**
```
PointName,Easting,Northing,Elevation,Code
H-001,3000.0000,5000.0000,102.5000,HANGER-INSERT 1"
```

Coordinates are output to 4 decimal places.

## Dialog Options

| Setting | Options | Default | Description |
|---------|---------|---------|-------------|
| Scope | Selection / Active View / By Level | Active View (or Selection if pre-selected) | Which hangers to export |
| Coordinate System | Shared (survey) / Project Internal | Shared | Shared = real-world survey coordinates |
| Coordinate Order | Northing,Easting / Easting,Northing | Northing,Easting | Must match Trimble controller setting |
| Units | US Feet / Meters | US Feet | Coordinate and elevation units |
| Point Prefix | Text | "H" | Prefix for auto-generated point names |
| Code | Text | "HANGER-INSERT" | Value for the Code column (description) |
| Elevation Offset | Number (feet) | 0 | Added to all elevations (e.g., if project 0 = real-world 1272.35') |

## Hanger Identification

Elements are identified as valid hangers if they are in the `OST_PipeAccessory` category and their family name contains any of:
- `"-Pipe Hanger"`
- `"-Basic Adjustable"`
- `"Adjustable Ring Hanger"`

## Coordinate System Details

### Shared Coordinates (recommended)
Uses `ProjectLocation.GetTotalTransform()` to convert Revit internal coordinates to the real-world survey coordinate system. This is correct when the model's Survey Point has been properly located and rotated to match the project's survey control.

### Project Internal Coordinates
Uses Revit's internal coordinate system directly. Only use this if the field crew's Trimble job is set up relative to the project origin rather than real-world survey coordinates.

### Elevation Offset
Many projects set Revit's 0'-0" level at a convenient floor elevation rather than the true geodetic elevation. The offset field adds a constant to all Z values to convert from project elevation to real-world elevation.

## Point Naming

Points are named `{Prefix}-{NNN}` where NNN is a zero-padded sequential number. Hangers are sorted by level elevation, then by Y (northing), then by X (easting) for consistent numbering.

The Code column includes the user-specified code text plus the hanger's Nominal Diameter value string if available.

## Trimble Equipment Compatibility

| Equipment | Import Method |
|-----------|--------------|
| Trimble FieldLink | Import > CSV file |
| Trimble Access (RTS) | Import > "CSV Grid points N-E" or "CSV Grid points E-N" |
| Trimble Layout Manager | Import > Point file (.csv) |
| Generic total station | Any CSV point import |

## Important Notes

- **Coordinate order must match** the Trimble controller's coordinate order setting (N-E vs E-N)
- **Units must match** the Trimble job's unit system
- **Survey control alignment** is critical — the Revit model's Shared Coordinates must match the field survey control for points to land in the correct locations
- This is a **read-only** command — it does not modify any Revit elements
- UTF-8 encoding is used for the output file
