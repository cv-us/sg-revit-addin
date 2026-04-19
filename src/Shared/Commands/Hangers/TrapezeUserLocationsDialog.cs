using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Trapeze Hang — User Locations command.
    ///
    /// Collects:
    ///   - Pipe type filter
    ///   - Trapeze hanger family
    ///   - Rod position mode (closest side vs middle)
    ///   - Pipe hanger type code (Hydratec)
    ///   - Trapeze hanger type code (Hydratec)
    ///   - Distance down to trapeze pipe (inches)
    ///   - Max clash height (feet)
    ///   - Structural source (local or linked model)
    ///
    /// No spacing controls — hanger locations are determined by detail lines.
    /// </summary>
    public class TrapezeUserLocationsDialog : Form
    {
        // ── Results ──
        public string SelectedFamily { get; private set; }
        public string PipeTypeFilter { get; private set; }
        public string RodPositionMode { get; private set; } // "C" or "M"
        public string PipeHangerTypeCode { get; private set; }
        public string TrapezeTypeCode { get; private set; }
        public double DistanceDownToTrapezeFeet { get; private set; }
        public double MaxClashHeightFeet { get; private set; }
        public bool UseLocalFraming { get; private set; }
        public string SelectedLinkName { get; private set; }

        // ── Controls ──
        private ComboBox cboFamily;
        private ComboBox cboPipeFilter;
        private RadioButton rbClosestSide;
        private RadioButton rbMiddle;
        private TextBox txtPipeTypeCode;
        private TextBox txtTrapezeTypeCode;
        private TextBox txtDistanceDown;
        private TextBox txtMaxClashHeight;
        private RadioButton rbLocalFraming;
        private RadioButton rbLinkedModel;
        private ComboBox cboStructuralLink;

        public TrapezeUserLocationsDialog(
            IList<string> trapezeFamilyNames,
            IList<string> pipeTypeNames,
            IList<string> linkNames)
        {
            InitializeForm(trapezeFamilyNames, pipeTypeNames, linkNames);
        }

        private void InitializeForm(
            IList<string> trapezeFamilyNames,
            IList<string> pipeTypeNames,
            IList<string> linkNames)
        {
            Text = "Auto Trapeze Hang — User Locations";
            Size = new Size(540, 530);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 10;
            int inputX = 220;
            int inputW = 280;

            // ── About note ──
            var note = new Label
            {
                Text = "Draw detail lines across pipes to mark trapeze locations.\n" +
                       "Select BOTH the pipes AND detail lines, then configure below.",
                Left = 15, Top = y, Width = 490, Height = 36,
                ForeColor = Color.DarkSlateGray
            };
            Controls.Add(note);
            y += 42;

            // ── Pipe Type Filter ──
            AddLabel("Pipe Type Filter:", 15, y);
            cboPipeFilter = new ComboBox
            {
                Left = inputX, Top = y - 2, Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboPipeFilter.Items.Add("ALL Pipes");
            foreach (string name in pipeTypeNames.OrderBy(n => n))
                cboPipeFilter.Items.Add(name);
            cboPipeFilter.SelectedIndex = 0;
            Controls.Add(cboPipeFilter);
            y += 32;

            // ── Trapeze Family ──
            AddLabel("Trapeze Family:", 15, y);
            cboFamily = new ComboBox
            {
                Left = inputX, Top = y - 2, Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (string name in trapezeFamilyNames.OrderBy(n => n))
                cboFamily.Items.Add(name);

            // Pre-select a trapeze family
            int trapezeIdx = -1;
            for (int i = 0; i < cboFamily.Items.Count; i++)
            {
                string item = cboFamily.Items[i].ToString();
                if (item.IndexOf("Trapeze", StringComparison.OrdinalIgnoreCase) >= 0
                    && item.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    trapezeIdx = i;
                    break;
                }
            }
            if (trapezeIdx < 0)
            {
                for (int i = 0; i < cboFamily.Items.Count; i++)
                {
                    if (cboFamily.Items[i].ToString().IndexOf("Trapeze", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        trapezeIdx = i;
                        break;
                    }
                }
            }
            cboFamily.SelectedIndex = trapezeIdx >= 0 ? trapezeIdx : (cboFamily.Items.Count > 0 ? 0 : -1);
            Controls.Add(cboFamily);
            y += 35;

            // ── Rod Position Mode ──
            AddSectionLabel("Trapeze Rod Positioning:", 15, y);
            y += 22;
            rbClosestSide = new RadioButton
            {
                Text = "Closest Side of Structural Elements",
                Left = 30, Top = y, AutoSize = true, Checked = true
            };
            Controls.Add(rbClosestSide);
            y += 22;
            rbMiddle = new RadioButton
            {
                Text = "Middle of Structural Elements",
                Left = 30, Top = y, AutoSize = true
            };
            Controls.Add(rbMiddle);
            y += 30;

            // ── Type Codes ──
            AddLabel("Pipe Hanger Type (Hydratec):", 15, y);
            txtPipeTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = "R3R" };
            Controls.Add(txtPipeTypeCode);
            y += 28;

            AddLabel("Trapeze Type (Hydratec):", 15, y);
            txtTrapezeTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = "19A" };
            Controls.Add(txtTrapezeTypeCode);
            y += 32;

            // ── Distance Down to Trapeze Pipe ──
            AddLabel("Distance Down to Trapeze (in):", 15, y);
            txtDistanceDown = new TextBox { Left = inputX, Top = y - 2, Width = 60, Text = "7" };
            Controls.Add(txtDistanceDown);
            y += 32;

            // ── Max Clash Height ──
            AddLabel("Max Clash Height (ft):", 15, y);
            txtMaxClashHeight = new TextBox { Left = inputX, Top = y - 2, Width = 60, Text = "10" };
            Controls.Add(txtMaxClashHeight);
            y += 35;

            // ── Structural Source ──
            AddSectionLabel("Structural Source:", 15, y);
            y += 22;
            rbLocalFraming = new RadioButton
            {
                Text = "Local Structural Framing",
                Left = 30, Top = y, AutoSize = true, Checked = true
            };
            Controls.Add(rbLocalFraming);
            y += 22;
            rbLinkedModel = new RadioButton
            {
                Text = "Linked Model:",
                Left = 30, Top = y, AutoSize = true,
                Enabled = linkNames.Count > 0
            };
            Controls.Add(rbLinkedModel);

            cboStructuralLink = new ComboBox
            {
                Left = 180, Top = y - 2, Width = 300,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            foreach (string name in linkNames.OrderBy(n => n))
                cboStructuralLink.Items.Add(name);
            if (cboStructuralLink.Items.Count > 0)
                cboStructuralLink.SelectedIndex = 0;
            Controls.Add(cboStructuralLink);

            rbLinkedModel.CheckedChanged += (s, e) => cboStructuralLink.Enabled = rbLinkedModel.Checked;

            if (linkNames.Count == 0)
            {
                rbLocalFraming.Checked = true;
                rbLinkedModel.Enabled = false;
            }
            y += 35;

            // ── OK / Cancel ──
            var btnOK = new Button
            {
                Text = "Place Trapezes",
                Left = 290, Top = y, Width = 120, Height = 32,
                DialogResult = DialogResult.OK
            };
            btnOK.Click += BtnOK_Click;
            Controls.Add(btnOK);
            AcceptButton = btnOK;

            var btnCancel = new Button
            {
                Text = "Cancel",
                Left = 420, Top = y, Width = 90, Height = 32,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
        }

        private void AddLabel(string text, int x, int y)
        {
            var lbl = new Label { Text = text, Left = x, Top = y + 2, AutoSize = true };
            lbl.Font = new Font(lbl.Font, FontStyle.Bold);
            Controls.Add(lbl);
        }

        private void AddSectionLabel(string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text, Left = x, Top = y, AutoSize = true,
                ForeColor = Color.DarkSlateGray
            };
            lbl.Font = new Font(lbl.Font.FontFamily, lbl.Font.Size + 1, FontStyle.Bold);
            Controls.Add(lbl);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (cboFamily.SelectedItem == null)
            {
                MessageBox.Show("Please select a trapeze hanger family.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!double.TryParse(txtDistanceDown.Text, out double distDown) || distDown < 0)
            {
                MessageBox.Show("Enter a valid distance down to trapeze pipe (inches).", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!double.TryParse(txtMaxClashHeight.Text, out double clashHeight) || clashHeight <= 0)
            {
                MessageBox.Show("Enter a valid max clash height (feet).", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (rbLinkedModel.Checked && cboStructuralLink.SelectedItem == null)
            {
                MessageBox.Show("Select a linked model or choose Local Structural Framing.", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedFamily = cboFamily.SelectedItem.ToString();
            PipeTypeFilter = cboPipeFilter.SelectedItem.ToString();
            RodPositionMode = rbClosestSide.Checked ? "C" : "M";
            PipeHangerTypeCode = txtPipeTypeCode.Text.Trim();
            TrapezeTypeCode = txtTrapezeTypeCode.Text.Trim();
            DistanceDownToTrapezeFeet = distDown / 12.0;
            MaxClashHeightFeet = clashHeight;
            UseLocalFraming = rbLocalFraming.Checked;
            SelectedLinkName = cboStructuralLink.SelectedItem?.ToString() ?? "";
        }
    }
}
