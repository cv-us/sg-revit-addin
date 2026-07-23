# SprinklerLayout: Layout

**Command:** `LayoutCommand`
**Domain:** SprinklerLayout
**Ribbon:** SG Revit Addin > Sprinkler Layout > Layout

## Purpose

Fills a picked area with branch lines and sprinklers at **variable spacings**.
Fixed-spacing array tools (e.g. Hydratec's layout) place every line and head at
one spacing; here the spacings live in **slots** and **sequence strings** that
**repeat to fill the area**, so a mixed rhythm tiles across the whole space in
one pass.

- **Line slots 1-6** — feet-inches spacing values for line-to-line intervals.
- **Head slots A-F** — feet-inches spacing values for head-to-head intervals.
- **Line sequence** — cycles across the area's width. `112112` with slot 1 =
  11'-6" and slot 2 = 8'-0" lays lines at 11-6, 11-6, 8-0, … then **repeats**.
- **Head sequence** — cycles along each line. `AABA` with A = 8'-0" and
  B = 10'-0" places heads at 8-0, 8-0, 10-0, 8-0, … then **repeats**.

## Pick modes

Chosen with the **Pick mode** dropdown:

### Fill area — pick 2 corners
Pick two opposite corners of an axis-aligned rectangle. Lines run in the
direction set by the **branch-line direction** arrows toggle; the head sequence
tiles along each line and the pipe extends past the last head to its cap.

### Area + central main — pick 2 corners + a main point
Pick two corners, then a third point where the **cross-main** runs. Branch lines
run perpendicular to the main. The head sequence tiles **once, continuously**
across each whole line, so the head-to-head gap that straddles the main is the
same sequence gap as every other gap — the main never eats a head or squeezes the
two heads next to it. If the picked main lands closer than **Head clear** to a head
centerline, the **main** slides to that head's near side at exactly that distance;
the heads never move. The summary reports any shift. If the head spacing is so
tight that no spot in the area clears a head, the command says so and places
nothing rather than building a kinked crossing.

Each branch is a shallow **V** — lowest where it
crosses the main, rising to both outer edges at the **branch slope** — so the
branch drains toward the main (dry / pre-action systems). At each crossing the
branch is continuous with a **Firelock tee** and a vertical **riser nipple**
(its own **riser size**) drops to the main, where a **GOL / grooved outlet**
taps it. The cross-main is one **continuous** sloped pipe running at the picked
point, sloping down toward its **riser end** (reversible); past the last riser
at the top of the slope it continues a short 6″ piece that is **capped**, and
the low/riser end is left **open** to tie into the system riser.

### Two mains — pick 2 corners + 2 mains
Pick two corners, then the **primary** main and the **secondary / floater** main.
Each branch line runs **between the two mains** (flat / unsloped), tying into both
with a **riser nipple** + **GOL** on the main. With **Tailback** on, the branch
continues a short capped stub past each main (a **Firelock tee**); with it off,
the branch **elbows** into the riser at each main (no stub). Heads tile from the
lower main across to the far one; if the last head lands within **Head clear** of
the far main, that main slides **outward** to make room (the head stays put). The
mains break at every crossing (GOL) and are left **open** at their ends (6″ stubs)
to tie in. Unsloped.

The **Branch outlet on main** and **Riser tee** fittings are selectable (defaulting
to a GOL and a Firelock tee found by name). They're forced by temporarily injecting a
top-priority routing-preference rule for the one placement, then removing it, so
the pipe type is left untouched; if a chosen family can't be placed, that
junction falls back to the routing-preference default and the count is reported.

### Branch tie-in — riser nipple vs side outlet
Both main modes offer a **Branch tie-in** style (default *riser nipple above the main*):

- **Riser nipple above the main** — the branch runs above the main; a vertical riser
  nipple drops from the branch to the main, tapped with the **Branch outlet on main**
  fitting, and the branch halves meet at the **Riser tee** on top of the nipple. This
  is the drain-to-main dry / pre-action arrangement.
- **Side outlet at main elevation** — the branch sits **at the main's centerline
  elevation** and ties straight into the side of the main, with no nipple. Where a
  branch crosses an interior main on both sides it becomes a **4-way outlet** (main
  both ways + branch both ways); at a main **end**, or a **two-mains** tap where the
  branch is on one side, it's a **tee**. The **Branch outlet on main** family is used
  for both. If that family (or the pipe type's routing preferences) has no cross for
  the interior 4-way case, the junction is connected without a fitting and the count
  is reported — pick a cross-capable outlet family, or add a cross to the pipe type's
  routing preferences, if you need the fitting itself.

## Workflow

1. Run **Layout**. Fill the slots and sequences, set the branch-line direction
   (click the arrows to rotate X ⇄ Y), pick pipe/system/size, sprinkler type,
   level, elevations, slopes, cap options, and — in main mode — the main size,
   elevation, and slope. Everything is remembered.
2. Click **Place**, then in a plan view pick the corners (and, in main mode, the
   main point).
3. The command tiles the sequences, builds the pipes, places and connects the
   heads, inserts tees/elbows, caps the branch ends, and (main mode) drops a
   riser nipple + tee to the cross-main at every crossing.
4. A summary reports lines/segments/heads/sprigs/nipples/tees/elbows/caps,
   connections, and anything skipped or failed.

## Dialog Options

| Field | Description |
|---|---|
| Line / head spacing slots | Feet + inches per slot; blank slots can't be referenced. |
| Sequences | Digits (lines) / letters (heads); repeat to fill the area. A single character = uniform spacing. |
| Pick mode | Fill area (2 corners), Area + central main (3 points), or Two mains (4 points). The pick guidance shows next to the **Place** button. |
| Branch-line direction | Clickable arrows — rotate the branch direction between the screen X and Y axes. |
| Pipe type / System / Line size | The pipe type, piping system, and size for the branch lines. **Size is a dropdown** of standard sprinkler-pipe sizes (¼″ … 12″; above that is underground only). Main / Riser / Sprig sizes use the same dropdown. |
| Level / Start elevation | Reference level and the branch centerline elevation above it (in main mode, the branch's low point at the main). |
| Slope | Branch slope, **inches per 10 ft**. In main mode this is the downhill toward the main (both sides). |
| Start offset | Distance from the **first-picked corner** to the **first branch line**, before the line sequence repeats. E.g. set 2′-3″ to hold the first line 2′-3″ off a beam you click as the first corner. `0` keeps the original behavior (first line one spacing in). |
| End offset | *(Fill-area mode)* Distance from that **same first corner** to the **last sprinkler** along the line — that corner is treated as the branch lines' capped/dead end. The pipe runs one cap-length (Extend to cap) further to the cap **toward** that corner, and heads tile away toward the far (open) end. So one corner click sets both the first-line and the last-head dimension. `0` = original behavior (heads from the near edge, cap at the far end). |
| Cap branch-line ends / Extend to cap | Place the pipe type's routing-preference cap a set distance past the last head. |
| Cross-main: Main size / Riser size | The cross-main's diameter and the riser-nipple diameter (off the main to the branches). |
| Main elevation / slope | The cross-main's centerline elevation above the level and its slope (in/10 ft) toward the riser. |
| Head clear | Minimum **centerline** distance from a main to the nearest sprinkler (default 6″). Head spacing is never altered to make room — the **main shifts** off the picked point instead, and the summary says by how much. On a **sloped** branch the command raises it to 12″ (and says so): the branch runs flat for 12″ through the crossing so the riser tee stays straight, and a head inside that flat run would sit on the slope break and get a kinked tee. Unsloped branches (two mains, or slope 0) honor the value as typed. |
| Main outlet | Fitting where each riser nipple taps the main (default a GOL / grooved outlet, tapped without cutting the main). |
| Riser tee | Fitting at the top of the riser where the branch lines meet (default an HCAD Firelock tee). |
| Main / slope image | Clickable image of the main (perpendicular to the branches). In 3-point mode it labels **HIGH / LOW** — click to flip the slope (which end drains to the riser). The branch-direction toggle reorients it; two-mains shows both mains. |
| Tailback at mains | Two-mains only: tee + short stub past each main (on) vs an elbow into the riser (off). |
| Heads directly at outlets | Heads placed with their inlet on the line, joined as the tee branch. |
| Sprigs up to heads | A vertical sprig pipe rises from the line to each head. |
| Common / Fixed sprig | All heads at one elevation (sprigs adapt to the slope) or every sprig the same length. |

## Notes

- The fill rectangle is **axis-aligned** to the plan view. For a rotated
  building, rotate the view/crop (a rotation option may come later).
- A vertical nipple/branch into a sloped main or a V's slight kink can defeat
  `NewTeeFitting`; the command then falls back to a plain connector join and
  reports the count (check the pipe type's fitting/routing preferences).
- Caps come from the pipe type's routing-preference **Caps** group; if the type
  has none, any loaded pipe-cap family is used, else ends are left open (the
  report says so). A cap needs a non-zero **extend to cap** so there's a stub.
  Only the cap is placed — no separate coupling (the grooved cap accounts for
  the coupling already).
- In main mode the head sequence is anchored at the **first line edge** and tiles
  straight across, so any leftover shorter-than-a-gap remainder lands at the far
  edge — not at the main. (Older builds tiled inward from *both* edges, which put
  the odd gap right at the crossing.)
- In main mode, if the **Main elevation** is at or above the branch low point the
  nipple can't drop — that crossing is skipped and reported (raise the branch or
  lower the main).
- Each fill-mode line's near end is left open to tie into the main/riser.
- A very tight spacing over a large area can create thousands of elements; the
  command warns and asks to confirm past ~4000 heads.
