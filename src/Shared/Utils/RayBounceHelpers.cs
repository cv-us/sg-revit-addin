using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SSG_FP_Suite.Utils
{
    /// <summary>
    /// Helpers for performing ReferenceIntersector (raybounce) operations.
    /// Used to find the nearest structural element above a point — typically
    /// to determine rod length for pipe hangers.
    ///
    /// Wraps the native ReferenceIntersector API with linked model support.
    /// </summary>
    public static class RayBounceHelpers
    {
        /// <summary>
        /// Categories to search for when shooting rays upward to find structure.
        /// </summary>
        private static readonly BuiltInCategory[] StructuralCategories = new[]
        {
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Stairs
        };

        /// <summary>
        /// Result of a raybounce hit — the element found above the origin point.
        /// </summary>
        public class RayHitResult
        {
            /// <summary>The structural element that was hit.</summary>
            public Element HitElement { get; set; }

            /// <summary>The 3D point on the underside of the structural element.</summary>
            public XYZ HitPoint { get; set; }

            /// <summary>Distance from origin to hit point (feet).</summary>
            public double Distance { get; set; }

            /// <summary>Category name of the hit element (e.g., "Floors", "Structural Framing").</summary>
            public string CategoryName { get; set; }
        }

        /// <summary>
        /// Find or create a 3D view suitable for ReferenceIntersector operations.
        /// The view is named "3D-RayBounce" and has category visibility set to show
        /// only the relevant structural + pipe categories.
        /// </summary>
        public static View3D GetOrCreateRayBounceView(Document doc)
        {
            // Look for existing 3D-RayBounce view
            View3D existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == "3D-RayBounce");

            if (existing != null) return existing;

            // Create new 3D view
            ViewFamilyType vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null) return null;

            View3D view = View3D.CreateIsometric(doc, vft.Id);
            view.Name = "3D-RayBounce";

            ConfigureRayBounceView(doc, view);
            return view;
        }

        /// <summary>
        /// Configure category visibility on the raybounce view.
        /// Hides all model categories, then re-enables only the ones needed.
        /// </summary>
        private static void ConfigureRayBounceView(Document doc, View3D view)
        {
            // Categories to keep visible for raybounce
            var visibleCategories = new HashSet<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Roofs
            };

            // Hide everything except what we need
            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.CategoryType != CategoryType.Model) continue;
                try
                {
                    bool shouldBeVisible = false;
                    foreach (var bic in visibleCategories)
                    {
                        if (cat.Id.IntegerValue == (int)bic)
                        {
                            shouldBeVisible = true;
                            break;
                        }
                    }

                    if (!shouldBeVisible && view.CanCategoryBeHidden(cat.Id))
                        view.SetCategoryHidden(cat.Id, true);
                }
                catch { /* Some categories can't be modified */ }
            }

            // Set detail level to Fine
            view.DetailLevel = ViewDetailLevel.Fine;
        }

        /// <summary>
        /// Shoot a ray upward from the given point and find the nearest structural element.
        /// Returns null if nothing is found within maxDistance.
        /// </summary>
        /// <param name="doc">The document</param>
        /// <param name="view3D">A 3D view for the ReferenceIntersector</param>
        /// <param name="origin">The point to shoot from (typically the hanger location)</param>
        /// <param name="maxDistance">Maximum search distance in feet (default 50')</param>
        /// <returns>RayHitResult or null if nothing found</returns>
        public static RayHitResult ShootRayUpward(
            Document doc, View3D view3D, XYZ origin, double maxDistance = 50.0)
        {
            if (view3D == null) return null;

            XYZ direction = XYZ.BasisZ; // straight up

            // Build a multi-category filter for structural elements
            var catFilters = new List<ElementFilter>();
            foreach (var cat in StructuralCategories)
            {
                catFilters.Add(new ElementCategoryFilter(cat));
            }
            var multiCatFilter = new LogicalOrFilter(catFilters);

            var intersector = new ReferenceIntersector(
                multiCatFilter, FindReferenceTarget.Face, view3D);
            intersector.FindReferencesInRevitLinks = false;

            ReferenceWithContext refWithContext = intersector.FindNearest(origin, direction);
            if (refWithContext == null) return null;

            double distance = refWithContext.Proximity;
            if (distance > maxDistance) return null;

            Reference hitRef = refWithContext.GetReference();
            Element hitElement = doc.GetElement(hitRef.ElementId);
            if (hitElement == null) return null;

            XYZ hitPoint = hitRef.GlobalPoint;

            string categoryName = hitElement.Category?.Name ?? "Unknown";

            return new RayHitResult
            {
                HitElement = hitElement,
                HitPoint = hitPoint,
                Distance = distance,
                CategoryName = categoryName
            };
        }

        /// <summary>
        /// Shoot a ray upward and return just the distance to the nearest structure.
        /// Returns -1 if nothing found.
        /// </summary>
        public static double GetDistanceToStructureAbove(
            Document doc, View3D view3D, XYZ origin, double maxDistance = 50.0)
        {
            var hit = ShootRayUpward(doc, view3D, origin, maxDistance);
            return hit?.Distance ?? -1;
        }

        /// <summary>
        /// Map a structural category name to a hanger type code.
        /// Maps category to the corresponding user-configured type code.
        /// </summary>
        /// <param name="categoryName">The category name from the raybounce hit</param>
        /// <param name="roofCode">Type code for roof structures</param>
        /// <param name="floorCode">Type code for floor/deck structures</param>
        /// <param name="framingCode">Type code for structural framing (steel)</param>
        /// <param name="stairsCode">Type code for stairs</param>
        /// <returns>The appropriate type code string</returns>
        public static string GetTypeCodeForCategory(
            string categoryName,
            string roofCode, string floorCode, string framingCode, string stairsCode)
        {
            if (string.IsNullOrEmpty(categoryName))
                return framingCode; // default to framing

            string upper = categoryName.ToUpper();

            if (upper.Contains("STAIR"))
                return stairsCode;
            if (upper.Contains("STRUCTURAL") || upper.Contains("FRAMING"))
                return framingCode;
            if (upper.Contains("FLOOR"))
                return floorCode;
            if (upper.Contains("ROOF"))
                return roofCode;

            return framingCode; // default
        }
    }
}
