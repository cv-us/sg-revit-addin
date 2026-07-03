using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Uniform Rod Lengths command.
    ///
    /// Collects:
    ///   • Type Code — combo box, populated from distinct codes present
    ///                  in the current selection.
    ///   • Max length (inches) — only hangers with Rod Length ≤ this
    ///                            value are touched.
    ///   • Target length (inches) — the uniform Rod Length value applied.
    ///
    /// LAYOUT:
    ///   Hints are stacked *below* their inputs rather than to the right,
    ///   so the form stays narrow enough to fit on small monitors without
    ///   clipping the hint text.
    /// </summary>
    public class UniformRodLengthsDialog : DpiAwareForm
    {
        private const string MemKey = "UniformRodLengths";

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
            AllowResize = false;   // fixed stack of options — resizing adds nothing
            InitializeComponent(hangerCount, availableCodes);
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void InitializeComponent(int hangerCount, List<string> availableCodes)
        {
            // Layout constants — generous so labels never clip.
            const int Margin = 15;
            const int FormWidth = 480;
            const int LabelW = 200;
            const int InputX = Margin + LabelW + 10;
            const int NumericW = 100;
            const int HintW = FormWidth - Margin * 2;
            const int RowGap = 8;
            const int SectionGap = 14;

            Text = "Uniform Rod Lengths";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = Margin;

            // ── Selection summary ──
            var lblSummary = new Label
            {
                Text = $"Selected hangers: {hangerCount}",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 20),
                AutoSize = false,
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblSummary);
            y += 26;

            // ── Description (multi-line, generous height) ──
            var lblDesc = new Label
            {
                Text = "Sweeps Rod Length on hangers of the chosen Type Code to a " +
                       "uniform target — but only on hangers whose current Rod " +
                       "Length is at or below the max cutoff. Anything longer " +
                       "(likely on a lower pipe) is left alone.",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 64),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 64 + SectionGap;

            // ── Type Code ──
            var lblCode = new Label
            {
                Text = "Type Code:",
                Location = new Point(Margin, y + 3),
                Size = new Size(LabelW, 20),
                AutoSize = false
            };
            Controls.Add(lblCode);

            cbTypeCode = new ComboBox
            {
                Location = new Point(InputX, y),
                Size = new Size(FormWidth - InputX - Margin, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var code in availableCodes)
                cbTypeCode.Items.Add(code);
            if (cbTypeCode.Items.Count > 0)
                cbTypeCode.SelectedIndex = 0;
            // Re-select the last-used code if it exists in this selection.
            int remembered = cbTypeCode.Items.IndexOf(DialogMemory.Get(MemKey, "TypeCode", ""));
            if (remembered >= 0)
                cbTypeCode.SelectedIndex = remembered;
            Controls.Add(cbTypeCode);
            y += 24 + SectionGap;

            // ── Max length (label + numeric on row 1, hint on row 2) ──
            var lblMax = new Label
            {
                Text = "Max Rod Length (in):",
                Location = new Point(Margin, y + 3),
                Size = new Size(LabelW, 20),
                AutoSize = false
            };
            Controls.Add(lblMax);

            nudMax = new NumericUpDown
            {
                Location = new Point(InputX, y),
                Size = new Size(NumericW, 24),
                Minimum = 0.25m,
                Maximum = 1000m,
                DecimalPlaces = 2,
                Increment = 0.25m,
                Value = Clamp((decimal)DialogMemory.GetDouble(MemKey, "MaxInches", 24.0), 0.25m, 1000m)
            };
            Controls.Add(nudMax);
            y += 24 + RowGap;

            var lblMaxHint = new Label
            {
                Text = "Hangers with Rod Length ≤ this value are eligible to be changed.",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 20),
                ForeColor = SystemColors.GrayText,
                AutoSize = false
            };
            Controls.Add(lblMaxHint);
            y += 20 + SectionGap;

            // ── Target length (label + numeric on row 1, hint on row 2) ──
            var lblTarget = new Label
            {
                Text = "Target Rod Length (in):",
                Location = new Point(Margin, y + 3),
                Size = new Size(LabelW, 20),
                AutoSize = false
            };
            Controls.Add(lblTarget);

            nudTarget = new NumericUpDown
            {
                Location = new Point(InputX, y),
                Size = new Size(NumericW, 24),
                Minimum = 0.25m,
                Maximum = 1000m,
                DecimalPlaces = 2,
                Increment = 0.25m,
                Value = Clamp((decimal)DialogMemory.GetDouble(MemKey, "TargetInches", 12.0), 0.25m, 1000m)
            };
            Controls.Add(nudTarget);
            y += 24 + RowGap;

            var lblTargetHint = new Label
            {
                Text = "Each eligible hanger's Rod Length is set to this value.",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 20),
                ForeColor = SystemColors.GrayText,
                AutoSize = false
            };
            Controls.Add(lblTargetHint);
            y += 20 + SectionGap + 6;

            // ── Buttons (right-aligned) ──
            const int BtnW = 95;
            const int BtnH = 30;
            const int BtnGap = 10;

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(FormWidth - Margin - BtnW, y),
                Size = new Size(BtnW, BtnH)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            btnOK = new Button
            {
                Text = "Apply",
                DialogResult = DialogResult.OK,
                Location = new Point(FormWidth - Margin - BtnW * 2 - BtnGap, y),
                Size = new Size(BtnW, BtnH)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            y += BtnH + Margin;
            ClientSize = new Size(FormWidth, y);
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

            // Remember for next time.
            DialogMemory.Set(MemKey, "TypeCode", TypeCode);
            DialogMemory.SetDouble(MemKey, "MaxInches", MaxInches);
            DialogMemory.SetDouble(MemKey, "TargetInches", TargetInches);
            DialogMemory.Flush();
        }
    }
}
