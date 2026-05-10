using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Helpers for working with Revit Pipe elements.
    ///
    /// All length/diameter values are returned in Revit's internal units (feet).
    /// Use UnitConversion.FeetToInches() to convert for display.
    ///
    /// USAGE:
    ///   Pipe pipe = ...; // from ElementFilters or user selection
    ///
    ///   double dia = PipeHelpers.GetPipeDiameter(pipe);         // e.g., 0.0833 (1")
    ///   double len = PipeHelpers.GetPipeLength(pipe);           // e.g., 10.5 (10'-6")
    ///   XYZ start  = PipeHelpers.GetPipeStartPoint(pipe);       // start XYZ coordinate
    ///   XYZ end    = PipeHelpers.GetPipeEndPoint(pipe);         // end XYZ coordinate
    ///   string sys = PipeHelpers.GetSystemTypeName(pipe);       // "Fire Protection Wet"
    ///
    ///   // To display in inches:
    ///   double diaInches = UnitConversion.FeetToInches(dia);    // → 1.0
    /// </summary>
    public static class PipeHelpers
    {
        /// <summary>
        /// Get the pipe's nominal diameter in feet (internal units).
        /// Convert with UnitConversion.FeetToInches() for display.
        /// </summary>
        public static double GetPipeDiameter(Pipe pipe)
        {
            return pipe.Diameter;
        }

        /// <summary>
        /// Get the pipe's length in feet (internal units).
        /// This is the centerline length of the pipe segment.
        /// </summary>
        public static double GetPipeLength(Pipe pipe)
        {
            return pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble();
        }

        /// <summary>
        /// Get the XYZ coordinate of the pipe's start point.
        /// Returns null if the pipe doesn't have a valid location curve.
        /// </summary>
        public static XYZ GetPipeStartPoint(Pipe pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc?.Curve.GetEndPoint(0);
        }

        /// <summary>
        /// Get the XYZ coordinate of the pipe's end point.
        /// Returns null if the pipe doesn't have a valid location curve.
        /// </summary>
        public static XYZ GetPipeEndPoint(Pipe pipe)
        {
            LocationCurve lc = pipe.Location as LocationCurve;
            return lc?.Curve.GetEndPoint(1);
        }

        /// <summary>
        /// Get the pipe's system type name (e.g., "Fire Protection Wet", "Fire Protection Dry").
        /// Returns empty string if not set.
        /// </summary>
        public static string GetSystemTypeName(Pipe pipe)
        {
            return pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? string.Empty;
        }
    }
}

