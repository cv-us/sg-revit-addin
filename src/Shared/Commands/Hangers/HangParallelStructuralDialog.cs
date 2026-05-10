using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Hang — Typical Spaced Runs (Parallel to Structural Framing) command.
    ///
    /// Collects the same spacing options as the Decks version, plus:
    ///   - Widemouth type code (for thick-flanged steel)
    ///   - Attach to TOP or BOTTOM of structural
    ///   - C-Clamp visibility
    ///   - Structural source (local or linked)
    /// </summary>
    public class HangParallelStructuralDialog : Form
    {
        // ── Results ──
        public string SelectedFamily { get; private set; }
        public string PipeTypeFilter { get; private set; }
        public bool EvenlyDistributed { get; private set; }
        public double MaxSpacingFeet { get; private set; }
        public string HangerTypeCode { get; private set; }
        public string WidemouthTypeCode { get; private set; }
        public bool AttachToBottom { get; private set; }
        public bool ShowCClamp { get; private set; }
        public string SelectedLinkName { get; private set; }
        public bool UseLocalFraming { get; private set; }

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
        private TextBox txtWidemouthCode;
        private ComboBox cboAttachTo;
        private ComboBox cboCClamp;
        private ComboBox cboLink;
        private CheckBox chkLocalFraming;
        private Button btnOK;
        private Button btnCancel;

        public HangParallelStructuralDialog(
            IList<string> hangerFamilyNames,
            IList<string> pipeTypeNames,
            IList<string> linkNames,
            string defaultFamily = "",
            string defaultTypeCode = "01",
            string defaultWidemouthCode = "01A")
        {
            InitializeForm(hangerFamilyNames, pipeTypeNames, linkNames,
                defaultFamily, defaultTypeCode, defaultWidemouthCode);
        }

        private void InitializeForm(
            IList<string> hangerFamilyNames, IList<string> pipeTypeNames,
            IList<string> linkNames,
            string defaultFamily, string defaultTypeCode, string defaultWidemouthCode)
        {
            Text = "Auto Hang — Typical Spacing (Parallel to Structural)";
            Size = new Size(520, 720);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;
            int inputX = 210;
            int inputW = 270;

            // ── About note ──
            var note = new Label
            {
                Text = "Places hangers at typical spacing along pipe runs. Hangers attach\n" +
                       "to structural framing (beams/joists) running parallel to the pipes.",
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
            y += 38;

            // ── Spacing Mode ──
            AddSectionLabel("Spacing Mode:", 15, y);
            y += 22;
            rbEvenlySpaced = new RadioButton
            {
                Text = "Evenly spaced along pipe run length", Left = 30, Top = y, AutoSize = true, Checked = true
            };
            Controls.Add(rbEvenlySpaced);
            y += 22;
            rbExactSpacing = new RadioButton
            {
                Text = "Use exact spacing distance", Left = 30, Top = y, AutoSize = true
            };
            Controls.Add(rbExactSpacing);
            y += 30;

            // ── Max Spacing ──
            AddSectionLabel("Maximum Hanger Spacing:", 15, y);
            y += 22;
            rb10_6 = new RadioButton { Text = "10'-6\" (Default)", Left = 30, Top = y, AutoSize = true, Checked = true };
            Controls.Add(rb10_6);
            y += 22;
            rb12_0 = new RadioButton { Text = "12'-0\"", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rb12_0);
            y += 22;
            rb15_0 = new RadioButton { Text = "15'-0\"", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rb15_0);
            y += 22;
            rbCustom = new RadioButton { Text = "Custom:", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rbCustom);
            txtCustomSpacing = new TextBox { Left = 130, Top = y - 2, Width = 60, Enabled = false };
            Controls.Add(txtCustomSpacing);
            Controls.Add(new Label { Text = "ft", Left = 195, Top = y + 2, AutoSize = true });
            rbCustom.CheckedChanged += (s, e) => txtCustomSpacing.Enabled = rbCustom.Checked;
            y += 32;

            // ── Separator ──
            Controls.Add(new Label { Left = 15, Top = y, Width = 470, Height = 2, BorderStyle = BorderStyle.Fixed3D });
            y += 12;

            // ── Type Codes ──
            AddLabel("Type Code (Hydratec):", 15, y);
            txtTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultTypeCode };
            Controls.Add(txtTypeCode);
            y += 32;

            AddLabel("Widemouth Type (Hydratec):", 15, y);
            txtWidemouthCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultWidemouthCode };
            Controls.Add(txtWidemouthCode);
            y += 32;

            // ── Attach To ──
            AddLabel("Attach Hangers To:", 15, y);
            cboAttachTo = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboAttachTo.Items.AddRange(new object[] { "BOTTOM where possible (Default)", "TOP of Structural Elements" });
            cboAttachTo.SelectedIndex = 0;
            Controls.Add(cboAttachTo);
            y += 32;

            // ── C-Clamp ──
            AddLabel("C-Clamp Visibility:", 15, y);
            cboCClamp = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboCClamp.Items.AddRange(new object[] { "Hide (Default)", "Show" });
            cboCClamp.SelectedIndex = 0;
            Controls.Add(cboCClamp);
            y += 35;

            // ── Separator ──
            Controls.Add(new Label { Left = 15, Top = y, Width = 470, Height = 2, BorderStyle = BorderStyle.Fixed3D });
            y += 12;

            // ── Structural Source ──
            AddSectionLabel("Structural Source:", 15, y);
            y += 22;
            chkLocalFraming = new CheckBox { Text = "Use LOCAL structural framing", Left = 25, Top = y, AutoSize = true };
            Controls.Add(chkLocalFraming);
            y += 26;
            AddLabel("Or select linked model:", 25, y, false);
            cboLink = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboLink.Items.Add("(None — use local framing)");
            foreach (string name in linkNames) cboLink.Items.Add(name);
            cboLink.SelectedIndex = 0;
            Controls.Add(cboLink);
            chkLocalFraming.CheckedChanged += (s, e) => {
                cboLink.Enabled = !chkLocalFraming.Checked;
                if (chkLocalFraming.Checked) cboLink.SelectedIndex = 0;
            };
            cboLink.SelectedIndexChanged += (s, e) => {
                if (cboLink.SelectedIndex > 0) chkLocalFraming.Checked = false;
            };
            y += 42;

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
                MessageBox.Show("Please select a hanger family.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            double maxSpacing;
            if (rb10_6.Checked) maxSpacing = 10.5;
            else if (rb12_0.Checked) maxSpacing = 12.0;
            else if (rb15_0.Checked) maxSpacing = 15.0;
            else
            {
                if (!double.TryParse(txtCustomSpacing.Text, out maxSpacing) || maxSpacing <= 0)
                {
                    MessageBox.Show("Custom spacing must be a positive number (feet).", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
            }

            if (!chkLocalFraming.Checked && cboLink.SelectedIndex == 0)
            {
                MessageBox.Show("Select a linked model or check 'Use LOCAL structural framing'.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedFamily = cboFamily.SelectedItem.ToString();
            PipeTypeFilter = cboPipeFilter.SelectedItem.ToString();
            EvenlyDistributed = rbEvenlySpaced.Checked;
            MaxSpacingFeet = maxSpacing;
            HangerTypeCode = txtTypeCode.Text.Trim();
            WidemouthTypeCode = txtWidemouthCode.Text.Trim();
            AttachToBottom = cboAttachTo.SelectedIndex == 0;
            ShowCClamp = cboCClamp.SelectedIndex == 1;
            UseLocalFraming = chkLocalFraming.Checked;
            SelectedLinkName = cboLink.SelectedIndex > 0 ? cboLink.SelectedItem.ToString() : null;
        }
    }
}
