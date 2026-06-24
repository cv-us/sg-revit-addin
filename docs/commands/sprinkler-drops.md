# Sprinkler Drops (Flex to Pendent Heads)

**Command:** `SgRevitAddin.Commands.PipeRouting.SprinklerDropCommand`
**Domain:** Pipe Routing
**Ribbon:** SG ♈ > Pipe Routing > Sprinkler Drops

> ⚠️ **Under development.** This is a first implementation of a hard Revit MEP
> routing flow that can't be fully validated without a live model. Elbow
> resolution, the branch tee, and the flex join are the touchy steps —
> expect field iteration.

## Purpose

Places a hard-pipe drop from a branch line to a pendent sprinkler head, ending in a **real threaded elbow** at the drop base, with a **flexible sprinkler hose** from the elbow to the head.

## Why an elbow, not a union

HydraCAD's native drop runs the flex **collinear** with the hard pipe, so Revit resolves a **union/coupling**. This command instead builds a genuine angular turn: a short horizontal **stub** off the bottom of the vertical drop. The fitting between the vertical drop and the horizontal stub is a real **90° elbow** (resolved from the drop pipe type's routing preferences), so:

- The BOM lists an **elbow** (the correct, resolvable fitting).
- The elbow can be **rotated to aim the flex** in any direction.
- The "Can't create swept blend" failure (from forcing an elbow into a union's geometry slot) is avoided — the angle is real.

## Geometry (up-over-down return bend)

Per head, in feet internally:

| Point | Where | Segment | Pipe type |
|-------|-------|---------|-----------|
| `T` | tap point on the branch (closest point to head) | — | — |
| `R1` | `T + rise` | riser (vertical) | armover |
| `R2` | `(head.XY, R1.Z)` | arm (horizontal) | armover |
| `R3` | `(head.XY, head.Z + termHeight)` | drop (vertical) | drop |
| `R4` | `R3 + aim × stub` | stub (horizontal) | drop |
| — | `head inlet → R4` | flex hose | flex |

Real elbows are inserted at **R1**, **R2**, and **R3** (the drop base) because each is a 90° turn. If `rise` is ~0 the riser is skipped; if the head sits directly over the branch the arm is skipped and the stub aims perpendicular to the branch.

## How the pipes are built (reliability)

- **Every hard segment is a free `Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, a, b)`** sharing endpoints with its neighbours (degenerate <½" segments are dropped).
- At **each interior joint** the command then calls **`doc.Create.NewElbowFitting(c1, c2)`** between the two coincident free end connectors — this *guarantees* a real BOM elbow (the earlier connector-overload approach connected the pipes but left some joints with no fitting). If an elbow can't resolve (collinear runs, or the type has no elbow rule) it falls back to a bare `ConnectTo`.
- **Branch tee:** `PlumbingUtils.BreakCurve(doc, branchId, T)` splits the branch at the tap point, then `doc.Create.NewTeeFitting(b1, b2, riserConnector)`.
- **Flex:** `FlexPipe.Create(doc, systemTypeId, flexTypeId, levelId, path)` — **head-first on purpose.** The flex family's first connector carries the **reducing nipple + bracket**, which belongs at the **sprinkler head**; the plain threaded end then lands on the hard pipe. The path always includes **two mandatory guide vertices** so the whip renders cleanly without hand-editing: one **just above the head** (the inlet faces up, so the whip leaves going up — 4" guide), and one **horizontally in front of the base elbow** toward the head (so the whip leaves the elbow going across, not straight down). If a **whip length** is set, extra vertices droop between them to model the full length with grab points. Both ends `ConnectTo`, then the flex diameter is set **last** (so a connect-time auto-resize doesn't override the chosen flex size).
- **Sloped branches:** the tap point is the **plan-perpendicular foot** of the head onto the branch (carrying the branch's sloped Z), not the 3D-closest point — so the armover stays straight/perpendicular to the branch in plan even when the main is sloped, and the rise/drop absorb the slope.
- All in one transaction with an `IFailuresPreprocessor` that deletes recoverable **warnings** (errors still roll back). Each head is wrapped in try/catch and reported individually.

## Dialog

| Field | Unit | Notes |
|-------|------|-------|
| **Connection mode** | — | **Continuous** (click a head, then its pipe; repeat until Esc) or **Batch** (select heads, then one pipe) |
| Drop pipe type | — | required; defaults to **hcad3 lines threaded** if present, else any threaded, else first |
| Armover pipe type | — | defaults to the same |
| Flex pipe type | — | from loaded `FlexPipeType` (family : type) |
| Drop / armover pipe size | in | default **1"** |
| Flex pipe size | in | default **1"** — set explicitly so flex doesn't come in at its type's nominal (often 4") |
| Return-bend rise above branch | in | **0 = simple perpendicular armover** (drop from branch height); >0 = up-over-down |
| Hard-pipe termination above head | in | where the hard pipe ends / flex takes over |
| Elbow stub length | in | the short turn that forces a real elbow (e.g. 3"). **0 = no stub** — the flex runs straight off the base elbow with no extra pipe. A **"No elbow stub"** checkbox sets it to 0. |
| **Drop offset toward branch** | in | **0 = drop directly over the head.** >0 pulls the hard drop back toward the branch by this much, so it lands *short* of the head; the flex whip reaches the rest. The base elbow aims the whip at the head. Clamped so the drop never crosses the branch. |
| **Flex whip length** | in | **0 = taut/minimal** (straight flex on the shortest path). >0 builds the flex at that full length, drooping through interior vertices, so the whole whip is modeled with grab points to shape — instead of a short segment you have to lengthen by hand. |
| Max flex reach check | in | 0 = no check; otherwise rejects a head whose straight drop-to-head distance exceeds this |
| Swallow recoverable warnings | bool | default on |

All settings persist between runs via `DialogMemory` and are restored on reopen.

## Workflow / modes

- **Continuous:** click a sprinkler, then the pipe to tie it to — it connects, then prompts for the next head/pipe pair. Press **Esc** to finish. Each connection is its own undo step.
- **Batch:** select the heads first (or pick them), then click **one** pipe. Each head ties in on **its own perpendicular armover** to that pipe — a head whose location isn't in-line/perpendicular to the pipe's run is skipped and reported. Teeing splits the pipe; the command tracks the pieces so later heads tie into the correct segment.

Per head: project the head perpendicular onto the pipe → arm over → drop → stub (real elbow at the base) → flex to the head. The summary reports placed / failed counts with a reason per failed head.

## Known risk areas (for field iteration)

1. **No elbow at a joint** — `NewElbowFitting` can throw if the drop pipe type's *Elbows* routing group lacks a rule for the size, or the two runs are near-collinear; the command then falls back to a bare `ConnectTo` (pipes joined, no fitting). Check the type's routing preferences if elbows are missing.
2. **Branch tee fails** — if the tap point lands at a branch end, or the branch's system doesn't match. The hard pipe is still placed; the message flags it.
3. **Flex won't join** — size/domain mismatch; the flex is created but a `ConnectTo` may need a transition. Reported per head.
4. **Wrong inlet picked** on multi-connector heads — filtered to open piping End connectors.

## See Also

- **Shorten Flex Pipes** — straightens existing flex.
- **Flex Drops Set / Auto** — *tagging* commands (flex length annotations), not geometry.
