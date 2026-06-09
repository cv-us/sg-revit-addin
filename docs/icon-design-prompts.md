# Icon Design Brief — SG Revit Addin

This document is a creative brief for replacing the ~55 ribbon icons in
`src/Shared/UI/Resources/icons/`. Each entry pairs the button's display
label with a one-line visual prompt suitable for feeding to an image
generator (Midjourney, DALL-E, SDXL, etc.) or handing to a designer.

---

## Progress tracking

The **Done** column in each table below uses GitHub task-list syntax:
- `[ ]` = not yet replaced (still using placeholder/generic icon)
- `[x]` = new custom icon delivered, dropped into `src/Shared/UI/Resources/icons/`, build verified

To mark one done: open the file, change its `[ ]` to `[x]`, commit. A
single command counts as "done" only when **both** the 32×32 and 16×16
PNGs are in place.

**Status as of v0.2.2:** 0 / 56 commands have custom icons.

---

## Style brief (read this first — applies to the whole set)

- **Flat 2-tone vector pictograms.** No 3D, no shadows, no gradients, no
  text or numbers in the icon (they won't read at 16 px).
- **Strong silhouette.** The icon must remain identifiable at **16×16**.
  Generate at 256×256, deliver as transparent-background PNG (no
  baked-in canvas color).
- **Two sizes per command.** `{name}-32.png` (32×32 for large
  ribbon buttons) and `{name}-16.png` (16×16 for stacked buttons). The
  32 carries a touch more detail; the 16 is the silhouette.
- **Consistent line weight** across the set — 8–10% of the icon width.
- **One accent color allowed** when it adds meaning (e.g. orange spheres
  for "Mark Family Instances", magenta column for "Mark for Review",
  blue diagonal for "Seismic Braces"). Otherwise stick to neutral
  silhouettes.
- **Revit ribbon background:** test on **both dark (#2D2D30) and light
  (#F0F0F0)** Revit themes; mid-value strokes (~#888) generally read on
  both, but a contrasting fill is fine.
- **Visual family.** All icons share the same illustration style,
  perspective, and line weight — the set should feel like one suite,
  not 55 random images.
- **Recurring objects:** pipe (cylinder), hanger (vertical rod with
  ring/clamp), beam (I-shape), trapeze (two rods + horizontal bar),
  sprinkler (drop with deflector). Reuse them consistently across icons
  so users recognize them.

Filenames listed below are the **current** filenames in the project. Keep
the same name when you replace them (or update `App.cs` accordingly).

---

## Pipe Routing panel

| Button | Files | Prompt | Done |
|---|---|---|---|
| Shorten Flex Pipes | `shorten-flex-{16,32}.png` | A wavy/curly flex pipe segment with a horizontal compress arrow squeezing it into a short straight pipe. | [ ] |

---

## Hangers panel — Placement (basic)

| Button | Files | Prompt | Done |
|---|---|---|---|
| Hang at CAD | `hang-cad-{16,32}.png` | A pipe hanger silhouette over a thin CAD line grid (dashed lines) crossing beneath. | [ ] |
| Hang at Steel | `hang-struct-{16,32}.png` | A pipe hanger hooked to the underside of a steel I-beam (clear flange/web shape). | [ ] |
| Hang Downstream | `hang-downstream-{16,32}.png` | A horizontal pipe with a hanger at its right (downstream) end and a small directional arrow showing flow. | [ ] |

## Hangers panel — Placement (spacing)

| Button | Files | Prompt | Done |
|---|---|---|---|
| Hang Spaced | `hang-spacing-{16,32}.png` | Three identical hangers in a row on a horizontal pipe, with small equal-spacing tick marks between them. | [ ] |
| Hang Parallel | `hang-parallel-{16,32}.png` | A pipe running parallel to a beam (both horizontal, one above the other) with a hanger spanning between them. | [ ] |
| Hang User Loc | `hang-userloc-{16,32}.png` | A pipe hanger with a small map-pin / location marker beneath it indicating user-placed position. | [ ] |

## Hangers panel — Placement (special)

| Button | Files | Prompt | Done |
|---|---|---|---|
| Hang Tee Stems | `hang-tee-{16,32}.png` | A concrete double-tee profile (TT cross-section) with hangers attached to the sides of its stems. | [ ] |
| Format Ticks | `format-ticks-{16,32}.png` | Two pipe-hanger tick symbols (small / slashes) being rotated to face the same direction by a curved arrow. | [ ] |

## Hangers panel — Section ID writers

| Button | Files | Prompt | Done |
|---|---|---|---|
| Section IDs | `section-ids-{16,32}.png` | A pipe hanger silhouette next to a small rectangular tag/label hanging off it. | [ ] |
| Ring Section IDs | `ring-section-ids-{16,32}.png` | An adjustable-ring pipe hanger (open circular clamp) with a small rectangular tag/label attached. | [ ] |
| Ring IDs (+Hardware) | `ring-section-ids-hardware-{16,32}.png` | Same ring hanger + tag, plus a small "+" mark or hardware bracket symbol indicating the 1.5" hardware add. | [ ] |

## Hangers panel — Type Code / Section ID edits

| Button | Files | Prompt | Done |
|---|---|---|---|
| Change Type Code | `change-type-code-{16,32}.png` | A small rectangular tag with two opposing arrows (↔) above it, showing the value being swapped. | [ ] |
| Strip Section ID Code | `strip-section-id-code-{16,32}.png` | A small rectangular tag with a stylized prefix being erased — left portion fading or being cut off. | [ ] |
| Strip # From IDs | `strip-hashes-{16,32}.png` | A bold "#" symbol with a clean diagonal strike-through line through it. | [ ] |

## Hangers panel — Trapeze placement

| Button | Files | Prompt | Done |
|---|---|---|---|
| Trapeze Hang (large) | `hang-trapeze-{16,32}.png` | A clean trapeze hanger silhouette: two vertical rods bridged by a horizontal bar, with a pipe resting on the bar. | [ ] |
| Trapeze User Loc | `hang-trapeze-ul-{16,32}.png` | Same trapeze silhouette with a small map-pin / location marker beneath, indicating user-placed position. | [ ] |
| Unistrut Trapeze | `hang-unistrut-{16,32}.png` | Trapeze hanger built from a U-channel (Unistrut) horizontal cross-bar — emphasize the square channel cross-section. | [ ] |
| Unistrut 21A | `hang-uni21a-{16,32}.png` | Unistrut trapeze variant — same as Unistrut Trapeze but visibly simpler/smaller, perhaps a single rod with U-channel. | [ ] |

## Hangers panel — Sync to pipes (large) + resize variants

| Button | Files | Prompt | Done |
|---|---|---|---|
| Sync to Pipes (large) | `sync-pipes-{16,32}.png` | A hanger snapping onto a horizontal pipe, with small directional arrows showing alignment. | [ ] |
| Match Sizes | `match-sizes-{16,32}.png` | A pipe-hanger silhouette next to a pipe, with a small equals sign (=) between them indicating size matching. | [ ] |
| Replace Sizes | `replace-sizes-{16,32}.png` | A pipe hanger with a circular refresh/replace arrow around it, suggesting delete-and-recreate. | [ ] |

## Hangers panel — Swap / Inspect / Uniform

| Button | Files | Prompt | Done |
|---|---|---|---|
| Swap HydraCAD | `swap-hydracad-{16,32}.png` | Two different pipe-hanger silhouettes (HydraCAD ring vs SG ring) with crossed swap arrows between them. | [ ] |
| Inspect Params | `inspect-params-{16,32}.png` | A magnifying glass hovering over a pipe hanger, with a faint parameter list inside the lens. | [ ] |
| Uniform Rods | `uniform-rods-{16,32}.png` | Three pipe hangers in a row at the **same** rod length, with a faint horizontal dashed line connecting their ring positions to emphasize uniformity. | [ ] |

## Hangers panel — Mark for Review (large)

| Button | Files | Prompt | Done |
|---|---|---|---|
| Mark for Review (large) | `mark-review-{16,32}.png` | A pipe hanger pierced by a tall vertical magenta/purple cylinder column extending above and below it. | [ ] |

## Hangers panel — Rod-length syncs

| Button | Files | Prompt | Done |
|---|---|---|---|
| Sync Ref Plane | `sync-refplane-{16,32}.png` | A pipe hanger hanging from a clearly-drawn horizontal reference plane line above it. | [ ] |
| Sync Raybounce | `sync-raybounce-{16,32}.png` | A pipe hanger with an upward-pointing arrow / ray shooting from its top toward structure above. | [ ] |
| Sync Surface | `sync-surface-{16,32}.png` | A pipe hanger touching a structural surface (slab underside or beam bottom) directly — emphasize the contact face. | [ ] |

## Hangers panel — Trapeze utilities

| Button | Files | Prompt | Done |
|---|---|---|---|
| Sync Trapeze | `sync-trapeze-{16,32}.png` | A trapeze hanger with circular sync arrows around it, showing rotation/length alignment to a pipe above it. | [ ] |
| Flip Trapeze | `flip-trapeze-{16,32}.png` | A trapeze hanger silhouette with a 180° rotation arrow curling over it. | [ ] |

---

## Seismic panel

| Button | Files | Prompt | Done |
|---|---|---|---|
| Seismic Braces (large) | `seismic-braces-{16,32}.png` | A horizontal pipe with a diagonal seismic brace strut going up to structure, forming a clear triangle. | [ ] |
| Hanger Gap Check (large) | `hanger-gap-{16,32}.png` | A pipe hanger with a measuring caliper or two-headed vertical arrow between the top of pipe and the structure above, indicating the gap. | [ ] |

---

## Coordination panel

| Button | Files | Prompt | Done |
|---|---|---|---|
| Color Code Pipes (large) | `color-pipes-{16,32}.png` | Three stacked horizontal pipes in three distinct colors (red/yellow/blue) showing color-by-size. | [ ] |
| Mark Family Instances (large) | `mark-family-{16,32}.png` | A generic family/component icon with three bright orange dots/spheres scattered near it, suggesting tagged instances. | [ ] |

---

## Annotation panel — Pipe Elevations (large)

| Button | Files | Prompt | Done |
|---|---|---|---|
| Pipe Elevations (large) | `pipe-elevations-{16,32}.png` | A horizontal pipe with a small dimensioned elevation tag pointing to its top with the letters "TOS" subtly implied (or just a height arrow). | [ ] |

## Annotation panel — Flex drops + scale bars

| Button | Files | Prompt | Done |
|---|---|---|---|
| Flex Drops Set | `flex-drop-{16,32}.png` | A sprinkler head dangling from a wavy flex pipe, with a single rectangular tag indicating one fixed length. | [ ] |
| Flex Drops Auto | `flex-auto-{16,32}.png` | A sprinkler head dangling from a wavy flex pipe with a magic-wand / automation sparkle symbol next to its tag — suggesting auto-sizing. | [ ] |
| Scale Bars | `scale-bars-{16,32}.png` | A graphic scale bar — alternating black/white segments — as you'd see on a printed sheet. | [ ] |

## Annotation panel — Sleeve elevations + sleeve placement

| Button | Files | Prompt | Done |
|---|---|---|---|
| Sleeve Elevations | `sleeve-elevations-{16,32}.png` | A pipe sleeve (short cylindrical tube around a pipe) with a small elevation tag attached. | [ ] |
| Sleeves at Beams | `sleeves-beams-{16,32}.png` | A pipe passing through a horizontal I-beam web, with a clear sleeve ring around the pipe at the crossing. | [ ] |
| Sleeves at Decks | `sleeves-decks-{16,32}.png` | A vertical pipe passing through a horizontal deck/slab, with a sleeve ring at the penetration. | [ ] |

## Annotation panel — Walls + rooms + beams

| Button | Files | Prompt | Done |
|---|---|---|---|
| Sleeves at Walls | `sleeves-walls-{16,32}.png` | A horizontal pipe passing through a vertical wall, with a sleeve ring at the penetration. | [ ] |
| Room Text Notes | `room-text-{16,32}.png` | A simple room outline (rectangle) with small stacked text lines centered inside it. | [ ] |
| Beam Penetrations | `beam-penetration-{16,32}.png` | An I-beam in elevation with a small annotation symbol marking the location where a pipe penetrates it. | [ ] |

## Annotation panel — SSB + cleanup

| Button | Files | Prompt | Done |
|---|---|---|---|
| SSB Symbols | `ssb-symbols-{16,32}.png` | An SSB hanger symbol (a stylized H-bracket or specific hanger glyph) placed at one end of a horizontal pipe. | [ ] |
| Delete Dup Text | `delete-dupe-text-{16,32}.png` | Two stacked offset text-note rectangles with a small red "×" mark over the duplicate one. | [ ] |
| Clear Annotations | `clear-annotations-{16,32}.png` | A generic annotation tag with a stylized eraser or broom sweeping it away. | [ ] |

---

## Views & Sheets panel

| Button | Files | Prompt | Done |
|---|---|---|---|
| Create Plan Views (large) | `plan-views-{16,32}.png` | A stacked stack of floor-plan rectangles representing multiple plan views per level. | [ ] |
| Legend Transfer (large) | `legend-transfer-{16,32}.png` *(currently reuses `dependent-views-{16,32}.png` as a placeholder)* | Two stacked rectangles labeled "Legend" with a horizontal arrow between them — copying from one document to another. | [ ] |
| Dependent Views | `dependent-views-{16,32}.png` | One parent plan rectangle with two smaller child rectangles branching down from it (tree). | [ ] |
| Rotate Scope Box | `rotate-scopebox-{16,32}.png` | A scope-box rectangle (dashed outline) with a curved rotation arrow around it indicating angle adjustment. | [ ] |
| Remove Scope Boxes | `remove-scopebox-{16,32}.png` | A scope-box rectangle (dashed outline) with a red "×" mark on it. | [ ] |

---

## Setup panel

| Button | Files | Prompt | Done |
|---|---|---|---|
| Load Families (large) | `load-families-{16,32}.png` | A folder icon with several small component shapes inside it and an arrow pointing into a project window. | [ ] |
| Copy Levels/Grids | `copy-levels-{16,32}.png` | Two horizontal level lines and two vertical grid lines forming a small crosshatch, with a duplicate-arrow indicator. | [ ] |
| Global Params | `global-params-{16,32}.png` | A small gear or sliders icon — generic "settings/parameters" pictogram. | [ ] |
| Clear Elev Params | `clear-params-{16,32}.png` | A small elevation tag with a red "×" mark, indicating the parameter is being removed. | [ ] |

---

## Export panel

| Button | Files | Prompt | Done |
|---|---|---|---|
| Trimble Points (large) | `trimble-points-{16,32}.png` | A grid of small dots on a surface plane representing point exports, with one dot lifted/highlighted as a survey-style location pin. | [ ] |
| Trimble Markers | `trimble-markers-{16,32}.png` | A small surveyor's stake / orange location marker placed at the base of a pipe hanger. | [ ] |
| Import AS Pipes | `import-pipes-{16,32}.png` | A small CSV/spreadsheet icon with an arrow pointing into a horizontal pipe. | [ ] |
| Import AS Sprinklers | `import-sprinklers-{16,32}.png` | A small CSV/spreadsheet icon with an arrow pointing into a sprinkler head pictogram. | [ ] |

---

## Model Check panel

| Button | Files | Prompt | Done |
|---|---|---|---|
| Sprinkler Clearance | `sprinkler-clearance-{16,32}.png` | An upright sprinkler with a circular clearance zone (dashed circle) around it, indicating the 3" NFPA zone. | [ ] |
| Deflector Distance | `deflector-distance-{16,32}.png` | An upright sprinkler beneath a structural deck, with a vertical measuring arrow between the deflector and the deck. | [ ] |
| Pipes Too Short | `pipes-too-short-{16,32}.png` | A short pipe segment with a small yellow warning triangle next to it. | [ ] |

---

## Tips for the generator / designer

1. **Generate as a set, not one at a time.** Most generators do better
   when given the full style brief at the top of each prompt — paste
   the "Style brief" section before each individual command's prompt,
   or use a tool that supports a system prompt / style preset.

2. **Render at 256×256, downsample to 32×32 + 16×16.** Designers should
   manually clean up the 16×16 silhouette rather than just resizing —
   antialiasing at 16 px is brutal.

3. **Deliver as transparent-background PNG.** No solid canvas. Revit
   composites the icon onto the ribbon's panel color.

4. **Common pitfalls.**
   - Text or numbers inside the icon (won't read at 16 px).
   - Photorealistic shading (clashes with Revit's flat ribbon style).
   - Inconsistent line weight across the set (icons should look like
     siblings).
   - More than ~3 colors in a single icon (gets noisy at 16 px).

5. **Reuse the recurring objects** (pipe, hanger, beam, trapeze,
   sprinkler) consistently — users learn them once and recognize them
   everywhere.

6. **The 32 and 16 versions of the same icon should be the same
   illustration** at different sizes, not different designs. Generate
   one, then size and clean both.

7. **Test on the ribbon.** Drop your PNGs into
   `src/Shared/UI/Resources/icons/` keeping the same filenames, rebuild,
   and inspect at actual ribbon size before committing to a full set.
