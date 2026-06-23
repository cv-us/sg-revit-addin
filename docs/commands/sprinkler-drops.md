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
| Drop pipe type | — | required |
| Armover pipe type | — | defaults to drop type |
| Flex pipe type | — | from loaded `FlexPipeType` (family : type) |
| Return-bend rise above branch | in | 0 = no up-over (drop from branch height) |
| Hard-pipe termination above head | in | where the hard pipe ends / flex takes over |
| Elbow stub length | in | the short turn that forces a real elbow (e.g. 3") |
| Max flex length | in | 0 = no check; otherwise validates reachability |
| Swallow recoverable warnings | bool | default on |

All settings persist between runs via `DialogMemory`.

## Workflow

1. Select pendent heads (and optionally the branch pipe) — or run and pick.
2. Run **Sprinkler Drops**; configure the dialog; **Place Drops**.
3. Per head: build the route, auto-insert elbows, tee onto the branch, run the flex.
4. Summary reports placed / failed counts with a reason per failed head.

## Known risk areas (for field iteration)

1. **Elbow resolves as union/coupling** — if a join ends up near-collinear or the drop pipe type's *Elbows* routing group lacks a rule for the size. Fallback: explicit `NewElbowFitting`, or place + rotate the elbow `FamilySymbol`.
2. **Branch tee fails** — if the tap point lands at a branch end, or the branch's system doesn't match. The hard pipe is still placed; the message flags it.
3. **Flex won't join** — size/domain mismatch; the flex is created but a `ConnectTo` may need a transition. Reported per head.
4. **Wrong inlet picked** on multi-connector heads — filtered to open piping End connectors.

## See Also

- **Shorten Flex Pipes** — straightens existing flex.
- **Flex Drops Set / Auto** — *tagging* commands (flex length annotations), not geometry.
