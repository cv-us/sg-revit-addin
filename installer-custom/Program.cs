using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SgSetup.Core;
using SgSetup.Ui;

namespace SgSetup
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)
                                        || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
            bool silent = args.Any(a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                                     || a.Equals("--silent", StringComparison.OrdinalIgnoreCase));

            if (uninstall)
                return RunUninstall(silent);

            return RunInstall();
        }

        private static int RunInstall()
        {
            string payload = ResolvePayload();
            if (payload == null)
            {
                MessageBox.Show(
                    "Installer payload not found.\n\nExpected a \"payload\" folder next to the setup, or a " +
                    "self-contained package. This build looks incomplete.",
                    "SG Revit Addin Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            using (var wiz = new MainWizard(payload))
                Application.Run(wiz);
            return 0;
        }

        private static int RunUninstall(bool silent)
        {
            if (!silent)
            {
                var res = MessageBox.Show(
                    "Remove SG Revit Addin from all installed Revit versions?",
                    "SG Revit Addin — Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res != DialogResult.Yes) return 0;
            }

            bool removeFamilies = false;
            if (!silent && Directory.Exists(InstallEngine.FamiliesDir))
            {
                var res = MessageBox.Show(
                    "Also remove the shared family library at:\n\n" + InstallEngine.FamiliesDir +
                    "\n\nChoose No to keep any custom families you added there.",
                    "SG Revit Addin — Uninstall", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                removeFamilies = res == DialogResult.Yes;
            }

            try
            {
                new InstallEngine(null).Uninstall(removeFamilies);
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show("Uninstall error:\n\n" + ex.Message, "SG Revit Addin — Uninstall",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            if (!silent)
                MessageBox.Show("SG Revit Addin has been removed.", "SG Revit Addin — Uninstall",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        /// <summary>Find the payload folder (next to the exe, or one level up during dev).</summary>
        private static string ResolvePayload()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            foreach (var candidate in new[]
                     {
                         Path.Combine(exeDir, "payload"),
                         Path.Combine(exeDir, "..", "payload"),
                     })
            {
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }
    }
}
