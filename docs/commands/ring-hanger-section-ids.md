# Ring Hanger Section IDs

**Ribbon:** SG → Hangers → Ring Section IDs
**Class:** `SgRevitAddin.Commands.Hangers.RingHangerSectionIDsCommand`

> **Sister command:** [Hanger Section IDs](hanger-section-ids.md) writes the
> same `Section_ID (Hydratec)` parameter using the full rod length without
> any takeout subtraction. Use that one for non-ring hangers; use this one
> for HydraCAD Adjustable Ring Hangers where the rod length needs to be
> reduced by the ring hardware takeout.

## Purpose

Populates `Section_ID (Hydratec)` on selected Adjustable Ring Hanger
instances with a tag-friendly string that combines the **Type Code** and
**remaining rod length** (rod length minus the ring takeout for the
hanger's nominal diameter).

The Rod Length parameter itself is **not modified** — only the
`Section_ID (Hydratec)` label is written, which is what hanger tags
read.

## Workflow

1. Pre-select Adjustable Ring Hanger instances in the model.
2. Run **Hangers → Ring Section IDs**.
3. For each hanger:
   - Read `Rod Length` and `Nominal Diameter` from the hanger.
   - Look up the takeout for that nominal diameter (table below).
   - Compute `remaining = rod_length − takeout`.
   - Read `Type Code (Hydratec)`.
   - Round the remaining length to the nearest ¼".
   - Write `(WHOLE#TYPECODE_FRACTION)` to `Section_ID (Hydratec)`.
4. Report counts (updated, skipped, unmatched sizes).

## Takeout table

| Ring / pipe nominal diameter | Takeout |
|---|---|
| 1" | 1.5" |
| 1¼" | 1.5" |
| 1½" | 1.5" |
| 2" | 2.0" |
| 2½" | 3.0" |
| 3" | 3.0" |
| 4" | 3.5" |
| 6" | 5.5" |
| 8" | 6.5" |

Nominal diameter is read from the hanger's own `Nominal Diameter`
parameter (not the connected pipe). Matching uses a ±0.05" tolerance to
absorb floating-point noise from Revit's internal feet ↔ inches
conversion.

Pipe sizes outside this table (e.g. 10", 12") are skipped and reported
in the summary so you can extend the table in `RingHangerSectionIDsCommand.cs`
if needed.

## Output format

```
#TYPECODE(LENGTH)
```

Examples:

| Rod Length | Nominal Dia | Takeout | Type Code | Section_ID |
|---|---|---|---|---|
| 24" | 2" | 2.0" | `R3R` | `#R3R(22)` |
| 18¼" | 1½" | 1.5" | `01V` | `#01V(16¾)` |
| 30" | 4" | 3.5" | `02C` | `#02C(26½)` |
| 9" | 1" | 1.5" | `05S` | `#05S(7½)` |

Fractions render as Unicode `¼`, `½`, `¾`. Whole inches have no fraction
suffix.

> The format intentionally differs from the non-ring
> [Hanger Section IDs](hanger-section-ids.md) command (which uses
> `(WHOLE#TYPECODE_FRACTION)`). Hanger tag families can read either
> shape — match whichever your tag is set up for.

## Filtering

Only family instances whose family name contains `"Adjustable Ring
Hanger"` are processed. Other selected elements (SG hangers, pipes, tags,
etc.) are silently filtered out — no error if the selection is mixed.

## Skip conditions

A hanger is skipped (with a per-reason count in the summary report) when
any of these apply:

- **No Rod Length** — parameter missing or value ≤ 0.
- **No Nominal Diameter** — parameter missing or value ≤ 0.
- **Size not in table** — nominal diameter doesn't match any row.
  Unmatched sizes are listed in the summary.
- **Takeout > Rod Length** — would produce a non-positive remaining
  length. Usually means the rod is too short for the ring size; check
  the hanger.

## Re-running

Idempotent — re-running overwrites `Section_ID (Hydratec)` with a
freshly computed value. Safe to re-run after rod length changes
(raybounce sync, manual adjustment) to keep the tag label current.

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Rod Length` (Length, instance, writable) | Input |
| Hanger family | `Nominal Diameter` (Length, instance) | Takeout lookup |
| Hanger family | `Type Code (Hydratec)` (Text, instance) | Output prefix |
| Hanger family | `Section_ID (Hydratec)` (Text, instance, writable) | Output |

The `Section_ID (Hydratec)` parameter appears in the **Constraints**
group of the Properties palette for Hydratec-authored families.

## See also

- [Hanger Section IDs](hanger-section-ids.md) — non-ring variant; no
  takeout subtraction
- [Hanger Gap Check](hanger-gap-check.md) — flags hangers whose
  top-of-pipe to structure gap exceeds a threshold (related rod-length
  math, but the `01*` / `02*` / `05S*` hardware offsets there are not
  the same as the ring takeouts here — the gap check uses rod-end-to-
  pipe-top hardware; this command uses ring takeout)
