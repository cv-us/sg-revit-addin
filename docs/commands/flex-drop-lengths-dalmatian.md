# AutoInsert — Flexible Drop Lengths (Dalmatian Fire Style)

**Command:** `FlexDropLengthsDalmatianCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Flex Drop Dalmatian

## Purpose

Inserts flexible drop length tags on sprinkler heads, automatically calculating the standard length from the actual flex pipe connected to each sprinkler. Supports **Wet** and **Dry** system types with different standard length thresholds.

## Key Difference from Standard Flex Drop Command

| Feature | Standard (`FlexDropLengthsCommand`) | Dalmatian (this command) |
|---------|------------------------------------------|--------------------------|
| Length source | User picks one fixed length for all | Auto-reads each sprinkler's actual flex pipe |
| System types | N/A | Wet and Dry with different thresholds |
| Length values | 31" / 36" / 48" / 60" / 72" | Wet: 48" / 60" / 72"; Dry: 38" / 50" / 58" |
| Max length check | No | Yes — flags pipes exceeding max |
| Tag family | Fixed "-Flex Drop Length Tag" | Dynamic: [Description]-Flex Drop Length Tag |

## Workflow

1. User selects sprinkler heads
2. Dialog: pick Wet or Dry system type, tag orientation
3. For each sprinkler, find connected flex pipe via connector traversal
4. Read actual flex pipe length from the "Length" parameter
5. Assign standard length string based on threshold buckets
6. Delete existing flex drop tags on selected sprinklers in the view
7. Write "Flex Pipe Length" parameter on each sprinkler
8. Create tag for each sprinkler using the resolved tag family
9. Flag and highlight sprinklers with flex pipes exceeding the maximum

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| System Type | Wet | Wet or Dry — changes length thresholds and max |
| Tag Orientation | NE | Compass direction for tag offset placement |

## Length Threshold Tables

### Wet System
| Flex Pipe Length | Assigned Standard Length |
|-----------------|------------------------|
| <= 3'-6" (3.5 ft) | "48" |
| <= 4'-6" (4.5 ft) | "60" |
| <= 5'-6" (5.5 ft) | "72" |
| > 5'-6" | Flagged: "Exceeds 5.5 Ft Length" |

### Dry System
| Flex Pipe Length | Assigned Standard Length |
|-----------------|------------------------|
| <= 2'-8" (32/12 ft) | "38" |
| <= 3'-8" (44/12 ft) | "50" |
| <= 4'-4" (52/12 ft) | "58" |
| > 4'-4" | Flagged: "Exceeds 4'-4\" Length" |

## Connected Flex Pipe Finding

The command traverses MEP connectors from each sprinkler to find its flex pipe:

1. **Direct connection:** Sprinkler connector → FlexPipe (most common)
2. **Through fitting:** Sprinkler connector → PipeFitting → FlexPipe (when a fitting sits between)

The flex pipe's "Length" parameter is read in Revit internal units (feet).

## Tag Family Resolution

The Dalmatian style uses **dynamic tag family naming** based on the sprinkler's "Description" parameter:

1. Read sprinkler's "Description" parameter (e.g., "Pendant")
2. Try to find tag family named `"Pendant-Flex Drop Length Tag"`
3. If not found, fall back to generic `"-Flex Drop Length Tag"`

This allows different sprinkler types to have different tag families. Results are cached per description for performance.

## Existing Tag Handling

Before creating new tags, the command:
1. Finds all sprinkler tags in the active view
2. Checks if each tag's family name contains "Flex Drop Length Tag"
3. Checks if the tagged element is one of the selected sprinklers
4. Deletes matching tags to prevent duplicates

## Too-Long Flex Pipe Handling

Sprinklers with flex pipes exceeding the system maximum are:
- Assigned the exceeds message as their "Flex Pipe Length" parameter value
- Collected into a separate list
- **Highlighted in the Revit selection** after the command completes
- Reported in a warning dialog listing each sprinkler ID and actual flex pipe length

## Parameters Written

| Parameter | Written To | Value |
|-----------|-----------|-------|
| `Flex Pipe Length` | Sprinkler element | Standard length string ("48", "60", etc.) or exceeds message |

## Summary Dialog

Reports:
- Grouped breakdown: standard length → count of sprinklers
- Tags deleted (existing)
- Tags created (new)
- Parameters written
- Count exceeding max length (if any)

## Notes

- Flex pipe length is read from the Revit "Length" parameter in feet, then compared against the threshold table which is also in feet
- If no flex pipe is found connected to a sprinkler, the smallest bucket length is assigned as a fallback
