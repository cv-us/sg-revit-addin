# SG Revit Addin — Future Command Ideas & Enhancements

Track ideas for new commands, enhancements to existing commands, and general improvements here.
Anyone on the team can add to this list.

---

## Export / Field Layout

- [ ] **Dimensions to closest grid/column line per Trimble point** — After exporting Trimble hanger points, auto-dimension each point to the nearest column grid line in the active view. Gives field crews a paper backup for verifying Trimble shots. Could be part of ExportTrimblePoints or a separate command.
- [ ] **Dimensions to closest grid/column line per sleeve** — For each pipe sleeve (wall/deck/beam), place dimensions from the sleeve center to the nearest column grid lines (both directions). Useful for sleeve layout coordination sheets.
- [ ] **Export sleeve locations to Trimble** — Same CSV export as hangers but for pipe sleeves (wall/deck/beam penetrations). Field can lay out core drill locations or embed locations.
- [ ] **Export sprinkler head locations to Trimble** — For T-bar ceiling layout coordination, export head XY positions.
- [ ] **JobXML (.jxl) export option** — Add Trimble JobXML format as an alternative to CSV. Open-source C# library available at github.com/KubaSzostak/JobXML. Embeds coordinate system metadata directly in the file.

## Hangers

- [ ] **AutoSync Hangers to Structural Elements** — Complement to the Reference Plane sync. Use raybounce or linked structural geometry to find actual structure above each hanger and set rod length. (Dynamo version exists?)
- [ ] **Hanger schedule export** — Export hanger data (type, rod length, pipe size, location) to Excel for procurement.
- [ ] **Hanger conflict check** — Flag hangers that conflict with ductwork, cable tray, or other trades in linked models.

## Annotation

- [ ] **Auto-dimension sleeves to grids** — Place dimensions from each sleeve centerline to the two nearest perpendicular grid lines. For coordination drawings.
- [ ] **Sleeve schedule with grid references** — Generate a schedule of sleeves with auto-populated grid intersection references (e.g., "Between A-B / 1-2").

## Coordination

- [ ] **Clash report to Excel** — Export Navisworks-style clash data from linked model intersections to a formatted Excel report.
- [ ] **RFI tracking parameters** — Batch-write RFI numbers and status to elements for tracking open coordination items.

## General / Quality of Life

- [ ] **Batch parameter writer** — Select elements and write a value to a named parameter on all of them. Generic utility command.
- [ ] **Element count dashboard** — Quick summary dialog showing counts of all FP elements by category, level, and system.
- [ ] **Project health check** — Validate that all required shared parameters exist, families are loaded, and project is set up correctly for the suite.

---

*Last updated: 2026-04-04*

