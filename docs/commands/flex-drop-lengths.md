# Flex Drops Set — Insert Flexible Drop Lengths

**Ribbon:** SG → Annotation → **Flex Drops Set**
**Command class:** `SgRevitAddin.Commands.Annotation.FlexDropLengthsCommand`

## What it does

Tags selected sprinkler heads with a flexible drop length value and creates a visible tag annotation in the active view. Writes the `Flex Pipe Length` parameter on each sprinkler element and places a `-Flex Drop Length Tag` at the sprinkler location.

Every selected sprinkler gets the **same** length — the one you pick in the dialog. Companion command **Flex Drops Auto** instead auto-sizes each sprinkler from its measured flex pipe; see [Choosing a Command](choosing-a-command.md) for when to use which.

## How to use

1. Click **Flex Drops Set** in the ribbon (Annotation panel).
2. Select sprinkler heads — click sprinklers in the view, press Finish.
3. Fill in the dialog:
   - **Standard Length** — 31", 36", 48", 60", or 72".
   - **Tag Orientation** — N, NE, E, SE, S, SW, W, NW.
4. Click **Insert Tags**.

## What happens

1. **Existing flex-drop tags removed** — every `-Flex Drop Length Tag` instance in the active view is deleted first (clean slate).
2. **Parameter written on sprinklers** — `Flex Pipe Length` is set to e.g. `48 Inches`.
3. **Tags created** — a new `-Flex Drop Length Tag` is placed at each sprinkler location.

## Prerequisites

The `-Flex Drop Length Tag` annotation family must be loaded in the project. If not found, the command will prompt you to load it.

## Parameters modified

| Element | Parameter | Value |
|---|---|---|
| Sprinkler (FamilyInstance) | `Flex Pipe Length` | `31 Inches`, `36 Inches`, … |
| Tag (IndependentTag) | Family and Type | `-Flex Drop Length Tag` |

## See also

- [Flex Drops Auto](flex-drop-lengths-auto.md) — reads each sprinkler's actual connected flex pipe and assigns the matching standard. Use when flex pipes are already modeled.
- [Choosing a Command](choosing-a-command.md) — comparison of Set vs Auto.
