# Coordination: Inspect CAD Geometry

**Command:** `InspectCadGeometryCommand`
**Domain:** Coordination
**Ribbon:** SG Revit Addin > Coordination > Inspect CAD Geom

## Purpose

Read-only diagnostic for the question *"can we trace a linked coordination model
into real Revit pipe?"* It reports what Revit **actually hands the API** for a
linked/imported CAD, which is what decides whether that conversion is a weekend
or a quarter.

The motivating workflow: a Navisworks **NWC** can't be read at all — the Revit API
has no coordination-model geometry access in any version (2026 added
`CoordinationModelLinkUtils`, but it exposes properties-on-pick, visibility and
color only, never geometry). Round-tripping **NWC → FBX → 3ds Max → DWG** produces
a link that *is* readable. This command tells you what survived that trip.

## What it reports

| Section | Why it matters |
|---|---|
| **Geometry kind** | `Solid` vs `Mesh` counts. Solids carrying `CylindricalFace` are the best case — `Axis`, `Origin` and `Radius` are exact analytic values, so centerline, diameter and slope are *read*, not fitted. |
| **Face histogram** | Cylindrical / planar / conical / revolved / other. A faceted pipe (planar sides, no cylindrical faces) still works but needs cylinder fitting. |
| **Segmentation** | Solid and `GeometryInstance` counts. Many separate units = pipes arrived pre-separated and clustering is nearly free; a couple of huge units = one merged blob that has to be segmented first. |
| **Sizes** | Every cylinder radius grouped and matched to nominal steel pipe OD (½″–12″), with the miss in **mils**. Shows whether real pipe sizes are recoverable. |
| **Slope** | Pitch of each cylinder axis in **in / 10 ft**, plus a level / sloped / vertical breakdown and the min/median/max pitch of the sloped runs. |
| **Coordinates** | Model extent and the **float32 ulp** at that distance — the precision trap (see below). |
| **Layers** | `GraphicsStyle` names that survived. Once FBX has discarded sizes and systems, layers are the only metadata left. |

## Segmentation probe

Imported CAD usually arrives as a handful of **merged** solids (Revit tends to group
by layer), so the question that decides the project's size is whether those can be
split back into individual pipes. The probe attacks it three ways and prints all of it:

1. **Per-solid detail** — faces, edges, volume, surface area, `IsValidForTessellation`,
   and the layer name. `volume = 0` means an **open shell** rather than a closed solid
   (common after an FBX round trip, which often drops caps or leaves surfaces unwelded).
2. **`SolidUtils.SplitVolumes()`** — free segmentation when the solids are valid volumes.
   Reports the piece count per solid, or the exception if it refuses. Open shells
   generally won't split this way.
3. **Face-adjacency connected components** — faces are joined where they share an edge,
   with edges keyed on **quantised endpoint pairs** (~1/40″, order-independent). Object
   identity is deliberately avoided: Revit returns a *fresh* `Face`/`Edge` wrapper on
   every call, so matching `Edge.GetFace()` against `Solid.Faces` by reference never hits
   (an early version did exactly that and every one of 42,089 edges failed to resolve,
   leaving each face as its own component). The report prints `edge uses` and
   `matched pairs` so a matching failure is visible rather than silent.
4. **Region growing into single pipes.** A connected component is a whole welded *run*,
   not one pipe, so fitting it directly fails on mixed axes. For two **adjacent** faces on
   the same cylinder both normals are perpendicular to the axis, so **n₁ × n₂ *is* the
   axis** — exactly, whatever the triangulation. Seed on such a pair, then flood outward
   taking every face whose normal is perpendicular to that axis; growth stops by itself at
   a joint. The seed uses the neighbour with the **smallest dihedral**: an adjacent side
   facet is ~22° away, but an end **cap** is 90° away *and* parallel to the axis, so
   seeding on a cap yields an axis at right angles to the real one.

   (An earlier version instead assumed "a side facet's longest edge is parallel to the
   axis". That fails on triangulated input — and the real model *is* triangulated,
   2E/F = 3.21 — because the longest edge of a split facet is the **diagonal**, 5.2°
   off-axis and tilting opposite ways on alternating triangles. It shattered 26,217 faces
   into 22,270 fragments.)

### Arc coverage

Fitting a circle to a partial **arc** massively over-reads the radius: 4-face fragments of
real pipe came back as "9–13 in OD". Every fit therefore reports **wrap** and the circle-fit
**RMS residual**, and the summary counts only the well-conditioned ones (≥300° wrap and
&lt;50 mil RMS).

Wrap is `360 − largest angular gap` between consecutive facet normals, *not* a count of
filled bins: an n-facet cylinder has only n distinct normals, so a full 16-facet ring could
never fill more than 16 of 36 bins (160°) and every real pipe would fail a "≥300°" test. By
the gap measure a full 16-facet ring scores 337.5° and a 90° arc scores 90°.

### Reading `volume`

A **negative** volume means a closed solid whose faces are **inverted** — a common FBX /
3ds Max round-trip artifact — not an empty or open one. It is harmless for fitting, since
the axis comes from `n·nᵀ` and the normal's sign cancels. The report counts inverted
solids separately so this can't be misread as "no geometry".

It then **fits** the largest 600 components — axis from the area-weighted normal
covariance, radius from a circle fit with cap facets excluded — and reports fitted OD
against nominal steel sizes (with the miss in mils), the on-catalog hit rate, and the
level / sloped / vertical split with min-median-max pitch.

Reading the result: hundreds of components each landing within a few mils of a real pipe
size, with sensible slopes, means the pipeline works and the remaining effort is topology
(tees vs elbows) and building the Revit pipe. A handful of thousand-face blobs instead
means the geometry is welded into one mesh and connectivity won't separate it — that's
the RANSAC case.

Note the probe triangulates far more geometry than the basic pass, so expect a few
seconds on a large model.

## Workflow

1. Link the DWG (**Insert > Link CAD**).
2. Run **Inspect CAD Geom**. With an import selected it inspects that one;
   with nothing selected it sweeps every `ImportInstance` in the document.
3. Read the **VERDICT** block at the bottom, or copy the whole report out.

Nothing is modified — the command is `[Transaction(TransactionMode.ReadOnly)]`.

## Notes

- **The precision trap.** FBX commonly stores float32 (~7 significant digits). On
  state-plane coordinates (~2,000,000 ft) one ulp is ≈0.24 ft of vertex jitter.
  **Slope is the first casualty, well before diameter**: at 500 mil of jitter a
  dead-level pipe reads as ≈0.17 in/10 ft of phantom pitch — the same order as a
  real ¼ in/10 ft design slope — and past ~2900 mil a 4″ pipe measures as 6″.
  Export near the origin, not on site coordinates. The report flags this
  automatically (DANGER / marginal / fine).
- **Cap facets bias diameter low.** If you end up fitting rather than reading
  cylinders, exclude end-cap triangles. They're usually a fan from the axis
  centre, and that centre vertex sits at radius 0 — including it measured
  4.500″ as 4.108″, enough to call a 2½″ pipe a 2″. Side facets have their normal
  perpendicular to the axis, so one dot product separates them.
- Fitting itself is reliable on clean tessellated CAD geometry (axis recovered to
  ~0.008° with 50-mil noise, diameter exact). Published 71–90% accuracy figures
  come from noisy, occluded laser scans and are a pessimistic floor here.
- Junctions are the hard part, not the straight runs — tees are where the
  published systems lose accuracy (~71% vs ~88% for elbows).
