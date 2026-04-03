# SSG FP Suite - Claude Code Conventions

## Project Structure
- **Dual-project**: SSG24 (.NET 4.8, Revit 2022-2024) and SSG25 (.NET 8, Revit 2025+)
- **Shared code** lives in `src/Shared/` and is linked into both projects via .csproj glob includes
- **Namespace**: `SSG_FP_Suite` for all shared code, commands, utils, models, config
- **Commands** use namespace `SSG_FP_Suite.Commands.{Domain}.{CommandName}`

## Code Conventions
- Every command class implements `IExternalCommand` with `[Transaction(TransactionMode.Manual)]`
- Use `TransactionWrapper` (in Utils) for safe transaction handling with auto-rollback
- Use `ParameterHelpers` and `ElementFilters` from Utils instead of writing inline parameter/filter logic
- Revit internal units are in feet — use `UnitConversion` when displaying to users
- Model-check / read-only commands use `[Transaction(TransactionMode.ReadOnly)]`

## Version-Specific Code
- Use `#if REVIT2024` / `#if REVIT2025` preprocessor directives for API differences
- Version-specific `App.cs` files live in `src/SSG24/` and `src/SSG25/` respectively

## Adding a New Command
1. Create `src/Shared/Commands/{Domain}/{CommandName}Command.cs`
2. Implement `IExternalCommand` with the correct namespace
3. Register in both `src/SSG24/SSG24.addin` and `src/SSG25/SSG25.addin`
4. Add ribbon button in both `App.cs` files
5. Update `docs/command-catalog.md`

## Sandbox
- `sandbox/` is for macro prototyping — files here are NOT compiled into the plugin
- Use `_macro_template.cs` as a starting point for new macros
