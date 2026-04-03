using System;
using System.IO;
using System.Text.Json;

namespace SSG_FP_Suite.Config
{
    /// <summary>
    /// Loads and saves plugin settings to a JSON file in the user's AppData folder.
    ///
    /// Settings file location:
    ///   %AppData%\SSG_FP_Suite\settings.json
    ///
    /// HOW IT WORKS:
    ///   - On first run, no settings.json exists → returns defaults from PluginSettings
    ///   - When user changes settings → Save() writes to settings.json
    ///   - On next load → reads from settings.json
    ///
    /// USAGE IN COMMANDS:
    ///   // Read a setting
    ///   double spacing = SettingsManager.Current.DefaultSprinklerSpacing;
    ///   string exportPath = SettingsManager.Current.FabricationExportPath;
    ///
    ///   // Save updated settings (e.g., from a settings dialog)
    ///   var settings = SettingsManager.Current;
    ///   settings.DefaultHangerSpacing = 12.0;
    ///   SettingsManager.Save(settings);
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SSG_FP_Suite");

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

        private static PluginSettings _current;

        /// <summary>
        /// The current settings instance. Loads from disk on first access, then caches.
        /// </summary>
        public static PluginSettings Current => _current ?? (_current = Load());

        /// <summary>
        /// Load settings from the JSON file. Returns defaults if file doesn't exist.
        /// </summary>
        public static PluginSettings Load()
        {
            if (!File.Exists(SettingsFile))
                return new PluginSettings();

            string json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<PluginSettings>(json) ?? new PluginSettings();
        }

        /// <summary>
        /// Save settings to the JSON file. Creates the folder if it doesn't exist.
        /// </summary>
        public static void Save(PluginSettings settings)
        {
            Directory.CreateDirectory(SettingsFolder);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
            _current = settings;
        }
    }
}
