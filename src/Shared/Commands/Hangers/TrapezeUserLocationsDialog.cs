using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
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
    ///
    /// All inputs persist between runs via <see cref="DialogMemory"/>
    /// (combo selections are restored only when still present).
    /// </summary>
    public class TrapezeUserLocationsDialog : DpiAwareForm
    {
        private const string MemKey = "TrapezeUserLocations";

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
            ClientSize = new Size(600, 446);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 10;
            int inputX = 260;
            int inputW = 305;

            // ── About note ──
            var note = new Label
            {
                Text = "Draw detail lines across pipes to mark trapeze locations.\n" +
                       "Select BOTH the pipes AND detail lines, then configure below.",
                Left = 15, Top = y, Width = 570, Height = 36,
                ForeColor = Color.DarkSlateGray
            };
            Controls.Add(note);
            y += 42;

            // ── Pipe Type Filter ──
            AddLabel("Pipe Type Filter:", 15, y);
            cboPipeFilter = new ComboBox
            {
                Left = inputX, Top = y - 2, Width = inputW,
                DropDownStyle = ComboBoxStyle.DropDownList,
                // Widen with the window so long type names stay readable.
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
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
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
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

            // ── Rod Position Mode ── (each radio section lives in its OWN panel so
            //    it forms an independent radio group — otherwise every radio on the
            //    form is one group and only one can ever be checked.)
            AddSectionLabel("Trapeze Rod Positioning:", 15, y);
            y += 22;
            var pnlRod = MakeRadioPanel(y, 44);
            rbClosestSide = new RadioButton
            {
                Text = "Closest Side of Structural Elements",
                Left = 15, Top = 0, AutoSize = true, Checked = true
            };
            pnlRod.Controls.Add(rbClosestSide);
            rbMiddle = new RadioButton
            {
                Text = "Middle of Structural Elements",
                Left = 15, Top = 22, AutoSize = true
            };
            pnlRod.Controls.Add(rbMiddle);
            y += 52;

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
            new ToolTip().SetToolTip(txtMaxClashHeight,
                "How far above the pipe to search for structural elements (feet).");
            y += 35;

            // ── Structural Source ── (own panel = own radio group)
            AddSectionLabel("Structural Source:", 15, y);
            y += 22;
            var pnlSource = MakeRadioPanel(y, 46);
            rbLocalFraming = new RadioButton
            {
                Text = "Local Structural Framing",
                Left = 15, Top = 0, AutoSize = true, Checked = true
            };
            pnlSource.Controls.Add(rbLocalFraming);
            rbLinkedModel = new RadioButton
            {
                Text = "Linked Model:",
                Left = 15, Top = 24, AutoSize = true,
                Enabled = linkNames.Count > 0
            };
            pnlSource.Controls.Add(rbLinkedModel);

            cboStructuralLink = new ComboBox
            {
                Left = 165, Top = 22, Width = 385,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            foreach (string name in linkNames.OrderBy(n => n))
                cboStructuralLink.Items.Add(name);
            if (cboStructuralLink.Items.Count > 0)
                cboStructuralLink.SelectedIndex = 0;
            pnlSource.Controls.Add(cboStructuralLink);

            rbLinkedModel.CheckedChanged += (s, e) => cboStructuralLink.Enabled = rbLinkedModel.Checked;

            if (linkNames.Count == 0)
            {
                rbLocalFraming.Checked = true;
                rbLinkedModel.Enabled = false;
            }
            y += 54;

            // ── OK / Cancel (right-aligned) ──
            // Form width 600, margin 15 → Cancel right edge at 585.
            var btnCancel = new Button
            {
                Text = "Cancel",
                Left = 495, Top = y, Width = 90, Height = 32,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;

            var btnOK = new Button
            {
                Text = "Place Trapezes",
                Left = 365, Top = y, Width = 120, Height = 32,
                DialogResult = DialogResult.OK
            };
            btnOK.Click += BtnOK_Click;
            Controls.Add(btnOK);
            AcceptButton = btnOK;

            RestoreFromMemory();
        }

        /// <summary>Re-applies the last-used values (combo picks only when still present).</summary>
        private void RestoreFromMemory()
        {
            int i = cboPipeFilter.Items.IndexOf(DialogMemory.Get(MemKey, "PipeFilter", ""));
            if (i >= 0) cboPipeFilter.SelectedIndex = i;

            i = cboFamily.Items.IndexOf(DialogMemory.Get(MemKey, "Family", ""));
            if (i >= 0) cboFamily.SelectedIndex = i;

            if (DialogMemory.Get(MemKey, "RodPosition", "C") == "M") rbMiddle.Checked = true;

            txtPipeTypeCode.Text = DialogMemory.Get(MemKey, "PipeTypeCode", txtPipeTypeCode.Text);
            txtTrapezeTypeCode.Text = DialogMemory.Get(MemKey, "TrapezeTypeCode", txtTrapezeTypeCode.Text);
            txtDistanceDown.Text = DialogMemory.Get(MemKey, "DistanceDown", txtDistanceDown.Text);
            txtMaxClashHeight.Text = DialogMemory.Get(MemKey, "MaxClashHeight", txtMaxClashHeight.Text);

            if (!DialogMemory.GetBool(MemKey, "UseLocalFraming", true) && rbLinkedModel.Enabled)
                rbLinkedModel.Checked = true;
            i = cboStructuralLink.Items.IndexOf(DialogMemory.Get(MemKey, "LinkName", ""));
            if (i >= 0) cboStructuralLink.SelectedIndex = i;
        }

        private void SaveToMemory()
        {
            DialogMemory.Set(MemKey, "PipeFilter", PipeTypeFilter);
            DialogMemory.Set(MemKey, "Family", SelectedFamily);
            DialogMemory.Set(MemKey, "RodPosition", RodPositionMode);
            DialogMemory.Set(MemKey, "PipeTypeCode", PipeHangerTypeCode);
            DialogMemory.Set(MemKey, "TrapezeTypeCode", TrapezeTypeCode);
            DialogMemory.Set(MemKey, "DistanceDown", txtDistanceDown.Text.Trim());
            DialogMemory.Set(MemKey, "MaxClashHeight", txtMaxClashHeight.Text.Trim());
            DialogMemory.SetBool(MemKey, "UseLocalFraming", UseLocalFraming);
            DialogMemory.Set(MemKey, "LinkName", SelectedLinkName);
            DialogMemory.Flush();
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

        /// <summary>Borderless container that gives its radios their own group.</summary>
        private Panel MakeRadioPanel(int top, int height)
        {
            var p = new Panel
            {
                Left = 15, Top = top, Width = 570, Height = height,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(p);
            return p;
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

            SaveToMemory();
        }
    }
}

