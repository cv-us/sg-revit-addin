# Views & Sheets: Remove Scope Boxes

**Command:** `RemoveScopeBoxesCommand`
**Domain:** ViewsAndSheets
**Ribbon:** SG Revit Addin > Views & Sheets > Remove Scope Boxes

## Purpose

Deletes scope boxes from the project. Used during project cleanup to remove temporary scope boxes that were used for view setup and are no longer needed.

## Workflow

1. Checks for pre-selected scope boxes (`OST_VolumeOfInterest`)
2. If none are pre-selected, collects all scope boxes in the project
3. Prompts user to confirm deletion with count
4. Deletes all targeted scope boxes in a single transaction
5. Reports count deleted

## Selection

- **Pre-selection:** if scope boxes are already selected when the command runs, only those are deleted
- **No pre-selection:** all scope boxes project-wide are targeted after confirmation

## Notes

- Scope boxes that are pinned will fail to delete — count as skipped
- Views that reference a deleted scope box will revert to their default crop settings
- The confirmation prompt always shows how many scope boxes will be affected
