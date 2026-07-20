# PipeRouting: Trace CAD Pipe

**Command:** `TraceCadPipeCommand`
**Domain:** PipeRouting
**Ribbon:** SG Revit Addin > Pipe Routing > Trace CAD Pipe

## Purpose

Traces the pipe in a linked/imported CAD and builds **real Revit pipe** from it.

The motivating workflow: a Navisworks coordination model can't be read at all — the
Revit API exposes no geometry for a linked NWC in any version. Round-tripping
**NWC → FBX → 3ds Max → DWG** produces a link the API *can* walk, and this command
turns it into pipe.

## How the source geometry is shaped

Measured on a real 1641 × 801 ft warehouse model:

| | |
|---|---|
| **Pipe** | arrives as a **Mesh** — coarse triangulated tubes, one connected component per straight run (97 runs, 5,480 linear ft, 6″/8″/10″) |
| **Fittings** | arrive as separate **Solids** — compact ~14″ bodies. Every one sat within **0.8 ft** of a pipe-run end, so they mark the junctions |

Only the mesh is traced. The solids are left alone — they're where fittings go, and
Revit makes its own when pipes are connected.

## Method

1. **Weld** mesh vertices (1e-4 ft) and take **connected components** of the triangle
   graph — each component is one straight run.
2. The component's **principal axis** (largest eigenvector of the vertex covariance) is
   the pipe axis; the extent along it gives the endpoints.
3. The **median radial distance** from that axis gives the radius — median rather than
   mean because a coarse tube's vertices sit on the circumscribed polygon, and stray
   end triangles would drag a mean.
4. Snap to the nearest nominal steel OD, and create pipe with `Pipe.Create`.

## Dialog options

| Field | Description |
|---|---|
| *(header)* | Reports what was found before anything is placed — run count, total length, and the fitted size histogram. **Sanity-check these against the model before placing.** |
| Pipe type / System / Level | What the created pipe is made of and hosted on. |
| Sizing | **Snap to nearest nominal** (recommended), **use the measured diameter exactly**, or **force one size** for everything. |
| Skip runs shorter than | Ignores short stubs. Default 2 ft. |
| Flatten to level | Ignores the traced slope and places each run dead level at its mid-height. |

## Notes

- Pipes are placed but **NOT connected** — no fittings are inserted, no systems are
  built. Check the alignment against the link before doing anything else with them.
- Slope comes straight off the fitted axis and is reliable when the model sits near the
  origin. Far-from-origin geometry (state-plane coordinates) plus a float32 FBX leg
  injects vertex jitter, and **slope is the first casualty** — a level pipe can read as
  pitched. Run **Coordination > Inspect CAD Geom** first; it reports the float32 ulp at
  the model's distance and flags this.
- If the command reports no runs, the piping did not survive the export. Inspect CAD
  Geom will show whether the link has any mesh at all.
- Sizes depend on how coarsely the tube was tessellated. The measured diameter of a
  faceted tube is slightly under the true OD, which is why snapping to nominal is the
  default.
