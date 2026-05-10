using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Hang at Structural Framing command.
    /// </summary>
    public class HangAtStructuralDialog : Form
    {
        // ── Results ──
        public string SelectedFamily { get; private set; }
        public string TypeCode { get; private set; }
        public string WidemouthTypeCode { get; private set; }
        public bool AttachToBottom { get; private set; }
        public bool ShowCClamp { get; private set; }
        public string SelectedLinkName { get; private set; }
        public double MaxClashHeightFeet { get; private set; }
        public bool UseLocalFraming { get; private set; }

        // ── Controls ──
        private ComboBox cboFamily;
        private TextBox txtTypeCode;
        private TextBox txtWidemouthType;
        private ComboBox cboAttachTo;
        private ComboBox cboCClamp;
        private ComboBox cboLink;
        private TextBox txtMaxClash;
        private CheckBox chkLocalFraming;
        private Button btnOK;
        private Button btnCancel;

        public HangAtStructuralDialog(
            IList<string> hangerFamilyNames,
            IList<string> linkNames,
            string defaultFamily = "",
            string defaultTypeCode = "01",
            string defaultWidemouthType = "01A",
            double defaultMaxClash = 10)
        {
            InitializeForm(hangerFamilyNames, linkNames,
                defaultFamily, defaultTypeCode, defaultWidemouthType, defaultMaxClash);
        }

        private void InitializeForm(
            IList<string> hangerFamilyNames, IList<string> linkNames,
            string defaultFamily, string defaultTypeCode, string defaultWidemouthType, double defaultMaxClash)
        {
            Text = "Auto Hang — Structural Framing";
            Size = new Size(500, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;
            int labelW = 170;
            int inputX = 185;
            int inputW = 280;

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
            y += 35;

            // ── Type Code (Hydratec) ──
            AddLabel("Type Code (Hydratec):", 15, y);
            txtTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultTypeCode };
            Controls.Add(txtTypeCode);
            y += 35;

            // ── Widemouth Type Code (Hydratec) ──
            AddLabel("Widemouth Type (Hydratec):", 15, y);
            txtWidemouthType = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultWidemouthType };
            Controls.Add(txtWidemouthType);
            y += 35;

            // ── Attach To ──
            AddLabel("Attach Hangers To:", 15, y);
            cboAttachTo = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboAttachTo.Items.AddRange(new object[] { "BOTTOM of Structural (Default)", "TOP of Structural" });
            cboAttachTo.SelectedIndex = 0;
            Controls.Add(cboAttachTo);
            y += 35;

            // ── C-Clamp ──
            AddLabel("C-Clamp Visibility:", 15, y);
            cboCClamp = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboCClamp.Items.AddRange(new object[] { "Hide (Default)", "Show" });
            cboCClamp.SelectedIndex = 0;
            Controls.Add(cboCClamp);
            y += 35;

            // ── Max Clash Height ──
            AddLabel("Max Clash Height (ft):", 15, y);
            txtMaxClash = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultMaxClash.ToString() };
            Controls.Add(txtMaxClash);
            y += 40;

            // ── Separator ──
            var sep = new Label { Left = 15, Top = y, Width = inputW + labelW, Height = 2, BorderStyle = BorderStyle.Fixed3D };
            Controls.Add(sep);
            y += 15;

            // ── Structural Source ──
            AddLabel("Structural Source:", 15, y, false);
            y += 22;

            chkLocalFraming = new CheckBox { Text = "Use LOCAL structural framing", Left = 25, Top = y, AutoSize = true };
            Controls.Add(chkLocalFraming);
            y += 28;

            AddLabel("Or select linked model:", 25, y, false);
            cboLink = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboLink.Items.Add("(None — use local framing)");
            foreach (string name in linkNames)
                cboLink.Items.Add(name);
            cboLink.SelectedIndex = 0;
            Controls.Add(cboLink);

            // Sync checkbox and dropdown
            chkLocalFraming.CheckedChanged += (s, e) =>
            {
                cboLink.Enabled = !chkLocalFraming.Checked;
                if (chkLocalFraming.Checked) cboLink.SelectedIndex = 0;
            };
            cboLink.SelectedIndexChanged += (s, e) =>
            {
                if (cboLink.SelectedIndex > 0) chkLocalFraming.Checked = false;
            };
            y += 45;

            // ── OK / Cancel ──
            btnOK = new Button { Text = "Place Hangers", Left = 255, Top = y, Width = 110, Height = 32, DialogResult = DialogResult.OK };
            btnOK.Click += BtnOK_Click;
            Controls.Add(btnOK);
            AcceptButton = btnOK;

            btnCancel = new Button { Text = "Cancel", Left = 375, Top = y, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
        }

        private void AddLabel(string text, int x, int y, bool bold = true)
        {
            var lbl = new Label { Text = text, Left = x, Top = y + 2, AutoSize = true };
            if (bold) lbl.Font = new Font(lbl.Font, FontStyle.Bold);
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

            if (!double.TryParse(txtMaxClash.Text, out double maxClash) || maxClash <= 0)
            {
                MessageBox.Show("Max clash height must be a positive number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!chkLocalFraming.Checked && cboLink.SelectedIndex == 0)
            {
                MessageBox.Show("Select a linked model or check 'Use LOCAL structural framing'.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedFamily = cboFamily.SelectedItem.ToString();
            TypeCode = txtTypeCode.Text;
            WidemouthTypeCode = txtWidemouthType.Text;
            AttachToBottom = cboAttachTo.SelectedIndex == 0;
            ShowCClamp = cboCClamp.SelectedIndex == 1;
            MaxClashHeightFeet = maxClash;
            UseLocalFraming = chkLocalFraming.Checked;
            SelectedLinkName = cboLink.SelectedIndex > 0 ? cboLink.SelectedItem.ToString() : null;
        }
    }
}
