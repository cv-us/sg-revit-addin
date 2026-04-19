using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Auto Trapeze Hang — Unistrut 21A — Auto Spaced command.
    ///
    /// Same full set of options as the regular Unistrut dialog, with 21A-specific defaults:
    ///   - Family pre-selects "21A" families
    ///   - Pipe hanger type default: "04"
    ///   - Trapeze hanger type default: "21A"
    ///   - Distance to unistrut default: "6"
    ///   - Extension distance default: "1"
    /// </summary>
    public class TrapezeUnistrut21ADialog : Form
    {
        // ── Results ──
        public string SelectedFamily { get; private set; }
        public string PipeTypeFilter { get; private set; }
        public string RodPositionMode { get; private set; } // "C" or "M"
        public bool EvenlyDistributed { get; private set; }
        public double MaxSpacingFeet { get; private set; }
        public string PipeHangerTypeCode { get; private set; }
        public string TrapezeTypeCode { get; private set; }
        public double DistanceDownToUnistrutFeet { get; private set; }
        public double MaxClashHeightFeet { get; private set; }
        public bool UseLocalFraming { get; private set; }
        public string SelectedLinkName { get; private set; }

        // Unistrut-specific
        public string ExtensionMeasuredFrom { get; private set; } // "F" or "R"
        public double ExtensionDistanceFeet { get; private set; }

        // ── Controls ──
        private ComboBox cboFamily;
        private ComboBox cboPipeFilter;
        private RadioButton rbClosestSide, rbMiddle;
        private RadioButton rbEvenlyDistributed, rbExactSpacing;
        private RadioButton rbSpacing10_6, rbSpacing12, rbSpacing15, rbCustomSpacing;
        private TextBox txtCustomSpacing;
        private TextBox txtPipeTypeCode, txtTrapezeTypeCode;
        private TextBox txtDistanceDown, txtMaxClashHeight;
        private RadioButton rbLocalFraming, rbLinkedModel;
        private ComboBox cboStructuralLink;
        private RadioButton rbExtFromFraming, rbExtFromRod;
        private TextBox txtExtensionDistance;

        public TrapezeUnistrut21ADialog(
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
            Text = "Auto Trapeze Hang — Unistrut 21A — Auto Spaced";
            Size = new Size(540, 780);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScroll = true;

            int y = 10;
            int inputX = 220;
            int inputW = 280;

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
            AddLabel("Unistrut 21A Family:", 15, y);
            cboFamily = new ComboBox
            {
                Left = inputX, Top = y - 2, Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (string name in trapezeFamilyNames.OrderBy(n => n))
                cboFamily.Items.Add(name);

            // Pre-select a 21A family, then fall back to any Unistrut
            int preselect = -1;
            for (int i = 0; i < cboFamily.Items.Count; i++)
            {
                if (cboFamily.Items[i].ToString().IndexOf("21A", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    preselect = i;
                    break;
                }
            }
            if (preselect < 0)
            {
                for (int i = 0; i < cboFamily.Items.Count; i++)
                {
                    if (cboFamily.Items[i].ToString().IndexOf("Unistrut", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        preselect = i;
                        break;
                    }
                }
            }
            cboFamily.SelectedIndex = preselect >= 0 ? preselect : (cboFamily.Items.Count > 0 ? 0 : -1);
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

            // ── Spacing Mode ──
            AddSectionLabel("Spacing Mode:", 15, y);
            y += 22;
            rbEvenlyDistributed = new RadioButton
            {
                Text = "Evenly distributed based on pipe run length",
                Left = 30, Top = y, AutoSize = true, Checked = true
            };
            Controls.Add(rbEvenlyDistributed);
            y += 22;
            rbExactSpacing = new RadioButton
            {
                Text = "Use exact spacing distance",
                Left = 30, Top = y, AutoSize = true
            };
            Controls.Add(rbExactSpacing);
            y += 30;

            // ── Max Spacing ──
            AddSectionLabel("Max Hanger Spacing:", 15, y);
            y += 22;
            rbSpacing10_6 = new RadioButton { Text = "10'-6\" (Default)", Left = 30, Top = y, AutoSize = true, Checked = true };
            Controls.Add(rbSpacing10_6);
            y += 22;
            rbSpacing12 = new RadioButton { Text = "12'-0\"", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rbSpacing12);
            y += 22;
            rbSpacing15 = new RadioButton { Text = "15'-0\"", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rbSpacing15);
            y += 22;
            rbCustomSpacing = new RadioButton { Text = "Custom Spacing (FT):", Left = 30, Top = y, AutoSize = true };
            Controls.Add(rbCustomSpacing);
            txtCustomSpacing = new TextBox { Left = 220, Top = y - 2, Width = 60, Enabled = false };
            Controls.Add(txtCustomSpacing);
            rbCustomSpacing.CheckedChanged += (s, e) => txtCustomSpacing.Enabled = rbCustomSpacing.Checked;
            y += 32;

            // ── Type Codes ──
            AddLabel("Pipe Hanger Type (Hydratec):", 15, y);
            txtPipeTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = "04" };
            Controls.Add(txtPipeTypeCode);
            y += 28;

            AddLabel("Trapeze Type (Hydratec):", 15, y);
            txtTrapezeTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = "21A" };
            Controls.Add(txtTrapezeTypeCode);
            y += 32;

            // ── Distance Down to Unistrut ──
            AddLabel("Distance to Top of Unistrut (in):", 15, y);
            txtDistanceDown = new TextBox { Left = inputX, Top = y - 2, Width = 60, Text = "6" };
            Controls.Add(txtDistanceDown);
            y += 35;

            // ── Unistrut Extension ──
            AddSectionLabel("Unistrut Extensions:", 15, y);
            y += 22;
            AddLabel("Extension Measured From:", 15, y);
            y += 20;
            rbExtFromFraming = new RadioButton
            {
                Text = "Middle of Framing",
                Left = 30, Top = y, AutoSize = true, Checked = true
            };
            Controls.Add(rbExtFromFraming);
            y += 22;
            rbExtFromRod = new RadioButton
            {
                Text = "Hanger Rod",
                Left = 30, Top = y, AutoSize = true
            };
            Controls.Add(rbExtFromRod);
            y += 26;
            AddLabel("Extension Distance (in):", 15, y);
            txtExtensionDistance = new TextBox { Left = inputX, Top = y - 2, Width = 60, Text = "1" };
            Controls.Add(txtExtensionDistance);
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

            double spacing = 10.5;
            if (rbSpacing12.Checked) spacing = 12.0;
            else if (rbSpacing15.Checked) spacing = 15.0;
            else if (rbCustomSpacing.Checked)
            {
                if (!double.TryParse(txtCustomSpacing.Text, out spacing) || spacing <= 0)
                {
                    MessageBox.Show("Enter a valid custom spacing value in feet.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
            }

            if (!double.TryParse(txtDistanceDown.Text, out double distDown) || distDown < 0)
            {
                MessageBox.Show("Enter a valid distance to top of unistrut (inches).", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!double.TryParse(txtExtensionDistance.Text, out double extDist) || extDist < 0)
            {
                MessageBox.Show("Enter a valid extension distance (inches).", "Validation",
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
            EvenlyDistributed = rbEvenlyDistributed.Checked;
            MaxSpacingFeet = spacing;
            PipeHangerTypeCode = txtPipeTypeCode.Text.Trim();
            TrapezeTypeCode = txtTrapezeTypeCode.Text.Trim();
            DistanceDownToUnistrutFeet = distDown / 12.0;
            MaxClashHeightFeet = clashHeight;
            UseLocalFraming = rbLocalFraming.Checked;
            SelectedLinkName = cboStructuralLink.SelectedItem?.ToString() ?? "";
            ExtensionMeasuredFrom = rbExtFromFraming.Checked ? "F" : "R";
            ExtensionDistanceFeet = extDist / 12.0;
        }
    }
}
