# Utils/ — Reusable Helper Classes

These are shared utility functions that multiple commands use. Instead of writing the same Revit API boilerplate in every command, you call these helpers.

**Rule of thumb:** If you find yourself copying the same 5+ lines of code between commands, extract it into a helper here.

---

## Helper Reference

### `TransactionWrapper.cs` — Safe Transaction Handling
**Use this in EVERY command that modifies the Revit model.**

Revit requires all model changes to happen inside a Transaction. If something goes wrong mid-transaction, you need to roll back. This wrapper does that automatically:

```csharp
// Instead of manually managing Transaction start/commit/rollback:
using (var tw = new TransactionWrapper(doc, "Place Hangers"))
{
    // Do your model changes here
    // If an exception is thrown, the transaction auto-rolls-back

    tw.Commit(); // Only if everything succeeded
}
// If you forget to call Commit(), or an exception happens,
// the Dispose() method rolls back automatically. Your model stays clean.
```

### `ElementFilters.cs` — Finding Elements in the Model
Pre-built queries that save you from writing `FilteredElementCollector` every time:

```csharp
// Get all pipes in the document
IList<Pipe> pipes = ElementFilters.GetAllPipes(doc);

// Get sprinklers visible in the current view
IList<FamilyInstance> heads = ElementFilters.GetSprinklersInView(doc, viewId);

// Get pipe fittings in a view
IList<FamilyInstance> fittings = ElementFilters.GetPipeFittingsInView(doc, viewId);
```

**Add more filters here** as you need them — hangers, flex pipes, specific families, etc.

### `ParameterHelpers.cs` — Reading/Writing Element Parameters
Every Revit element has parameters (Size, Length, System Type, etc.). This helper reads them without boilerplate:

```csharp
// Read any parameter by name — handles all storage types
string systemName = ParameterHelpers.GetParamValueAsString(pipe, "System Type");
string pipeDia = ParameterHelpers.GetParamValueAsString(pipe, "Diameter");

// Write parameters
ParameterHelpers.SetParamValue(element, "Comments", "Placed by SSG FP Suite");
ParameterHelpers.SetParamValue(element, "Offset", 10.5);
```

### `UnitConversion.cs` — Feet ↔ Inches ↔ Meters
**Critical:** Revit stores ALL lengths internally in **feet**. A 1-inch pipe has an internal diameter of `0.0833333...`. You need to convert when displaying to users:

```csharp
double internalLength = 10.0;  // 10 feet in Revit's internal units

double inches = UnitConversion.FeetToInches(internalLength);   // → 120.0
double meters = UnitConversion.FeetToMeters(internalLength);   // → 3.048
string display = UnitConversion.FormatFeetInches(internalLength); // → "10'-0.00\""

// Going the other direction (user input → Revit):
double feet = UnitConversion.InchesToFeet(6.0);  // → 0.5
```

### `GeometryHelpers.cs` — Point and Distance Math
Common geometry operations on Revit's XYZ points:

```csharp
double dist = GeometryHelpers.DistanceBetweenPoints(point1, point2);
XYZ mid = GeometryHelpers.MidPoint(point1, point2);

// 2D distance (ignores Z/elevation) — useful for plan-view spacing checks
double planDist = GeometryHelpers.HorizontalDistance(point1, point2);
```

### `PipeHelpers.cs` — Pipe-Specific Queries
Quick access to common pipe properties:

```csharp
double diameter = PipeHelpers.GetPipeDiameter(pipe);   // in feet (internal units)
double length = PipeHelpers.GetPipeLength(pipe);        // in feet
XYZ start = PipeHelpers.GetPipeStartPoint(pipe);
XYZ end = PipeHelpers.GetPipeEndPoint(pipe);
string system = PipeHelpers.GetSystemTypeName(pipe);    // e.g., "Fire Protection Wet"
```

### `ViewHelpers.cs` — View Operations
Helpers for working with Revit views:

```csharp
// Generate a unique view name (appends "(1)", "(2)", etc. if taken)
string name = ViewHelpers.MakeUniqueViewName(doc, "FP - Level 1");

// Read a view's parameter value as a string
string template = ViewHelpers.GetViewParamAsString(view, "View Template");
```

### `SelectionHelpers.cs` — User Selection Filters
Let users pick specific element types:

```csharp
// Prompt user to select only pipes
IList<Element> selectedPipes = SelectionHelpers.PickElementsByCategory(
    uidoc,
    BuiltInCategory.OST_PipeCurves,
    "Select pipes for cut list");

// Prompt user to select only sprinklers
IList<Element> selectedHeads = SelectionHelpers.PickElementsByCategory(
    uidoc,
    BuiltInCategory.OST_Sprinklers,
    "Select sprinkler heads");
```

### `RevitApiHelpers.cs` — General Revit Shortcuts
Quick access to commonly needed Revit objects:

```csharp
Document doc = RevitApiHelpers.GetActiveDocument(commandData);
UIDocument uidoc = RevitApiHelpers.GetActiveUIDocument(commandData);
View activeView = RevitApiHelpers.GetActiveView(doc);
```

---

## Adding a New Helper

1. If it fits in an existing file, add the method there
2. If it's a new category of helper, create a new `.cs` file here
3. Use the namespace `SSG_FP_Suite.Utils`
4. Make methods `public static` so any command can call them
5. Add a doc comment explaining what it does and a usage example
