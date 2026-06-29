# Setup: Create Plan Views By Level

**Command:** `CreatePlanViewsCommand`
**Domain:** ViewsAndSheets
**Ribbon:** SG Revit Addin > Views & Sheets > Create Plan Views

## Purpose

Creates floor and/or ceiling plan views, assigns a view template, sets a **Sub-Discipline**, and names each view. There are two source modes:

- **This model (by level)** — the original behaviour: one floor/ceiling view per selected level, named `LEVEL NAME - SUFFIX`.
- **Another model (copy views)** — replicate selected plan views from another **open or linked** document into this model (e.g. copy the architect's plan set). Each source view is **recreated** on the level of the same name, with your template, scope box (matched by name), and Sub-Discipline applied.

> Plan views are level-bound, so they can't literally be copied across documents — the command reads each source view's level/scale/scope-box and builds a fresh matching view here.

## Workflow

1. Dialog: choose view type, select levels, pick templates, set name suffix
2. Find the default ViewFamilyType for FloorPlan and CeilingPlan
3. Create `ViewPlan` for each selected level using `ViewPlan.Create()`
4. Apply the selected view template via `ViewTemplateId`
5. Rename each view to `UPPERCASE LEVEL NAME - SUFFIX`

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| Source mode | This model (by level) | Create from this model's levels, or copy views from another open/linked model |
| View type | Floor and Ceiling Plans | This model: which to create. Another model: a filter on which selected source views are replicated |
| Levels | All checked | (This model) Checklist sorted by elevation (highest first) |
| Source model + views | — | (Another model) Pick an open/linked document, then check which of its plan views to replicate |
| Floor plan template | "00 Working Floor Fine" | View template for new floor plans |
| Ceiling plan template | "00 Working Ceiling Fine" | View template for new ceiling plans |
| Sub-Discipline | (leave unset) | Value written to the `Sub-Discipline` parameter on each new view; dropdown of existing values + free text |
| Name suffix | OVERALL | Appended after the view name: "OVERALL", "FOR REFERENCE ONLY", or custom text (leave Custom blank for none) |

## View Naming

Views are named as:
```
LEVEL NAME (UPPERCASE) - SUFFIX
```

Examples:
- `LEVEL 1 - OVERALL`
- `BASEMENT - FOR REFERENCE ONLY`
- `LEVEL 2 - FIRE PROTECTION`


## View Template Application

Templates are applied using the `ViewTemplateId` property rather than `SetParameterByName("View Template")`:
- Templates are filtered by ViewType (FloorPlan templates only shown for floor plans, CeilingPlan templates only for ceiling plans)
- If "(none)" is selected, no template is applied

## View Creation

Uses `ViewPlan.Create(doc, viewFamilyTypeId, levelId)`:
- For floor plans: uses the first `ViewFamilyType` with `ViewFamily.FloorPlan`
- For ceiling plans: uses the first `ViewFamilyType` with `ViewFamily.CeilingPlan`
- If a view already exists at that level, Revit will create a new view with an auto-incremented name, then the rename step sets the desired name

## Summary Dialog

Reports:
- Floor plans created count
- Ceiling plans created count
- Skipped count (if any errors)
- Name format used
- Templates applied

## Copying views from another model

When **Another model** is selected:

1. The command lists candidate sources — every other **open** document plus every **loaded link** that contains floor/ceiling plan views.
2. Pick a source model; its plan views are listed as `Name  [Floor/RCP · Level · Scale · ⬚ScopeBox]`. Check the ones to replicate.
3. For each checked view, the command:
   - finds the destination level by **name** (falls back to the nearest level within 1 ft, otherwise skips with a reason),
   - creates a matching floor or ceiling `ViewPlan` on that level,
   - names it after the **source view** (+ suffix, collision-safe),
   - applies your floor/ceiling **template**,
   - sets the **scope box** if a scope box of the same name exists here,
   - writes the chosen **Sub-Discipline**.

What is **not** carried across: view-specific graphic overrides, annotations/tags/dimensions, dependent views, custom (non-rectangular) crop shapes, and the source's own template. You get the view shell on the right level with your template + sub-discipline.

> Tip: run **Setup → Import Link Levels/Grids** first so the destination has levels with the same names — that's what the level match keys on. Views whose level can't be matched are listed in the summary.

## Sub-Discipline

`Sub-Discipline` is a project/shared parameter on the view used for project-browser organization. It is set **after** the view template is applied, and only when the template doesn't lock it (`IsReadOnly`). The dropdown is seeded with the distinct values already in use in this model; you can also type a new one. Leave it on *(leave unset)* to skip writing it.

## Notes


