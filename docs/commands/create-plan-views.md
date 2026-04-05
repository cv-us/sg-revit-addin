# Setup: Create Plan Views By Level

**Command:** `CreatePlanViewsCommand`
**Domain:** ViewsAndSheets
**Ribbon:** SSG FP Suite > Views & Sheets > Create Plan Views

## Purpose

Creates floor and/or ceiling plan views for selected levels. Each new view is assigned a view template and renamed with a standardized suffix (e.g., "LEVEL 1 - OVERALL"). This is a project setup step for quickly generating the fire protection plan view set from the structural/architectural levels.

## Workflow

1. Dialog: choose view type, select levels, pick templates, set name suffix
2. Find the default ViewFamilyType for FloorPlan and CeilingPlan
3. Create `ViewPlan` for each selected level using `ViewPlan.Create()`
4. Apply the selected view template via `ViewTemplateId`
5. Rename each view to `UPPERCASE LEVEL NAME - SUFFIX`

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| View type | Floor and Ceiling Plans | Create both, floor only, or ceiling only |
| Levels | All checked | Checklist sorted by elevation (highest first) |
| Floor plan template | "00 Working Floor Fine" | View template for new floor plans |
| Ceiling plan template | "00 Working Ceiling Fine" | View template for new ceiling plans |
| Name suffix | OVERALL | Appended after level name: "OVERALL", "FOR REFERENCE ONLY", or custom text |

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

## Notes

