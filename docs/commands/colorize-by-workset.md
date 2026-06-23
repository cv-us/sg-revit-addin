# Colorize by Workset (Construction Status)

**Command:** `SgRevitAddin.Commands.Coordination.ColorizeByWorksetCommand`
**Domain:** Coordination
**Ribbon:** SG ♈ > Coordination > Colorize by Workset

## Purpose

Color-codes pipes and fittings by the **construction status** carried on their workset (Existing / Demo / Modify / New), for a sprinkler construction-status workflow. The headline goal is color that **survives export to Navisworks (.nwc)**.

## Why face paint (the NWC constraint)

These Revit mechanisms do **NOT** export to NWC:

- View filters
- Workset graphic overrides
- View-specific element graphic overrides (`OverrideGraphicSettings`)

Only **material color bakes into the geometry** and survives the GC's append. So the command's primary path assigns a per-status material and writes it onto elements via **`Document.Paint`** (face paint). Paint is used rather than the material *parameter* because:

- Pipe material (`RBS_PIPE_MATERIAL_PARAM`) is segment-driven and usually **read-only** per instance.
- Fitting material is inconsistent across loadable families.
- Paint reliably overrides display **and** export color for both, and is trivially reversible.

The view-graphic-override path is also offered for in-Revit visualization, but is clearly labeled **does NOT export**.

## Dialog

1. **Workset → Status grid** — every user workset, with its whole-model pipe/fitting count and a status dropdown (Existing / Demo / Modify / New / Ignore). Because worksets encode system *and* status (e.g. "Sys A - New"), multiple worksets can map to the same status. **Auto-suggest from names** sets each row by keyword (new / demo / modif / exist); every row is still editable.
2. **Status Colors** — a color swatch per status (defaults: Existing gray, Demo red, Modify amber, New green). Click to change; remembered between runs.
3. **Apply** — check one or both:
   - *Assign material* (recommended) — paints faces, exports to NWC.
   - *Apply view graphic override* — active view only, Revit visualization only.
4. **Scope** — Entire model / Active view's visible elements / Current selection.
5. **Also include sprinklers & pipe accessories** — extends the category set.
6. **Preview Count** — per-status element totals from the current mapping.
7. Buttons: **Apply**, **Clear All Coloring**, **Cancel**.

## Processing

- Collects `OST_PipeCurves` + `OST_PipeFitting` (+ `OST_Sprinklers` + `OST_PipeAccessory` if checked) per scope, `WhereElementIsNotElementType`.
- For each element: reads `WorksetId`, looks up the status; **Ignore / unmapped → skipped**.
- Ensures a `Status-{X}` material exists (creates if missing, updates its shading + surface-pattern color to the chosen color, solid fill, no transparency — shading color is what NWC reads).
- Material mode: paints every face of every solid (recurses into family `GeometryInstance` for fittings).
- View-override mode: sets surface/cut foreground (solid fill) + projection line color in the active view.
- All in a single transaction (one undo).
- Per-element try/catch so one bad element never aborts the batch.

## Clear All Coloring (reset)

The **Clear All Coloring** button reverts everything the command applied, **no matter how many times it was run**:

- `RemovePaint` on every face of every targeted element (idempotent — paint isn't cumulative).
- Clears element graphic overrides on the targeted categories across all graphical model views.
- Leaves the `Status-*` materials in the project (reused, harmless).

## Edge cases handled

- Non-workshared document → detected up front, clear message, exits.
- Element with no mapped/Ignored workset → skipped (counted).
- Element with no geometry/faces → skipped for paint (counted as "no paintable geometry").
- Re-running is idempotent: materials are reused (color refreshed), paint is replaced not stacked.

## Reporting

After Apply: per-status counts, faces painted, view overrides applied, and counts of skipped / no-geometry / failed elements.

## Notes / future

- Material assignment via the writable material *parameter* (instead of paint) was considered; paint was chosen as the dependable mechanism for both pipes and fittings and for clean reversal. If a project needs the parameter path, it can be added with original-material tracking for revert.
- For very large models the paint loop touches many faces; it collects once and avoids regeneration inside the loop.
