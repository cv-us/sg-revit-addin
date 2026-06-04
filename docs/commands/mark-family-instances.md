# Mark Family Instances

**Ribbon:** SG → Coordination → Mark Family Instances
**Class:** `SgRevitAddin.Commands.Coordination.MarkFamilyInstancesCommand`

## Purpose

Places a bright orange 12-inch DirectShape sphere at the center of every
instance of a chosen family — useful for spotting where a particular
family lives in a busy model.

The spheres are large enough to read at typical view scales and
distinctive enough (vivid orange Generic Model) to stand out against
piping, structure, and architecture.

## Workflow

1. Run **Coordination → Mark Family Instances**.
2. The dialog opens with every family in the project that has at least
   one instance, listed as `FamilyName [Category] ×count`.
3. **Filter the list** by typing in the Search box — matches against
   family name or category, case-insensitive.
4. **Pick which worksets to include** (right-hand checklist). Every
   workset is checked by default; **Select All** / **Select None**
   buttons toggle them in bulk. Changing the workset selection
   immediately refreshes the family list — families with zero instances
   on the chosen worksets disappear, and the `×count` figures reflect
   the selection. For non-workshared projects this section is replaced
   with a hint and has no effect.
5. **Pick a scope**:
   - **Active view only** — only mark instances visible in the current view.
   - **Whole project** — mark every instance in the project (default).
6. Choose an action:
   - **Place Markers** — drop spheres at the center of each matching
     instance (family + scope + worksets). Does **not** clear prior
     markers; placements accumulate.
   - **Delete All Markers** — remove every Family Instance Marker in the
     project (enabled only when markers exist).
   - **Close** — dismiss without changes.

## Marker geometry

Each marker is a 12-inch diameter sphere (6-inch radius) built via Revit's
revolved-geometry API:

- Profile: a half-circle arc in the world XZ plane from the bottom pole
  through the equator to the top pole, closed by a line along the Z axis.
- Revolved 360° around the Z axis through the instance center.
- Placed at the instance's **bounding-box center** — reliable across
  different family origins (an elbow's geometric middle, not one of its
  connector ends).

Material: vivid orange (`SG_FamilyInstanceMarker`, RGB 255/130/0).

Stamped with `ApplicationId = "SgRevitAddin"` and
`ApplicationDataId = "FamilyInstanceMarker"`, distinct from every other
SG marker command so they never collide.

## Re-running

Place Markers does **not** delete prior markers — by design. Running it
twice for the same family at the same scope produces duplicate spheres
at each instance. If that's not what you want, click **Delete All
Markers** first.

## Tips

- The Coordination panel sits next to **Color Code Pipes**; the two
  together are a good "find / visualize" pair.
- For hangers specifically, use [Mark Type for Review](mark-type-for-review.md)
  instead — it filters by Hydratec Type Code and draws tall columns
  instead of spheres.

## See also

- [Mark Type for Review](mark-type-for-review.md) — hanger-specific
  variant using `Type Code (Hydratec)` and tall magenta cylinders.
- [Color Code Pipes](color-code-pipes.md) — color-codes pipes by size or
  type instead of dropping point markers.
