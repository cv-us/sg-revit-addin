# Insert Room Name/Number Text Notes

**Command:** `RoomTextNotesCommand`
**Domain:** Annotation
**Ribbon:** SG Revit Addin > Annotation > Room Text Notes

## Purpose

Places TextNotes in the active plan view for rooms from a linked Revit model. Each room gets a text note with the room name stacked word-by-word, followed by the room number on the last line. Only rooms on the selected level and within the active view's crop region are included.

## Workflow

1. Dialog opens with linked model, level, text style, and delete options
2. Rooms are collected from the selected linked model, filtered by level
3. Room center points are checked against the active view's crop region
4. Optionally, existing text notes of the selected type are deleted first
5. TextNotes are created at each room's center point with stacked name + number

## Dialog Options

| Setting | Description |
|---------|-------------|
| Linked Model | Dropdown of loaded Revit link instances containing rooms |
| Level | Level filter — only rooms on this level are processed |
| Text Style | TextNoteType to use for the placed text notes |
| Delete Existing | If checked, deletes all text notes of the selected type in the active view before placing new ones (prevents duplicates on re-run) |

## Text Format

Room names are split by spaces, with each word placed on a new line. The room number appears on the final line.

**Example:** Room "ELECTRICAL ROOM" with number "101" produces:
```
ELECTRICAL
ROOM
101
```

## Crop Region Filtering

- If the active view has a crop region enabled, only rooms whose center falls within the crop boundary are included
- Uses crop shape polygon (point-in-polygon ray casting) when available
- Falls back to crop box bounding rectangle if no explicit crop shape
- If crop is disabled, all rooms on the selected level are included

## View Requirements

- Must be run from a plan view (not a sheet or 3D view)
- Text notes are placed at Z=0 (plan view elevation)
- Text is center-aligned horizontally at the room's location point

## Notes

- The "Delete existing" option removes all text notes of the selected type from the active view before placing new ones, enabling clean re-runs without manual cleanup

- Rooms must be "placed" (Area > 0) to be included — unplaced rooms are skipped
- Room location uses the Revit `LocationPoint` from the linked room, transformed to host coordinates
- Levels are collected from all loaded links and deduplicated for the dropdown
- The linked model must be loaded (not unloaded) for rooms to be accessible

