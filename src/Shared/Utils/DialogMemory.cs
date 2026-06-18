using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Lightweight per-dialog "remember last used" store. Each dialog keeps
    /// a small bag of string-keyed values that persist across command runs
    /// AND Revit restarts, so reopening a dialog restores whatever was last
    /// entered / checked.
    ///
    /// STORAGE:
    ///   %AppData%\SgRevitAddin\dialog-memory.json — a map of
    ///   { dialogKey → { field → value } }. Values are stored as strings;
    ///   typed accessors (bool/double/int) parse on read and format on write.
    ///
    /// USAGE (in a dialog):
    ///   // restore
    ///   chk.Checked = DialogMemory.GetBool("SyncRaybounce", "IncludeCAD", false);
    ///   txt.Text    = DialogMemory.Get("SyncRaybounce", "Floors", "05");
    ///   // persist (typically in the OK handler)
    ///   DialogMemory.SetBool("SyncRaybounce", "IncludeCAD", chk.Checked);
    ///   DialogMemory.Set("SyncRaybounce", "Floors", txt.Text);
    ///   DialogMemory.Flush();   // write once after setting everything
    ///
    /// All file IO is best-effort and swallows exceptions — a corrupt or
    /// unwritable settings file degrades to in-memory defaults, never a crash.
    /// </summary>
    public static class DialogMemory
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SgRevitAddin");

        private static readonly string FilePath = Path.Combine(Folder, "dialog-memory.json");

        // dialogKey → (field → value)
        private static Dictionary<string, Dictionary<string, string>> _store;
        private static bool _dirty;

        private static Dictionary<string, Dictionary<string, string>> Store
        {
            get
            {
                if (_store == null) _store = Load();
                return _store;
            }
        }

        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                    if (data != null) return data;
                }
            }
            catch { /* ignore — fall through to empty */ }
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Writes pending changes to disk. Safe to call always; no-op if nothing changed.</summary>
        public static void Flush()
        {
            if (!_dirty || _store == null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
                _dirty = false;
            }
            catch { /* ignore — keep in-memory state */ }
        }

        // ── String ──
        public static string Get(string dialogKey, string field, string fallback = "")
        {
            if (Store.TryGetValue(dialogKey, out var bag) && bag.TryGetValue(field, out var v) && v != null)
                return v;
            return fallback;
        }

        public static void Set(string dialogKey, string field, string value)
        {
            if (!Store.TryGetValue(dialogKey, out var bag))
            {
                bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Store[dialogKey] = bag;
            }
            bag[field] = value ?? "";
            _dirty = true;
        }

        // ── Bool ──
        public static bool GetBool(string dialogKey, string field, bool fallback)
        {
            string s = Get(dialogKey, field, null);
            return bool.TryParse(s, out bool b) ? b : fallback;
        }

        public static void SetBool(string dialogKey, string field, bool value)
            => Set(dialogKey, field, value ? "true" : "false");

        // ── Double ──
        public static double GetDouble(string dialogKey, string field, double fallback)
        {
            string s = Get(dialogKey, field, null);
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : fallback;
        }

        public static void SetDouble(string dialogKey, string field, double value)
            => Set(dialogKey, field, value.ToString(CultureInfo.InvariantCulture));

        // ── Int ──
        public static int GetInt(string dialogKey, string field, int fallback)
        {
            string s = Get(dialogKey, field, null);
            return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int i) ? i : fallback;
        }

        public static void SetInt(string dialogKey, string field, int value)
            => Set(dialogKey, field, value.ToString(CultureInfo.InvariantCulture));
    }
}
