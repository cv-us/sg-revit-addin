using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace SgSetup.Core
{
    /// <summary>
    /// Locates the installer payload — the add-in DLLs, manifests and family
    /// library. A released single-file installer carries it as an embedded
    /// "SgSetup.payload.zip" resource and extracts it to a temp folder on demand;
    /// a dev build finds an uncompressed "payload" folder next to the exe.
    /// </summary>
    internal static class Payload
    {
        private const string ResourceSuffix = "payload.zip";

        /// <summary>The uncompressed payload folder next to the exe (dev builds), or null.</summary>
        public static string FindSibling()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            foreach (var candidate in new[]
                     {
                         Path.Combine(exeDir, "payload"),
                         Path.Combine(exeDir, "..", "payload"),
                     })
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            return null;
        }

        /// <summary>True when the payload is embedded in this exe (self-contained build).</summary>
        public static bool HasEmbedded() => ResourceName() != null;

        private static string ResourceName() =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Extract the embedded payload zip to a fresh temp folder and return its
        /// path, reporting progress 0..100 as it goes. Throws if there is no
        /// embedded payload. The caller should <see cref="TryDelete"/> the folder
        /// once the install finishes.
        /// </summary>
        public static string ExtractEmbedded(Action<int, string> progress)
        {
            string res = ResourceName()
                ?? throw new InvalidOperationException("This installer has no embedded payload.");

            string dir = Path.Combine(Path.GetTempPath(), "SgRevitAddinSetup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string dirFull = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;

            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream(res))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                int total = Math.Max(1, zip.Entries.Count), done = 0;
                progress?.Invoke(0, "Extracting installer files…");
                foreach (var entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                    if (!target.StartsWith(dirFull, StringComparison.OrdinalIgnoreCase))
                        continue;   // guard against zip-slip (paths escaping the temp dir)

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);   // directory entry
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }

                    done++;
                    if ((done & 15) == 0 || done == total)
                        progress?.Invoke((int)(done * 100L / total),
                            $"Extracting installer files… ({done}/{total})");
                }
            }
            return dir;
        }

        public static void TryDelete(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* temp folder — best effort */ }
        }
    }
}
