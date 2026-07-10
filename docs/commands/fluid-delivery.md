# Hydraulics: Fluid Delivery

**Command:** `FluidDeliveryCommand`
**Domain:** Hydraulics
**Ribbon:** SG Revit Addin > Hydraulics > Fluid Delivery

## Purpose

Estimate the **water-delivery time** for a dry / (double-interlock) preaction
system — the time from valve trip / sprinkler activation until water reaches the
most-remote **flowing** sprinkler. You pick the source valve, draw a region to
flag the flowing (remote-area) heads, and the command traces the pipe network to
the governing head and runs a two-phase air-displacement model. Results show on
screen and export to a clean PDF.

## ⚠ Not a listed calculation — read first

This is a **documented engineering ESTIMATE** (roughly **±25–40 %**). It assumes a
tree layout and quasi-steady air displacement. It is **not** a listed calculation
and **not for NFPA code compliance** — verify final designs with a listed program
(e.g. Tyco SprinkFDT) or a physical trip test. The caveat is printed on every
result and PDF.

## Workflow

1. **Fill the dialog** (all values are remembered):
   - **Remote area** — how to mark the flowing heads: draw a **rectangle** (2
     corners), draw a **polygon** (click corners, Esc to finish), or **use heads
     already flagged** `Flowing (Hydratec)`.
   - **Hazard class** — sets the NFPA 13 Table 8.2.3.6.1 target time (Light 60 s,
     Ordinary 50 s, Extra 45 s, High-pile 40 s, Dwelling 15 s). Editable target.
   - **Water supply** — a two-point curve: static psi, residual psi @ flow gpm.
   - **System & air** — **Preaction (electric double-interlock)** uses a
     detection/valve latency (the electric valve opens on the signal, ≈ immediate);
     **Dry-pipe (differential)** instead models the pneumatic air blowdown from the
     supervisory pressure to the trip pressure. Plus C-factor (100 black steel /
     120 nitrogen), and gas temperature.
   - **Sprinkler** — K-factor (auto-read from the flowing heads by default).
2. **Pick the source** — the preaction/dry valve or riser where water enters.
3. **Draw the region** (unless *use existing flowing*). Heads inside get
   `Flowing (Hydratec) = 1` (a real, undoable model change).
4. The command **traces the network** from the source to every flagged head,
   picks the slowest (governing) head, and reports. Click **Save PDF…** to export.

## The model (what it computes)

Water can advance no faster than the trapped air ahead of it can vent through the
open sprinkler orifices — so the sprinkler **K-factor gives the vent area**
(`A_eff = 0.0263·K·N`), and **more open heads = faster fill**.

- **Trip phase** — for a dry-pipe differential valve, the air blows down over the
  whole system volume from supervisory to trip pressure (`≈ 1.12·V/(K·N)`). For an
  electric preaction valve this is just the detection/valve latency.
- **Transit phase** — the water front marches segment-by-segment to the remote
  head; each segment's fill rate is the balance of **air-venting-limited** (small
  orifice, low pressure) vs **supply-limited** (friction + lift + back-pressure).
  The report labels which one binds per segment.

**Two volumes, two jobs:** the **path volume** (valve → remote head) drives the
delivery time; the **whole-system volume** is the code gate (≤ 500 gal preaction is
typically exempt; > 750 gal dry must meet the table). Per-pipe volume comes from
`Volume Hydratec` when present, else is computed from the true bore
(`Inside Diameter`) × length.

## Reading the result

- **Total delivery** with the pass/fail banner against the target.
- Breakdown: detection/valve, trip (air blowdown), water transit.
- Governing head id, system, source, system & path volume, N open heads, K.
- **Path table** — per segment: size, length, gallons, fill gpm, binding regime
  (air-vent / supply), and cumulative seconds.
- **Warnings** (e.g. a segment that can't fill for lack of supply pressure) and
  **notes** (the volume code gate).

## Notes

- The trace follows joined connectors from the source into the flowing heads'
  system(s). If a head is unreachable, the usual cause is an **unjoined butt
  joint** in Revit (the connectors aren't actually connected) — it dead-ends the
  trace and the head is reported as not reached.
- Flagging heads Flowing is inside a transaction — **Undo** reverts it.
- Fittings/valves are treated as zero-length pass-throughs (their small cavity
  volume is negligible next to the pipe runs).

## See also

- [Layout](layout.md) — lays out the branch lines these heads sit on.
- `InspectElementParametersCommand` — dump a pipe/head/valve's parameters.
