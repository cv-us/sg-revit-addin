# AutoInsert — Graphic Scale Bars To Sheets

**Ribbon location:** SG Revit Addin tab > Annotation panel > "Scale Bars"
**Command class:** `SgRevitAddin.Commands.Annotation.GraphicScaleBarsCommand`

## What It Does

Automatically inserts graphic scale bar annotations on sheets based on the scale of each view placed on the sheet. Reads the view scale, maps it to the correct scale bar family type, and places the annotation on the sheet.

## How to Use

1. **Click "Scale Bars"** in the ribbon (Annotation panel)
2. **Choose sheets:**
   - All sheets in the project, or
   - Select specific sheets from the list
3. **Click "Insert Scale Bars"**

## What Happens

1. **Existing scale bars removed** — All "-Graphic Scale Bar" instances on target sheets are deleted
2. **View scales read** — Each view on the sheet has its scale extracted
3. **Scale mapped to type** — The view scale maps to the correct family type (e.g., scale 48 → "1/4\" = 1'-0\"")
4. **Scale bars placed** — One bar per unique scale, stacked vertically near the bottom-left of the sheet
5. **Unrecognized scales skipped** — Views with scales not in the mapping table are silently skipped

## Prerequisites

The "-Graphic Scale Bar" annotation family must be loaded in the project with all required type variants.

## Scale Mapping

| View Scale | Family Type Name |
|-----------|-----------------|
| 1:1 | 12" = 1'-0" |
| 1:2 | 6" = 1'-0" |
| 1:4 | 3" = 1'-0" |
| 1:8 | 1-1/2" = 1'-0" |
| 1:12 | 1" = 1'-0" |
| 1:16 | 3/4" = 1'-0" |
| 1:24 | 1/2" = 1'-0" |
| 1:32 | 3/8" = 1'-0" |
| 1:48 | 1/4" = 1'-0" |
| 1:64 | 3/16" = 1'-0" |
| 1:96 | 1/8" = 1'-0" |
| 1:128 | 3/32" = 1'-0" |
| 1:192 | 1/16" = 1'-0" |
| 1:256 | 3/64" = 1'-0" |
| 1:384 | 1/32" = 1'-0" |
| 1:768 | 1/64" = 1'-0" |
| 1:120 | 1" = 10'-0" |
| 1:240 | 1" = 20'-0" |
| 1:360 | 1" = 30'-0" |
| 1:480 | 1" = 40'-0" |
| 1:600 | 1" = 50'-0" |
| 1:720 | 1" = 60'-0" |
| 1:840 | 1" = 70'-0" |
| 1:960 | 1" = 80'-0" |
| 1:1080 | 1" = 90'-0" |
| 1:1200 | 1" = 100'-0" |
| 1:1920 | 1" = 160'-0" |
| 1:2400 | 1" = 200'-0" |
| 1:3600 | 1" = 300'-0" |
| 1:4800 | 1" = 400'-0" |

## Placement Position

Scale bars are placed near the bottom-left of the sheet:
- X offset: ~1" from left edge
- Y offset: 2" from bottom, stacking upward at 2" intervals for multiple scales

## Changing the Icon

The button icon shares `annotation-32.png`. Replace with a unique icon if desired.
