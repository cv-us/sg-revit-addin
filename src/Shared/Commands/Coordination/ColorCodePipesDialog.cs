using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Dialog for the Color Code Pipes command.
    /// Lets the user choose: By Size, By Type, or Reset.
    /// The last-used mode is remembered via <see cref="DialogMemory"/>.
    /// </summary>
    public class ColorCodePipesDialog : DpiAwareForm
    {
        private const string MemKey = "ColorCodePipes";

        public enum ColorMode { BySize, ByType, Reset }
        public ColorMode SelectedMode { get; private set; } = ColorMode.BySize;

        private RadioButton rbBySize, rbByType, rbReset;

        public ColorCodePipesDialog(int pipeCount)
        {
            Text = "Color Code Pipes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 210);

            int y = 15;

            var lblInfo = new Label
            {
                Text = $"{pipeCount} pipes will be color-coded in the active view.",
                Location = new Point(15, y),
                AutoSize = true
            };
            Controls.Add(lblInfo);
            y += 30;

            var grp = new GroupBox
            {
                Text = "Color Mode",
                Location = new Point(15, y),
                Size = new Size(410, 110)
            };

            rbBySize = new RadioButton
            {
                Text = "By Size — color pipes by nominal diameter",
                Location = new Point(15, 22),
                Size = new Size(390, 20),
                Checked = true
            };
            rbByType = new RadioButton
            {
                Text = "By Type — color pipes by type name (threaded, welded, etc.)",
                Location = new Point(15, 48),
                Size = new Size(390, 20)
            };
            rbReset = new RadioButton
            {
                Text = "Reset — remove all color overrides from pipes",
                Location = new Point(15, 74),
                Size = new Size(390, 20)
            };
            grp.Controls.AddRange(new Control[] { rbBySize, rbByType, rbReset });
            Controls.Add(grp);
            y += 120;

            // Restore the last-used mode.
            int remMode = DialogMemory.GetInt(MemKey, "Mode", 0);
            rbByType.Checked = remMode == (int)ColorMode.ByType;
            rbReset.Checked = remMode == (int)ColorMode.Reset;
            rbBySize.Checked = !rbByType.Checked && !rbReset.Checked;

            // Buttons (right-aligned with 10px gap)
            // Form width 440, margin 15 → Cancel right edge at 425.
            var btnOK = new Button
            {
                Text = "Apply",
                DialogResult = DialogResult.OK,
                Location = new Point(260, y),
                Size = new Size(80, 30)
            };
            btnOK.Click += (s, e) =>
            {
                SelectedMode = rbBySize.Checked ? ColorMode.BySize :
                               rbByType.Checked ? ColorMode.ByType :
                               ColorMode.Reset;
                DialogMemory.SetInt(MemKey, "Mode", (int)SelectedMode);
                DialogMemory.Flush();
            };
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(350, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }
    }
}

