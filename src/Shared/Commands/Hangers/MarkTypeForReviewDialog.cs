using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Mark Type for Review command.
    ///
    /// Collects:
    ///   • Type Code — combo box, populated from codes in the selection.
    ///   • Reach (ft) — how far the marker cylinder extends above and below
    ///                   the hanger elevation.
    ///   • Clear Markers Only — wipes existing review markers without placing.
    /// </summary>
    public class MarkTypeForReviewDialog : Form
    {
        // ── Results ──
        public string TypeCode { get; private set; }
        public double ReachFeet { get; private set; }
        public bool ClearOnly { get; private set; }

        // ── Controls ──
        private ComboBox cbTypeCode;
        private NumericUpDown nudReach;
        private Button btnPlace;
        private Button btnClear;
        private Button btnCancel;

        public MarkTypeForReviewDialog(int hangerCount, List<string> availableCodes)
        {
            InitializeComponent(hangerCount, availableCodes);
        }

        private void InitializeComponent(int hangerCount, List<string> availableCodes)
        {
            const int Margin = 15;
            const int FormWidth = 480;
            const int LabelW = 170;
            const int InputX = Margin + LabelW + 10;
            const int NumericW = 90;
            const int HintW = FormWidth - Margin * 2;
            const int RowGap = 8;
            const int SectionGap = 14;

            Text = "Mark Type for Review";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = Margin;

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

            var lblDesc = new Label
            {
                Text = "Places a tall magenta cylinder on every hanger of the chosen " +
                       "Type Code, extending above and below the hanger so it's easy " +
                       "to spot in plan and 3D views.",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 48),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 48 + SectionGap;

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
            Controls.Add(cbTypeCode);
            y += 24 + SectionGap;

            // ── Reach ──
            var lblReach = new Label
            {
                Text = "Reach above/below (ft):",
                Location = new Point(Margin, y + 3),
                Size = new Size(LabelW, 20),
                AutoSize = false
            };
            Controls.Add(lblReach);

            nudReach = new NumericUpDown
            {
                Location = new Point(InputX, y),
                Size = new Size(NumericW, 24),
                Minimum = 0.5m,
                Maximum = 50m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 5.0m
            };
            Controls.Add(nudReach);
            y += 24 + RowGap;

            var lblReachHint = new Label
            {
                Text = "Cylinder spans this far each way from the hanger elevation " +
                       "(5 ft → a 10 ft tall column).",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 20),
                ForeColor = SystemColors.GrayText,
                AutoSize = false
            };
            Controls.Add(lblReachHint);
            y += 20 + SectionGap + 6;

            // ── Buttons (right-aligned) ──
            const int BtnH = 30;
            const int BtnGap = 10;

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(FormWidth - Margin - 80, y),
                Size = new Size(80, BtnH)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            btnClear = new Button
            {
                Text = "Clear Markers Only",
                Location = new Point(FormWidth - Margin - 80 - BtnGap - 140, y),
                Size = new Size(140, BtnH)
            };
            btnClear.Click += (s, e) =>
            {
                ClearOnly = true;
                DialogResult = DialogResult.OK;
            };
            Controls.Add(btnClear);

            btnPlace = new Button
            {
                Text = "Place Markers",
                Location = new Point(FormWidth - Margin - 80 - BtnGap - 140 - BtnGap - 120, y),
                Size = new Size(120, BtnH)
            };
            btnPlace.Click += BtnPlace_Click;
            AcceptButton = btnPlace;
            Controls.Add(btnPlace);

            y += BtnH + Margin;
            ClientSize = new Size(FormWidth, y);
        }

        private void BtnPlace_Click(object sender, EventArgs e)
        {
            ClearOnly = false;
            TypeCode = cbTypeCode.SelectedItem as string;
            ReachFeet = (double)nudReach.Value;

            if (string.IsNullOrWhiteSpace(TypeCode))
            {
                MessageBox.Show(this,
                    "Pick a Type Code.",
                    "Mark Type for Review",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
