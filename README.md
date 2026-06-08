# SG Revit Addin

A Revit plugin suite that automates fire protection / fire sprinkler design workflows. Built by and for fire sprinkler designers who do layout, hydraulic calcs, coordination, fabrication lists, and plan detailing every day.

---

## What is this?

A **single Visual Studio solution** that builds into Revit add-in DLLs. Each "tool" you use in Revit (place sprinklers, generate a cut list, color-code pipes, etc.) is a **command** — a C# class that Revit loads and runs when you click a ribbon button.

Every command appears on the **SG ♈** tab in Revit's ribbon.

---

## Folder map

```
C:\dev\sg\
│
├── SgRevitAddin.sln          ← Open this in Visual Studio
├── CLAUDE.md                  ← Rules for Claude Code to follow when editing this project
├── .gitignore                 ← Keeps build junk out of git
│
├── src/                       ← ALL SOURCE CODE lives here
│   ├── Shared/                ← Code that works in ALL Revit versions (this is where you work 95% of the time)
│   │   ├── Commands/          ← Every ribbon button has a class here, organized by domain
│   │   ├── Utils/             ← Helper functions reused across commands
│   │   ├── Config/            ← Plugin settings (defaults, user prefs)
│   │   ├── Models/            ← Data classes (sprinkler info, pipe info, etc.)
│   │   └── UI/                ← Dialogs and ribbon icons
│   ├── SgRevit24/             ← Revit 2023-2024 build config (.NET 4.8, you rarely touch this)
│   └── SgRevit25/             ← Revit 2025-2026 build config (.NET 8,  you rarely touch this)
│
├── tests/                     ← Unit tests (optional, grows over time)
├── docs/                      ← Reference docs, command catalog, design briefs
├── installer/                 ← Inno Setup script + bundled families for the .exe installer
└── tools/                     ← Build & deploy PowerShell scripts
```

---

## The two projects — SgRevit24 vs SgRevit25

Autodesk changed the underlying .NET runtime between Revit 2024 and 2025:

| Project | .NET version | Revit versions | DLL output |
|---|---|---|---|
| **SgRevit24** | .NET Framework 4.8 | 2023, 2024 | `SgRevit24.dll` |
| **SgRevit25** | .NET 8 | 2025, 2026 | `SgRevit25.dll` |

**You almost never need to think about this.** All command code goes in `src/Shared/`, and both projects automatically pull every `.cs` file in via:

```xml
<Compile Include="..\Shared\**\*.cs" LinkBase="Shared" />
```

You only touch `src/SgRevit24/` or `src/SgRevit25/` when:
- The Revit API differs between versions (use `#if REVIT2024` / `#if REVIT2025` directives).
- You're registering a new ribbon button — `App.cs` is per-project (but typically identical).
- You're updating the `.addin` manifest.

---

## Command domains (how tools are organized)

Commands are grouped by **what part of the workflow they help with**:

| Folder | What it's for | Examples |
|---|---|---|
| `SprinklerLayout/` | Placing and checking heads | Auto-place in rooms, spacing/coverage check |
| `PipeRouting/` | Pipe routing and modification | Shorten flex pipes, branchline tools |
| `Hangers/` | All hanger operations | Place at structure / CAD lines, trapeze, sync rod length, section IDs, review markers |
| `Hydraulics/` | Hydraulic calculations | Run calcs, pressure loss, water supply checks |
| `Fabrication/` | Shop lists and material takeoffs | Pipe cut lists, sprinkler BOMs, hanger BOMs |
| `Coordination/` | MEP coordination | Color-code pipes, mark family instances |
| `Annotation/` | Plan detailing for the field | Pipe elevations, sleeve placement, flex drop tags, room text notes |
| `ViewsAndSheets/` | View management | Create plan views, dependent views, scope boxes |
| `Setup/` | New-project initialization | Load families, copy linked levels/grids, set global parameters |
| `ModelCheck/` | QA/QC validation | Sprinkler clearance, deflector distance, pipes-too-short |
| `Seismic/` | Seismic braces and gap checks | Seismic brace placement, hanger gap check |

If none fit, add a new domain folder and a panel in `App.cs`.

---

## Adding a new command

See `CLAUDE.md` for the step-by-step convention. Short version:

1. Create `src/Shared/Commands/{Domain}/{CommandName}Command.cs`. If it needs a dialog, add `{CommandName}Dialog.cs` next to it.
2. Register a ribbon button in BOTH `src/SgRevit24/App.cs` and `src/SgRevit25/App.cs`.
3. Build both projects and deploy.
4. Write `docs/commands/{command-name}.md`. Add a line to `docs/command-catalog.md`.

---

## Building and installing

### Prerequisites

- **Visual Studio 2022+** (Community is fine) or the `dotnet` CLI.
- **Revit installed locally** — the build needs the API DLLs from your Revit installation.
  - SgRevit24 references: `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`
  - SgRevit25 references: `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll`

### Build

```powershell
# Build both projects
dotnet build src/SgRevit24/SgRevit24.csproj -c Release
dotnet build src/SgRevit25/SgRevit25.csproj -c Release
```

### Deploy locally (for dev / testing)

```powershell
powershell -File tools/deploy-addin.ps1 -RevitVersion 2024
powershell -File tools/deploy-addin.ps1 -RevitVersion 2025
```

This copies the DLL and `.addin` manifest to `%APPDATA%\Autodesk\Revit\Addins\{year}\`. Restart Revit and the **SG ♈** tab appears in the ribbon.

### Build the installer

```powershell
powershell -File installer/build-installer.ps1
```

Output lands at `installer/Output/SgRevitAddin-{version}-Setup.exe`. The installer deploys to `%PROGRAMDATA%\Autodesk\Revit\Addins\{year}\` (system-wide) and bundles the SG family library to `C:\SG\Revit Families\`.

---

## Key files to know

| File | What it does |
|---|---|
| `SgRevitAddin.sln` | Open in Visual Studio to see everything |
| `src/Shared/Utils/TransactionWrapper.cs` | Wraps Revit transactions safely — use in every command that modifies the model |
| `src/Shared/Utils/ElementFilters.cs` | Pre-built queries for pipes, sprinklers, fittings |
| `src/Shared/Utils/ParameterHelpers.cs` | Read/write element parameters without boilerplate |
| `src/Shared/Utils/UnitConversion.cs` | Convert Revit's internal feet to inches/meters for display |
| `src/Shared/Utils/IconHelper.cs` | Loads embedded PNG icons by filename |
| `src/SgRevit24/SgRevit24.addin` | Revit 2023-2024 manifest |
| `src/SgRevit25/SgRevit25.addin` | Revit 2025-2026 manifest |
| `docs/command-catalog.md` | Master list of every command and what it does |
| `docs/icon-design-prompts.md` | Creative brief + tracker for ribbon icon replacements |
