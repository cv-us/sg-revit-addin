# Legend Transfer

**Ribbon:** SG → Views & Sheets → Legend Transfer
**Command class:** `SgRevitAddin.Commands.ViewsAndSheets.LegendTransferCommand`

## Purpose

Copies Legend views from one currently-open Revit document into another.
Useful for pulling standard sprinkler / fire-protection legend blocks
from a legend-library project into a fresh project without having to
rebuild them by hand.

## How to use

1. **Open both projects in the same Revit session** — the source
   (the legend library) and the target (the project that needs the legends).
2. Click **Views & Sheets → Legend Transfer**. A WPF dialog opens.
3. **Pick a source** in *Copy From* (defaults to the first non-active doc).
4. **Pick a target** in *Copy To* (defaults to the active doc).
5. The legend list refills with every Legend view in the source. Each
   row shows the legend's name, scale (e.g. `1:50`), and element count.
6. **Filter** by typing in the **Search** box — matches any part of the
   name, case-insensitive.
7. **Check the legends you want to copy.** **Select All** / **Deselect
   All** toggle only the currently-visible (filter-matched) rows.
8. Click **Transfer**.
9. A progress bar fills as legends transfer one at a time. When it
   finishes, a summary dialog reports how many succeeded, were
   skipped, or failed (with reasons).

## What gets transferred

For each selected legend:

- A new Legend view is created in the target with the **same name** as
  the source legend.
- The **scale** is set to match the source legend's scale.
- The legend's **visible elements** (legend components, lines, text)
  are copied across via `ElementTransformUtils.CopyElements`.

## What doesn't get transferred (v1)

- **View templates** — the new legend has no template applied. Add one
  manually after transfer if needed.
- **Existing-legend overwrite** — if a legend with the matching name
  already exists in the target, the transfer is **skipped** for that
  legend (logged in the summary). Nothing is overwritten.
- **Cross-session copy** — both projects must already be open in the
  same Revit session. The command does not open files from disk.
- **Thumbnail previews** — the dialog list is text-only.

## Required setup in the target

Revit's API does **not** provide a factory for creating Legend views
from scratch. The transfer mechanism duplicates an existing legend in
the target (`View.Duplicate`), then renames + rescales + clears it
before copying the source's elements in.

That means **the target document must already contain at least one
Legend view** (an empty placeholder is fine). If it doesn't, the
command shows a validation error and asks you to create one manually
first, then re-run.

## Conflict handling

- **Existing legend names** — skipped per the rule above.
- **Family / type name conflicts** during the element copy — handled
  silently with "use destination types". Source types whose names match
  an existing target type are mapped to the existing one rather than
  re-imported.

## Transactions

The entire batch is wrapped in a `TransactionGroup` ("Transfer
Legends"), so a single **Undo** in Revit rolls back every legend that
was transferred. Each individual legend gets its own inner `Transaction`
so one failure doesn't abort the rest — the failing legend is rolled
back, logged, and the next one continues.

## Logging

The command appends a per-run summary to
`%APPDATA%\SgRevitAddin\LegendTransfer\log.txt` — source/target titles,
counts, and a line per legend (status + reason). Log writes are
best-effort; failures to write don't affect the transfer result.

## Validation

Before opening the dialog:
- At least **two** project documents must be open in the Revit session.

Before running the transfer:
- Source ≠ target.
- Target is not read-only.
- Source has at least one Legend view.
- Target has at least one Legend view (the seed for duplication).
- At least one legend is checked.

If any of these fail, a clear TaskDialog explains the problem and the
command aborts cleanly.

## Code organization

| File | What it does |
|---|---|
| `LegendTransferCommand.cs` | `IExternalCommand` entry point; opens the WPF dialog, dispatches to the service, shows the summary |
| `LegendTransferService.cs` | Core transfer logic — validation, TransactionGroup, per-legend Transaction, CopyElements |
| `LegendTransferWindow.cs` | WPF window (programmatic — no XAML) bound to the view model |
| `ViewModels/LegendTransferViewModel.cs` | MVVM view model — document selection, legend list, search/filter, commands |
| `Models/LegendInfo.cs` | Per-legend display + selection state |
| `Models/DocumentInfo.cs` | Combo-box wrapper around `Document` |
| `Models/TransferResult.cs` | Per-legend result + status enum |

## See also

- [Create Plan Views](create-plan-views.md) — companion view-creation
  command on the Views & Sheets panel.
