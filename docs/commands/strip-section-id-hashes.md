# Strip # From Section IDs

**Ribbon:** SG → Hangers → Strip # From IDs
**Class:** `SgRevitAddin.Commands.Hangers.StripSectionIDHashesCommand`

## Purpose

Removes every `#` character from `Section_ID (Hydratec)` on all selected
hangers. No Type Code filter, no dialog — it sweeps the whole selection.

This is the blunt-instrument companion to
[Strip Section ID Type Code](strip-section-id-type-code.md): that command
removes the entire prefix before the first `(` for a *chosen* Type Code;
this one just deletes the hash marks wherever they appear and keeps
everything else.

## Example

| Before | After |
|---|---|
| `#11T(5)` | `11T(5)` |
| `#05S(7½)` | `05S(7½)` |
| `12#R3R¼` | `12R3R¼` |
| `(5)` | `(5)` (no change — no `#`) |

## Workflow

1. Pre-select hangers in the model (no pick prompt).
2. Run **Hangers → Strip # From IDs**.
3. Summary dialog reports counts: stripped / already had no `#` /
   no Section_ID value / read-only.

The whole operation runs in a single transaction, so a single Undo
reverts everything.

## Selection filter

Recognises hangers by family-name substring, guarded by the
PipeAccessory category:

- `-Pipe Hanger`
- `-Pipe Trapeze`
- `-Basic Adjustable`
- `Adjustable Ring Hanger`
- `Ring Hanger`

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Section_ID (Hydratec)` (Text, instance, writable) | Read + write |

## Idempotency

Re-running is safe — once the `#` marks are gone, subsequent runs find
nothing to strip and tally every hanger under "already had no #".

## See also

- [Strip Section ID Type Code](strip-section-id-type-code.md) — removes
  the full `#TYPECODE` prefix for a chosen Type Code, leaving `(length)`.
- [Ring Section IDs (+Hardware)](ring-section-ids-hardware.md) — writes
  Section IDs in the no-`#` `type(length)` format directly.
