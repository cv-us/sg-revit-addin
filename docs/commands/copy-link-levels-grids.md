# Setup: Copy Link Levels and Grids

**Command:** `CopyLinkLevelsGridsCommand`
**Domain:** Setup
**Ribbon:** SG Revit Addin > Setup > Copy Link Levels/Grids

## Purpose

Copies levels and/or grids from a selected linked Revit model into the host document. Detects and skips duplicates by name, reassigns grid types to match the host project standard, and optionally pins the new elements. This is typically the first step when setting up a new fire protection model — importing the structural/architectural coordination levels and grids.

## Workflow

1. Dialog: select linked model, import mode, grid type, selection options, pin toggle
2. Collect all levels/grids from the linked model
3. Filter out duplicates (name already exists in host)
4. Optional: secondary dialog for user to pick specific levels/grids
5. Create new levels at matching elevations with matching names
6. Create new grids from transformed curve geometry with matching names
7. Reassign grid type to user-selected type
8. Optionally pin all new elements
9. Summary dialog with counts and skipped duplicates

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| Link selection | First loaded link | Which linked model to import from |
| Import mode | Levels and Grids | Import both, levels only, or grids only |
| Grid type | "SS Grid Head - 3/8\" Bubble" | Grid type to assign to imported grids |
| Level selection | Copy ALL | Copy all or select specific levels |
| Grid selection | Copy ALL | Copy all or select specific grids |
| Pin elements | Checked | Pin all imported levels and grids |
| Set up Copy/Monitor instead | Unchecked | Skip the recreate-import and launch Revit's native Copy/Monitor tool for the chosen link (the only way to get a real monitor relationship) |

### Selection Dialogs (Secondary)

If "Select specific" is chosen for levels or grids, a secondary checklist dialog appears showing all available items (pre-checked). User can check/uncheck individual items, or use Select All / Select None buttons.

Level items display as: `"Level Name  (Elev: X.XX)"`
Grid items display by name.

## Duplicate Detection

- **Levels:** Compared by name (case-insensitive). A link level with the same name as an existing host level is skipped.
- **Grids:** Compared by name (case-insensitive). A link grid with the same name as an existing host grid is skipped.
- Skipped elements are listed by name in the summary dialog.

## Level Creation

For each non-duplicate link level:
1. Read the level's elevation from the linked document
2. Apply the link instance transform if the link is not at origin
3. Create via `Level.Create(doc, elevation)`
4. Set the name to match the link level name

## Grid Creation

For each non-duplicate link grid:
1. Get the grid's curve geometry from the linked document
2. Transform the curve to host coordinates via `Curve.CreateTransformed(linkTransform)`
3. Create via `Grid.Create(doc, line)` or `Grid.Create(doc, arc)` depending on curve type
4. Set the name to match the link grid name
5. Change the grid type to the user-selected type via `ChangeTypeId()`

## Pinning

If enabled, all newly created levels and grids have `Element.Pinned = true` set after creation. This prevents accidental moves during design work.

## Summary Dialog

Reports:
- Levels copied count
- Levels skipped (with names listed)
- Grids copied count
- Grids skipped (with names listed)
- Pin status
- Grid type applied

## Copy/Monitor (native tool hand-off)

The Revit public API **cannot create a Copy/Monitor (coordination monitor) relationship** — it can only read (`GetMonitoredLinkElementIds`) or stop (`StopMonitoring`) one. So this command's normal path *recreates* levels/grids as plain, unmonitored elements.

When you actually want the copied datums **monitored**, check **"Set up Copy/Monitor instead."** The command then skips its own import and posts Revit's native Copy/Monitor command (`PostableCommand.CopyMonitorSelectLink`). Revit's tool both copies AND monitors:

1. The dialog's other import options are disabled (they don't apply in this mode).
2. On OK, a short notice appears, then Revit drops you into Copy/Monitor's "select link" mode.
3. Pick the link, then use **Copy** or **Batch Copy** to copy and monitor its levels/grids, and Finish.

Because Revit does the copy here, don't also run the normal import on the same datums or you'll get duplicates.

## Notes

- Grid curves are properly transformed from linked model coordinates to host model coordinates
- Arc grids (curved grids) are supported in addition to linear grids
- The Copy/Monitor checkbox is a hand-off to Revit's native tool — the add-in can't establish the monitor relationship itself

