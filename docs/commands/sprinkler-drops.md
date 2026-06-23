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
| — | `R4 → head inlet` | flex hose | flex |

Elbows auto-resolve at **R1**, **R2**, and **R3** (the drop base) because each is a 90° turn. If `rise` is ~0 the riser is skipped; if the head sits directly over the branch the arm is skipped and the stub aims perpendicular to the branch.

## How the pipes are built (reliability)

- The **first** segment is a free `Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, p0, p1)`.
- **Each subsequent** segment is created with the **connector overload** `Pipe.Create(doc, pipeTypeId, levelId, previousFreeEndConnector, endPoint)` — which inserts the routing-preference fitting **at creation**. This is more dependable than a post-hoc `Connector.ConnectTo`.
- **Branch tee:** `PlumbingUtils.BreakCurve(doc, branchId, T)` splits the branch at the tap point, then `doc.Create.NewTeeFitting(b1, b2, riserConnector)`.
- **Flex:** `FlexPipe.Create(doc, systemTypeId, flexTypeId, levelId, {R4, head})` then `ConnectTo` each end (to the stub's open connector and the head inlet).
- All in one transaction with an `IFailuresPreprocessor` that deletes recoverable **warnings** (errors still roll back). Each head is wrapped in try/catch and reported individually.

## Dialog

| Field | Unit | Notes |
|-------|------|-------|
| **Connection mode** | — | **Continuous** (click a head, then its pipe; repeat until Esc) or **Batch** (select heads, then one pipe) |
| Drop pipe type | — | required; defaults to **hcad3 lines threaded** if present, else any threaded, else first |
| Armover pipe type | — | defaults to the same |
| Flex pipe type | — | from loaded `FlexPipeType` (family : type) |
| Drop / armover pipe size | in | default **1"** |
| Return-bend rise above branch | in | **0 = simple perpendicular armover** (drop from branch height); >0 = up-over-down |
| Hard-pipe termination above head | in | where the hard pipe ends / flex takes over |
| Elbow stub length | in | the short turn that forces a real elbow (e.g. 3") |
| Max flex length | in | 0 = no check; otherwise validates reachability |
| Swallow recoverable warnings | bool | default on |

All settings persist between runs via `DialogMemory` and are restored on reopen.

## Workflow / modes

- **Continuous:** click a sprinkler, then the pipe to tie it to — it connects, then prompts for the next head/pipe pair. Press **Esc** to finish. Each connection is its own undo step.
- **Batch:** select the heads first (or pick them), then click **one** pipe. Each head ties in on **its own perpendicular armover** to that pipe — a head whose location isn't in-line/perpendicular to the pipe's run is skipped and reported. Teeing splits the pipe; the command tracks the pieces so later heads tie into the correct segment.

Per head: project the head perpendicular onto the pipe → arm over → drop → stub (real elbow at the base) → flex to the head. The summary reports placed / failed counts with a reason per failed head.

## Known risk areas (for field iteration)

1. **Elbow resolves as union/coupling** — if a join ends up near-collinear or the drop pipe type's *Elbows* routing group lacks a rule for the size. Fallback: explicit `NewElbowFitting`, or place + rotate the elbow `FamilySymbol`.
2. **Branch tee fails** — if the tap point lands at a branch end, or the branch's system doesn't match. The hard pipe is still placed; the message flags it.
3. **Flex won't join** — size/domain mismatch; the flex is created but a `ConnectTo` may need a transition. Reported per head.
4. **Wrong inlet picked** on multi-connector heads — filtered to open piping End connectors.

## See Also

- **Shorten Flex Pipes** — straightens existing flex.
- **Flex Drops Set / Auto** — *tagging* commands (flex length annotations), not geometry.
