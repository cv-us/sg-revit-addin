# Ring Section IDs (+Hardware)

**Ribbon:** SG → Hangers → Ring IDs (+Hardware)
**Class:** `SgRevitAddin.Commands.Hangers.RingSectionIDsHardwareCommand`

## Purpose

Hardware-compensated variant of [Ring Section IDs](ring-hanger-section-ids.md).
Computes the ring-takeout length the same way, then **adds 1.5" back**
for hanger assemblies whose `Type Code (Hydratec)` starts with `01` or
`02` — these carry an extra 1.5" of hardware between the rod end and the
pipe that the plain ring takeout over-subtracts.

The result is written to `Section_ID (Hydratec)` as `TYPECODE(LENGTH)`
**with no leading `#`**.

## Example

1" hanger (ring takeout 1.5"), Type Code `02D`, Rod Length 4":

```
4"  − 1.5" (ring takeout)         = 2.5"
2.5" + 1.5" (01/02 hardware add)  = 4.0"
Section_ID = "02D(4)"
```

Type codes that don't start with `01` or `02` get the plain ring takeout
with no add-back (same number as Ring Section IDs, but without the `#`):

| Type Code | Nominal | Rod | Takeout | +1.5"? | Section_ID |
|---|---|---|---|---|---|
| `02D` | 1" | 4" | 1.5" | yes | `02D(4)` |
| `01` | 1" | 9" | 1.5" | yes | `01(9)` |
| `R3R` | 2" | 24" | 2.0" | no | `R3R(22)` |
| `05S` | 1" | 9" | 1.5" | no | `05S(7½)` |

## Workflow

1. Pre-select hangers (no pick prompt).
2. Run **Hangers → Ring IDs (+Hardware)**.
3. For each hanger:
   - Read `Rod Length` and `Nominal Diameter`.
   - Subtract the ring takeout for that nominal diameter.
   - If Type Code starts with `01` or `02`, add 1.5" back.
   - Round to the nearest ¼", format as `TYPECODE(LENGTH)`.
   - Write to `Section_ID (Hydratec)`.
4. Summary dialog reports counts, including how many got the +1.5"
   hardware add-back.

`Rod Length` itself is **not** modified — only the Section_ID label.

## Takeout table

| Nominal diameter | Takeout |
|---|---|
| 1" / 1¼" / 1½" | 1.5" |
| 2" | 2.0" |
| 2½" / 3" | 3.0" |
| 4" | 3.5" |
| 6" | 5.5" |
| 8" | 6.5" |

Sizes outside the table (10", 12") are skipped and reported in the
summary.

## Format vs. the other Section ID commands

| Command | Format |
|---|---|
| [Section IDs](hanger-section-ids.md) | `(LENGTH#TYPECODE_FRACTION)` e.g. `(12#R3R¼)` |
| [Ring Section IDs](ring-hanger-section-ids.md) | `#TYPECODE(LENGTH)` e.g. `#02D(4)` |
| **Ring Section IDs (+Hardware)** | `TYPECODE(LENGTH)` e.g. `02D(4)` — no `#` |

If you've already written IDs with one of the `#` formats and just want
the hashes gone, use [Strip # From Section IDs](strip-section-id-hashes.md).

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Rod Length` (Length, instance) | Takeout math |
| Hanger family | `Nominal Diameter` (Length, instance) | Takeout lookup |
| Hanger family | `Type Code (Hydratec)` (Text, instance) | Prefix + 01/02 hardware test |
| Hanger family | `Section_ID (Hydratec)` (Text, instance, writable) | Output |

## See also

- [Ring Section IDs](ring-hanger-section-ids.md) — same takeout, `#TYPECODE(LENGTH)` format, no hardware add-back.
- [Strip # From Section IDs](strip-section-id-hashes.md) — remove `#` from existing Section IDs.
