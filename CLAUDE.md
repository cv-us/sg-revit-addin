# SG Revit Addin - Claude Code Conventions

## Project Structure
- **Dual-project**: SgRevit24 (.NET 4.8, Revit 2022-2024) and SgRevit25 (.NET 8, Revit 2025+)
- **Shared code** lives in `src/Shared/` and is linked into both projects via .csproj glob includes
- **Namespace**: `SgRevitAddin` for all shared code, commands, utils, models, config
- **Commands** use namespace `SgRevitAddin.Commands.{Domain}.{CommandName}`

## Code Conventions
- Every command class implements `IExternalCommand` with `[Transaction(TransactionMode.Manual)]`
- Use `TransactionWrapper` (in Utils) for safe transaction handling with auto-rollback
- Use `ParameterHelpers` and `ElementFilters` from Utils instead of writing inline parameter/filter logic
- Revit internal units are in feet — use `UnitConversion` when displaying to users
- Model-check / read-only commands use `[Transaction(TransactionMode.ReadOnly)]`
- **No Dynamo references** — all commands are standalone. Do NOT mention Dynamo, .dyn files, or migration origins in code comments, XML summaries, or documentation.
- The `"Dynamo Setting - "` prefix on global parameter names is a real Revit parameter name, not a reference to Dynamo — keep those as-is.

## Version-Specific Code
- Use `#if REVIT2024` / `#if REVIT2025` preprocessor directives for API differences
- Version-specific `App.cs` files live in `src/SgRevit24/` and `src/SgRevit25/` respectively

## Adding a New Command
1. Create `src/Shared/Commands/{Domain}/{CommandName}Command.cs`
2. If it needs a dialog, create `src/Shared/Commands/{Domain}/{CommandName}Dialog.cs`
3. Register ribbon button in both `src/SgRevit24/App.cs` and `src/SgRevit25/App.cs`
4. Build: `dotnet build src/SgRevit24/SgRevit24.csproj -c Release` and `dotnet build src/SgRevit25/SgRevit25.csproj -c Release`
5. Deploy: `powershell -File tools/deploy-addin.ps1 -RevitVersion {2023|2024|2025|2026}` (all 4)
6. Write docs: `docs/commands/{command-name}.md`
7. Update `docs/command-catalog.md`
8. Git commit and push

## Command Naming
- Use short, direct names that describe what the command does
- No `Auto` or `Insert` prefixes — e.g. `HangAtStructuralCommand`, `PipeElevationsCommand`
- Class name = filename = `{Name}Command.cs`, dialog = `{Name}Dialog.cs`

## Domain Categories
| Domain | Panel | Purpose |
|--------|-------|---------|
| SprinklerLayout | Sprinkler Layout | Head placement |
| PipeRouting | Pipe Routing | Branchlines, flex pipes |
| Hangers | Hangers | All hanger placement and sync |
| Seismic | Seismic | Seismic braces (currently in Annotation namespace) |
| Hydraulics | Hydraulics | Calc data |
| Fabrication | Fabrication | Cut lists |
| Export | Export | Trimble points, CSV |
| Coordination | Coordination | Color coding, clash |
| Annotation | Annotation | Elevations, sleeves, scale bars, text notes |
| ViewsAndSheets | Views & Sheets | Plan views, scope boxes |
| Setup | Setup | Families, global params, link levels/grids |
| ModelCheck | Model Check | QA/QC validation |

## Build & Deploy
- `dotnet build src/SgRevit24/SgRevit24.csproj -c Release` and `dotnet build src/SgRevit25/SgRevit25.csproj -c Release`
- Deploy: `powershell -File tools/deploy-addin.ps1 -RevitVersion {2023|2024|2025|2026}`

## Sandbox
- `sandbox/` is for macro prototyping — files here are NOT compiled into the plugin

## Dynamo Script Migration Queue
When the user provides a .dyn file to migrate, follow this workflow:
1. Read the .dyn JSON to understand the workflow (nodes, connections, Python scripts, code blocks)
2. Check `docs/command-catalog.md` for existing commands that do the same thing
3. If it's a duplicate or trivial variant of an existing command, tell the user and skip
4. If it's new or significantly different, create the command following "Adding a New Command" above
5. Use succinct command names, no Dynamo references anywhere

### Dynamo source folders (for reference)
- `C:\dev\Dynamo Revit\Revit 2024\` — DONE (all 36 scripts migrated)
- `C:\dev\Dynamo Revit\Revit 2023\` — to be checked for unique scripts not in Revit 2024
- `C:\dev\Dynamo Revit\2.3\` — oldest folder, 98 scripts, many may be older versions of already-migrated scripts but some are unique

