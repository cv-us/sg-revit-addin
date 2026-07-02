# Tag Pipes

**Ribbon:** Modify tab → SG panel → **Tag Pipes**
**Class:** `SgRevitAddin.Commands.Modify.TagPipesCommand`

## Purpose

Places pipe length / stocklist tags, recreating HydraCAD's *Tag Pipes* workflow
with **your own loaded tag families**. It's a placement tool: each tag type just
places the family you pick for it, and the family's own label shows the length —
Tag Pipes does **not** compute center-to-center or cut lengths (your dimensioning
step, e.g. HydraCAD, fills those parameters; Dynamic uses native Revit length).

The dialog uses the SG-blue custom title bar (`ChromeDpiAwareForm`).

## Dialog

| Group | Control | Notes |
|---|---|---|
| **Pipe Tag Type** | Radios: Center to Center Length / Cut Length / Dynamic Length / Stocklisting Tags | Each radio has its own **family + type** dropdowns (the type list repopulates when you change the family). Stocklisting is special — it has a **Line** and a **Main** family/type pair, applied by pipe name (see below). |
| **Selection Method** | User Selection / System Walker Selection | See below. |
| **Drops** | Tag Drops Only · Include Drops with Selection · Drop family | A "drop" = a vertical pipe reaching a sprinkler. |
| **Options** | Reset Take-Out · Reset Cut · Run Cleanup · Transparent Backgrounds · Homogenize | See below. |

Every control (radios, all four family dropdowns, checkboxes, drop family) is
**remembered between runs** via DialogMemory.

## Stocklisting: line vs. main

When **Stocklisting Tags** is selected, each pipe is tagged based on its name:
- pipe name (or type name) contains **"main"** → the **Main** family/type
- else contains **"line"** → the **Line** family/type
- neither → skipped (reported in the summary)

## Selection Method

- **User Selection** — tags the pipes you have selected before running.
- **System Walker Selection** — starts from your selected seed pipe(s) and walks
  the connected piping network (through fittings / flex) collecting **every**
  connected pipe, then tags them all.

Select the pipe(s) first, then run Tag Pipes (the command reads the current
selection — a seed for the walker, or the set to tag).

## Drops

- **Neither box** → horizontal pipes are tagged, drops are skipped.
- **Include Drops with Selection** → drops are tagged too (with the Drop family).
- **Tag Drops Only** → only drops are tagged.

## Options

- **Homogenize Tags** — re-types **every existing pipe tag in the active view** to
  the chosen tag type (uniformity pass).
- **Transparent Backgrounds** — uses the transparent `-T` variant of the chosen
  family if it's loaded (HydraCAD's own approach — a separate family, since tag
  background opacity can't be toggled via the API).
- **Reset Take-Out** — writes `Length-Adjustment (Hydratec)` = `Length-Center_Center` − `Length-Cut_Length`.
- **Reset Cut** — writes `Length-Cut_Length (Hydratec)` = `Length-Center_Center` − `Length-Adjustment`.
  Both resets are **arithmetic on existing HydraCAD parameters** — if a project
  doesn't have those parameters, the option simply does nothing.
- **Run Cleanup** — a first-pass anti-overlap sweep that nudges overlapping new
  tags apart vertically. (A basic pass, not HydraCAD's full solver.)

## Notes

- Pipes that already carry a tag of the chosen family are skipped (no duplicates).
- The button lives on the Modify tab, which fires outside Revit's API context, so
  the work runs through an `ExternalEvent` (`DeferredActionHandler`).
- Tags are placed at each pipe's midpoint.

## See also

- [Sprig Tags](sprig-tags.md), [Flex Drops Set](flex-drop-lengths.md) — other tag commands.
