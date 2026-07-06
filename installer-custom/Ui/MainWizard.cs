using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SgSetup.Core;

namespace SgSetup.Ui
{
    /// <summary>The install wizard: Welcome → Select versions → Install → Done.</summary>
    public class MainWizard : WizardForm
    {
        private readonly string _payloadRoot;

        private Panel _pageWelcome, _pageVersions, _pageProgress, _pageDone;
        private readonly List<Panel> _pages = new List<Panel>();
        private int _index;

        private readonly Dictionary<string, CheckBox> _versionChecks = new Dictionary<string, CheckBox>();
        private CheckBox _chkFamilies;
        private ProgressBar _bar;
        private Label _progressStatus;
        private Label _doneHeading, _doneBody;
        private bool _installFailed;
        private string _installError;

        public MainWizard(string payloadRoot)
        {
            _payloadRoot = payloadRoot;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);   // builds chrome + ContentHost + footer

            _pageWelcome = BuildWelcome();
            _pageVersions = BuildVersions();
            _pageProgress = BuildProgress();
            _pageDone = BuildDone();
            _pages.AddRange(new[] { _pageWelcome, _pageVersions, _pageProgress, _pageDone });
            foreach (var p in _pages) { p.Visible = false; ContentHost.Controls.Add(p); }

            BackButton.Click += (s, ev) => GoTo(_index - 1);
            NextButton.Click += (s, ev) => OnNext();
            CancelButton2.Click += (s, ev) => Close();

            GoTo(0);
        }

