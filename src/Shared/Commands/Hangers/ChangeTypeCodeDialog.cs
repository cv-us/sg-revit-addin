using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Change Type Code command.
    ///
    /// Collects:
    ///   • From Type Code — combo box, populated from distinct codes in
    ///                       the current selection (no free-text; we
    ///                       can't change a code we don't have).
    ///   • To   Type Code — text box, free-form.
    ///
    /// LAYOUT:
    ///   Generous label-column width and an explicit input column so
    ///   nothing clips on small monitors. Buttons right-aligned at the
    ///   bottom.
    /// </summary>
    public class ChangeTypeCodeDialog : Form
    {
        // ── Results ──
        public string FromCode { get; private set; }
        public string ToCode { get; private set; }

        // ── Controls ──
        private ComboBox cbFromCode;
        private TextBox txtToCode;
        private Button btnOK;
        private Button btnCancel;

        public ChangeTypeCodeDialog(int hangerCount, int hangersWithNoCode, List<string> availableCodes)
        {
            InitializeComponent(hangerCount, hangersWithNoCode, availableCodes);
        }

        private void InitializeComponent(int hangerCount, int hangersWithNoCode, List<string> availableCodes)
        {
            const int Margin = 15;
            const int FormWidth = 480;
            const int LabelW = 160;
            const int InputX = Margin + LabelW + 10;
            const int HintW = FormWidth - Margin * 2;
            const int SectionGap = 14;

            Text = "Change Type Code";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = Margin;

            // ── Selection summary ──
            string summary = $"Selected hangers: {hangerCount}";
            if (hangersWithNoCode > 0)
                summary += $"   ({hangersWithNoCode} with no Type Code — will be left alone)";

            var lblSummary = new Label
            {
                Text = summary,
                Location = new Point(Margin, y),
                Size = new Size(HintW, 20),
                AutoSize = false,
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblSummary);
            y += 26;

            var lblDesc = new Label
            {
                Text = "Hangers carrying the From code will be re-stamped with the " +
                       "To code. Hangers with any other code (or none) are left " +
                       "unchanged.",
                Location = new Point(Margin, y),
                Size = new Size(HintW, 48),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 48 + SectionGap;

            // ── From Type Code ──
            var lblFrom = new Label
            {
                Text = "From Type Code:",
                Location = new Point(Margin, y + 3),
                Size = new Size(LabelW, 20),
                AutoSize = false
            };
            Controls.Add(lblFrom);

            cbFromCode = new ComboBox
            {
                Location = new Point(InputX, y),
                Size = new Size(FormWidth - InputX - Margin, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var code in availableCodes)
                cbFromCode.Items.Add(code);
            if (cbFromCode.Items.Count > 0)
                cbFromCode.SelectedIndex = 0;
            Controls.Add(cbFromCode);
            y += 24 + SectionGap;

            // ── To Type Code ──
            var lblTo = new Label
            {
                Text = "To Type Code:",
                Location = new Point(Margin, y + 3),
                Size = new Size(LabelW, 20),
                AutoSize = false
            };
            Controls.Add(lblTo);

            txtToCode = new TextBox
            {
                Location = new Point(InputX, y),
                Size = new Size(FormWidth - InputX - Margin, 24)
            };
            Controls.Add(txtToCode);
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
                Text = "Change",
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
            FromCode = cbFromCode.SelectedItem as string;
            ToCode = txtToCode.Text;

            if (string.IsNullOrWhiteSpace(FromCode))
            {
                MessageBox.Show(this,
                    "Pick a From code.",
                    "Change Type Code",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (string.IsNullOrWhiteSpace(ToCode))
            {
                MessageBox.Show(this,
                    "Enter a To code.",
                    "Change Type Code",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
        }
    }
}
