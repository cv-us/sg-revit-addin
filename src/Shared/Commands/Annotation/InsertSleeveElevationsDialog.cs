using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Pipe Sleeve Elevations command.
    ///
    /// Collects:
    ///   - Linked model for AFF reference (architectural floors)
    ///   - Linked model for BBD reference (structural decks)
    ///   - Elevation display format (decimal feet vs feet-and-inches)
    ///
    /// Migrated from: "AutoInsert - Pipe Sleeve Elevations.dyn"
    /// </summary>
    public class InsertSleeveElevationsDialog : Form
    {
        // ── Results ──
        public int AFFLinkIndex { get; private set; } = -1;
        public int BBDLinkIndex { get; private set; } = -1;
        public bool UseDecimalFeet { get; private set; } = true;

        // ── Controls ──
        private ComboBox cboAFFLink, cboBBDLink;
        private RadioButton rbDecimal, rbFeetInches;

        private readonly List<string> _linkNames;

        public InsertSleeveElevationsDialog(List<string> linkNames)
        {
            _linkNames = linkNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Insert Pipe Sleeve Elevations";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 290);

            int margin = 15;
            int y = margin;

            // ── Linked Models Section ──
            var grpLinks = new GroupBox
            {
                Text = "Linked Model References",
                Location = new Point(margin, y),
                Size = new Size(430, 110)
            };

            var lblAFF = new Label
            {
                Text = "AFF Reference (Architectural Link):",
                Location = new Point(10, 25),
                Size = new Size(200, 20)
            };
            cboAFFLink = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(215, 22),
                Size = new Size(200, 24)
            };
            foreach (var name in _linkNames)
                cboAFFLink.Items.Add(name);
            if (cboAFFLink.Items.Count > 0) cboAFFLink.SelectedIndex = 0;

            var lblBBD = new Label
            {
                Text = "BBD Reference (Structural Link):",
                Location = new Point(10, 60),
                Size = new Size(200, 20)
            };
            cboBBDLink = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(215, 57),
                Size = new Size(200, 24)
            };
            foreach (var name in _linkNames)
                cboBBDLink.Items.Add(name);
            // Default structural to second link if available
            if (cboBBDLink.Items.Count > 1)
                cboBBDLink.SelectedIndex = 1;
            else if (cboBBDLink.Items.Count > 0)
                cboBBDLink.SelectedIndex = 0;

            grpLinks.Controls.AddRange(new Control[] { lblAFF, cboAFFLink, lblBBD, cboBBDLink });
            Controls.Add(grpLinks);
            y += 120;

            // ── Format Section ──
            var grpFormat = new GroupBox
            {
                Text = "Elevation Display Format",
                Location = new Point(margin, y),
                Size = new Size(430, 75)
            };

            rbDecimal = new RadioButton
            {
                Text = "Decimal Feet  (+10.50')",
                Location = new Point(15, 22),
                Size = new Size(200, 20),
                Checked = true
            };
            rbFeetInches = new RadioButton
            {
                Text = "Feet and Inches  (+10'-6\" AFF)",
                Location = new Point(15, 46),
                Size = new Size(250, 20)
            };

            grpFormat.Controls.AddRange(new Control[] { rbDecimal, rbFeetInches });
            Controls.Add(grpFormat);
            y += 85;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Select Sleeves",
                DialogResult = DialogResult.OK,
                Location = new Point(240, y),
                Size = new Size(120, 30)
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
            if (_linkNames.Count == 0)
            {
                MessageBox.Show("No loaded Revit links found in the project.\n" +
                    "Load at least one linked model containing floors or structural decks.",
                    "No Links", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            AFFLinkIndex = cboAFFLink.SelectedIndex;
            BBDLinkIndex = cboBBDLink.SelectedIndex;
            UseDecimalFeet = rbDecimal.Checked;
        }
    }
}
