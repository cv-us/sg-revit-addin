SSG FP Suite - Families Bundle
==============================

Drop all .rfa family files that should ship with the installer into this
folder. When the installer is built, every .rfa file in this directory is
bundled into the setup .exe and deployed to the user's machine.

Install destination on the user's PC:
  C:\SSG FP\Revit Families\

This matches the default path hardcoded in the "Load Families" command
(LoadFamiliesCommand.cs). Users can override the path at runtime via
the Load Families dialog.

Subfolders are preserved. For example:
  installer\Families\Hangers\-Pipe Hanger.rfa
  -->  C:\SSG FP\Revit Families\Hangers\-Pipe Hanger.rfa

Suggested organization (matches command domains):
  Families\
    Hangers\            (-Pipe Hanger.rfa, -Pipe Trapeze.rfa, etc.)
    Seismic\            (-SeismicBrace-*.rfa)
    Sleeves\            (-Pipe Sleeve-*.rfa)
    Annotation\         (-Flex Drop Length Tag.rfa, -Graphic Scale Bar.rfa)
    Trimble\            (-Trimble-*.rfa)
    ModelCheck\         (-Model Check - *.rfa, - Clearance - *.rfa)
    Fittings\           (-POL.rfa, O-LET.rfa, etc.)

Upgrade behavior:
  The installer overwrites existing .rfa files at C:\SSG FP\Revit Families\
  (ignoreversion flag). If a user has customized a family in place, their
  changes will be lost on re-install. Users who customize should copy the
  family out of that folder first.

Uninstall behavior:
  On uninstall, the user is prompted (default No) whether to also remove
  C:\SSG FP\Revit Families\. This protects custom families the user may
  have added to that folder themselves.

Families that need to be authored by hand
=========================================
A few commands look up specific family names that we don't yet ship.
Until you author and drop them into a year folder under Generic Models
or Annotations, the matching command will run but skip the visual
flagging step.

  -Model Check - Upright Sprinkler Deflector Distance.rfa
    Used by:  DeflectorDistanceCheckCommand
    Category: Generic Annotation
    Spec:     Text-based annotation with a writable string parameter
              named "Model Check - Text". Optionally style by status
              (green for OK, red for EXCEEDS).

  -Hanger Gap Marker.rfa
    Used by:  HangerGapCheckCommand (new in v0.1.x)
    Category: Generic Model (so it is visible in plan AND 3D)
    Spec:     A simple eye-catching marker placed at the hanger
              location to visually flag it. Recommended geometry:
              a red sphere (~3" diameter) or vertical cylinder
              centered at the family origin. No required parameters.
              The command places the marker 6" above the hanger XY
              location, so design the family with its visible
              geometry around the origin (not above or below).

