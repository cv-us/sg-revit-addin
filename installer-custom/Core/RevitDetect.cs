using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace SgSetup.Core
{
    /// <summary>Revit version detection + the folder each add-in payload targets.</summary>
    public static class RevitDetect
    {
        /// <summary>The Revit versions this add-in supports.</summary>
        public static readonly string[] Years = { "2023", "2024", "2025", "2026" };

        /// <summary>%ProgramData%\Autodesk\Revit\Addins\{year} — the all-users add-in folder.</summary>
        public static string AddinsFolder(string year) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk", "Revit", "Addins", year);

        /// <summary>%APPDATA%\Autodesk\Revit\Addins\{year} — per-user (a deploy copy would shadow us).</summary>
        public static string UserAddinsFolder(string year) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk", "Revit", "Addins", year);

        /// <summary>True when Revit {year} looks installed (registered or its Addins folder exists).</summary>
        public static bool IsInstalled(string year)
        {
            try
            {
                using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey($@"SOFTWARE\Autodesk\Revit\Autodesk Revit {year}"))
                    if (k != null) return true;
            }
            catch { }
            try { return Directory.Exists(AddinsFolder(year)); }
            catch { return false; }
        }

        /// <summary>SgRevit24 covers 2023/2024 (.NET 4.8); SgRevit25 covers 2025/2026 (.NET 8).</summary>
        public static bool IsDotNet8(string year) => year == "2025" || year == "2026";

        public static IEnumerable<string> AllYears() => Years;
    }
}
