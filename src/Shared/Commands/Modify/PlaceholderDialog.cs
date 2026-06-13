using System;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// Scratch dialog wired to the Modify-tab SG panel placeholder buttons.
    /// Just a multiline TextBox the user can type into — the contents are
    /// thrown away when the dialog closes. It's a visible reminder that
    /// the slot is reserved for a future tool.
    /// </summary>
    public class PlaceholderDialog : Form
    {
        public PlaceholderDialog(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 240);

            const int Margin = 15;

            var lblHint = new Label
            {
                Text = "Scratch space — type whatever, this slot is reserved for a future tool.",
                Location = new Point(Margin, Margin),
                Size = new Size(410, 36),
                AutoSize = false,
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblHint);

            var txt = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(Margin, Margin + 40),
                Size = new Size(410, 130)
            };
            Controls.Add(txt);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(440 - Margin - 85, 240 - Margin - 30),
                Size = new Size(85, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(440 - Margin - 85 - 10 - 85, 240 - Margin - 30),
                Size = new Size(85, 30)
            };
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }
    }
}
