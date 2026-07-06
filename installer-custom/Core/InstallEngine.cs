using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace SgSetup.Core
{
    /// <summary>
    /// Does the actual install / uninstall — the logic ported from the Inno script:
    /// copies each selected version's DLLs + manifest into
    /// %ProgramData%\Autodesk\Revit\Addins\{year}\, bundles the shared families to
    /// C:\SG\Revit Families\, cleans stale/conflicting copies, and registers an
    /// Add/Remove Programs entry that points back at this exe for uninstall.
    ///
    /// Payload layout (folder next to the exe, or an extracted self-contained blob):
    ///   payload\SgRevit24\*        -> Addins\{2023,2024}\SgRevitAddin\
    ///   payload\SgRevit25\*        -> Addins\{2025,2026}\SgRevitAddin\
    ///   payload\SgRevit24.addin    -> Addins\{2023,2024}\SgRevit24.addin
    ///   payload\SgRevit25.addin    -> Addins\{2025,2026}\SgRevit25.addin
    ///   payload\Families\**        -> C:\SG\Revit Families\
    /// </summary>
    public class InstallEngine
    {
        public const string AppName = "SG Revit Addin";
        public static readonly string AppVersion = ReadVersion();
        public const string Publisher = "SG Fire Protection";

        private static string ReadVersion()
        {
            try
            {
                string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string v = System.Diagnostics.FileVersionInfo.GetVersionInfo(loc).ProductVersion;
                return string.IsNullOrWhiteSpace(v) ? "0.0.0" : v.Trim();
            }
            catch { return "0.0.0"; }
        }
        public const string SubFolder = "SgRevitAddin";
        public const string FamiliesDir = @"C:\SG\Revit Families";
        private const string UninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SgRevitAddin";
        private static readonly string SupportDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SgRevitAddin");

        private readonly string _payloadRoot;
        public Action<int, string> Progress;   // (percent, message)

        public InstallEngine(string payloadRoot) { _payloadRoot = payloadRoot; }

        private void Report(int pct, string msg) => Progress?.Invoke(pct, msg);

        /// <summary>Install the add-in for the given years. Throws on hard failure.</summary>
        public void Install(IList<string> years, bool installFamilies)
        {
            int total = years.Count + (installFamilies ? 1 : 0) + 1;
            int step = 0;

            foreach (var year in years)
            {
                Report(step++ * 100 / total, $"Installing for Revit {year}…");
                InstallVersion(year);
            }

            if (installFamilies)
            {
                Report(step++ * 100 / total, "Copying shared families…");
                CopyFamilies();
            }

            Report(step++ * 100 / total, "Registering uninstaller…");
            RegisterUninstall();

            Report(100, "Done.");
        }

        private void InstallVersion(string year)
        {
            string addinsFolder = RevitDetect.AddinsFolder(year);
            string sub = Path.Combine(addinsFolder, SubFolder);
            bool net8 = RevitDetect.IsDotNet8(year);
            string manifest = net8 ? "SgRevit25.addin" : "SgRevit24.addin";
            string payloadDll = net8 ? "SgRevit25" : "SgRevit24";

            // ── Clean stale / conflicting copies first (mirrors [InstallDelete]) ──
            SafeDeleteDir(sub);
            SafeDeleteFile(Path.Combine(addinsFolder, "SgRevit24.addin"));
            SafeDeleteFile(Path.Combine(addinsFolder, "SgRevit25.addin"));
            // legacy v0.1.x names
            SafeDeleteDir(Path.Combine(addinsFolder, "SSG-FP-Suite"));
            SafeDeleteFile(Path.Combine(addinsFolder, "SSG24.addin"));
            SafeDeleteFile(Path.Combine(addinsFolder, "SSG25.addin"));
            // per-user deploy copies share our ClientId and would shadow this install
            string userFolder = RevitDetect.UserAddinsFolder(year);
            SafeDeleteFile(Path.Combine(userFolder, "SgRevit24.addin"));
            SafeDeleteFile(Path.Combine(userFolder, "SgRevit24.dll"));
            SafeDeleteFile(Path.Combine(userFolder, "SgRevit25.addin"));
            SafeDeleteFile(Path.Combine(userFolder, "SgRevit25.dll"));

            // ── Copy payload ──
            Directory.CreateDirectory(sub);
            string dllSource = Path.Combine(_payloadRoot, payloadDll);
            CopyTree(dllSource, sub);

            string manifestSrc = Path.Combine(_payloadRoot, manifest);
            if (File.Exists(manifestSrc))
                File.Copy(manifestSrc, Path.Combine(addinsFolder, manifest), true);
        }

        private void CopyFamilies()
        {
            string src = Path.Combine(_payloadRoot, "Families");
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(FamiliesDir);
            CopyTree(src, FamiliesDir);
        }

        private void RegisterUninstall()
        {
            // Copy this exe to a persistent location so the uninstall string survives.
            Directory.CreateDirectory(SupportDir);
            string selfExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string persistExe = Path.Combine(SupportDir, "SgRevitAddinSetup.exe");
            try { File.Copy(selfExe, persistExe, true); } catch { persistExe = selfExe; }

            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                if (k == null) return;
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", AppVersion);
                k.SetValue("Publisher", Publisher);
                k.SetValue("DisplayIcon", persistExe);
                k.SetValue("InstallLocation", SupportDir);
                k.SetValue("UninstallString", $"\"{persistExe}\" /uninstall");
                k.SetValue("QuietUninstallString", $"\"{persistExe}\" /uninstall /silent");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 350_000, RegistryValueKind.DWord); // KB, rough
            }
        }

        /// <summary>Remove the add-in from every version + the registry entry.</summary>
        public void Uninstall(bool removeFamilies)
        {
            int i = 0;
            var years = RevitDetect.Years;
            foreach (var year in years)
            {
                Report(i++ * 100 / (years.Length + 2), $"Removing from Revit {year}…");
                string addinsFolder = RevitDetect.AddinsFolder(year);
                SafeDeleteDir(Path.Combine(addinsFolder, SubFolder));
                SafeDeleteFile(Path.Combine(addinsFolder, "SgRevit24.addin"));
                SafeDeleteFile(Path.Combine(addinsFolder, "SgRevit25.addin"));
                SafeDeleteDir(Path.Combine(addinsFolder, "SSG-FP-Suite"));
                SafeDeleteFile(Path.Combine(addinsFolder, "SSG24.addin"));
                SafeDeleteFile(Path.Combine(addinsFolder, "SSG25.addin"));
            }

            if (removeFamilies)
            {
                Report(90, "Removing families…");
                SafeDeleteDir(FamiliesDir);
            }

            Report(95, "Removing registry entry…");
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
            Report(100, "Uninstalled.");
        }

        // ── helpers ──
        private static void CopyTree(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(src, dst));
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(src, dst), true);
        }

        private static void SafeDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        private static void SafeDeleteFile(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }
}
