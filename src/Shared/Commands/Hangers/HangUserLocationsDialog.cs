using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Hang — User Locations (Underside of Structural) command.
    ///
    /// Collects:
    ///   - Hanger family
    ///   - Pipe type filter
    ///   - Hanger type code (Hydratec)
    /// </summary>
    public class HangUserLocationsDialog : Form
    {
        // ── Results ──
        public string SelectedFamily { get; private set; }
        public string PipeTypeFilter { get; private set; }
        public string HangerTypeCode { get; private set; }

        // ── Controls ──
        private ComboBox cboFamily;
        private ComboBox cboPipeFilter;
        private TextBox txtTypeCode;
        private Button btnOK;
        private Button btnCancel;

        public HangUserLocationsDialog(
            IList<string> hangerFamilyNames,
            IList<string> pipeTypeNames,
            string defaultFamily = "",
            string defaultTypeCode = "01")
        {
            InitializeForm(hangerFamilyNames, pipeTypeNames,
                defaultFamily, defaultTypeCode);
        }

        private void InitializeForm(
            IList<string> hangerFamilyNames, IList<string> pipeTypeNames,
            string defaultFamily, string defaultTypeCode)
        {
            Text = "Auto Hang — User Locations (Raybounce to Structure)";
            Size = new Size(500, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;
            int inputX = 190;
            int inputW = 270;

            // ── About note ──
            var note = new Label
            {
                Text = "Draw detail lines across pipes to mark hanger locations.\n" +
                       "Select BOTH the pipes AND the detail lines, then configure below.\n" +
                       "Rod length is set by raybounce to the nearest structure above.",
                Left = 15, Top = y, Width = 460, Height = 50,
                ForeColor = Color.DarkSlateGray
            };
            Controls.Add(note);
            y += 55;

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
            y += 35;

            // ── Type Code (Hydratec) ──
            AddLabel("Type Code (Hydratec):", 15, y);
            txtTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultTypeCode };
            Controls.Add(txtTypeCode);
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

        private void AddLabel(string text, int x, int y)
        {
            var lbl = new Label { Text = text, Left = x, Top = y + 2, AutoSize = true };
            lbl.Font = new Font(lbl.Font, FontStyle.Bold);
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

            SelectedFamily = cboFamily.SelectedItem.ToString();
            PipeTypeFilter = cboPipeFilter.SelectedItem.ToString();
            HangerTypeCode = txtTypeCode.Text.Trim();
        }
    }
}

