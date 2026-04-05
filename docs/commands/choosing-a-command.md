# Choosing the Right Command

Many SSG FP Suite commands do related things. This guide helps you pick the right one.

---

## Hanger Placement — Which "Hang" Command?

All of these **place new hangers** on pipes. The difference is where they put them.

| Situation | Command | Why |
|-----------|---------|-----|
| Pipes cross steel beams/joists | **Hang at Structural** | Places a hanger at each pipe-beam crossing point |
| Pipes cross lines in a linked CAD file | **Hang at CAD Lines** | Same idea, but uses CAD linework instead of Revit structural elements |
| Straight pipe runs need evenly-spaced hangers | **Hang Typical Spacing** | Distributes hangers along the pipe at NFPA intervals (default 10'-6") |
| Pipes run parallel to beams (no crossings) | **Hang Parallel Structural** | Searches perpendicular to the pipe to find the nearest beam, places hangers at typical spacing |
| You want to pick exact hanger locations | **Hang User Locations** | Draw detail lines where you want hangers, then run the command |
| Threaded branchline downstream ends only | **Hang Downstream** | Places one hanger near the last sprinkler on each branchline |
| Concrete double tee stems | **Hang Concrete Tee** | Specialized for placing hangers on the side of tee stems using detail lines to mark locations |

### Common workflow

Most projects use a combination:
1. **Hang at Structural** first — gets all the beam-crossing hangers
2. **Hang Typical Spacing** next — fills in the gaps on long runs between beams
3. **Hang Downstream** — catches the branchline ends
4. **Hang User Locations** — places any remaining hangers at specific spots the automated commands missed

---

## Trapeze Hanger Placement — Which "Trapeze" Command?

All of these place **two-rod trapeze hangers**. They differ in spacing method and hanger style.

| Situation | Command | Why |
|-----------|---------|-----|
| Standard pipe trapeze at regular intervals | **Trapeze Hang** | Auto-spaces trapeze hangers along selected pipes |
| Standard pipe trapeze at exact spots | **Trapeze User Locations** | Place at detail-line/pipe intersections — full manual control |
| Unistrut channel trapeze | **Trapeze Unistrut** | Uses Unistrut channel family with calculated extensions beyond rod positions |
| Unistrut 21A (simplified) | **Trapeze Unistrut 21A** | Simplified Unistrut variant with auto-calculated extensions |

**Tip:** If you're not sure whether to use standard or Unistrut, check your project specs. Unistrut is typically specified for larger pipe sizes or when the trapeze needs to carry additional load.

---

## Syncing Hanger Rod Lengths — Which "Sync" Command?

These commands **update parameters on existing hangers** — they don't place new ones. The main question is: how should rod length be calculated?

| Situation | Command | Why |
|-----------|---------|-----|
| Move hangers to nearest pipe first | **Sync to Pipes** | Repositions hangers and sets ring size. Does NOT calculate rod length — run one of the others after this |
| Calculate rod length from actual structure above | **Sync Raybounce** | Shoots a ray straight up to find the closest structural element. Best general-purpose option |
| Same, but need top/bottom of framing choice | **Sync Surface** | Uses geometric face intersection instead of raybounce. Lets you choose framing top or bottom surface. Also persists settings to global parameters |
| Uniform slab — just measure to a known elevation | **Sync to Ref Plane** | Measures vertical distance to a reference plane. Fastest option when structure is a flat slab at a constant elevation |
| Trapeze hangers specifically | **Sync Trapeze** | Specialized for two-rod trapeze — syncs rotation, rod positions, offsets, and pipe diameter in addition to rod length |

### Raybounce vs. Surface — when to use which?

Both calculate rod length from structure above. The differences:

- **Raybounce** requires a 3D view ("3D-Raybounce") and uses Revit's `ReferenceIntersector`. It's simpler but always finds the closest hit — you can't choose top vs. bottom of a beam.
- **Surface** extracts actual geometry faces and does point-in-polygon math. It lets you choose whether framing hangers sync to the **top** or **bottom** flange, and it saves your settings to global parameters for next time. It also excludes angles, hollows, and C-channels automatically.
- **Surface** is generally better for steel-heavy projects where you need the top/bottom choice. **Raybounce** is faster for simple slab-above situations.

### Typical rod length workflow

1. **Sync to Pipes** — snap hangers to their pipes (position + rotation + ring size)
2. **Sync Raybounce** or **Sync Surface** — calculate rod lengths from structure above
3. Review any "missed" hangers highlighted in the selection

---

## Pipe Sleeves — Which "Sleeves" Command?

Each command targets a different type of structural penetration. **Use all three** on most projects — they don't overlap.

| Penetration type | Command | Sizing method |
|-----------------|---------|---------------|
| Pipes through **beams** | **Sleeves at Beams** | NFPA clearance: pipe OD + 2" (< 4") or + 4" (≥ 4") |
| Pipes through **floors/roofs** | **Sleeves at Decks** | Same NFPA clearance; sleeve length = deck thickness ± 2" wet extension |
| Pipes through **walls** | **Sleeves at Walls** | NFPA **lookup table** with separate seismic vs. non-seismic sizes; wall type filtering available |

