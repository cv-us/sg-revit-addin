using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Seismic Braces on Welded Mains command.
    ///
    /// Collects brace type, families, spacing, orientation, and linked model settings.
    /// </summary>
    public class SeismicBracesDialog : Form
    {
        // ── Results ──
        public int BraceMode { get; private set; }              // 0=Lateral, 1=Longitudinal, 2=Both
        public int LateralFamilyIndex { get; private set; }
        public double LateralSpacingFt { get; private set; } = 40;
        public double LateralDistFromEndFt { get; private set; } = 6;
        public int LongitudinalFamilyIndex { get; private set; }
        public double LongitudinalSpacingFt { get; private set; } = 80;
        public int LateralOrientation { get; private set; }     // 0=Left/Above, 1=Right/Below
        public int LongitudinalOrientation { get; private set; } // 0=Right/Upward, 1=Left/Downward
        public int ArchLinkIndex { get; private set; }

        // ── Controls ──
        private RadioButton rbLateral, rbLongitudinal, rbBoth;
        private ComboBox cboLatFamily, cboLongFamily;
        private NumericUpDown nudLatSpacing, nudLatDistEnd, nudLongSpacing;
        private RadioButton rbLatLeft, rbLatRight;
        private RadioButton rbLongRight, rbLongLeft;
        private ComboBox cboArchLink;
        private GroupBox grpLateral, grpLongitudinal;

        private readonly List<string> _lateralFamilies;
        private readonly List<string> _longitudinalFamilies;
        private readonly List<string> _linkNames;

        public SeismicBracesDialog(
            List<string> lateralFamilies,
            List<string> longitudinalFamilies,
            List<string> linkNames)
        {
            _lateralFamilies = lateralFamilies;
            _longitudinalFamilies = longitudinalFamilies;
            _linkNames = linkNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Seismic Braces on Welded Mains";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 590);
            AutoScroll = true;

            int margin = 12;
            int y = margin;

            // ── Brace Type ──
            var grpType = new GroupBox
            {
                Text = "Seismic Brace Types to Insert",
                Location = new Point(margin, y),
                Size = new Size(474, 50)
            };
            rbLateral = new RadioButton { Text = "Lateral Only", Location = new Point(10, 20), Size = new Size(120, 20) };
            rbLongitudinal = new RadioButton { Text = "Longitudinal Only", Location = new Point(140, 20), Size = new Size(140, 20) };
            rbBoth = new RadioButton { Text = "Both Lateral && Longitudinal", Location = new Point(290, 20), Size = new Size(180, 20), Checked = true };
            rbLateral.CheckedChanged += (s, e) => UpdatePanelVisibility();
            rbLongitudinal.CheckedChanged += (s, e) => UpdatePanelVisibility();
            rbBoth.CheckedChanged += (s, e) => UpdatePanelVisibility();
            grpType.Controls.AddRange(new Control[] { rbLateral, rbLongitudinal, rbBoth });
            Controls.Add(grpType);
            y += 58;

            // ── Lateral Settings ──
            grpLateral = new GroupBox
            {
                Text = "Lateral Brace Settings",
                Location = new Point(margin, y),
                Size = new Size(474, 150)
            };

            grpLateral.Controls.Add(new Label { Text = "Family:", Location = new Point(10, 22), Size = new Size(50, 18) });
            cboLatFamily = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(65, 19),
                Size = new Size(395, 24)
            };
            foreach (var f in _lateralFamilies) cboLatFamily.Items.Add(f);
            if (cboLatFamily.Items.Count > 0) cboLatFamily.SelectedIndex = 0;
            grpLateral.Controls.Add(cboLatFamily);

            grpLateral.Controls.Add(new Label { Text = "Max Spacing (ft):", Location = new Point(10, 52), Size = new Size(110, 18) });
            nudLatSpacing = new NumericUpDown
            {
                Location = new Point(125, 49),
                Size = new Size(70, 24),
                Minimum = 1, Maximum = 40, Value = 40, DecimalPlaces = 0, Increment = 1
            };
            grpLateral.Controls.Add(nudLatSpacing);

            grpLateral.Controls.Add(new Label { Text = "Max Dist from End (ft):", Location = new Point(220, 52), Size = new Size(155, 18) });
            nudLatDistEnd = new NumericUpDown
            {
                Location = new Point(380, 49),
                Size = new Size(70, 24),
                Minimum = 1, Maximum = 6, Value = 6, DecimalPlaces = 1, Increment = 0.5m
            };
            grpLateral.Controls.Add(nudLatDistEnd);

            grpLateral.Controls.Add(new Label { Text = "Orientation:", Location = new Point(10, 82), Size = new Size(75, 18) });
            rbLatLeft = new RadioButton { Text = "Left of Pipe / Above Pipe", Location = new Point(90, 80), Size = new Size(180, 20), Checked = true };
            rbLatRight = new RadioButton { Text = "Right of Pipe / Below Pipe", Location = new Point(280, 80), Size = new Size(185, 20) };
            grpLateral.Controls.AddRange(new Control[] { rbLatLeft, rbLatRight });

            // Warn if no lateral families found
            if (_lateralFamilies.Count == 0)
            {
                grpLateral.Controls.Add(new Label
                {
                    Text = "No lateral seismic brace families found (name must contain \"-SeismicBrace\" and \"Lateral\")",
                    Location = new Point(10, 110),
                    Size = new Size(450, 30),
                    ForeColor = Color.Red
                });
            }

            Controls.Add(grpLateral);
            y += 158;

            // ── Longitudinal Settings ──
            grpLongitudinal = new GroupBox
            {
                Text = "Longitudinal Brace Settings",
                Location = new Point(margin, y),
                Size = new Size(474, 120)
            };

            grpLongitudinal.Controls.Add(new Label { Text = "Family:", Location = new Point(10, 22), Size = new Size(50, 18) });
            cboLongFamily = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(65, 19),
                Size = new Size(395, 24)
            };
            foreach (var f in _longitudinalFamilies) cboLongFamily.Items.Add(f);
            if (cboLongFamily.Items.Count > 0) cboLongFamily.SelectedIndex = 0;
            grpLongitudinal.Controls.Add(cboLongFamily);

            grpLongitudinal.Controls.Add(new Label { Text = "Max Spacing (ft):", Location = new Point(10, 52), Size = new Size(110, 18) });
            nudLongSpacing = new NumericUpDown
            {
                Location = new Point(125, 49),
                Size = new Size(70, 24),
                Minimum = 1, Maximum = 80, Value = 80, DecimalPlaces = 0, Increment = 1
            };
            grpLongitudinal.Controls.Add(nudLongSpacing);

            grpLongitudinal.Controls.Add(new Label { Text = "Orientation:", Location = new Point(10, 82), Size = new Size(75, 18) });
            rbLongRight = new RadioButton { Text = "Right or Upward Along Pipe", Location = new Point(90, 80), Size = new Size(190, 20), Checked = true };
            rbLongLeft = new RadioButton { Text = "Left or Downward Along Pipe", Location = new Point(290, 80), Size = new Size(180, 20) };
            grpLongitudinal.Controls.AddRange(new Control[] { rbLongRight, rbLongLeft });

            if (_longitudinalFamilies.Count == 0)
            {
                grpLongitudinal.Controls.Add(new Label
                {
                    Text = "No longitudinal seismic brace families found (name must contain \"-SeismicBrace\" and \"Longitudinal\")",
                    Location = new Point(10, 100),
                    Size = new Size(450, 18),
                    ForeColor = Color.Red
                });
            }

            Controls.Add(grpLongitudinal);
            y += 128;

            // ── Linked Model ──
            var grpLink = new GroupBox
            {
                Text = "Linked Model (for structure above)",
                Location = new Point(margin, y),
                Size = new Size(474, 55)
            };
            grpLink.Controls.Add(new Label { Text = "Architectural Link:", Location = new Point(10, 23), Size = new Size(115, 18) });
            cboArchLink = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(130, 20),
                Size = new Size(330, 24)
            };
            foreach (var n in _linkNames) cboArchLink.Items.Add(n);
            if (cboArchLink.Items.Count > 0) cboArchLink.SelectedIndex = 0;
            grpLink.Controls.Add(cboArchLink);
            Controls.Add(grpLink);
            y += 65;

            // ── Buttons (right-aligned with 10px gap) ──
            // Form width 500, margin 15 → Cancel right edge at 485.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(410, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Select Pipes",
                DialogResult = DialogResult.OK,
                Location = new Point(290, y),
                Size = new Size(110, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void UpdatePanelVisibility()
        {
            grpLateral.Enabled = rbLateral.Checked || rbBoth.Checked;
            grpLongitudinal.Enabled = rbLongitudinal.Checked || rbBoth.Checked;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            bool doLateral = rbLateral.Checked || rbBoth.Checked;
            bool doLong = rbLongitudinal.Checked || rbBoth.Checked;

            if (doLateral && _lateralFamilies.Count == 0)
            {
                MessageBox.Show("No lateral seismic brace families found in the project.\n\n" +
                    "Load a family whose name contains \"-SeismicBrace\" and \"Lateral\".",
                    "Missing Family", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (doLong && _longitudinalFamilies.Count == 0)
            {
                MessageBox.Show("No longitudinal seismic brace families found in the project.\n\n" +
                    "Load a family whose name contains \"-SeismicBrace\" and \"Longitudinal\".",
                    "Missing Family", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (_linkNames.Count == 0)
            {
                MessageBox.Show("No loaded Revit links found.\n\n" +
                    "A linked model containing floors/roofs is required to detect structure above.",
                    "No Links", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            BraceMode = rbLateral.Checked ? 0 : rbLongitudinal.Checked ? 1 : 2;
            LateralFamilyIndex = cboLatFamily.SelectedIndex;
            LateralSpacingFt = (double)nudLatSpacing.Value;
            LateralDistFromEndFt = (double)nudLatDistEnd.Value;
            LongitudinalFamilyIndex = cboLongFamily.SelectedIndex;
            LongitudinalSpacingFt = (double)nudLongSpacing.Value;
            LateralOrientation = rbLatLeft.Checked ? 0 : 1;
            LongitudinalOrientation = rbLongRight.Checked ? 0 : 1;
            ArchLinkIndex = cboArchLink.SelectedIndex;
        }
    }
}

