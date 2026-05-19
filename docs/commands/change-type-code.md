# Change Type Code

**Ribbon:** SG → Hangers → Change Type Code
**Class:** `SgRevitAddin.Commands.Hangers.ChangeTypeCodeCommand`

## Purpose

Bulk-changes the `Type Code (Hydratec)` parameter on selected hangers
from one value to another. Hangers whose current code matches the
chosen *From* value are re-stamped with the *To* value; everything else
in the selection is left alone.

Use it after a HydraCAD round-trip when a batch of hangers came in
with the wrong type code, or when a job convention changes mid-stream
(e.g. all `03A` should become `03B`).

## Workflow

1. Pre-select hangers in the model (no pick prompt — the command reads
   the active selection).
2. Run **Hangers → Change Type Code**.
3. The command scans the selection and lists every distinct
   `Type Code (Hydratec)` value it finds.
4. Dialog:
   - **From Type Code** — dropdown, populated with the codes actually
     present in your selection.
   - **To Type Code** — text box, free-form. Whatever you type is
     written verbatim (preserves your casing).
5. Click **Change**. Only hangers whose current code matches *From*
   are updated.
6. Summary dialog reports counts:
   - Total hangers in selection
   - Matched + changed
   - Other type codes (unchanged)
   - Hangers with no code (unchanged)
   - Skipped (parameter read-only / wrong storage type)

## Selection filter

Recognises hangers by family-name substring (same set used by the
other Hangers commands), guarded by the PipeAccessory category so
tag families with matching names don't slip in:

- `-Pipe Hanger`
- `-Pipe Trapeze`
- `-Basic Adjustable`
- `Adjustable Ring Hanger`
- `Ring Hanger`

If your selection contains a mix of hangers and other elements (pipes,
fittings, tags), the non-hangers are silently dropped before the
dialog opens.

## Comparison rules

- *From* matching is case-insensitive and whitespace-trimmed —
  `"03A "`, `"03a"`, and `"03A"` all match.
- *To* is written verbatim — your dialog input becomes the new
  parameter value.
- Same *From* and *To* values are rejected before the transaction
  opens (no-op).

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Type Code (Hydratec)` (Text, instance, writable) | Both read (to identify *From* matches) and write (the new value) |

If a hanger's `Type Code (Hydratec)` is read-only or non-string
storage, it's tallied under *Skipped* and reported in the summary.

## Idempotency

Running the same change twice is safe — the second pass finds no
hangers with the *From* code (they all became *To* on the first
pass) and reports `Matched: 0`. The transaction commits with no
changes.

## See also

- [Section IDs](hanger-section-ids.md) — populates `Section_ID (Hydratec)`
  using the current `Type Code (Hydratec)` value, so re-run Section IDs
  after a Change Type Code if any tags display the section ID string.
- [Ring Section IDs](ring-hanger-section-ids.md) — Adjustable Ring
  Hanger variant of Section IDs.
- [Inspect Element Parameters](inspect-element-parameters.md) — useful
  for confirming what `Type Code (Hydratec)` value a hanger currently
  carries before running this command.
