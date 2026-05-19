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
    ///   • To   Type Code — text box, free-form. The new code can be
    ///                       anything; we don't validate against an
    ///                       allowed list.
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
            Text = "Change Type Code";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 230);

            const int margin = 15;
            int y = margin;

            // ── Selection summary ──
            string summary = $"Selected hangers: {hangerCount}";
            if (hangersWithNoCode > 0)
                summary += $"   ({hangersWithNoCode} with no Type Code — will be left alone)";

            var lblSummary = new Label
            {
                Text = summary,
                Location = new Point(margin, y),
                Size = new Size(330, 18),
                AutoSize = false
            };
            Controls.Add(lblSummary);
            y += 28;

            var lblDesc = new Label
            {
                Text = "Hangers with the From code will be re-stamped with the\n" +
                       "To code. Hangers with other codes are left unchanged.",
                Location = new Point(margin, y),
                Size = new Size(330, 34),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 44;

            // ── From Type Code ──
            var lblFrom = new Label
            {
                Text = "From Type Code:",
                Location = new Point(margin, y + 3),
                Size = new Size(110, 18),
                AutoSize = false
            };
            Controls.Add(lblFrom);

            cbFromCode = new ComboBox
            {
                Location = new Point(margin + 115, y),
                Size = new Size(215, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var code in availableCodes)
                cbFromCode.Items.Add(code);
            if (cbFromCode.Items.Count > 0)
                cbFromCode.SelectedIndex = 0;
            Controls.Add(cbFromCode);
            y += 32;

            // ── To Type Code ──
            var lblTo = new Label
            {
                Text = "To Type Code:",
                Location = new Point(margin, y + 3),
                Size = new Size(110, 18),
                AutoSize = false
            };
            Controls.Add(lblTo);

            txtToCode = new TextBox
            {
                Location = new Point(margin + 115, y),
                Size = new Size(215, 22)
            };
            Controls.Add(txtToCode);
            y += 38;

            // ── Buttons ──
            btnOK = new Button
            {
                Text = "Change",
                DialogResult = DialogResult.OK,
                Location = new Point(155, y),
                Size = new Size(90, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(250, y),
                Size = new Size(80, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
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
