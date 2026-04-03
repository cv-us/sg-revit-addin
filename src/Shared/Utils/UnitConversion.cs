using System;

namespace SSG_FP_Suite.Utils
{
    /// <summary>
    /// Unit conversion helpers for Revit's internal unit system.
    ///
    /// IMPORTANT: Revit stores ALL lengths internally in FEET.
    /// This means:
    ///   - A 1" diameter pipe has Diameter = 0.08333 (1/12 of a foot)
    ///   - A 10'-6" pipe has Length = 10.5
    ///   - A room that's 15' x 20' has dimensions 15.0 x 20.0
    ///
    /// When DISPLAYING values to users (TaskDialog, export, etc.),
    /// always convert from feet to the user's expected unit.
    ///
    /// When READING user input, convert TO feet before passing to Revit.
    ///
    /// USAGE:
    ///   double pipeDiameter = pipe.Diameter;  // in feet (e.g., 0.08333)
    ///   double inches = UnitConversion.FeetToInches(pipeDiameter);  // → 1.0
    ///   string display = UnitConversion.FormatFeetInches(10.5);     // → "10'-6.00\""
    /// </summary>
    public static class UnitConversion
    {
        // ── Length conversions ──

        /// <summary>Convert feet to inches (multiply by 12)</summary>
        public static double FeetToInches(double feet) => feet * 12.0;

        /// <summary>Convert inches to feet (divide by 12)</summary>
        public static double InchesToFeet(double inches) => inches / 12.0;

        /// <summary>Convert feet to meters</summary>
        public static double FeetToMeters(double feet) => feet * 0.3048;

        /// <summary>Convert meters to feet</summary>
        public static double MetersToFeet(double meters) => meters / 0.3048;

        // ── Formatting ──

        /// <summary>
        /// Format a length in feet as a feet-inches string.
        /// Example: 10.5 → "10'-6.00\""
        /// Example: 0.08333 → "0'-1.00\""
        /// </summary>
        public static string FormatFeetInches(double feet)
        {
            int wholeFeet = (int)Math.Floor(feet);
            double remainingInches = (feet - wholeFeet) * 12.0;
            return $"{wholeFeet}'-{remainingInches:F2}\"";
        }
    }
}
