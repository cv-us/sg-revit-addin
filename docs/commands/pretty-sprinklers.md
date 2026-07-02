# Pretty Sprinklers

**Ribbon:** Modify tab → SG panel → **Pretty Sprinklers**
**Class:** `SgRevitAddin.Commands.Modify.PrettySprinklersCommand`

## Purpose

Places an **opaque head-symbol overlay** family coincident with each selected
sprinkler head, so the plan head symbol reads solid instead of having pipe /
branch lines run through it. Recreates HydraCAD's "Pretty Sprinklers."

Run with **no sprinklers selected** to **remove all** head overlays from the
active view (a toggle-style cleanup).

## How the overlay is chosen

For each selected sprinkler, the command reads its **type** (Symbol) graphics
parameters to decide which head symbol to use:

1. The checked `Symbol - HeadN` Yes/No parameter (`Symbol - Head1` … `Symbol - Head29`).
2. If none is checked, it parses the `HeadSymbol` text parameter (e.g. `Head02.png`).

That number *N* selects the overlay family named **`HeadN`** (e.g. `Head2`; a
zero-padded `Head02` is also accepted). Overlay families are category **Sprinkler
Tags** and must be loaded in the project.

## Workflow

1. Select the sprinklers to prettify (in the active view).
2. Run **Modify → Pretty Sprinklers**.
3. An opaque `HeadN` overlay is placed at each head. Re-running replaces the
   overlay already at a selected head (no stacking).
4. Run again with **nothing selected** to remove every head overlay in the view.

## Summary

Reports overlays placed, overlays replaced, sprinklers skipped (no head symbol
resolved or the `HeadN` family isn't loaded), and any missing overlay family
names to load.

## Notes

- The overlay is placed as a view annotation at the sprinkler's location.
- Skipped sprinklers usually mean the sprinkler type has no `Symbol - HeadN`
  checked / no `HeadSymbol`, or the matching `HeadN` family isn't loaded.
- Runs through an `ExternalEvent` (`DeferredActionHandler`) since the Modify-tab
  button fires outside Revit's API context.
