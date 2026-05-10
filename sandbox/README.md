# sandbox/ — Macro Prototyping Playground

This is where you experiment. Files here are **NOT compiled** into the plugin — they're just text files you paste into Revit's macro editor.

## How It Works

```
sandbox/
├── _macro_template.cs     ← Copy this to start any new experiment
├── experiments/           ← Your active work-in-progress macros
│   ├── try_pipe_colors.cs
│   ├── try_auto_hang.cs
│   └── ...
└── graduated/             ← Archive of macros that became real commands
    ├── pipe_colors.cs     ← Now lives in Commands/Coordination/
    └── ...
```

## Step-by-Step: Writing a New Macro

### 1. Copy the Template

Copy `_macro_template.cs` to `experiments/` with a descriptive name:
```
_macro_template.cs  →  experiments/try_hanger_placement.cs
```

### 2. Write Your Logic

Open your new file and replace the placeholder with your code:

```csharp
public void TryHangerPlacement()
{
    UIDocument uidoc = this.ActiveUIDocument;
    Document doc = uidoc.Document;

    // Get all pipes in the active view
    var pipes = new FilteredElementCollector(doc, doc.ActiveView.Id)
        .OfClass(typeof(Pipe))
        .Cast<Pipe>()
        .ToList();

    TaskDialog.Show("Test", $"Found {pipes.Count} pipes");
}
```

### 3. Run It in Revit

1. Open Revit with a test project
2. Go to **Manage → Macro Manager**
3. Click the **Application** tab (not Document)
4. Click **Module** → create a new one if needed (e.g., "SG_Experiments")
5. Click **Edit** to open the macro code editor
6. Paste your code into the `ThisApplication` partial class
7. Click **Build** (the green play button)
8. Close the editor, select your macro, click **Run**

### 4. Iterate

- Edit the file in your code editor (VS Code, etc.)
- Copy-paste updated code into Revit's macro editor
- Build and run again
- Repeat until it works

### 5. Graduate It

When the macro works and you want it permanently in the plugin:

1. Move the file: `experiments/try_hanger_placement.cs` → `graduated/try_hanger_placement.cs`
2. Create the real command: `src/Shared/Commands/Hangers/AutoHangCommand.cs`
3. Follow the full process in `docs/macro-to-command-workflow.md`

## Important Notes

- **These files don't compile** — they're excluded from the `.csproj`. They only run inside Revit's macro manager.
- **`this.ActiveUIDocument`** in macros becomes **`commandData.Application.ActiveUIDocument`** in commands. That's the biggest difference.
- **No transactions needed in macros** — Revit's macro environment handles them. But in commands, you MUST wrap changes in `TransactionWrapper`.
- **Name your experiments clearly** — `try_pipe_colors.cs` is better than `test1.cs`. Future-you will thank present-you.
- **Don't be precious** — Macros are meant to be rough. Polish happens when you graduate to a command.

## The Macro Template

`_macro_template.cs` gives you a ready-to-go skeleton with:
- All the common `using` statements you'll need
- The `ThisApplication` partial class wrapper Revit expects
- A placeholder method to fill in
- Comments explaining what to do next
