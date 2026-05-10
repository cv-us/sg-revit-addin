# tools/ — Build and Deploy Scripts

PowerShell scripts for building the plugin and deploying it to Revit.

## Scripts

### `build.ps1` — Build the Plugin

Compiles the source code into DLLs.

```powershell
# Build both SgRevit24 and SgRevit25 in Debug mode (default)
.\tools\build.ps1

# Build only the Revit 2023-2024 version
.\tools\build.ps1 -Project SgRevit24

# Build only the Revit 2025-2026 version
.\tools\build.ps1 -Project SgRevit25

# Build for release (optimized, no debug symbols)
.\tools\build.ps1 -Configuration Release
```

**Output locations:**
- `src/SgRevit24/bin/Debug/SgRevit24.dll`
- `src/SgRevit25/bin/Debug/SgRevit25.dll`

### `deploy-addin.ps1` — Install to Revit

Copies the built DLL and `.addin` manifest to Revit's add-ins folder so Revit loads it on startup.

```powershell
# Deploy to Revit 2024
.\tools\deploy-addin.ps1 -RevitVersion 2024

# Deploy to Revit 2025 (Release build)
.\tools\deploy-addin.ps1 -RevitVersion 2025 -Configuration Release
```

**Where it copies to:**
```
%AppData%\Autodesk\Revit\Addins\2024\SgRevit24.dll
%AppData%\Autodesk\Revit\Addins\2024\SgRevit24.addin

%AppData%\Autodesk\Revit\Addins\2025\SgRevit25.dll
%AppData%\Autodesk\Revit\Addins\2025\SgRevit25.addin
```

**Important:** You must **restart Revit** after deploying for changes to take effect. Revit only loads add-ins at startup.

## Typical Development Cycle

```
1. Edit code in src/Shared/
2. .\tools\build.ps1 -Project SgRevit24
3. .\tools\deploy-addin.ps1 -RevitVersion 2024
4. Start Revit → test your command
5. Close Revit → edit → repeat
```

## Troubleshooting

**"Build output not found"** — Run `build.ps1` before `deploy-addin.ps1`.

**"DLL is locked"** — Close Revit before deploying. Revit locks the DLL while running.

**"Command not showing up"** — Check that:
1. The command is registered in the `.addin` file (not commented out)
2. The `FullClassName` matches your actual namespace + class name exactly
3. You restarted Revit after deploying

