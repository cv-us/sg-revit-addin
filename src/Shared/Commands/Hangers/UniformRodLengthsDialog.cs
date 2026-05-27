using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Uniform Rod Lengths command.
    ///
    /// Collects:
    ///   • Type Code — combo box, populated from distinct codes present
    ///                  in the current selection.
    ///   • Max length (inches) — only hangers with Rod Length ≤ this
    ///                            value are touched. Hangers above this
    ///                            threshold are assumed to be on a lower
    ///                            pipe and left alone.
    ///   • Target length (inches) — the uniform Rod Length value applied
    ///                               to in-range matching hangers.
    /// </summary>
    public class UniformRodLengthsDialog : Form
    {
        // ── Results ──
        public string TypeCode { get; private set; }
        public double MaxInches { get; private set; }
        public double TargetInches { get; private set; }

        // ── Controls ──
        private ComboBox cbTypeCode;
        private NumericUpDown nudMax;
        private NumericUpDown nudTarget;
        private Button btnOK;
        private Button btnCancel;

        public UniformRodLengthsDialog(int hangerCount, List<string> availableCodes)
        {
            InitializeComponent(hangerCount, availableCodes);
        }

        private void InitializeComponent(int hangerCount, List<string> availableCodes)
        {
            Text = "Uniform Rod Lengths";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 280);

            const int margin = 15;
            int y = margin;

            // ── Selection summary ──
            var lblSummary = new Label
            {
                Text = $"Selected hangers: {hangerCount}",
                Location = new Point(margin, y),
                Size = new Size(370, 18),
                AutoSize = false
            };
            Controls.Add(lblSummary);
            y += 26;

            var lblDesc = new Label
            {
                Text = "Sweeps Rod Length on hangers of the chosen Type Code\n" +
                       "to a uniform target value — but only on hangers whose\n" +
                       "current Rod Length is at or below the max cutoff. Anything\n" +
                       "longer (likely on a lower pipe) is left alone.",
                Location = new Point(margin, y),
                Size = new Size(370, 60),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 68;

            // ── Type Code ──
            var lblCode = new Label
            {
                Text = "Type Code:",
                Location = new Point(margin, y + 3),
                Size = new Size(140, 18),
                AutoSize = false
            };
            Controls.Add(lblCode);

            cbTypeCode = new ComboBox
            {
                Location = new Point(margin + 145, y),
                Size = new Size(225, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var code in availableCodes)
                cbTypeCode.Items.Add(code);
            if (cbTypeCode.Items.Count > 0)
                cbTypeCode.SelectedIndex = 0;
            Controls.Add(cbTypeCode);
            y += 32;

            // ── Max length ──
            var lblMax = new Label
            {
                Text = "Max Rod Length (in):",
                Location = new Point(margin, y + 3),
                Size = new Size(140, 18),
                AutoSize = false
            };
            Controls.Add(lblMax);

            nudMax = new NumericUpDown
            {
                Location = new Point(margin + 145, y),
                Size = new Size(80, 22),
                Minimum = 0.25m,
                Maximum = 1000m,
                DecimalPlaces = 2,
                Increment = 0.25m,
                Value = 24.00m
            };
            Controls.Add(nudMax);

            var lblMaxHint = new Label
            {
                Text = "(hangers ≤ this length get changed)",
                Location = new Point(margin + 232, y + 3),
                Size = new Size(160, 18),
                ForeColor = SystemColors.GrayText,
                AutoSize = false
            };
            Controls.Add(lblMaxHint);
            y += 30;

            // ── Target length ──
            var lblTarget = new Label
            {
                Text = "Target Rod Length (in):",
                Location = new Point(margin, y + 3),
                Size = new Size(140, 18),
                AutoSize = false
            };
            Controls.Add(lblTarget);

            nudTarget = new NumericUpDown
            {
                Location = new Point(margin + 145, y),
                Size = new Size(80, 22),
                Minimum = 0.25m,
                Maximum = 1000m,
                DecimalPlaces = 2,
                Increment = 0.25m,
                Value = 12.00m
            };
            Controls.Add(nudTarget);

            var lblTargetHint = new Label
            {
                Text = "(new Rod Length value)",
                Location = new Point(margin + 232, y + 3),
                Size = new Size(160, 18),
                ForeColor = SystemColors.GrayText,
                AutoSize = false
            };
            Controls.Add(lblTargetHint);
            y += 38;

            // ── Buttons ──
            btnOK = new Button
            {
                Text = "Apply",
                DialogResult = DialogResult.OK,
                Location = new Point(195, y),
                Size = new Size(85, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(285, y),
                Size = new Size(85, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            TypeCode = cbTypeCode.SelectedItem as string;
            MaxInches = (double)nudMax.Value;
            TargetInches = (double)nudTarget.Value;

            if (string.IsNullOrWhiteSpace(TypeCode))
            {
                MessageBox.Show(this,
                    "Pick a Type Code.",
                    "Uniform Rod Lengths",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (TargetInches > MaxInches)
            {
                MessageBox.Show(this,
                    $"Target ({TargetInches:F2}\") is longer than the max " +
                    $"cutoff ({MaxInches:F2}\").\n\n" +
                    "A target above the max would never get applied — " +
                    "pick a target ≤ max.",
                    "Uniform Rod Lengths",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
        }
    }
}
