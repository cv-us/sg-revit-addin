# Strip Section ID Type Code

**Ribbon:** SG → Hangers → Strip Section ID Code
**Class:** `SgRevitAddin.Commands.Hangers.StripSectionIDTypeCodeCommand`

## Purpose

For hangers whose `Type Code (Hydratec)` matches a chosen value,
strips the type-code prefix from `Section_ID (Hydratec)` — keeping
only the `(length)` portion. The matching is done against the
**Type Code parameter**, not against the contents of the Section_ID
string, so a hanger whose Section_ID was somehow set without a prefix
will still be correctly identified by its Type Code value.

## Example

Selection contains hangers with these values:

| Type Code | Section_ID | Strip target = `11T` | After |
|---|---|---|---|
| `11T` | `#11T(5)` | match | `(5)` |
| `11T` | `#11T(7½)` | match | `(7½)` |
| `11T` | `(5)` | match, already stripped | `(5)` (no change) |
| `03A` | `#03A(12)` | other code | `#03A(12)` (no change) |

## Workflow

1. Pre-select hangers in the model (no pick prompt).
2. Run **Hangers → Strip Section ID Code**.
3. The command scans the selection for distinct Type Codes.
4. Dialog: pick the Type Code whose hangers should have their prefix
   stripped.
5. Click **Strip**.
6. Summary dialog reports counts (stripped / already stripped /
   other type codes / no Section_ID / no parenthesis / read-only).

## Parsing rules

For each hanger whose Type Code matches:

- If `Section_ID (Hydratec)` is empty → tally as *No Section_ID value*.
- If the string has no `(` at all → tally as *No `(` in Section_ID*
  (we have nothing to keep).
- If the string already starts with `(` → tally as *Already stripped*
  (no write).
- Otherwise → keep everything from the first `(` onward.

So `"#11T(7½)"` becomes `"(7½)"`. `"someprefix(12)"` becomes `"(12)"`.
`"11T(5)"` (no `#`) becomes `"(5)"`.

The first-`(` rule means the command works for any prefix shape, not
just the `#TYPECODE(LENGTH)` format written by **Ring Section IDs**.

## Selection filter

Recognises hangers by family-name substring (same set used by other
Hangers commands), guarded by the PipeAccessory category:

- `-Pipe Hanger`
- `-Pipe Trapeze`
- `-Basic Adjustable`
- `Adjustable Ring Hanger`
- `Ring Hanger`

Non-hangers are silently dropped before the dialog opens.

## Required parameters

| Where | Parameter | Used for |
|---|---|---|
| Hanger family | `Type Code (Hydratec)` (Text, instance) | Selection filter |
| Hanger family | `Section_ID (Hydratec)` (Text, instance, writable) | Read + write |

If `Section_ID (Hydratec)` is read-only on a particular hanger, that
hanger is reported under *Skipped (read-only)*.

## Idempotency

Re-running with the same Type Code is safe — every previously-stripped
hanger is detected by its leading `(` and tallied under *Already
stripped*; no write occurs. The transaction commits with no changes.

## See also

- [Section IDs](hanger-section-ids.md) — writes the original
  `(LENGTH#TYPECODE_FRACTION)` format.
- [Ring Section IDs](ring-hanger-section-ids.md) — writes the
  `#TYPECODE(LENGTH)` format that this command undoes the prefix of.
- [Change Type Code](change-type-code.md) — paired command on the
  same Type Code edits stack; changes the code itself instead of
  removing it from the label.
