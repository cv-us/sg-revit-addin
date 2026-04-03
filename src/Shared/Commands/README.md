# Commands/ — Your Plugin Tools

Each subfolder here is a **domain** — a group of related tools for a specific part of the fire protection workflow. Each `.cs` file in a domain folder is one command that shows up in Revit.

## Domain Folders

### `SprinklerLayout/` — Sprinkler Heads
Tools for placing, adjusting, and validating sprinkler head positions.

**Typical commands you'd build here:**
- Auto-place sprinklers in rooms based on coverage rules (light hazard, ordinary hazard)
- Check spacing and coverage against NFPA 13 tables
- Verify deflector-to-ceiling distances for uprights/pendents
- Calculate and set flex drop lengths

### `PipeRouting/` — Pipes
Tools for creating and modifying pipe routes.

**Typical commands:**
- Auto-route branchlines from mains to heads
- Shorten/adjust flex pipes
- Flag pipes that are too short to fabricate

### `Hangers/` — Hangers and Supports
The biggest category — covers all hanger types, placement, and synchronization.

**Typical commands:**
- Auto-place single-pipe hangers at typical spacing (e.g., every 10')
- Auto-place hangers at structural members (beams, joists)
- Trapeze hanger placement (standard and unistrut)
- Sync hanger rod lengths to pipe elevation changes
- Sync hangers to structure above (raybounce to find attachment point)
- Swap HydraCAD hangers for your standard families
- Place seismic braces per code requirements

### `Hydraulics/` — Calculations
Tools for running or exporting hydraulic calculation data.

**Typical commands:**
- Export pipe/node data for external calc software (HydraCAD, Hydratec)
- Run pressure loss calculations on selected paths
- Check water supply adequacy

### `Fabrication/` — Shop Lists and Material Takeoffs
Tools for generating lists the shop and field need.

**Typical commands:**
- Pipe cut list (every pipe segment with size, length, material)
- Sprinkler material takeoff (head count by type)
- Fitting schedule (tees, elbows, reducers, etc.)
- Hanger BOM (rod, clamps, attachments)
- Loose list (everything that ships separate from spool pieces)

### `Coordination/` — MEP Coordination
Tools for working with other trades and managing constructability.

**Typical commands:**
- Auto-insert pipe sleeves at wall/beam/deck intersections
- Annotate sleeve elevations
- Color-code pipes by size (for coordination meetings)
- Color-code pipes by system type
- Reset color overrides
- Clash detection helpers

### `Annotation/` — Plan Detailing
Tools that add text, tags, and labels to views for field use.

**Typical commands:**
- Insert pipe elevation callouts
- Insert fitting elevation callouts
- Place room names and numbers in views
- Format hanger section IDs and tick marks
- Add graphic scale bars to sheets
- Clear/remove annotations from a view

### `ViewsAndSheets/` — Views and Sheets
Tools for creating and managing views.

**Typical commands:**
- Duplicate existing FP plan views (with filters, templates, etc.)
- Create new plan views per level
- Create dependent views for large floor plates
- Rotate/adjust scope boxes

### `Setup/` — Project Initialization
Tools you run once when starting a new project.

**Typical commands:**
- Load your standard FP families (sprinkler heads, hangers, fittings)
- Copy levels and grids from the architectural link
- Set global project parameters

### `ModelCheck/` — QA/QC Validation
Read-only tools that check the model for errors but don't change anything.

**Typical commands:**
- Verify upright sprinkler deflector clearances
- Find pipes too short to fabricate
- Clear check-related annotations after review

**Note:** ModelCheck commands use `[Transaction(TransactionMode.ReadOnly)]` instead of `Manual` since they don't modify the model.

---

## Anatomy of a Command

Every command follows this exact pattern:

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SSG_FP_Suite.Commands.Hangers  // ← domain name
{
    [Transaction(TransactionMode.Manual)]           // ← Manual for model changes, ReadOnly for checks
    [Regeneration(RegenerationOption.Manual)]
    public class AutoHangCommand : IExternalCommand  // ← class name = command name
    {
        public Result Execute(
            ExternalCommandData commandData,          // ← Revit gives you this
            ref string message,                       // ← Error message if you return Failed
            ElementSet elements)                      // ← Elements to highlight on failure
        {
            // Step 1: Get the document
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Step 2: Your logic here (use Utils helpers!)

            // Step 3: Return success/failure
            return Result.Succeeded;
        }
    }
}
```

## Adding a New Command

1. Pick the domain folder that fits (or create a new one)
2. Create `YourCommandNameCommand.cs`
3. Copy the pattern above, change the namespace and class name
4. Write your logic inside `Execute()`
5. Register in `src/SSG24/SSG24.addin` AND `src/SSG25/SSG25.addin`
6. Add a ribbon button in both `App.cs` files (optional — can also run from Add-Ins tab)
7. Update `docs/command-catalog.md`
