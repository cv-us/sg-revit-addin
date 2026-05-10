using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Helpers for working with Revit views.
    ///
    /// USAGE:
    ///   // When duplicating views, ensure the name is unique
    ///   string name = ViewHelpers.MakeUniqueViewName(doc, "FP - Level 1");
    ///   // Returns "FP - Level 1" if available, or "FP - Level 1 (1)", "(2)", etc.
    ///
    ///   // Read a view's parameter
    ///   string template = ViewHelpers.GetViewParamAsString(view, "View Template");
    /// </summary>
    public static class ViewHelpers
    {
        /// <summary>
        /// Generate a unique view name by appending (1), (2), etc. if the base name is taken.
        /// Revit doesn't allow duplicate view names, so always use this when creating/duplicating views.
        /// </summary>
        /// <param name="doc">The Revit document</param>
        /// <param name="baseName">The desired name (e.g., "FP - Level 1")</param>
        /// <returns>The base name if available, or baseName + " (N)" if taken</returns>
        public static string MakeUniqueViewName(Document doc, string baseName)
        {
            // Collect all existing view names in the document
            HashSet<string> existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Select(v => v.Name)
                .ToHashSet();

            if (!existingNames.Contains(baseName))
                return baseName;

            int counter = 1;
            string candidate;
            do
            {
                candidate = $"{baseName} ({counter})";
                counter++;
            } while (existingNames.Contains(candidate));

            return candidate;
        }

        /// <summary>
        /// Read a view parameter's value as a string.
        /// Tries AsString() first (for text parameters), then AsValueString() (for other types).
        /// Returns empty string if the parameter doesn't exist.
        /// </summary>
        public static string GetViewParamAsString(View view, string paramName)
        {
            Parameter param = view.LookupParameter(paramName);
            return param?.AsString() ?? param?.AsValueString() ?? string.Empty;
        }
    }
}
