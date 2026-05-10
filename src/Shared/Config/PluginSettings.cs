namespace SgRevitAddin.Config
{
    /// <summary>
    /// All configurable settings for the SG Revit Addin plugin.
    ///
    /// Default values are set here. Users can override them via the settings dialog
    /// (or by editing the JSON file directly). SettingsManager handles loading/saving.
    ///
    /// TO ADD A NEW SETTING:
    ///   1. Add a property here with a sensible default value
    ///   2. Add the same key/value to defaults.json
    ///   3. Use it in your command: SettingsManager.Current.YourNewSetting
    /// </summary>
    public class PluginSettings
    {
        /// <summary>Company name used in annotations and exports</summary>
        public string CompanyName { get; set; } = "SG";

        /// <summary>Default pipe system type name (e.g., "Fire Protection Wet", "Fire Protection Dry")</summary>
        public string DefaultPipeSystem { get; set; } = "Fire Protection Wet";

        /// <summary>Default sprinkler spacing in feet (NFPA 13 light hazard max = 15')</summary>
        public double DefaultSprinklerSpacing { get; set; } = 15.0;

        /// <summary>Minimum pipe length in inches that can be fabricated (shorter = flag it)</summary>
        public double MinPipeFabLength { get; set; } = 6.0;

        /// <summary>Folder path for fabrication list exports (CSV, Excel, etc.)</summary>
        public string FabricationExportPath { get; set; } = "";

        /// <summary>Default hanger family name to use when placing hangers</summary>
        public string DefaultHangerFamily { get; set; } = "";

        /// <summary>Default hanger spacing in feet along pipe runs</summary>
        public double DefaultHangerSpacing { get; set; } = 10.0;
    }
}

