# Colorize by Workset (Construction Status)

**Command:** `SgRevitAddin.Commands.Coordination.ColorizeByWorksetCommand`
**Domain:** Coordination
**Ribbon:** SG ♈ > Coordination > Colorize by Workset

## Purpose

Color-codes pipes and fittings by the **construction status** carried on their workset (Existing / Demo / Modify / New), for a sprinkler construction-status workflow whose color must **survive export to Navisworks (.nwc)**.

## What survives NWC (the hard-won facts)

Navisworks colors each object from its **body material's Graphics-tab *shading* color**. These do **NOT** export to NWC and were dead ends:

- View filters, workset overrides, view-specific element graphic overrides (`OverrideGraphicSettings`).
- **Face paint** (`Document.Paint`) — it's a per-face *finish* the exporter ignores. (This was the v1 approach; it looked right in Revit's shaded view but vanished on export.)

So color must live on the **material the element actually resolves to**:

| Category | Mechanism | Why |
|----------|-----------|-----|
| **Pipes** (`OST_PipeCurves`) | colored per-status **duplicate pipe type** + `ChangeTypeId` | Material is segment/type-driven; instance material param is **read-only**. |
| **Fittings / accessories / sprinklers** (loadable families) | writable **instance** material param if present, else **duplicate the symbol** with its material params set + reassign `Symbol`. If the family has **no** material param (By-Category solids), the **Deep-color** pre-pass first *binds one* (see below). | There is no per-instance material override API for family geometry; a type with the material set is the only route. |
| **Flex pipes** (`OST_FlexPipeCurves`) | **one global color** — the command sets the **flex *category* material** (`Category.Material`) to the dominant flex status's color | `FlexPipeType` has **no** material parameter and the flex body is **By Category**, so there is *no per-element hook*. A single category color is the only thing Navisworks can read. |

### Deep-color: binding a material param to By-Category fab families

Fab fittings (HydraCAD threaded/grooved elbows, mech tees, couplings, etc.) are commonly modeled with **"By Category" solids and no material parameter** — confirmed with the **Inspect Materials** command (their geometry faces resolve to `<By Category>`). Nothing can color those per-status *as authored*.

With **Deep-color** on (default), the command runs a **pre-pass before the coloring transaction**: for each unique such family that has an in-scope, status-mapped instance, it

1. `Document fdoc = doc.EditFamily(family)`,
2. adds a **Material type parameter** (`SG Status Material`),
3. **associates every solid's `MATERIAL_ID_PARAM`** to it — *only* genuine in-family solids (`GenericForm` extrusions/blends/sweeps/revolves + `FreeFormElement`); nested instances, imports, voids and connectors are skipped on purpose (associating those corrupts the family and makes the reload throw),
4. `fdoc.LoadFamily(doc, …)` (overwrite).

After that the family carries a real material param, so the normal symbol-duplicate path colors it per status. This is **non-destructive** within the recommended workflow — the family edits live only in the open session and are **discarded on close-without-saving**. Families whose solids can't be bound (imported/SAT geometry, deeply nested) are reported with a reason.

### Pipe type duplication

For each pipe being colored, the command finds-or-creates `"{OriginalType} - {Status}"` (e.g. **`Welded - New`**): it duplicates the pipe type, and repoints the duplicate's routing-preference **segment(s)** at a segment that uses the `Status-{X}` material (a colored segment is created per material+schedule and reused). The pipe is then swapped with `ChangeTypeId`. This **preserves the system distinction** (welded / threaded / grooved) while coloring it — the NWC keeps the real type name *and* shows the status color.

## Recommended workflow (keep the fab model clean)

The colored types/segments/materials are real model objects. To avoid persisting them in your fabrication model:

> **Run the command → export the NWC → close the model WITHOUT saving** (and, on a workshared model, **without synchronizing**).

Everything the command created is discarded on close. Nothing reaches the central model. (In-session, **Clear All Coloring** also reverts.)

## Dialog

- **Workset → Status grid** — every user workset, its whole-model pipe/fitting count, and a status dropdown. **Auto-suggest from names** (new / demo / modif / exist); fully editable; multiple worksets can map to one status. **Each workset's chosen status is remembered** (by name) between runs.
- **Status Colors** — a swatch per status (defaults: Existing gray, Demo red, Modify amber, New green; remembered between runs).
- **Apply**
  - *Assign material* (the NWC path: pipe-type swap + fitting materials).
  - *Deep-color By-Category fittings & flex* (default on) — the in-memory family-rebind pre-pass for fittings, plus the single global flex color. Turn off to skip the (slower) family edits.
  - *Apply view graphic override* (active view, Revit-only).
- **Scope** — entire model / active view / selection. **Include sprinklers & accessories** toggle.
- **Preview Count**, **Apply**, **Clear All Coloring**, **Cancel**.

## Clear All Coloring (revert)

- **Pipes:** the colored type name encodes the original, so each pipe is `ChangeTypeId`'d **back to its original type** — stateless, reliable across any number of runs.
- **Fittings/etc.:** instance material reset to "by category" (best effort).
- **View overrides:** cleared on the target categories across graphical model views.
- Colored `Status-*` materials/segments/types are left in the project (reused, harmless). If you never saved, just close without saving to drop them.

## Notes / caveats

- **Shading color** is what NWC reads (set on the `Status-*` materials). In Navisworks, set the viewpoint render style to **Shaded** to see it. (Full Render mode reads the appearance asset, which this v1 does not set — a documented limitation.)
- Each head/element is wrapped in try/catch; the transaction commits only successful swaps. The summary reports pipes re-typed, fittings colored, and any types whose colored duplicate failed (with the reason) — never silent.
- **The summary names the families.** It lists the fitting/accessory/sprinkler **families it recolored** (✓) and the families it **couldn't** (✗ — no bindable material parameter, i.e. "By Category"/hardcoded solids). Use the ✗ list to know exactly which `.rfa` files need a Material parameter wired to their solids. If a family is in the ✓ list but still shows uncolored in the NWC, its material parameter exists but isn't wired to the visible solids — also a `.rfa` fix.
- **Field testing pending** for the pipe type/segment duplication (intricate, can't be validated without a live model). If a pipe's colored type fails it's reported and the pipe is left unchanged.
