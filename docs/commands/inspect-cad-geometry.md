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
