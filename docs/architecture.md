# Architecture Overview

How the SG Revit Addin plugin is structured and why.

## The Big Picture

```
                    ┌──────────────────────────────────┐
                    │          Revit Application         │
                    │                                    │
                    │  ┌───────────┐    ┌───────────┐   │
                    │  │ SgRevit24.dll │    │ SgRevit25.dll │   │
                    │  │ (.NET 4.8)│    │ (.NET 8)  │   │
                    │  └─────┬─────┘    └─────┬─────┘   │
                    │        │                │          │
                    │        └───────┬────────┘          │
                    │                │                   │
                    │    ┌───────────▼──────────┐        │
                    │    │     Shared Code       │        │
                    │    │ Commands/ Utils/ etc.  │        │
                    │    └──────────────────────┘        │
                    └──────────────────────────────────┘
```

Only ONE DLL loads per Revit version. Revit 2023/2024 loads `SgRevit24.dll`. Revit 2025/2026 loads `SgRevit25.dll`. Both contain the same shared code.

## Why One DLL, Not One Per Tool?

Revit's add-in system works like this:
- One `.addin` manifest → one DLL → many commands
- Each command is a class implementing `IExternalCommand`
- Revit loads the DLL once, then calls the right class when you click a button

If we made separate DLLs per tool, we'd have:
- Dozens of `.addin` files to manage
- Duplicate utility code in each DLL
- Multiple ribbon tabs cluttering the UI
- A nightmare to version, update, and deploy

One DLL keeps everything simple. One build, one deploy, one ribbon tab.

## How a Command Gets From Code to Ribbon Button

```
1. You write:     src/Shared/Commands/Hangers/HangAtCADLinesCommand.cs
                  (class SgRevitAddin.Commands.Hangers.HangAtCADLinesCommand)

2. You register:  src/SgRevit24/SgRevit24.addin
                  <FullClassName>SgRevitAddin.Commands.Hangers.HangAtCADLinesCommand</FullClassName>

3. You add UI:    src/SgRevit24/App.cs
                  (create a PushButton pointing to the same class name)

4. You build:     .\tools\build.ps1

5. You deploy:    .\tools\deploy-addin.ps1 -RevitVersion 2024

6. User sees:     Revit ribbon → "SG" tab → "Hangers" panel → "Hang at CAD" button
```

## The Transaction Model

Revit protects model integrity through transactions. Every model change MUST happen inside a transaction:

```
Without TransactionWrapper (dangerous):
  Transaction t = new Transaction(doc, "Do stuff");
  t.Start();
  // ... if an exception happens here, your model is corrupted
  t.Commit();

With TransactionWrapper (safe):
  using (var tw = new TransactionWrapper(doc, "Do stuff"))
  {
      // ... if an exception happens, Dispose() rolls back automatically
      tw.Commit();
  }
```

**Always use `TransactionWrapper` from Utils.** If you forget to commit, or if your code throws an error, it rolls back cleanly. No half-modified models.

## Data Flow Pattern

Commands follow this pattern for complex operations:

```
  Revit Model
      │
      ▼
  ElementFilters      ← Collect elements from the model
      │
      ▼
  Models (DTOs)       ← Convert to simple data objects
      │
      ▼
  Business Logic      ← Do calculations, make decisions
      │
      ▼
  TransactionWrapper  ← Apply changes back to the model
      │
      ▼
  Revit Model
```

By converting to Models (DTOs) in the middle, you can:
- Unit test the business logic without Revit
- Export data to files (CSV, JSON) for fabrication lists
- Keep commands focused on Revit interaction, not math

## The Preprocessor: Version-Specific Code

When the Revit API differs between versions, use `#if`:

```csharp
public static double GetPipeLength(Pipe pipe)
{
    #if REVIT2025
        // Revit 2025+ uses ForgeTypeId for units
        return pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)
                   .AsDouble();  // May need UnitUtils.ConvertFromInternalUnits
    #else
        // Revit 2024 and earlier
        return pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)
                   .AsDouble();
    #endif
}
```

The symbols `REVIT2024` and `REVIT2025` are defined in each project's `.csproj`. The compiler includes only the matching branch for each build.

## File Organization Philosophy

- **Commands/**: One file per tool. Named `{WhatItDoes}Command.cs`.
- **Utils/**: One file per category of helper. Named `{Category}Helpers.cs`.
- **Models/**: One file per domain object. Named `{Object}Data.cs`.
- **Config/**: Settings classes and JSON defaults.
- **UI/**: WPF dialogs and ribbon icon images.

Everything is in `Shared/` unless it absolutely must be version-specific.
