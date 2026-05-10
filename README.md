# SG Revit Addin

A Revit plugin suite that automates fire protection / fire sprinkler design workflows. Built by and for fire sprinkler designers who do layout, hydraulic calcs, coordination, fabrication lists, and plan detailing every day.

---

## What Is This?

This is a **single Visual Studio solution** that builds into Revit add-in DLLs. Each "tool" you use in Revit (place sprinklers, generate a cut list, color-code pipes, etc.) is a **command** — a C# class that Revit loads and runs when you click a ribbon button.

You don't need to understand all of this up front. The project is designed so you can:

1. **Write a quick macro** in `sandbox/` to test an idea
2. **Promote it** into a real command when it works
3. **Use it in Revit** from a custom ribbon tab

---

## Folder Map (What Goes Where)

```
C:\dev\sg\
│
├── SgRevitAddin.sln          ← Open this in Visual Studio
├── CLAUDE.md                  ← Rules for Claude Code to follow when editing this project
├── .gitignore                 ← Keeps build junk out of git
│
├── src/                       ← ALL SOURCE CODE lives here
│   ├── Shared/                ← Code that works in ALL Revit versions (this is where you work 95% of the time)
│   │   ├── Commands/          ← Your tools, organized by what they do
│   │   ├── Utils/             ← Helper functions you reuse across commands
│   │   ├── Config/            ← Plugin settings (defaults, user prefs)
│   │   ├── Models/            ← Data classes (sprinkler info, pipe info, etc.)
│   │   └── UI/                ← Dialogs and ribbon icons
│   ├── SgRevit24/                 ← Revit 2022-2024 build config (you rarely touch this)
│   └── SgRevit25/                 ← Revit 2025-2026 build config (you rarely touch this)
│
├── sandbox/                   ← MACRO PLAYGROUND — start here when prototyping
│   ├── _macro_template.cs     ← Copy this to start a new macro
│   ├── experiments/           ← Your work-in-progress macros
│   └── graduated/             ← Archive of macros that became real commands
│
├── tests/                     ← Unit tests (optional, grows over time)
├── docs/                      ← Reference docs, command list, workflow guides
├── dynamo-reference/          ← Notes on Dynamo scripts you're migrating to C#
└── tools/                     ← Build & deploy PowerShell scripts
```

---

## How Development Works (The Two Phases)

### Phase 1: Macro Prototyping (Quick & Dirty)

When you have an idea for a new tool:

1. Copy `sandbox/_macro_template.cs` → `sandbox/experiments/my_idea.cs`
2. Write your logic in the macro method
3. Open Revit → **Manage → Macro Manager → Application tab**
4. Create/open a macro module, paste your code, run it
5. Iterate until it works the way you want

**Macros are throwaway code.** They don't need to be perfect. They just need to prove the idea works.

### Phase 2: Graduating to a Plugin Command

When a macro is working and you want it in the ribbon permanently:

1. Create a proper command file in `src/Shared/Commands/{Domain}/`
2. Register it in the `.addin` files
3. Build → Deploy → Restart Revit → It's on your ribbon

See `docs/macro-to-command-workflow.md` for the step-by-step process.

---

## The Two Projects — SgRevit24 vs SgRevit25

Autodesk changed the underlying technology between Revit 2024 and 2025:

| Project | .NET Version | Revit Versions | DLL Output |
|---------|-------------|----------------|------------|
| **SgRevit24** | .NET Framework 4.8 | 2023, 2024 | `SgRevit24.dll` |
| **SgRevit25** | .NET 8 | 2025, 2026 | `SgRevit25.dll` |

**You almost never need to think about this.** All your command code goes in `src/Shared/`, and both projects automatically include it. The only time you'll touch `src/SgRevit24/` or `src/SgRevit25/` is if Revit's API changed between versions and you need version-specific code.

---

## Command Domains (How Tools Are Organized)

Commands are grouped by **what part of your workflow they help with**:

| Folder | What It's For | Examples |
|--------|--------------|---------|
| `SprinklerLayout/` | Placing and checking heads | Auto-place in rooms, spacing/coverage check |
| `PipeRouting/` | Pipe routing and modification | Auto-route branchlines, shorten flex pipes |
| `Hangers/` | All hanger operations | Auto-hang at spacing, trapeze hangers, seismic braces, sync to structure |
| `Hydraulics/` | Hydraulic calculations | Run calcs, pressure loss reports, water supply checks |
| `Fabrication/` | Shop lists and material takeoffs | Pipe cut lists, sprinkler BOMs, loose lists, hanger BOMs |
| `Coordination/` | MEP coordination | Pipe sleeves at walls/beams/deck, color-code pipes, clash helpers |
| `Annotation/` | Plan detailing for the field | Pipe elevations, fitting elevations, room names, hanger ticks |
| `ViewsAndSheets/` | View management | Duplicate FP plans, create dependent views, scope boxes |
| `Setup/` | New project initialization | Load families, copy linked levels/grids, set parameters |
| `ModelCheck/` | QA/QC validation | Sprinkler clearance checks, pipes-too-short-to-fab |

When you create a new command, just pick the folder that fits. If none fit, we can add a new domain.

---

## Building and Installing

### Prerequisites

- **Visual Studio 2022+** (Community edition is fine) or just the `dotnet` CLI
- **Revit installed** — the build needs the API DLLs from your Revit installation
  - SgRevit24 references: `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll`
  - SgRevit25 references: `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll`

### Build

```powershell
# Build everything
.\tools\build.ps1

# Build just one version
.\tools\build.ps1 -Project SgRevit24
.\tools\build.ps1 -Project SgRevit25

# Release build
.\tools\build.ps1 -Configuration Release
```

### Deploy to Revit

```powershell
# Deploy to Revit 2024
.\tools\deploy-addin.ps1 -RevitVersion 2024

# Deploy to Revit 2025
.\tools\deploy-addin.ps1 -RevitVersion 2025
```

This copies the DLL and `.addin` manifest to Revit's add-ins folder. Restart Revit and you'll see the **"SG"** tab in the ribbon.

---

## Quick Start — Your First Macro

1. Open `sandbox/_macro_template.cs` — read the comments
2. Copy it to `sandbox/experiments/test_pipe_colors.cs`
3. Write some simple logic (e.g., select all pipes and show a count)
4. Paste into Revit's Macro Manager and run it
5. Once it works → follow `docs/macro-to-command-workflow.md` to make it a real command

---

## Key Files to Know

| File | What It Does |
|------|-------------|
| `SgRevitAddin.sln` | Open this in Visual Studio to see everything |
| `src/Shared/Utils/TransactionWrapper.cs` | Wraps Revit transactions safely — use this in every command that modifies the model |
| `src/Shared/Utils/ElementFilters.cs` | Pre-built queries for pipes, sprinklers, fittings — saves you writing FilteredElementCollector every time |
| `src/Shared/Utils/ParameterHelpers.cs` | Read/write element parameters without boilerplate |
| `src/Shared/Utils/UnitConversion.cs` | Convert Revit's internal feet to inches/meters for display |
| `src/SgRevit24/SgRevit24.addin` | Tells Revit 2023-2024 which commands exist — you add entries here when graduating a command |
| `src/SgRevit25/SgRevit25.addin` | Same thing for Revit 2025-2026 |
| `sandbox/_macro_template.cs` | Starting point for every new macro experiment |
| `docs/command-catalog.md` | Master checklist of all commands and their status |

