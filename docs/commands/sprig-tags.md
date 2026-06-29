# Sprig Tags

**Ribbon:** SG → Annotation → Sprig Tags
**Class:** `SgRevitAddin.Commands.Annotation.SprigTagsCommand`

## Purpose

When the Hydratec tags are placed for submittal / stocklist plans, a
vertical piece of pipe gets a special direction tag that reads **UP**,
**DN**, or **RN** (riser nipple) based on which way the host pipe runs.
That's correct for risers and drops, but on a 1" **sprig** (the short
vertical pipe rising from a branch line up to an upright sprinkler) an
"UP" can confuse the field.

This command finds those small sprig tags and re-types them to a
dedicated **SPRIG** tag type that you create — so the tag reads
`SPRIG` plus the size and length it already shows. Genuine drops and
riser nipples keep their original tag.

## What counts as a sprig

A tag is converted only when **all** of these are true for its host pipe:

1. The tag's host is a **Pipe** (not a fitting or sprinkler).
2. The pipe is **vertical** — within 30° of plumb.
3. The pipe's nominal diameter is **≤ the max size** chosen in the
   dialog (default **1"**).
4. A **sprinkler is reachable from the pipe's UPPER end** — directly, or
   through a reducing coupling / short nipple (the connector walk passes
   through pipe fittings and pipes shorter than 1'-6").

Rule 4 is the geometric sprig-vs-drop test: the sprinkler sits on top of
a sprig, so the upper end reaches it. That naturally excludes:

- **Drops** — the sprinkler (pendent) is at the *bottom* end → left alone.
- **Riser nipples** — no sprinkler is reachable at either end → left alone.

The test never reads the existing tag's text, so it works regardless of
what the tag currently says.

## Workflow

1. *(Optional)* Select the vertical-pipe tags you want to convert — or
   select the sprig pipes themselves. If you select nothing useful, the
   command scans the **active view** instead.
2. Run **Annotation → Sprig Tags**.
3. Dialog:
   - **Max host pipe size (in)** — only tags on pipes this size or
     smaller are converted. Default `1.00`.
   - **Only convert tags of family** — a guard so unrelated tags on the
     same sprig pipe aren't touched. Defaults to *(Any tag family)*;
     narrow it to the Hydratec direction-tag family if a sprig pipe
     carries more than one tag.
   - **Convert matching sprig tags to this type** — the SPRIG tag type.
     The dropdown lists every loaded tag type in the pipe-tag /
     generic-annotation categories (plus whatever category the existing
     tags use). If a type's name contains "sprig" it's pre-selected.
4. Click **Convert**. Each qualifying sprig tag is re-typed via
   `IndependentTag.ChangeTypeId` in a single transaction.
5. Summary dialog reports counts: converted, plus why each skipped tag
   was left alone (already that type, pipe too big, not vertical, drop,
   no sprinkler, or other family).

## Source: selection vs. active view

| You selected… | The command works on… |
|---|---|
| Pipe tags | exactly those tags (host must be a pipe) |
| Pipe(s) | the active-view tags hosting those pipes |
| Nothing useful | every pipe tag in the active view |

The summary names which source was used ("from selection" or "in
active view").

## How the SPRIG type is designated

You create the SPRIG tag type yourself — typically a new **type** inside
the same Hydratec direction-tag family (alongside UP / DN / RN), but a
separate tag family works too. As long as it's loaded, it appears in the
**Convert To** dropdown. Nothing is created or modified at the family
level; the command only swaps each tag's type.

## Notes

- **Idempotent.** Re-running finds the already-converted tags are
  already the SPRIG type and reports them under *Already that type* — no
  change.
- **Non-destructive.** Tags are re-typed, never deleted; pipes,
  fittings, and sprinklers are untouched.
- **Vertical tolerance** is 30° off plumb, so a slightly racked sprig
  still qualifies. A horizontal armover never does.

## See also

- [Flex Drop Lengths](flex-drop-lengths.md) /
  [Flex Drops Auto](flex-drop-lengths-auto.md) — the other sprinkler tag
  commands.
- [Change Type Code](change-type-code.md) — the analogous bulk
  type-swap pattern for hangers.
