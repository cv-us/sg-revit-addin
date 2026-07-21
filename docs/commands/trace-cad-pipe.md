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

## Sizing: OD vs nominal, and material

Two distinct things, and getting either wrong mislabels every pipe.

**What is measured is the OUTSIDE diameter. What Revit's diameter parameter wants is the
NOMINAL size.** They are not the same number — 10" steel has a 10.750" OD. Handing Revit
the OD produces "10-3/4"" pipe, which is Revit faithfully displaying the nominal size it
was given.

**The OD for a given nominal depends on the MATERIAL.** Measured against a real
underground model:

| catalog | mean error | worst |
|---|---|---|
| Steel / IPS | 375 mil | 538 mil |
| **Ductile iron** (AWWA C151) | **36 mil** | 188 mil |
| PVC C900 (cast-iron OD) | 36 mil | 188 mil |

The clusters landed dead-on ductile iron — 6.90" -> DI 6" (0 mil), 11.10" -> DI 10"
(0 mil, 39 runs). Underground DI/C900 against a steel table is off by a whole size step.

So the size table is read from **the chosen pipe type's own segments** (`Segment.GetSizes()`
gives both `NominalDiameter` and `OuterDiameter`), which is self-consistent with whatever
material was picked. Built-in Steel / Ductile iron / PVC C900 catalogs are selectable as a
fallback, each shown with its measured fit in mils so the right one is obvious.

The report states which table was used, the mean fit, and warns if it is loose. It also
reports the size Revit **actually** took — a pipe type's size list can refuse a value.

## Dialog options

| Field | Description |
|---|---|
| *(header)* | Reports what was found before anything is placed — run count, total length, and the fitted size histogram. **Sanity-check these against the model before placing.** |
| Pipe type / System / Level | What the created pipe is made of and hosted on. |
| Match against | The pipe type's own size table (recommended), or an explicit Steel / Ductile iron / PVC C900 catalog. Each is listed with its measured fit in mils. |
| Sizing | **Snap to nearest nominal** (recommended), **use the measured OD exactly** (rarely wanted), or **force one nominal size** for everything. |
| Skip runs shorter than | Ignores short stubs. Default 2 ft. |
| Flatten to level | Ignores the traced slope and places each run dead level at its mid-height. |
| Join run ends | Inserts an elbow at each **genuine angled corner**, extending the two pipes a bounded amount to where their axes cross. Inline reducers/straight joins, tees/crosses, and any corner whose crossing isn't provably local are **left exactly as placed** — no pipe is moved or deleted. |

## Notes

- With **Join run ends** off, pipes are placed unconnected and no fittings are inserted.
  Check the alignment against the link before doing anything else with them.
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
