using Autodesk.Revit.DB;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Helpers for reading and writing Revit element parameters by name.
    ///
    /// Every element in Revit has parameters (Size, Length, System Type, Comments, etc.).
    /// Reading them requires checking the StorageType and calling the right method.
    /// These helpers handle that boilerplate.
    ///
    /// USAGE:
    ///   // Read any parameter as a string (handles all types)
    ///   string size = ParameterHelpers.GetParamValueAsString(pipe, "Diameter");
    ///   string system = ParameterHelpers.GetParamValueAsString(pipe, "System Type");
    ///
    ///   // Write parameters (inside a transaction!)
    ///   ParameterHelpers.SetParamValue(element, "Comments", "Placed by SG Revit Addin");
    ///   ParameterHelpers.SetParamValue(element, "Offset", 10.5);  // in feet!
    ///
    /// NOTE: Parameter names are CASE-SENSITIVE and must match exactly.
    /// If a parameter doesn't exist on the element, these methods return empty/false
    /// instead of throwing an error.
    /// </summary>
    public static class ParameterHelpers
    {
        /// <summary>
        /// Read any parameter's value as a string, regardless of its storage type.
        /// Returns empty string if the parameter doesn't exist.
        /// </summary>
        /// <param name="element">The Revit element (pipe, fitting, sprinkler, etc.)</param>
        /// <param name="paramName">Exact parameter name (case-sensitive)</param>
        public static string GetParamValueAsString(Element element, string paramName)
        {
            Parameter param = element.LookupParameter(paramName);
            if (param == null) return string.Empty;

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString() ?? string.Empty;
                case StorageType.Double:
                    // AsValueString() returns the formatted value with units (e.g., "1\"")
                    // AsDouble() returns the raw internal value in feet
                    return param.AsValueString() ?? param.AsDouble().ToString();
                case StorageType.Integer:
                    return param.AsInteger().ToString();
                case StorageType.ElementId:
                    return param.AsElementId().IntegerValue.ToString();
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Set a string parameter value. Returns false if parameter doesn't exist or is read-only.
        /// MUST be called inside a transaction (use TransactionWrapper).
        /// </summary>
        public static bool SetParamValue(Element element, string paramName, string value)
        {
            Parameter param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return false;
            return param.Set(value);
        }

        /// <summary>
        /// Set a numeric parameter value. The value should be in Revit's internal units (feet for lengths).
        /// Returns false if parameter doesn't exist or is read-only.
        /// MUST be called inside a transaction (use TransactionWrapper).
        /// </summary>
        public static bool SetParamValue(Element element, string paramName, double value)
        {
            Parameter param = element.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return false;
            return param.Set(value);
        }
        /// <summary>
        /// Get a pipe's diameter as a double (in feet, internal units).
        /// Tries the "Diameter" parameter, falls back to the built-in parameter.
        /// </summary>
        public static double GetPipeDiameterValue(Element pipe)
        {
            Parameter p = pipe.LookupParameter("Diameter");
            if (p != null && p.StorageType == StorageType.Double)
                return p.AsDouble();

            p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (p != null)
                return p.AsDouble();

            return 0;
        }
    }
}
