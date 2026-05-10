using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Hang — Typical Spaced Runs (Hangers to Decks) command.
    ///
    /// Collects:
    ///   - Pipe type filter (all pipes or specific type)
    ///   - Hanger family
    ///   - Spacing mode: evenly distributed or exact distance
    ///   - Max spacing preset (10'-6", 12'-0", 15'-0") or custom
    ///   - Hanger type code (Hydratec)
    ///   - Linked model selection (architectural + structural)
    ///   - Max clash height for raybounce
    /// </summary>
    public class HangTypicalSpacingDialog : Form
    {
        // ── Results ──
        public string SelectedFamily { get; private set; }
        public string PipeTypeFilter { get; private set; }
        public bool EvenlyDistributed { get; private set; }
        public double MaxSpacingFeet { get; private set; }
        public string HangerTypeCode { get; private set; }
        public string SelectedStructuralLink { get; private set; }
        public double MaxClashHeightFeet { get; private set; }

        // ── Controls ──
        private ComboBox cboFamily;
        private ComboBox cboPipeFilter;
        private RadioButton rbEvenlySpaced;
        private RadioButton rbExactSpacing;
        private RadioButton rb10_6;
        private RadioButton rb12_0;
        private RadioButton rb15_0;
        private RadioButton rbCustom;
        private TextBox txtCustomSpacing;
        private TextBox txtTypeCode;
        private ComboBox cboStructLink;
        private TextBox txtMaxClash;
        private Button btnOK;
        private Button btnCancel;

        public HangTypicalSpacingDialog(
            IList<string> hangerFamilyNames,
            IList<string> pipeTypeNames,
            IList<string> linkNames,
            string defaultFamily = "",
            string defaultTypeCode = "01",
            double defaultMaxClash = 10)
        {
            InitializeForm(hangerFamilyNames, pipeTypeNames, linkNames,
                defaultFamily, defaultTypeCode, defaultMaxClash);
        }

        private void InitializeForm(
            IList<string> hangerFamilyNames, IList<string> pipeTypeNames,
            IList<string> linkNames,
            string defaultFamily, string defaultTypeCode, double defaultMaxClash)
        {
            Text = "Auto Hang — Typical Spaced Straight Runs";
            Size = new Size(520, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;
            int inputX = 200;
            int inputW = 280;

            // ── About note ──
            var note = new Label
            {
                Text = "Places hangers at typical spacing along straight pipe runs.\n" +
                       "Rod length is set by raybounce to the nearest structure above.",
                Left = 15, Top = y, Width = 475, Height = 36,
                ForeColor = Color.DarkSlateGray
            };
            Controls.Add(note);
            y += 42;

            // ── Pipe Type Filter ──
            AddLabel("Pipe Type Filter:", 15, y);
            cboPipeFilter = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboPipeFilter.Items.Add("ALL Pipes");
            foreach (string name in pipeTypeNames.OrderBy(n => n))
                cboPipeFilter.Items.Add(name);
            cboPipeFilter.SelectedIndex = 0;
            Controls.Add(cboPipeFilter);
            y += 35;

            // ── Hanger Family ──
            AddLabel("Hanger Family:", 15, y);
            cboFamily = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string name in hangerFamilyNames.OrderBy(n => n))
                cboFamily.Items.Add(name);
            if (!string.IsNullOrEmpty(defaultFamily) && cboFamily.Items.Contains(defaultFamily))
                cboFamily.SelectedItem = defaultFamily;
            else if (cboFamily.Items.Count > 0)
                cboFamily.SelectedIndex = 0;
            Controls.Add(cboFamily);
            y += 40;

            // ── Spacing Mode ──
            AddSectionLabel("Spacing Mode:", 15, y);
            y += 22;

            rbEvenlySpaced = new RadioButton
            {
                Text = "Evenly spaced along pipe run length",
                Left = 30, Top = y, AutoSize = true, Checked = true
            };
            Controls.Add(rbEvenlySpaced);
            y += 24;

            rbExactSpacing = new RadioButton
            {
                Text = "Use exact spacing distance",
                Left = 30, Top = y, AutoSize = true
            };
            Controls.Add(rbExactSpacing);
            y += 32;

            // ── Max Spacing ──
            AddSectionLabel("Maximum Hanger Spacing:", 15, y);
            y += 22;

            rb10_6 = new RadioButton { Text = "10'-6\" (Default)", Left = 30, Top = y, AutoSize = true, Checked = true };
            Controls.Add(rb10_6);
            y += 24;

            rb12_0 = new RadioButton { Text = "12'-0\"", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rb12_0);
            y += 24;

            rb15_0 = new RadioButton { Text = "15'-0\"", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rb15_0);
            y += 24;

            rbCustom = new RadioButton { Text = "Custom:", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rbCustom);
            txtCustomSpacing = new TextBox { Left = 130, Top = y - 2, Width = 60, Enabled = false };
            Controls.Add(txtCustomSpacing);
            var lblFt = new Label { Text = "ft", Left = 195, Top = y + 2, AutoSize = true };
            Controls.Add(lblFt);
            y += 35;

            // Enable/disable custom text box
            rbCustom.CheckedChanged += (s, e) => txtCustomSpacing.Enabled = rbCustom.Checked;

            // ── Separator ──
            var sep = new Label { Left = 15, Top = y, Width = 470, Height = 2, BorderStyle = BorderStyle.Fixed3D };
            Controls.Add(sep);
            y += 15;

            // ── Type Code (Hydratec) ──
            AddLabel("Type Code (Hydratec):", 15, y);
            txtTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultTypeCode };
            Controls.Add(txtTypeCode);
            y += 35;

            // ── Structural Link ──
            AddLabel("Structural Link:", 15, y);
            cboStructLink = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboStructLink.Items.Add("(None — skip raybounce)");
            foreach (string name in linkNames)
                cboStructLink.Items.Add(name);
            cboStructLink.SelectedIndex = 0;
            Controls.Add(cboStructLink);
            y += 35;

            // ── Max Clash Height ──
            AddLabel("Max Clash Height (ft):", 15, y);
            txtMaxClash = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultMaxClash.ToString() };
            Controls.Add(txtMaxClash);
            y += 45;

            // ── OK / Cancel ──
            btnOK = new Button { Text = "Place Hangers", Left = 275, Top = y, Width = 110, Height = 32, DialogResult = DialogResult.OK };
            btnOK.Click += BtnOK_Click;
            Controls.Add(btnOK);
            AcceptButton = btnOK;

            btnCancel = new Button { Text = "Cancel", Left = 395, Top = y, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
        }

        private void AddLabel(string text, int x, int y, bool bold = true)
        {
            var lbl = new Label { Text = text, Left = x, Top = y + 2, AutoSize = true };
            if (bold) lbl.Font = new Font(lbl.Font, FontStyle.Bold);
            Controls.Add(lbl);
        }

        private void AddSectionLabel(string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text, Left = x, Top = y + 2, AutoSize = true,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold | FontStyle.Italic),
                ForeColor = Color.DarkSlateBlue
            };
            Controls.Add(lbl);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (cboFamily.SelectedItem == null)
            {
                MessageBox.Show("Please select a hanger family.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            // Determine max spacing
            double maxSpacing;
            if (rb10_6.Checked)
                maxSpacing = 10.5;
            else if (rb12_0.Checked)
                maxSpacing = 12.0;
            else if (rb15_0.Checked)
                maxSpacing = 15.0;
            else
            {
                if (!double.TryParse(txtCustomSpacing.Text, out maxSpacing) || maxSpacing <= 0)
                {
                    MessageBox.Show("Custom spacing must be a positive number (feet).",
                        "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
            }

            if (!double.TryParse(txtMaxClash.Text, out double maxClash) || maxClash <= 0)
            {
                MessageBox.Show("Max clash height must be a positive number.",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedFamily = cboFamily.SelectedItem.ToString();
            PipeTypeFilter = cboPipeFilter.SelectedItem.ToString();
            EvenlyDistributed = rbEvenlySpaced.Checked;
            MaxSpacingFeet = maxSpacing;
            HangerTypeCode = txtTypeCode.Text.Trim();
            SelectedStructuralLink = cboStructLink.SelectedIndex > 0
                ? cboStructLink.SelectedItem.ToString() : null;
            MaxClashHeightFeet = maxClash;
        }
    }
}
