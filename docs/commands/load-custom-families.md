# Setup: Load Custom Families

**Command:** `LoadFamiliesCommand`
**Domain:** Setup
**Ribbon:** SSG FP Suite > Setup > Load Families
**Migrated from:** `! Setup - Load Custom Families.dyn` (V02)

## Purpose

Loads custom Revit family (.rfa) files from a specified folder into the current project. Families that are already loaded (by name) are skipped. This is a project setup step for bulk-loading the standard fire protection family library.

## Workflow

1. Dialog: select folder path, choose whether to include subfolders
2. Enumerate all `.rfa` files in the selected folder
3. Collect names of families already loaded in the project
4. Load each missing family via `Document.LoadFamily()`
5. Report summary: loaded, skipped, failed counts

## Dialog Options

| Setting | Default | Description |
|---------|---------|-------------|
| Family Folder | `C:\BIM Support\Revit\Families\{version}` | Path to folder containing .rfa files |
| Include subfolders | Checked | Recursively search subdirectories for .rfa files |

The default subfolder is version-aware:
- Revit 2021+ uses the `2021` subfolder
- Older versions use the `2017` subfolder

This matches the Dynamo script's `version >= 2021 ? "2021" : "2017"` logic.

## Family Loading

Uses `Document.LoadFamily(path, out family)`:
- Returns `true` if the family was loaded successfully
- Returns `false` if a family with the same name is already loaded
- Before attempting load, the command pre-checks against a set of already-loaded family names (case-insensitive) for efficiency
- If the same family name exists in multiple subfolders, only the first encountered is loaded

## Skip Logic

The Dynamo Clockwork `Document.LoadFamily` node states: "Will *not* reload an already loaded family of the same name." The C# version replicates this by:
1. Collecting all `Family` elements in the document into a `HashSet<string>` (case-insensitive)
2. Checking each .rfa filename (without extension) against this set before loading
3. Also handling the `LoadFamily()` return value as a fallback

## Summary Dialog

Reports:
- Folder path searched
- Total .rfa files found
- Number of new families loaded
- Number already loaded (skipped)
- Number failed (with names, up to 10)

## Notes

- The Dynamo version used Clockwork's `Document.LoadFamily` custom node and `FileSystem.GetDirectoryContents`
- The C# version uses `Directory.GetFiles()` with `SearchOption.AllDirectories` and the native `Document.LoadFamily()` API
- The Dynamo version hardcoded `C:\BIM Support\Revit\Families\` as the base path; the C# version uses it as a default but allows browsing to any folder
- No `IFamilyLoadOptions` override is used — the default behavior (do not overwrite existing) matches the Dynamo script's behavior
