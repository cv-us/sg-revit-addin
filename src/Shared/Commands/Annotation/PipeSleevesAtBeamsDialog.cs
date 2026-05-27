using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Pipe Sleeves at Intersecting Beams command.
    ///
    /// Collects:
    ///   - Structural linked model selection
    ///   - Sleeve length in inches
    /// </summary>
    public class PipeSleevesAtBeamsDialog : Form
    {
        // ── Results ──
        public int SelectedLinkIndex { get; private set; } = -1;
        public double SleeveLengthInches { get; private set; } = 6.0;

        // ── Controls ──
        private ComboBox cboLink;
        private TextBox txtSleeveLength;

        private readonly List<string> _linkNames;

        public PipeSleevesAtBeamsDialog(List<string> linkNames)
        {
            _linkNames = linkNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Auto-Populate Pipe Sleeves at Intersecting Beams";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 260);

            int margin = 15;
            int y = margin;

            // ── Info Section ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(450, 60)
            };
            var lblInfo = new Label
            {
                Text = "Places pipe sleeves at all intersections of selected pipes and linked structural beams.\n" +
                       "Sleeves are sized per NFPA annular clearances. Beam types are written to Comments parameter.",
                Location = new Point(10, 18),
                Size = new Size(430, 35)
            };
            grpInfo.Controls.Add(lblInfo);
            Controls.Add(grpInfo);
            y += 70;

            // ── Settings Section ──
            var grpSettings = new GroupBox
            {
                Text = "Settings",
                Location = new Point(margin, y),
                Size = new Size(450, 100)
            };

            var lblLink = new Label
            {
                Text = "Structural Link (Beams):",
                Location = new Point(10, 25),
                Size = new Size(165, 20)
            };
            cboLink = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(180, 22),
                Size = new Size(260, 24)
            };
            foreach (var name in _linkNames)
                cboLink.Items.Add(name);
            if (cboLink.Items.Count > 0) cboLink.SelectedIndex = 0;

            var lblLength = new Label
            {
                Text = "Sleeve Length (inches):",
                Location = new Point(10, 62),
                Size = new Size(165, 20)
            };
            txtSleeveLength = new TextBox
            {
                Location = new Point(180, 59),
                Size = new Size(80, 24),
                Text = "6"
            };

            grpSettings.Controls.AddRange(new Control[] { lblLink, cboLink, lblLength, txtSleeveLength });
            Controls.Add(grpSettings);
            y += 110;

            // ── Buttons (right-aligned with 10px gap) ──
            // Form width 480, margin 15 → Cancel right edge at 465.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(390, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Select Pipes",
                DialogResult = DialogResult.OK,
                Location = new Point(270, y),
                Size = new Size(110, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (_linkNames.Count == 0)
            {
                MessageBox.Show("No loaded Revit links found in the project.\n" +
                    "Load a linked structural model containing beams.",
                    "No Links", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!double.TryParse(txtSleeveLength.Text, out double length) || length <= 0)
            {
                MessageBox.Show("Enter a valid positive sleeve length in inches.",
                    "Invalid Length", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedLinkIndex = cboLink.SelectedIndex;
            SleeveLengthInches = length;
        }
    }
}

