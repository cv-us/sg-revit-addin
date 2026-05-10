# Shared/ — Your Main Working Directory

This is where virtually all your code lives. Everything here is compiled into BOTH SgRevit24.dll and SgRevit25.dll automatically.

## Subfolders

### `Commands/` — Your Tools
Each command is a C# class that Revit runs when you click a ribbon button (or trigger it from the Add-Ins tab). Organized by fire protection workflow domain. See `Commands/README.md` for details.

### `Utils/` — Reusable Helpers
Utility classes that multiple commands share. Things like "get all pipes in a view," "convert feet to inches," or "safely run a Revit transaction." See `Utils/README.md` for what each helper does.

### `Config/` — Plugin Settings
- `PluginSettings.cs` — A plain C# class with all the settings (company name, default spacing, export paths, etc.)
- `SettingsManager.cs` — Loads and saves settings to a JSON file in `%AppData%\SgRevitAddin\`
- `defaults.json` — The factory defaults that ship with the plugin

**How settings work:** On first run, the plugin uses `defaults.json`. When the user changes settings, `SettingsManager` saves them to their AppData folder. Next time Revit loads, it reads the user's saved settings.

### `Models/` — Data Classes (DTOs)
Plain C# classes that represent fire protection objects without depending on a running Revit instance:

- `SprinklerData.cs` — Sprinkler location, type, K-factor, orientation, coverage
- `PipeSegmentData.cs` — Pipe endpoints, diameter, length, material, system
- `HangerData.cs` — Hanger location, type, rod length, pipe size
- `FabricationItem.cs` — Generic item for cut lists and BOMs

**Why separate from Revit?** So you can pass data to export functions, calculation logic, or unit tests without needing a live Revit session. Commands create these from Revit elements, do work with them, then write results back.

### `UI/` — Dialogs and Icons
- `Dialogs/` — WPF windows (`.xaml` + `.xaml.cs` pairs) for settings screens, progress bars, input forms
- `Resources/icons/` — PNG images for ribbon buttons (Revit uses 16x16 and 32x32)

## Adding a New File

Just create a `.cs` file anywhere under `Shared/`. It's automatically compiled into both projects — no need to edit any `.csproj` file. The glob pattern `Include="..\Shared\**\*.cs"` catches everything.

## Namespace Convention

All code under Shared uses the `SgRevitAddin` root namespace:

```
SgRevitAddin.Commands.{Domain}.{CommandName}    ← Commands
SgRevitAddin.Utils                               ← Utilities
SgRevitAddin.Config                              ← Settings
SgRevitAddin.Models                              ← Data classes
```
