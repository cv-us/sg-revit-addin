using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SSG_FP_Suite.Utils
{
    /// <summary>
    /// General-purpose shortcuts for common Revit API access patterns.
    ///
    /// These save you from typing the long chain every time:
    ///   commandData.Application.ActiveUIDocument.Document
    ///
    /// USAGE:
    ///   Document doc = RevitApiHelpers.GetActiveDocument(commandData);
    ///   UIDocument uidoc = RevitApiHelpers.GetActiveUIDocument(commandData);
    ///   View view = RevitApiHelpers.GetActiveView(doc);
    ///
    /// NOTE: Most commands already get uidoc and doc in their first two lines.
    /// These helpers are more useful in nested methods where you pass commandData around.
    /// </summary>
    public static class RevitApiHelpers
    {
        /// <summary>
        /// Get the active Revit Document (database level — for querying and modifying elements).
        /// </summary>
        public static Document GetActiveDocument(ExternalCommandData commandData)
        {
            return commandData.Application.ActiveUIDocument.Document;
        }

        /// <summary>
        /// Get the active UIDocument (UI level — for selection, view control, etc.).
        /// </summary>
        public static UIDocument GetActiveUIDocument(ExternalCommandData commandData)
        {
            return commandData.Application.ActiveUIDocument;
        }

        /// <summary>
        /// Get the currently active view in the document.
        /// Many operations (like element collection) can be scoped to a specific view.
        /// </summary>
        public static View GetActiveView(Document doc)
        {
            return doc.ActiveView;
        }
    }
}
