# Annotation: Delete Duplicate Text

**Command:** `DeleteDuplicateTextCommand`
**Domain:** Annotation
**Ribbon:** SG Revit Addin > Annotation > Delete Duplicate Text

## Purpose

Deletes duplicate text notes at the same location in the active view. Cleans up views where annotation commands have been run multiple times, leaving stacked identical text notes.

## Workflow

1. Collects all `TextNote` elements in the active view
2. Groups by text content and XY location (within 0.5 ft proximity threshold)
3. Keeps one instance from each group, deletes the rest
4. Reports count of duplicates removed

## Notes

- Only text notes in the active view are examined
- Two text notes are considered duplicates if they share the same text string and their origins are within 0.5 ft of each other
- The first note in each group (by element ID order) is retained
- Read/write operation — requires a transaction

## See Also

- **[Choosing a Command](choosing-a-command.md)** — comparison of annotation cleanup commands
- **Clear Annotations** — removes ALL generic annotation instances from the view (use when you want a complete reset)

