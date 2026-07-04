using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Format Hanger Ticks command.
    ///
    /// Collects:
    ///   - Symbol direction preference (Forward slash, Backslash, or Default)
    /// </summary>
    public class FormatHangerTicksDialog : DpiAwareForm
    {
        private const string MemKey = "FormatHangerTicks";

        // ── Result ──
        /// <summary>
        /// "Forward", "Back", or "Default"
        /// </summary>
        public string SymbolDirection { get; private set; }

        // ── Controls ──
        private RadioButton rbForward;
        private RadioButton rbBack;
        private RadioButton rbDefault;
        private Button btnOK;
        private Button btnCancel;

        public FormatHangerTicksDialog()
        {
            InitializeComponent();

            // Restore the last-used direction.
            switch (DialogMemory.Get(MemKey, "Direction", "Forward"))
            {
                case "Back": rbBack.Checked = true; break;
                case "Default": rbDefault.Checked = true; break;
                default: rbForward.Checked = true; break;
            }
        }

        private void InitializeComponent()
        {
            Text = "Format Pipe Hanger Ticks";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(340, 210);

            int margin = 15;
            int y = margin;

            // ── Description ──
            var lblDesc = new Label
            {
                Text = "Auto-formats all selected pipe hanger symbols\nto face the same direction.",
                Location = new Point(margin, y),
                Size = new Size(310, 36),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 44;

            // ── Direction group ──
            var grpDirection = new GroupBox
            {
                Text = "Hanger Symbol Direction",
                Location = new Point(margin, y),
                Size = new Size(310, 95)
            };

            rbForward = new RadioButton
            {
                Text = "/  —  Forward Slash",
                Location = new Point(15, 22),
                Size = new Size(260, 20),
                Checked = true
            };

            rbBack = new RadioButton
            {
                Text = "\\  —  Backslash",
                Location = new Point(15, 46),
                Size = new Size(260, 20)
            };

            rbDefault = new RadioButton
            {
                Text = "Default  (reset to unflipped)",
                Location = new Point(15, 70),
                Size = new Size(260, 20)
            };

            grpDirection.Controls.Add(rbForward);
            grpDirection.Controls.Add(rbBack);
            grpDirection.Controls.Add(rbDefault);
            Controls.Add(grpDirection);
            y += 105;

            // ── Buttons (right-aligned with 10px gap) ──
            btnOK = new Button
            {
                Text = "Format Ticks",
                DialogResult = DialogResult.OK,
                Location = new Point(340 - 15 - 75 - 10 - 95, y),
                Size = new Size(95, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(340 - 15 - 75, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (rbForward.Checked)
                SymbolDirection = "Forward";
            else if (rbBack.Checked)
                SymbolDirection = "Back";
            else
                SymbolDirection = "Default";

            DialogMemory.Set(MemKey, "Direction", SymbolDirection);
            DialogMemory.Flush();
        }
    }
}

