using Autodesk.Revit.DB;

namespace SgRevitAddin.Commands.ViewsAndSheets.Models
{
    /// <summary>
    /// Thin wrapper around a Revit <see cref="Document"/> so it can be shown
    /// in a WPF ComboBox (ToString → file title).
    /// </summary>
    public class DocumentInfo
    {
        public Document Document { get; }
        public string Title { get; }

        public DocumentInfo(Document doc)
        {
            Document = doc;
            Title = string.IsNullOrEmpty(doc?.Title) ? "(untitled)" : doc.Title;
        }

        public override string ToString() => Title;
    }
}
