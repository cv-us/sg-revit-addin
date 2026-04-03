using Autodesk.Revit.DB;

namespace SSG_FP_Suite.Models
{
    /// <summary>
    /// Data transfer object (DTO) representing a sprinkler head.
    ///
    /// WHY THIS EXISTS:
    ///   Commands extract data from Revit elements into these simple objects.
    ///   This lets you pass sprinkler info to calculation logic, export functions,
    ///   or unit tests WITHOUT needing a running Revit instance.
    ///
    /// USAGE:
    ///   // In a command — convert a Revit FamilyInstance to a SprinklerData:
    ///   var data = new SprinklerData
    ///   {
    ///       ElementId = sprinkler.Id,
    ///       Location = (sprinkler.Location as LocationPoint).Point,
    ///       FamilyName = sprinkler.Symbol.Family.Name,
    ///       TypeName = sprinkler.Symbol.Name,
    ///       Orientation = ParameterHelpers.GetParamValueAsString(sprinkler, "Orientation"),
    ///       KFactor = 5.6
    ///   };
    /// </summary>
    public class SprinklerData
    {
        /// <summary>Revit ElementId — use this to find the element again in the model</summary>
        public ElementId ElementId { get; set; }

        /// <summary>XYZ location in the model (feet, internal units)</summary>
        public XYZ Location { get; set; }

        /// <summary>Family name (e.g., "Sprinkler - Pendent")</summary>
        public string FamilyName { get; set; }

        /// <summary>Type name (e.g., "1/2\" NPT - K5.6")</summary>
        public string TypeName { get; set; }

        /// <summary>Orientation: "Upright", "Pendent", or "Sidewall"</summary>
        public string Orientation { get; set; }

        /// <summary>K-factor (flow coefficient) — e.g., 5.6, 8.0, 11.2</summary>
        public double KFactor { get; set; }

        /// <summary>Coverage area in square feet assigned to this head</summary>
        public double CoverageArea { get; set; }
    }
}
