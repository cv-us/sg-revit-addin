using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Hang — User Locations (Underside of Structural) command.
    ///
    /// Collects:
    ///   - Hanger family
    ///   - Pipe type filter
    ///   - Hanger type code (Hydratec)
    ///
    /// Last-used inputs are remembered via <see cref="DialogMemory"/>.
    /// </summary>
    public class HangUserLocationsDialog : DpiAwareForm
    {
        private const string MemKey = "HangUserLocations";

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
            ClientSize = new Size(500, 232);
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
            cboPipeFilter = new ComboBox
            {
                Left = inputX, Top = y - 2, Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            cboPipeFilter.Items.Add("ALL Pipes");
            foreach (string name in pipeTypeNames.OrderBy(n => n))
                cboPipeFilter.Items.Add(name);
            cboPipeFilter.SelectedIndex = 0;
            SelectRemembered(cboPipeFilter, DialogMemory.Get(MemKey, "PipeFilter", null));
            Controls.Add(cboPipeFilter);
            y += 35;

            // ── Hanger Family ──
            AddLabel("Hanger Family:", 15, y);
            cboFamily = new ComboBox
            {
                Left = inputX, Top = y - 2, Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            foreach (string name in hangerFamilyNames.OrderBy(n => n))
                cboFamily.Items.Add(name);
            if (!string.IsNullOrEmpty(defaultFamily) && cboFamily.Items.Contains(defaultFamily))
                cboFamily.SelectedItem = defaultFamily;
            else if (cboFamily.Items.Count > 0)
                cboFamily.SelectedIndex = 0;
            SelectRemembered(cboFamily, DialogMemory.Get(MemKey, "Family", null));
            Controls.Add(cboFamily);
            y += 35;

            // ── Type Code (Hydratec) ──
            AddLabel("Type Code (Hydratec):", 15, y);
            txtTypeCode = new TextBox
            {
                Left = inputX, Top = y - 2, Width = 80,
                Text = DialogMemory.Get(MemKey, "TypeCode", defaultTypeCode)
            };
            Controls.Add(txtTypeCode);
            y += 45;

            // ── OK / Cancel (bottom-right, 10px gap, 15px margins) ──
            btnOK = new Button { Text = "Place Hangers", Left = 275, Top = y, Width = 110, Height = 32, DialogResult = DialogResult.OK };
            btnOK.Click += BtnOK_Click;
            Controls.Add(btnOK);
            AcceptButton = btnOK;

            btnCancel = new Button { Text = "Cancel", Left = 395, Top = y, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
        }

        /// <summary>Apply a remembered combo value only if it still exists in Items.</summary>
        private static void SelectRemembered(ComboBox cbo, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = cbo.FindStringExact(text);
            if (i >= 0) cbo.SelectedIndex = i;
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

            // Remember for next time.
            DialogMemory.Set(MemKey, "Family", SelectedFamily);
            DialogMemory.Set(MemKey, "PipeFilter", PipeTypeFilter);
            DialogMemory.Set(MemKey, "TypeCode", HangerTypeCode);
            DialogMemory.Flush();
        }
    }
}

