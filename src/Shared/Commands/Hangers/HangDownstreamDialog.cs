using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Hang — Threaded Branchlines (Downstream Ends) command.
    ///
    /// Collects:
    ///   - Hanger family selection
    ///   - Type codes per structural category (Roofs, Floors, Framing, Stairs)
    ///   - Distance from pipe end for hanger placement
    ///   - Minimum pipe length to qualify
    ///   - C-Clamp visibility
    /// </summary>
    public class HangDownstreamDialog : Form
    {
        // ── Results ──
        public string SelectedFamily { get; private set; }
        public string RoofTypeCode { get; private set; }
        public string FloorDeckTypeCode { get; private set; }
        public string FramingTypeCode { get; private set; }
        public string StairsTypeCode { get; private set; }
        public double DistanceFromEndInches { get; private set; }
        public double MinPipeLengthInches { get; private set; }
        public bool ShowCClamp { get; private set; }

        // ── Controls ──
        private ComboBox cboFamily;
        private TextBox txtRoofCode;
        private TextBox txtFloorCode;
        private TextBox txtFramingCode;
        private TextBox txtStairsCode;
        private TextBox txtDistFromEnd;
        private TextBox txtMinLength;
        private ComboBox cboCClamp;
        private Button btnOK;
        private Button btnCancel;

        public HangDownstreamDialog(
            IList<string> hangerFamilyNames,
            string defaultFamily = "",
            string defaultRoofCode = "03A",
            string defaultFloorCode = "05",
            string defaultFramingCode = "01",
            string defaultStairsCode = "",
            double defaultDistFromEnd = 12,
            double defaultMinLength = 18)
        {
            InitializeForm(hangerFamilyNames, defaultFamily,
                defaultRoofCode, defaultFloorCode, defaultFramingCode, defaultStairsCode,
                defaultDistFromEnd, defaultMinLength);
        }

        private void InitializeForm(
            IList<string> hangerFamilyNames, string defaultFamily,
            string defaultRoofCode, string defaultFloorCode,
            string defaultFramingCode, string defaultStairsCode,
            double defaultDistFromEnd, double defaultMinLength)
        {
            Text = "Auto Hang — Threaded Lines (Downstream Ends)";
            Size = new Size(500, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;
            int labelW = 200;
            int inputX = 215;
            int inputW = 250;

            // ── About note ──
            var note = new Label
            {
                Text = "Places hangers at downstream ends of threaded branchline pipes.\n" +
                       "Rod length is set by raybounce to the nearest structure above.",
                Left = 15, Top = y, Width = 455, Height = 36,
                ForeColor = Color.DarkSlateGray
            };
            Controls.Add(note);
            y += 42;

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

            // ── Separator: Type Codes ──
            AddSectionLabel("Hanger Type Codes (by structure above):", 15, y);
            y += 25;

            // ── Roof type code ──
            AddLabel("Roofs:", 30, y, false);
            txtRoofCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultRoofCode };
            Controls.Add(txtRoofCode);
            y += 30;

            // ── Floor deck type code ──
            AddLabel("Floor Decks:", 30, y, false);
            txtFloorCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultFloorCode };
            Controls.Add(txtFloorCode);
            y += 30;

            // ── Structural framing type code ──
            AddLabel("Structural Framing:", 30, y, false);
            txtFramingCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultFramingCode };
            Controls.Add(txtFramingCode);
            y += 30;

            // ── Stairs type code ──
            AddLabel("Stairs:", 30, y, false);
            txtStairsCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultStairsCode };
            Controls.Add(txtStairsCode);
            y += 35;

            // ── Separator ──
            var sep = new Label { Left = 15, Top = y, Width = 450, Height = 2, BorderStyle = BorderStyle.Fixed3D };
            Controls.Add(sep);
            y += 15;

            // ── Distance from end of pipe ──
            AddLabel("Distance from End of Pipe (in):", 15, y);
            txtDistFromEnd = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultDistFromEnd.ToString() };
            Controls.Add(txtDistFromEnd);
            y += 35;

            // ── Minimum pipe length ──
            AddLabel("Min Pipe Length to Hang (in):", 15, y);
            txtMinLength = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = defaultMinLength.ToString() };
            Controls.Add(txtMinLength);
            y += 35;

            // ── C-Clamp ──
            AddLabel("C-Clamp Visibility:", 15, y);
            cboCClamp = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            cboCClamp.Items.AddRange(new object[] { "Hide (Default)", "Show" });
            cboCClamp.SelectedIndex = 0;
            Controls.Add(cboCClamp);
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

            if (!double.TryParse(txtDistFromEnd.Text, out double distEnd) || distEnd <= 0)
            {
                MessageBox.Show("Distance from end must be a positive number (inches).",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!double.TryParse(txtMinLength.Text, out double minLen) || minLen <= 0)
            {
                MessageBox.Show("Minimum pipe length must be a positive number (inches).",
                    "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedFamily = cboFamily.SelectedItem.ToString();
            RoofTypeCode = txtRoofCode.Text.Trim();
            FloorDeckTypeCode = txtFloorCode.Text.Trim();
            FramingTypeCode = txtFramingCode.Text.Trim();
            StairsTypeCode = txtStairsCode.Text.Trim();
            DistanceFromEndInches = distEnd;
            MinPipeLengthInches = minLen;
            ShowCClamp = cboCClamp.SelectedIndex == 1;
        }
    }
}
