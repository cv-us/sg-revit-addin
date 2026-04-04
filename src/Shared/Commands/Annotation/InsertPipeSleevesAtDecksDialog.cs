using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Pipe Sleeves at Intersecting Decks command.
    ///
    /// Collects:
    ///   - Linked model selection (containing floors/roofs)
    ///   - Sleeve length behavior (same as deck thickness, or extend 2" for wet areas)
    ///
    /// Migrated from: "AutoInsert - Pipe Sleeves at Intersecting Decks.dyn"
    /// </summary>
    public class InsertPipeSleevesAtDecksDialog : Form
    {
        // ── Results ──
        public int SelectedLinkIndex { get; private set; } = -1;
        public bool ExtendForWetAreas { get; private set; } = true;

        // ── Controls ──
        private ComboBox cboLink;
        private RadioButton rbSame, rbExtend;

        private readonly List<string> _linkNames;

        public InsertPipeSleevesAtDecksDialog(List<string> linkNames)
        {
            _linkNames = linkNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Auto-Populate Pipe Sleeves at Intersecting Decks";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 290);

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
                Text = "Places pipe sleeves at all intersections of selected pipes and linked floors/roofs.\n" +
                       "Sleeves are sized per NFPA annular clearances. Deck types are written to Comments.",
                Location = new Point(10, 18),
                Size = new Size(430, 35)
            };
            grpInfo.Controls.Add(lblInfo);
            Controls.Add(grpInfo);
            y += 70;

            // ── Link Selection ──
            var grpLink = new GroupBox
            {
                Text = "Linked Model",
                Location = new Point(margin, y),
                Size = new Size(450, 55)
            };
            var lblLink = new Label
            {
                Text = "Structural/Arch Link:",
                Location = new Point(10, 23),
                Size = new Size(130, 20)
            };
            cboLink = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(145, 20),
                Size = new Size(290, 24)
            };
            foreach (var name in _linkNames)
                cboLink.Items.Add(name);
            if (cboLink.Items.Count > 0) cboLink.SelectedIndex = 0;
            grpLink.Controls.AddRange(new Control[] { lblLink, cboLink });
            Controls.Add(grpLink);
            y += 65;

            // ── Sleeve Length Section ──
            var grpLength = new GroupBox
            {
                Text = "Sleeve Lengths",
                Location = new Point(margin, y),
                Size = new Size(450, 75)
            };
            rbSame = new RadioButton
            {
                Text = "Same thickness as floor (non-wet areas)",
                Location = new Point(15, 22),
                Size = new Size(400, 20)
            };
            rbExtend = new RadioButton
            {
                Text = "Extend 2\" above floor (wet areas)",
                Location = new Point(15, 46),
                Size = new Size(400, 20),
                Checked = true
            };
            grpLength.Controls.AddRange(new Control[] { rbSame, rbExtend });
            Controls.Add(grpLength);
            y += 85;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Select Pipes",
                DialogResult = DialogResult.OK,
                Location = new Point(275, y),
                Size = new Size(110, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(390, y),
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
                    "Load a linked model containing floors, stairs, or roofs.",
                    "No Links", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedLinkIndex = cboLink.SelectedIndex;
            ExtendForWetAreas = rbExtend.Checked;
        }
    }
}
