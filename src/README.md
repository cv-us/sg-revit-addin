# src/ — All Source Code

Everything that gets compiled into the plugin lives here.

## How It's Organized

```
src/
├── Shared/       ← Where you write code (commands, utils, models, config)
├── SgRevit24/        ← Build wrapper for Revit 2023-2024 (.NET Framework 4.8)
└── SgRevit25/        ← Build wrapper for Revit 2025-2026 (.NET 8)
```

## The Key Idea: Write Once in Shared, Build Twice

```
┌─────────────────────────────────────────┐
│              src/Shared/                 │
│  Commands/ Utils/ Models/ Config/ UI/   │
│         (your actual code)              │
└──────────┬──────────────┬───────────────┘
           │              │
     ┌─────▼─────┐  ┌────▼──────┐
     │  SgRevit24/   │  │  SgRevit25/   │
     │ .NET 4.8  │  │  .NET 8   │
     │ Revit     │  │  Revit    │
     │ 2023-2024 │  │  2025-2026│
     └─────┬─────┘  └────┬──────┘
           │              │
      SgRevit24.dll      SgRevit25.dll
```

**You work in `Shared/` 95% of the time.** Both SgRevit24 and SgRevit25 automatically pull in every `.cs` file from Shared via this line in their `.csproj`:

```xml
<Compile Include="..\Shared\**\*.cs" LinkBase="Shared" />
```

This means: any `.cs` file you add anywhere under `Shared/` is automatically compiled into BOTH DLLs. No extra steps needed.

## When Would I Touch SgRevit24/ or SgRevit25/?

Only in these rare cases:

1. **Version-specific API differences** — If a Revit API method changed between 2024 and 2025, you'd use `#if REVIT2024` / `#if REVIT2025` preprocessor directives in the Shared code. Both projects define these symbols in their `.csproj` files.

2. **Ribbon layout changes** — Each version has its own `App.cs` for ribbon button setup. Usually these are identical, but they could differ if the ribbon needs version-specific tweaks.

3. **Adding a new command to the .addin manifest** — When you graduate a macro to a command, you register it in BOTH `SgRevit24.addin` and `SgRevit25.addin`.

## The .csproj Files (Build Configuration)

Each project's `.csproj` file:
- Sets the .NET target framework
- References the Revit API DLLs from the installed Revit version
- Includes all Shared source files
- Defines a preprocessor constant (`REVIT2024` or `REVIT2025`) for conditional compilation

If Revit is installed to a non-standard path, update the `<HintPath>` entries in the `.csproj`.

## The .addin Files (Revit Registration)

The `.addin` XML file tells Revit:
- What DLL to load
- What commands are available (by full class name)
- The vendor info

When you deploy, this file gets copied to:
```
%AppData%\Autodesk\Revit\Addins\2024\SgRevit24.addin
%AppData%\Autodesk\Revit\Addins\2025\SgRevit25.addin
```

Revit reads it on startup and registers your commands.

