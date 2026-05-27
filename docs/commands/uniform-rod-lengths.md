# Uniform Rod Lengths

**Ribbon:** SG → Hangers → Uniform Rods
**Class:** `SgRevitAddin.Commands.Hangers.UniformRodLengthsCommand`

## Purpose

Bulk-uniformizes `Rod Length` on hangers that share a `Type Code (Hydratec)`
**and** sit at or below a user-defined length cutoff. Hangers above the
cutoff (likely on a lower pipe with intentionally longer rods) are left
alone, as are hangers with any other type code.

The intended use case: a run of hangers placed sloppily on the same
pipe, with rod lengths drifting by ±a few inches when they should all
be identical. Below those same hangers, on a lower pipe, the same
type code may also be in use — but at a clearly longer rod length.
This command sweeps the upper-pipe sloppy values to a uniform target
without touching the lower-pipe rods.

## Workflow

1. Pre-select hangers in the model (no pick prompt).
2. Run **Hangers → Uniform Rods**.
3. The command scans the selection for distinct `Type Code (Hydratec)`
   values present.
4. Dialog:
   - **Type Code** — dropdown, populated from codes in the selection.
   - **Max Rod Length (in)** — the cutoff. Hangers whose current rod
     length is at or below this value will be touched; anything longer
     is left alone.
   - **Target Rod Length (in)** — the uniform value applied to in-range
     matching hangers.
5. Click **Apply**.
6. Summary dialog reports counts:
   - Total hangers in selection
   - For the chosen Type Code: updated / already at target / above cutoff
   - Other type codes (left alone)
   - Skipped (no Rod Length / read-only)

## Decision rules per hanger

Walked once per hanger in the selection, in this order:

| Check | Action |
|---|---|
| Type Code doesn't match the chosen value | **Skip** — tally as *Other type codes* |
| Rod Length parameter missing, non-numeric, or ≤ 0 | **Skip** — tally as *No Rod Length* |
| Rod Length > max cutoff | **Skip** — tally as *Above max* |
| Rod Length already equals target (within ~0.01") | **Skip** — tally as *Already at target* |
| Rod Length parameter read-only | **Skip** — tally as *Read-only* |
| Otherwise | **Set Rod Length to target** |

The "already at target" branch makes the command idempotent — running
the same operation twice updates nothing on the second pass.

## Selection filter

Recognises hangers by family-name substring (same set used by the other
Hangers commands), guarded by the PipeAccessory category:

- `-Pipe Hanger`
- `-Pipe Trapeze`
- `-Basic Adjustable`
- `Adjustable Ring Hanger`
- `Ring Hanger`

Non-hanger elements in the selection are silently dropped before the
dialog opens.

## Units

Dialog values are inches; Revit stores `Rod Length` in feet. Conversion
is local to the command — you only see inches.

Both inputs are rounded to the nearest ¼" (`NumericUpDown` increment),
which matches the typical Hydratec ring-hanger spacing granularity.

## Validation

- Both max and target must be > 0.
- Target must be ≤ max (a target above the cutoff would never get
  applied — the dialog rejects this before the transaction opens).

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Type Code (Hydratec)` (Text, instance) | Type-code filter |
| Hanger family | `Rod Length` (Length, instance, writable) | Threshold check + new value |

If `Rod Length` is read-only or non-numeric storage on a particular
hanger, that hanger is reported under *Skipped*.

## Example

Selection: 47 hangers — 30 are `03A` (rods 21–25"), 12 are `03A` on a
lower pipe (rods 32–36"), 5 are `02C`.

- Type Code = `03A`
- Max = `28`
- Target = `24`

Result:
- **Updated to 24.00":** 28 (the 21–25" 03A hangers, minus any already
  exactly 24")
- **Already at 24.00":** 2
- **Above 28.00":** 12 (the lower-pipe 03As — left alone)
- **Other type codes:** 5 (the 02Cs — left alone)

## See also

- [Change Type Code](change-type-code.md) — bulk-rewrites the type code
  itself instead of the rod length.
- [Sync Hangers to Ref Plane](sync-hangers-to-ref-plane.md),
  [Sync Hangers Raybounce](sync-hangers-raybounce.md),
  [Sync Hangers Surface](sync-hangers-surface.md) — compute rod lengths
  from structure geometry instead of setting them manually.
