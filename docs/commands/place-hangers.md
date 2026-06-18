# Place Hangers

**Command:** `SgRevitAddin.Commands.Hangers.PlaceHangers.PlaceHangersCommand`
**Domain:** Hangers
**Ribbon:** SG ♈ > Hangers > Place Hangers

## Purpose

One command and one dialog for all four auto-placement methods. A **method dropdown** at the top of the dialog selects the strategy; only the options relevant to that method are shown, and every input is remembered per method between runs.

This **replaces** four separate ribbon buttons (Hang Spaced, Hang Parallel, Hang Downstream, Hang at Steel). Their command classes remain in the codebase — `PlaceHangersCommand` calls each one's `RunPlacement` — so the placement algorithms are unchanged.

## Methods

| Method | Was | Places hangers… |
|--------|-----|-----------------|
| Auto-spaced (decks / raybounce) | `HangTypicalSpacingCommand` | at typical spacing along runs; rod length by raybounce to the deck/structure above |
| Auto-spaced (parallel framing) | `HangParallelStructuralCommand` | at typical spacing, attached to parallel structural framing (clamp angle + widemouth detection) |
| Downstream ends (threaded lines) | `HangDownstreamCommand` | at the downstream end of each threaded branchline (distance-from-end + min length) |
| At structural steel | `HangAtStructuralCommand` | where pipes cross structural framing members |

## Workflow

1. Run **Place Hangers**. The dialog opens on the last-used method.
2. Choose a method; the option groups update.
3. Set options (family, spacing, type codes, structural source, etc.).
4. Click **Place Hangers**. You're prompted to select pipe runs (for Downstream, the prompt asks for threaded-line pipes).
5. The chosen method's placement runs and reports a summary.

## Dialog Options by Method

**Shared (all methods):** Hanger family.

| Group | Shown for | Controls |
|-------|-----------|----------|
| Pipe filter | decks, parallel | ALL Pipes or a pipe type |
| Spacing | decks, parallel | Evenly / exact + 10'-6" / 12' / 15' / custom |
| Structural Source | parallel, at-steel | Local framing or a linked model; attach to bottom/top |
| Raybounce | decks, at-steel | Max clash height (ft) |
| Type Codes | all | decks: 1 code · parallel: code + widemouth · at-steel: widemouth · downstream: Roof/Floor Deck/Framing/Stairs |
| Downstream Placement | downstream | Distance from end (in), min pipe length (in) |
| C-Clamp | parallel, downstream, at-steel | Hide / Show |

## Settings Memory

Every input is persisted via `DialogMemory` under keys namespaced `PlaceHangers.<Method>.<field>` (e.g. `PlaceHangers.ParallelStructural.MaxClash`), so each method remembers its own last-used values across runs and Revit restarts. The last-used method is also restored.

## Implementation Notes

- Each method's algorithm lives in its original command class as a public `RunPlacement(uidoc, config, pipes)` method. The original `Execute` (still present, just unregistered from the ribbon) gathers selection + shows its own dialog + calls `RunPlacement`; `PlaceHangersCommand` gathers selection + shows the unified dialog + calls the same `RunPlacement`.
- Config objects (`TypicalSpacingConfig`, `ParallelStructuralConfig`, `DownstreamConfig`, `AtStructuralConfig`) decouple the placement logic from any specific dialog.
- All four methods select pipe curves the same way, so the pick happens once in `PlaceHangersCommand` after the dialog returns.

## See Also

- **Hang at CAD** — separate command (pipes crossing CAD lines)
- **Hang User Loc** — separate command (detail-line marked spots)
- **Sync Raybounce / Sync Surface** — recompute rod lengths after placement
