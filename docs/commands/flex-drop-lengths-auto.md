# Flex Drops Auto — Auto-Size from Connected Flex Pipe

**Ribbon:** SG → Annotation → **Flex Drops Auto**
**Command class:** `SgRevitAddin.Commands.Annotation.FlexDropLengthsAutoCommand`

## Purpose

Tags sprinkler heads with a flex drop length, but instead of asking you to pick one length, this command **reads each sprinkler's connected flex pipe** and auto-sizes the tag to the next standard up. Supports **Wet** and **Dry** systems with different threshold tables.

## How it differs from Flex Drops Set

| | **Flex Drops Set** | **Flex Drops Auto** *(this command)* |
|---|---|---|
| Length decision | User picks ONE length, applied to all | Auto-sized **per sprinkler** from each one's measured flex pipe |
| Dialog inputs | Standard length + tag orientation | Wet vs Dry + tag orientation |
| Result across a run | Every sprinkler gets the same number | Each sprinkler can end up with a different standard |
| Over-length handling | N/A — user picks the value | Pipes longer than the system max are flagged and selected for review |
| Tag family | Fixed `-Flex Drop Length Tag` | Tries `{Description}-Flex Drop Length Tag` first, falls back to generic |

**Use Auto** when sprinklers already have flex pipes modeled and connected; the command does the per-sprinkler measurement and lookup for you.
**Use Set** for initial layout, when flex pipes aren't modeled yet, or when you've measured the run and know it's uniform.

## Workflow

1. Select sprinkler heads.
2. Dialog: choose **Wet** or **Dry**, set tag orientation.
3. For each sprinkler the command:
   - Traverses connectors to find the connected flex pipe (directly or through one fitting).
   - Reads the flex pipe's `Length` parameter (feet).
   - Looks up the matching standard from the threshold table.
4. Existing flex-drop tags on the selected sprinklers are deleted in the active view.
5. `Flex Pipe Length` is written on each sprinkler; a new tag is placed.
6. Sprinklers whose flex pipe exceeds the max are highlighted in the selection so you can review them.

## Threshold tables

### Wet system  *(max 5'-6")*

| Flex pipe length | Assigned standard |
|---|---|
| ≤ 3'-6" (3.5 ft) | `48` |
| ≤ 4'-6" (4.5 ft) | `60` |
| ≤ 5'-6" (5.5 ft) | `72` |
| > 5'-6" | flagged `Exceeds 5.5 Ft Length` |

### Dry system  *(max 4'-4")*

| Flex pipe length | Assigned standard |
|---|---|
| ≤ 2'-8" | `38` |
| ≤ 3'-8" | `50` |
| ≤ 4'-4" | `58` |
| > 4'-4" | flagged `Exceeds 4'-4" Length` |

## Connected flex-pipe finding

The command walks MEP connectors from each sprinkler to locate its flex pipe:

1. **Direct connection** — sprinkler connector → FlexPipe (most common).
2. **Through one fitting** — sprinkler → PipeFitting → FlexPipe (when a fitting sits between).

Flex pipe length is read in Revit internal units (feet) and compared against the threshold tables, which are also in feet.

## Tag family resolution

Uses dynamic tag-family naming based on the sprinkler's `Description` parameter:

1. Try to find a tag family named `"{Description}-Flex Drop Length Tag"` (e.g. `"Pendant-Flex Drop Length Tag"` when Description is `Pendant`).
2. If not found, fall back to the generic `"-Flex Drop Length Tag"`.

Results are cached per Description string within the same run so the lookup happens once per sprinkler type.

## Existing-tag handling

Before creating new tags, the command:

1. Finds every sprinkler tag in the active view.
2. Keeps any whose family name contains `Flex Drop Length Tag`.
3. Checks which of those are tagging one of the selected sprinklers.
4. Deletes the matches to prevent duplicates.

## Over-length handling

Sprinklers whose measured flex pipe exceeds the system max are:

- Assigned the `"Exceeds …"` message as their `Flex Pipe Length` value (so the tag itself reads as flagged).
- Collected into a list and **highlighted in the Revit selection** when the command finishes.
- Reported in a summary dialog listing each sprinkler ID and its measured length.

## Parameters modified

| Element | Parameter | Value |
|---|---|---|
| Sprinkler (FamilyInstance) | `Flex Pipe Length` | Standard length string (`48`, `60`, …) or `Exceeds …` message |
| Tag (IndependentTag) | Family and Type | `{Description}-Flex Drop Length Tag` (or generic fallback) |

## Summary dialog

Reports:
- Grouped breakdown — standard length → count of sprinklers.
- Tags deleted (pre-existing).
- Tags created (new).
- Parameters written.
- Count exceeding max length (if any).

## See also

- [Flex Drops Set](flex-drop-lengths.md) — pick one length, apply it to every selected sprinkler.
- [Choosing a Command](choosing-a-command.md) — comparison of Set vs Auto.
