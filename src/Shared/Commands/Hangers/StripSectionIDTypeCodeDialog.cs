using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Strip Section ID Type Code command.
    ///
    /// Collects:
    ///   • Type Code — combo box, populated from distinct codes present
    ///                  in the current selection. Only hangers whose
    ///                  Type Code matches this value will have their
    ///                  Section_ID prefix stripped.
    ///
    /// LAYOUT:
    ///   Same generous-spacing pattern as the other Hangers dialogs —
    ///   wide form, fixed label column, hint stacked under input where
    ///   needed.
    /// </summary>
    public class StripSectionIDTypeCodeDialog : Form
    {
        // ── Result ──
        public string TypeCode { get; private set; }

        // ── Controls ──
        private ComboBox cbTypeCode;
        private Button btnOK;
        private Button btnCancel;

        public StripSectionIDTypeCodeDialog(int hangerCount, List<string> availableCodes)
        {
            InitializeComponent(hangerCount, availableCodes);
        }

        private void InitializeComponent(int hangerCount, List<string> availableCodes)
        {
            const int Margin = 15;
            const int FormWidth = 480;
            const int LabelW = 130;
            const int InputX = Margin + LabelW + 10;
            const int HintW = FormWidth - Margin * 2;
            const int SectionGap = 14;

            Text = "Strip Section ID Type Code";
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

            // ── Description ──
            var lblDesc = new Label
            {
                Text = "For hangers whose Type Code matches the chosen value, " +
                       "strips everything before the first '(' in Section_ID " +
                       "(Hydratec). For example, picking 11T turns \"#11T(5)\" " +
                       "into \"(5)\". Hangers with other type codes are left " +
                       "alone.",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 80),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 80 + SectionGap;

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
            y += 24 + SectionGap + 6;

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
                Text = "Strip",
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

            if (string.IsNullOrWhiteSpace(TypeCode))
            {
                MessageBox.Show(this,
                    "Pick a Type Code.",
                    "Strip Section ID Type Code",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
        }
    }
}
