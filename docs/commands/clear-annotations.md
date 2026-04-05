# Annotation: Clear Annotations

**Command:** `ClearAnnotationsCommand`
**Domain:** Annotation
**Ribbon:** SSG FP Suite > Annotation > Clear Annotations

## Purpose

Deletes all generic annotation family instances from the active view. Used to clear pipe elevation markers, hanger section IDs, clearance flags, and other annotation families before re-running annotation commands.

## Workflow

1. Collects all `FamilyInstance` elements in the active view with category `OST_GenericAnnotation`
2. Prompts user to confirm deletion
3. Deletes all collected elements in a single transaction
4. Reports count of annotations removed

## Notes

- Only elements visible in the active view are affected; other views are not changed
- All generic annotation families are deleted regardless of family name
- Operates on the active view only — does not affect other views or sheets

## See Also

- **[Choosing a Command](choosing-a-command.md)** — comparison of annotation cleanup commands
- **Delete Duplicate Text** — surgical removal of only duplicate text notes (keeps one copy at each location)
