using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Dialog for the AutoSync Hangers to Reference Plane command.
    ///
    /// Collects:
    ///   - Reference plane selection (dropdown of named reference planes)
    /// </summary>
    public class SyncHangersToRefPlaneDialog : Form
    {
        // ── Results ──
        public int SelectedRefPlaneIndex { get; private set; } = -1;

        // ── Controls ──
        private ComboBox cboRefPlane;

        private readonly int _hangerCount;
        private readonly List<string> _refPlaneNames;

        public SyncHangersToRefPlaneDialog(int hangerCount, List<string> refPlaneNames)
        {
            _hangerCount = hangerCount;
            _refPlaneNames = refPlaneNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "AutoSync Hangers to Reference Plane";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 280);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(430, 70)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Calculates rod length for each selected pipe hanger by measuring\n" +
                       "the vertical distance from the hanger to the selected reference plane\n" +
                       "(representing the underside of the structural slab/deck above).",
                Location = new Point(10, 18),
                Size = new Size(410, 45)
            });
            Controls.Add(grpInfo);
            y += 80;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(430, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_hangerCount} pipe hanger{(_hangerCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 20),
                Size = new Size(410, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 60;

            // ── Reference Plane ──
            var grpSettings = new GroupBox
            {
                Text = "Reference Plane",
                Location = new Point(margin, y),
                Size = new Size(430, 55)
            };
            grpSettings.Controls.Add(new Label
            {
                Text = "Plane:",
                Location = new Point(10, 22),
                Size = new Size(45, 18)
            });
            cboRefPlane = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(60, 19),
                Size = new Size(355, 24)
            };
            foreach (var name in _refPlaneNames)
                cboRefPlane.Items.Add(name);
            if (cboRefPlane.Items.Count > 0) cboRefPlane.SelectedIndex = 0;
            grpSettings.Controls.Add(cboRefPlane);
            Controls.Add(grpSettings);
            y += 65;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Sync Rod Lengths",
                DialogResult = DialogResult.OK,
                Location = new Point(230, y),
                Size = new Size(130, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(365, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (_refPlaneNames.Count == 0)
            {
                MessageBox.Show("No reference planes found in the project.",
                    "No Reference Planes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedRefPlaneIndex = cboRefPlane.SelectedIndex;
        }
    }
}