**Tip:** Run them in order — Beams → Decks → Walls. Each uses a different sleeve family oriented for its penetration type.

---

## Flex Drop Lengths — Standard vs. Dalmatian

Both tag sprinkler heads with a flex drop length value. The difference is how the length is determined.

| Situation | Command | Why |
|-----------|---------|-----|
| All sprinklers get the same standard length | **Flex Drop Lengths** | You pick one length (31"/36"/48"/60"/72") and it applies to all selected sprinklers |
| Each sprinkler has a different flex pipe length | **Flex Drop Dalmatian** | Auto-reads the actual connected flex pipe length and assigns the correct standard size. Supports Wet and Dry systems with different thresholds |

**Tip:** If your sprinklers already have flex pipes modeled and connected, use **Dalmatian** — it reads the actual pipe and picks the right standard length automatically. If flex pipes aren't modeled yet or you're just doing initial layout, use **Standard** with a uniform length.

---

## Elevation Commands — Pipes vs. Sleeves

| What you're tagging | Command | Reference methods |
|--------------------|---------|-------------------|
| **Pipes and fittings** | **Pipe Elevations** | 4 methods: raybounce to structure, reference plane, user-entered Z, or level. Writes TOS, AFF, and slope classification |
| **Pipe sleeves** | **Sleeve Elevations** | Intersects with linked floors for AFF and linked structural decks for BBD (below bottom of deck) |

These target completely different element types and write different parameters — no overlap.

---

## Annotation Cleanup

| What you want to remove | Command | Scope |
|------------------------|---------|-------|
| All generic annotation families | **Clear Annotations** | Deletes every generic annotation instance in the active view — nuclear option for a clean slate |
| Only duplicate text notes | **Delete Duplicate Text** | Finds text notes stacked at the same location, keeps one copy, deletes the rest |

**Tip:** Use **Delete Duplicate Text** first (surgical). Only use **Clear Annotations** when you want to wipe all annotations and regenerate them.

---

## Scope Box Commands

| What you want to do | Command |
|--------------------|---------|
| Create dependent views with scope boxes | **Create Dependent Views** |
| Rotate a scope box to match a grid angle | **Rotate Scope Box** |
| Delete scope boxes | **Remove Scope Boxes** |

These don't overlap — each does something different with scope boxes.

---

## Import Commands

| Source data | Command |
|-------------|---------|
| AutoSPRINK pipe CSV export | **Import AS Pipes** |
| AutoSPRINK sprinkler CSV export | **Import AS Sprinklers** |
| Trimble field points (export, not import) | **Export Trimble Points** |

**Import AS Pipes** creates Revit pipe elements; **Import AS Sprinklers** places sprinkler family instances. Use both when bringing an AutoSPRINK design into Revit.
