using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Utils
{
    /// <summary>
    /// A selection filter that restricts user picks to a specific category.
    /// Used by SelectionHelpers.PickElementsByCategory().
    ///
    /// You can create more specialized filters by copying this pattern.
    /// For example, a filter that only allows pipes of a certain size,
    /// or only sprinklers of a specific family.
    /// </summary>
    public class CategorySelectionFilter : ISelectionFilter
    {
        private readonly BuiltInCategory _category;

        public CategorySelectionFilter(BuiltInCategory category)
        {
            _category = category;
        }

        /// <summary>
        /// Returns true if the element matches the target category.
        /// Revit calls this for every element the user hovers over.
        /// </summary>
        public bool AllowElement(Element elem)
        {
            return elem.Category?.Id.IntegerValue == (int)_category;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    /// <summary>
    /// Helpers for prompting the user to select elements in the Revit view.
    ///
    /// USAGE:
    ///   // Let user pick only pipes (other elements can't be selected)
    ///   IList&lt;Element&gt; pipes = SelectionHelpers.PickElementsByCategory(
    ///       uidoc,
    ///       BuiltInCategory.OST_PipeCurves,
    ///       "Select pipes for cut list");
    ///
    ///   // Let user pick only sprinklers
    ///   IList&lt;Element&gt; heads = SelectionHelpers.PickElementsByCategory(
    ///       uidoc,
    ///       BuiltInCategory.OST_Sprinklers,
    ///       "Select sprinkler heads to check");
    ///
    /// COMMON CATEGORIES for fire protection:
    ///   BuiltInCategory.OST_PipeCurves      → Pipes
    ///   BuiltInCategory.OST_PipeFitting      → Fittings (tees, elbows, etc.)
    ///   BuiltInCategory.OST_Sprinklers       → Sprinkler heads
    ///   BuiltInCategory.OST_PipeAccessory    → Pipe accessories (often includes hangers)
    ///   BuiltInCategory.OST_FlexPipeCurves   → Flex pipes / drops
    /// </summary>
    public static class SelectionHelpers
    {
        /// <summary>
        /// Prompt the user to select multiple elements of a specific category.
        /// The user clicks elements in the view and presses Finish when done.
        /// Only elements matching the category can be selected (others are ignored).
        /// </summary>
        /// <param name="uidoc">The active UI document</param>
        /// <param name="category">Which element category to allow (e.g., OST_PipeCurves)</param>
        /// <param name="prompt">Message shown in Revit's status bar (e.g., "Select pipes")</param>
        /// <returns>List of selected elements</returns>
        public static IList<Element> PickElementsByCategory(UIDocument uidoc, BuiltInCategory category, string prompt)
        {
            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element,
                new CategorySelectionFilter(category),
                prompt);

            return refs.Select(r => uidoc.Document.GetElement(r)).ToList();
        }
    }
}
