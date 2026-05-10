using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System.Collections.Generic;
using System.Linq;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Pre-built element queries using Revit's FilteredElementCollector.
    ///
    /// Instead of writing this in every command:
    ///   var pipes = new FilteredElementCollector(doc, viewId)
    ///       .OfClass(typeof(Pipe))
    ///       .Cast&lt;Pipe&gt;()
    ///       .ToList();
    ///
    /// Just call:
    ///   var pipes = ElementFilters.GetPipesInView(doc, viewId);
    ///
    /// ADD MORE FILTERS HERE as you need them. Common patterns:
    ///   - Filter by category (BuiltInCategory.OST_...)
    ///   - Filter by class (typeof(Pipe), typeof(FamilyInstance))
    ///   - Filter by view (only elements visible in a specific view)
    ///   - WhereElementIsNotElementType() to skip type definitions
    /// </summary>
    public static class ElementFilters
    {
        /// <summary>
        /// Get ALL pipes in the entire document (all views, all levels).
        /// Use GetPipesInView() if you only want pipes visible in a specific view.
        /// </summary>
        public static IList<Pipe> GetAllPipes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Pipe))
                .Cast<Pipe>()
                .ToList();
        }

        /// <summary>
        /// Get pipes visible in a specific view.
        /// Pass doc.ActiveView.Id to get pipes in the current view.
        /// </summary>
        public static IList<Pipe> GetPipesInView(Document doc, ElementId viewId)
        {
            return new FilteredElementCollector(doc, viewId)
                .OfClass(typeof(Pipe))
                .Cast<Pipe>()
                .ToList();
        }

        /// <summary>
        /// Get sprinkler head instances visible in a specific view.
        /// Returns FamilyInstance objects — use .Symbol.Family.Name to get the family name,
        /// or .Symbol.Name to get the type name.
        /// </summary>
        public static IList<FamilyInstance> GetSprinklersInView(Document doc, ElementId viewId)
        {
            return new FilteredElementCollector(doc, viewId)
                .OfCategory(BuiltInCategory.OST_Sprinklers)
                .WhereElementIsNotElementType()  // skip the type definitions, get only placed instances
                .Cast<FamilyInstance>()
                .ToList();
        }

        /// <summary>
        /// Get pipe fitting instances (tees, elbows, reducers, etc.) visible in a specific view.
        /// </summary>
        public static IList<FamilyInstance> GetPipeFittingsInView(Document doc, ElementId viewId)
        {
            return new FilteredElementCollector(doc, viewId)
                .OfCategory(BuiltInCategory.OST_PipeFitting)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();
        }

        // ──────────────────────────────────────────────────────────
        // ADD MORE FILTERS BELOW as you need them. Examples:
        // ──────────────────────────────────────────────────────────

        // public static IList<FamilyInstance> GetHangersInView(Document doc, ElementId viewId)
        // {
        //     return new FilteredElementCollector(doc, viewId)
        //         .OfCategory(BuiltInCategory.OST_PipeAccessory)  // hangers are often pipe accessories
        //         .WhereElementIsNotElementType()
        //         .Cast<FamilyInstance>()
        //         .ToList();
        // }

        // public static IList<Pipe> GetPipesBySystemType(Document doc, string systemTypeName)
        // {
        //     return GetAllPipes(doc)
        //         .Where(p => PipeHelpers.GetSystemTypeName(p) == systemTypeName)
        //         .ToList();
        // }
    }
}

