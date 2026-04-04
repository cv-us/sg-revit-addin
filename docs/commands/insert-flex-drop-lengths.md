# AutoInsert — Flexible Drop Lengths

**Ribbon location:** SSG FP Suite tab > Annotation panel > "Flex Drop Lengths"
**Command class:** `SSG_FP_Suite.Commands.Annotation.InsertFlexDropLengthsCommand`
**Migrated from:** `AutoInsert - Flexible Drop Lengths.dyn`

## What It Does

Tags selected sprinkler heads with a flexible drop length value and creates a visible tag annotation in the active view. Writes the "Flex Pipe Length" parameter on each sprinkler element and places a "-Flex Drop Length Tag" at the sprinkler location.

## How to Use

1. **Click "Flex Drop Lengths"** in the ribbon (Annotation panel)
2. **Select sprinkler heads** — Click sprinklers in the view, press Finish
3. **Fill in the dialog:**
   - **Standard Length** — 31", 36", 48", 60", or 72"
   - **Tag Orientation** — N, NE, E, SE, S, SW, W, NW
4. **Click "Insert Tags"**

## What Happens

1. **Existing flex drop tags removed** — All "-Flex Drop Length Tag" instances in the active view are deleted (clean slate)
2. **Parameter written on sprinklers** — "Flex Pipe Length" set to e.g. "48 Inches"
3. **Tags created** — New "-Flex Drop Length Tag" placed at each sprinkler location

## Prerequisites

The "-Flex Drop Length Tag" annotation family must be loaded in the project. If not found, the command will prompt you to load it.

## Parameters Modified

| Element | Parameter | Value |
|---------|-----------|-------|
| Sprinkler (FamilyInstance) | Flex Pipe Length | "31 Inches", "36 Inches", etc. |
| Tag (IndependentTag) | Family and Type | -Flex Drop Length Tag |

## Differences from the Dynamo Version

| Feature | Dynamo Script | Plugin Command |
|---------|--------------|----------------|
| Speed | Slow — Data-Shapes + Python deletion | Fast — native C# |
| Dependencies | Data-Shapes | Zero |
| UI | Data-Shapes radio + selection | WinForms dialog |
| Global param storage | Saves to Dynamo Setting params | Not needed |
| Tag orientation | Collected but unused | Collected (preserved for future use) |

## Changing the Icon

The button icon is at `src/Shared/UI/Resources/icons/annotation-32.png` (shared with other annotation commands). Replace with a unique icon if desired.
