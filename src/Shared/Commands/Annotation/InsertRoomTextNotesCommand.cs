using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SSG_FP_Suite.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Places TextNotes for linked room names and numbers in the active view.
    /// Room names are stacked with each word on a new line, followed by the
    /// room number on the last line.
    ///
    /// Migrated from: "AutoInsert - Text Notes - Room Names and Numbers.dyn"
    ///
    /// WORKFLOW:
    ///   1. Dialog: select linked model, level, text note type, delete option
    ///   2. Collect rooms from linked model filtered by level
    ///   3. Filter rooms to those within the active view's crop region
    ///   4. Optionally delete existing text notes of the selected type
    ///   5. Create TextNotes at room center points with stacked name + number
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class InsertRoomTextNotesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            // ── Validate active view ──
            if (activeView is ViewSheet || activeView is View3D)
            {
                TaskDialog.Show("Insert Room Text Notes",
                    "This command must be run from a plan view (not a sheet or 3D view).");
                return Result.Failed;
            }

            // ── Collect Revit link instances ──
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null)
                .ToList();

            if (links.Count == 0)
            {
                TaskDialog.Show("Insert Room Text Notes",
                    "No loaded Revit links found in the project.");
                return Result.Failed;
            }

            var linkNames = links.Select(l => l.Name).ToList();

            // ── Collect rooms from all links to build level list ──
            // Use first link initially; dialog will let user pick
            var allLevelNames = new List<string>();
            foreach (var link in links)
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;

                var rooms = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area > 0)
                    .ToList();

                foreach (var room in rooms)
                {
                    string levelName = room.Level?.Name;
                    if (!string.IsNullOrEmpty(levelName) && !allLevelNames.Contains(levelName))
                        allLevelNames.Add(levelName);
                }
            }

            allLevelNames.Sort(StringComparer.OrdinalIgnoreCase);

            // ── Collect TextNoteTypes ──
            var textNoteTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .OrderBy(t => t.Name)
                .ToList();

            var textNoteTypeNames = textNoteTypes.Select(t => t.Name).ToList();

            // ── Show dialog ──
            using (var dlg = new InsertRoomTextNotesDialog(linkNames, allLevelNames, textNoteTypeNames))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;

                RevitLinkInstance selectedLink = links[dlg.SelectedLinkIndex];
                string selectedLevel = dlg.SelectedLevelName;
                TextNoteType selectedTextType = textNoteTypes[dlg.SelectedTextNoteTypeIndex];
                bool deleteExisting = dlg.DeleteExisting;

                Document linkDoc = selectedLink.GetLinkDocument();
                Transform linkTransform = selectedLink.GetTotalTransform();

                // ── Get rooms from selected link, filtered by level ──
                var rooms = new FilteredElementCollector(linkDoc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area > 0)
                    .Where(r => r.Level != null &&
                           r.Level.Name.Equals(selectedLevel, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (rooms.Count == 0)
                {
                    TaskDialog.Show("Insert Room Text Notes",
                        $"No placed rooms found on level '{selectedLevel}' in the linked model.");
                    return Result.Failed;
                }

                // ── Get crop region outline for containment check ──
                CurveLoop cropLoop = null;
                if (activeView.CropBoxActive)
                {
                    try
                    {
                        ViewCropRegionShapeManager cropMgr = activeView.GetCropRegionShapeManager();
                        IList<CurveLoop> loops = cropMgr.GetCropShape();
                        if (loops != null && loops.Count > 0)
                            cropLoop = loops[0];
                    }
                    catch { /* no crop shape available */ }
                }

                // Fallback: use crop box bounding box
                BoundingBoxXYZ cropBox = activeView.CropBox;

                // ── Filter rooms to those within the active view ──
                var roomsInView = new List<(Autodesk.Revit.DB.Architecture.Room room, XYZ center)>();
                foreach (var room in rooms)
                {
                    // Get room location point and transform to host coordinates
                    LocationPoint locPt = room.Location as LocationPoint;
                    if (locPt == null) continue;

                    XYZ roomCenter = linkTransform.OfPoint(locPt.Point);
                    // Flatten to Z=0 for plan view placement
                    XYZ flatCenter = new XYZ(roomCenter.X, roomCenter.Y, 0);

                    if (IsPointInView(flatCenter, cropLoop, cropBox, activeView))
                        roomsInView.Add((room, flatCenter));
                }

                if (roomsInView.Count == 0)
                {
                    TaskDialog.Show("Insert Room Text Notes",
                        $"No rooms on level '{selectedLevel}' are within the active view's crop region.");
                    return Result.Failed;
                }

                // ── Execute in transaction ──
                int deletedCount = 0;
                int placedCount = 0;

                using (var tw = new TransactionWrapper(doc, "Insert Room Text Notes"))
                {
                    try
                    {
                        // Delete existing text notes of the selected type if requested
                        if (deleteExisting)
                        {
                            var existingNotes = new FilteredElementCollector(doc, activeView.Id)
                                .OfClass(typeof(TextNote))
                                .Cast<TextNote>()
                                .Where(tn => tn.TextNoteType.Id == selectedTextType.Id)
                                .ToList();

                            foreach (var note in existingNotes)
                            {
                                doc.Delete(note.Id);
                                deletedCount++;
                            }
                        }

                        // Create text notes for each room
                        foreach (var (room, center) in roomsInView)
                        {
                            string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                            string roomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";

                            if (string.IsNullOrWhiteSpace(roomName) && string.IsNullOrWhiteSpace(roomNumber))
                                continue;

                            // Stack each word of the room name on a new line, then room number
                            string stackedText = BuildStackedText(roomName, roomNumber);

                            // Create TextNote at room center
                            TextNoteOptions opts = new TextNoteOptions
                            {
                                TypeId = selectedTextType.Id,
                                HorizontalAlignment = HorizontalTextAlignment.Center
                            };

                            TextNote.Create(doc, activeView.Id, center, stackedText, opts);
                            placedCount++;
                        }

                        tw.Commit();
                    }
                    catch (Exception ex)
                    {
                        message = ex.Message;
                        return Result.Failed;
                    }
                }

                // ── Summary ──
                string summary = $"Placed {placedCount} text note{(placedCount != 1 ? "s" : "")} " +
                                 $"for rooms on level '{selectedLevel}'.";
                if (deleteExisting && deletedCount > 0)
                    summary += $"\nDeleted {deletedCount} existing text note{(deletedCount != 1 ? "s" : "")} first.";

                TaskDialog.Show("Insert Room Text Notes", summary);
                return Result.Succeeded;
            }
        }

        /// <summary>
        /// Builds the stacked text: each word of the room name on its own line,
        /// followed by the room number on the last line.
        /// Example: "ELECTRICAL ROOM" + "101" → "ELECTRICAL\nROOM\n101"
        /// </summary>
        private string BuildStackedText(string roomName, string roomNumber)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(roomName))
            {
                string[] words = roomName.Trim().Split(
                    new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                lines.AddRange(words);
            }

            if (!string.IsNullOrWhiteSpace(roomNumber))
                lines.Add(roomNumber.Trim());

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Checks whether a point falls within the active view's visible region.
        /// Uses crop shape polygon if available, otherwise falls back to crop box bounds.
        /// </summary>
        private bool IsPointInView(XYZ point, CurveLoop cropLoop, BoundingBoxXYZ cropBox, View view)
        {
            if (cropLoop != null)
                return IsPointInCropLoop(point, cropLoop);

            if (cropBox != null)
            {
                // Transform point to crop box coordinate system
                Transform inverse = cropBox.Transform.Inverse;
                XYZ localPt = inverse.OfPoint(point);

                return localPt.X >= cropBox.Min.X && localPt.X <= cropBox.Max.X &&
                       localPt.Y >= cropBox.Min.Y && localPt.Y <= cropBox.Max.Y;
            }

            // No crop — include all rooms
            return true;
        }

        /// <summary>
        /// Point-in-polygon test using ray casting algorithm against the crop shape.
        /// Projects to XY plane for plan view testing.
        /// </summary>
        private bool IsPointInCropLoop(XYZ point, CurveLoop loop)
        {
            // Extract polygon vertices from curve loop
            var vertices = new List<XYZ>();
            foreach (Curve curve in loop)
                vertices.Add(curve.GetEndPoint(0));

            if (vertices.Count < 3)
                return false;

            // Ray casting: count intersections of horizontal ray from point going +X
            int crossings = 0;
            int n = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                XYZ v1 = vertices[i];
                XYZ v2 = vertices[(i + 1) % n];

                // Check if the ray from point going +X crosses this edge
                if ((v1.Y <= point.Y && v2.Y > point.Y) ||
                    (v2.Y <= point.Y && v1.Y > point.Y))
                {
                    double xIntersect = v1.X + (point.Y - v1.Y) / (v2.Y - v1.Y) * (v2.X - v1.X);
                    if (point.X < xIntersect)
                        crossings++;
                }
            }

            return (crossings % 2) == 1;
        }
    }
}
