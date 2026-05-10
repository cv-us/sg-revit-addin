# Macro to Command Workflow

How to graduate a sandbox macro into a proper plugin command.

## Step 1: Prototype in Sandbox

1. Copy `sandbox/_macro_template.cs` to `sandbox/experiments/my_feature.cs`
2. Write your macro logic inside the `MyMacroName()` method
3. Paste into Revit's Macro Manager (Manage > Macro Manager > Application)
4. Run and iterate until it works

## Step 2: Identify the Command Domain

Pick the right folder under `src/Shared/Commands/`:

| If your macro does... | Put it in... |
|-----------------------|-------------|
| Places or checks sprinkler heads | `SprinklerLayout/` |
| Routes or modifies pipes | `PipeRouting/` |
| Places or adjusts hangers | `Hangers/` |
| Calculates hydraulics | `Hydraulics/` |
| Generates material lists | `Fabrication/` |
| Coordinates with other trades | `Coordination/` |
| Adds annotations/labels | `Annotation/` |
| Creates or manages views | `ViewsAndSheets/` |
| Sets up a new project | `Setup/` |
| Validates/checks the model | `ModelCheck/` |

## Step 3: Create the Command Class

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SgRevitAddin.Commands.YourDomain
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class YourFeatureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Replace: this.ActiveUIDocument
            // With:    commandData.Application.ActiveUIDocument
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Your macro logic here (adapted from the sandbox macro)

            return Result.Succeeded;
        }
    }
}
```

### Key Differences from Macros
- `this.ActiveUIDocument` becomes `commandData.Application.ActiveUIDocument`
- Wrap model changes in `TransactionWrapper` (from Utils)
- Extract reusable logic to `src/Shared/Utils/`

## Step 4: Register the Command

Add to both `.addin` files (`src/SgRevit24/SgRevit24.addin` and `src/SgRevit25/SgRevit25.addin`):

```xml
<AddIn Type="Command">
  <Name>Your Feature Name</Name>
  <Assembly>SgRevit24.dll</Assembly>  <!-- or SgRevit25.dll -->
  <FullClassName>SgRevitAddin.Commands.YourDomain.YourFeatureCommand</FullClassName>
  <ClientId>GENERATE-A-NEW-GUID-HERE</ClientId>
  <VendorId>SG</VendorId>
</AddIn>
```

## Step 5: Add Ribbon Button (Optional)

In `App.cs`, add the button to the appropriate panel.

## Step 6: Archive and Document

1. Move `sandbox/experiments/my_feature.cs` to `sandbox/graduated/`
2. Update `docs/command-catalog.md` with the new command