        // ── Page construction ──
        private Panel NewPage()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(Dpi(28), Dpi(22), Dpi(28), Dpi(16)) };
        }

        private int Dpi(int v) => (int)Math.Round(v * DeviceDpi / 96f);

        private Label Heading(string text)
            => new Label { Text = text, AutoSize = true, ForeColor = SgBlue,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold), Location = new Point(Dpi(28), Dpi(22)) };

        private Label Body(string text, int top, int width)
            => new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0),
                ForeColor = Color.FromArgb(0x33, 0x3A, 0x40),
                Font = new Font("Segoe UI", 9.75f), Location = new Point(Dpi(28), top) };

        private Panel BuildWelcome()
        {
            var p = NewPage();
            p.Controls.Add(Heading("Welcome"));
            p.Controls.Add(Body(
                "This installs the SG Revit Addin for Autodesk Revit — a suite of tools for sprinkler " +
                "layout, hanger placement, pipe routing, annotation, coordination and model checking.",
                Dpi(56), Dpi(584)));

            p.Controls.Add(new Label
            {
                Text = "Please close all running Revit sessions before continuing.",
                AutoSize = true,
                ForeColor = Color.FromArgb(0xC0, 0x28, 0x28),
                Font = new Font("Segoe UI Semibold", 9.75f, FontStyle.Bold),
                Location = new Point(Dpi(28), Dpi(102))
            });

            int gx = Dpi(28), gap = Dpi(16), cardH = Dpi(84);
            int colW = (Dpi(584) - gap) / 2;
            int rightX = gx + colW + gap;
            int top1 = Dpi(140), top2 = top1 + cardH + Dpi(12);

            p.Controls.Add(FeatureCard("feat-hangers.png", "Hangers",
                "Trapeze, seismic & rod hangers placed and synced automatically.", gx, top1, colW, cardH));
            p.Controls.Add(FeatureCard("feat-routing.png", "Pipe Routing",
                "Branch lines, drops & flex pipe driven from your sprinkler layout.", rightX, top1, colW, cardH));
            p.Controls.Add(FeatureCard("feat-annotation.png", "Annotation",
                "Pipe tags, elevations, sleeves & scale bars in a single click.", gx, top2, colW, cardH));
            p.Controls.Add(FeatureCard("feat-coordination.png", "Coordination",
                "Color-coding, clash cleanup & Trimble point export.", rightX, top2, colW, cardH));
            return p;
        }

        /// <summary>A rounded feature tile: icon on the left, bold title + wrapped blurb.</summary>
        private CardPanel FeatureCard(string iconSuffix, string title, string desc, int x, int y, int w, int h)
        {
            var card = new CardPanel { Location = new Point(x, y), Size = new Size(w, h), Radius = Dpi(12) };
            Color fill = card.Fill;
            int pad = Dpi(14);
            int icon = Dpi(34);

            var pic = new PictureBox
            {
                Location = new Point(pad, pad + Dpi(2)),
                Size = new Size(icon, icon),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = fill,
                Image = LoadEmbedded(iconSuffix)
            };
            card.Controls.Add(pic);

            int tx = pad + icon + Dpi(12);
            card.Controls.Add(new Label
            {
                Text = title, AutoSize = true, ForeColor = SgBlue, BackColor = fill,
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                Location = new Point(tx, pad - Dpi(1))
            });
            card.Controls.Add(new Label
            {
                Text = desc, AutoSize = false, ForeColor = Color.FromArgb(0x4A, 0x53, 0x5B), BackColor = fill,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(tx, pad + Dpi(20)),
                Size = new Size(w - tx - pad, h - (pad + Dpi(20)) - Dpi(6))
            });
            return card;
        }

        private static Image LoadEmbedded(string suffix)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using (var s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (var tmp = Image.FromStream(s)) return new Bitmap(tmp);
                }
            }
            catch { return null; }
        }

        private Panel BuildVersions()
        {
            var p = NewPage();
            p.Controls.Add(Heading("Select Revit Versions"));
            p.Controls.Add(Body("Detected versions are pre-checked. You can also install for versions that aren't installed yet.",
                Dpi(58), Dpi(560)));

            int y = Dpi(96);
            foreach (var year in RevitDetect.Years)
            {
                bool found = RevitDetect.IsInstalled(year);
                var cb = new CheckBox
                {
                    Text = found ? $"Revit {year}   (detected)" : $"Revit {year}",
                    Checked = found,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10.5f),
                    Location = new Point(Dpi(36), y)
                };
                _versionChecks[year] = cb;
                p.Controls.Add(cb);
                y += Dpi(30);
            }

            y += Dpi(10);
            _chkFamilies = new CheckBox
            {
                Text = "Also install the shared SG family library  (to C:\\SG\\Revit Families)",
                Checked = true,
                AutoSize = true,
                Font = new Font("Segoe UI", 9.75f),
                ForeColor = Color.FromArgb(0x33, 0x3A, 0x40),
                Location = new Point(Dpi(36), y)
            };
            p.Controls.Add(_chkFamilies);
            return p;
        }

        private Panel BuildProgress()
        {
            var p = NewPage();
            p.Controls.Add(Heading("Installing"));
            _progressStatus = Body("Preparing…", Dpi(70), Dpi(560));
            _progressStatus.Size = new Size(Dpi(560), Dpi(24));
            p.Controls.Add(_progressStatus);
            _bar = new ProgressBar
            {
                Location = new Point(Dpi(28), Dpi(104)),
                Size = new Size(Dpi(560), Dpi(18)),
                Style = ProgressBarStyle.Continuous,
                Minimum = 0, Maximum = 100
            };
            p.Controls.Add(_bar);
            return p;
        }

        private Panel BuildDone()
        {
            var p = NewPage();
            _doneHeading = Heading("Installation Complete");
            p.Controls.Add(_doneHeading);
            _doneBody = Body("", Dpi(60), Dpi(560));
            p.Controls.Add(_doneBody);
            return p;
        }

        // ── Navigation ──
        private void GoTo(int index)
        {
            if (index < 0 || index >= _pages.Count) return;
            _index = index;
            for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index;

            bool onVersions = index == 1;
            bool onProgress = index == 2;
            bool onDone = index == 3;

            BackButton.Visible = index == 1;              // only Welcome↔Versions
            BackButton.Enabled = !onProgress;
            CancelButton2.Visible = !onDone;
            CancelButton2.Enabled = !onProgress;

            NextButton.Enabled = !onProgress;
            NextButton.Text = onVersions ? "Install" : onDone ? "Finish" : "Next";
            SetNextAccent(true);

            if (onProgress) StartInstall();
        }

        private void OnNext()
        {
            switch (_index)
            {
                case 0: GoTo(1); break;
                case 1:
                    if (!_versionChecks.Values.Any(c => c.Checked))
                    {
                        MessageBox.Show(this, "Select at least one Revit version.", "SG Revit Addin Setup",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    GoTo(2);
                    break;
                case 3: Close(); break;
            }
        }

        private void StartInstall()
        {
            var years = _versionChecks.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
            bool families = _chkFamilies.Checked;

            var engine = new InstallEngine(_payloadRoot)
            {
                Progress = (pct, msg) =>
                {
                    if (IsDisposed) return;
                    try { BeginInvoke(new Action(() => { _bar.Value = Math.Max(0, Math.Min(100, pct)); _progressStatus.Text = msg; })); }
                    catch { }
                }
            };

            var t = new Thread(() =>
            {
                try { engine.Install(years, families); }
                catch (Exception ex) { _installFailed = true; _installError = ex.Message; }
                finally
                {
                    try { if (!IsDisposed) BeginInvoke(new Action(FinishInstall)); } catch { }
                }
            }) { IsBackground = true };
            t.Start();
        }

        private void FinishInstall()
        {
            if (_installFailed)
            {
                _doneHeading.Text = "Installation Failed";
                _doneHeading.ForeColor = Color.FromArgb(0xB0, 0x2A, 0x2A);
                _doneBody.Text = "Something went wrong:\n\n" + _installError +
                    "\n\nMake sure Revit is closed and try again.";
            }
            else
            {
                _doneHeading.Text = "Installation Complete";
                _doneHeading.ForeColor = SgBlue;
                _doneBody.Text = "SG Revit Addin has been installed.\n\n" +
                    "Start Revit and look for the SG tab on the ribbon.";
            }
            GoTo(3);
        }
    }
}
