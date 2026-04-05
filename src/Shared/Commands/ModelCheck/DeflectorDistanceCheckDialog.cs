using System.Drawing;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.ModelCheck
{
    /// <summary>
    /// Dialog for the Deflector Distance Check command.
    ///
    /// Collects:
    ///   - Distance type: Unobstructed (1-12"), Obstructed (1-22"), or Custom
    ///   - Sprinkler head height (inches)
    ///   - Annotation mode: All sprinklers or Exceeding only
    /// </summary>
    public class DeflectorDistanceCheckDialog : Form
    {
        /// <summary>Maximum allowable deflector distance in feet.</summary>
        public double MaxDistance { get; private set; } = 12.0 / 12.0; // 12 inches

        /// <summary>Sprinkler head height from pipe center to deflector top, in feet.</summary>
        public double HeadHeight { get; private set; } = 3.0 / 12.0; // 3 inches default

        /// <summary>If true, annotate all sprinklers. If false, only annotate exceeding ones.</summary>
        public bool AnnotateAll { get; private set; } = false;

        private RadioButton rbUnobstructed, rbObstructed, rbCustom;
        private NumericUpDown nudCustom, nudHeadHeight;
        private RadioButton rbAnnotateExceeds, rbAnnotateAll;

        public DeflectorDistanceCheckDialog(int sprinklerCount)
        {
            Text = "Deflector Distance Check";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 350);

            int y = 15;

            var lblInfo = new Label
            {
                Text = $"{sprinklerCount} upright sprinklers found in the active view.",
                Location = new Point(15, y),
                AutoSize = true
            };
            Controls.Add(lblInfo);
            y += 30;

            // ── Distance Type ──
            var grpDist = new GroupBox
            {
                Text = "NFPA 13 Maximum Deflector Distance",
                Location = new Point(15, y),
                Size = new Size(370, 105)
            };

            rbUnobstructed = new RadioButton
            {
                Text = "Unobstructed construction — 1\" to 12\" (NFPA 13 Table 8.6.2.1.1)",
                Location = new Point(15, 22),
                Size = new Size(340, 20),
                Checked = true
            };
            rbObstructed = new RadioButton
            {
                Text = "Obstructed construction — 1\" to 22\" (NFPA 13 Table 8.6.2.1.1)",
                Location = new Point(15, 46),
                Size = new Size(340, 20)
            };
            rbCustom = new RadioButton
            {
                Text = "Custom maximum:",
                Location = new Point(15, 70),
                Size = new Size(140, 20)
            };

            nudCustom = new NumericUpDown
            {
                Location = new Point(160, 68),
                Size = new Size(60, 22),
                Minimum = 1,
                Maximum = 48,
                Value = 12,
                Enabled = false,
                DecimalPlaces = 0
            };
            var lblInches = new Label
            {
                Text = "inches",
                Location = new Point(225, 72),
                AutoSize = true
            };

            rbCustom.CheckedChanged += (s, e) => nudCustom.Enabled = rbCustom.Checked;

            grpDist.Controls.AddRange(new Control[] {
                rbUnobstructed, rbObstructed, rbCustom, nudCustom, lblInches });
            Controls.Add(grpDist);
            y += 115;

            // ── Head Height ──
            var grpHead = new GroupBox
            {
                Text = "Sprinkler Head Height (pipe center to deflector top)",
                Location = new Point(15, y),
                Size = new Size(370, 55)
            };

            var lblHead = new Label
            {
                Text = "Head height:",
                Location = new Point(15, 24),
                AutoSize = true
            };
            nudHeadHeight = new NumericUpDown
            {
                Location = new Point(100, 22),
                Size = new Size(60, 22),
                Minimum = 0,
                Maximum = 24,
                Value = 3,
                DecimalPlaces = 1,
                Increment = 0.5M
            };
            var lblIn2 = new Label
            {
                Text = "inches (typical 2.5\" - 4\" depending on head model)",
                Location = new Point(165, 24),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            grpHead.Controls.AddRange(new Control[] { lblHead, nudHeadHeight, lblIn2 });
            Controls.Add(grpHead);
            y += 65;

            // ── Annotation Mode ──
            var grpAnnot = new GroupBox
            {
                Text = "Annotation Mode",
                Location = new Point(15, y),
                Size = new Size(370, 55)
            };
            rbAnnotateExceeds = new RadioButton
            {
                Text = "Annotate exceeding sprinklers only",
                Location = new Point(15, 22),
                Size = new Size(250, 20),
                Checked = true
            };
            rbAnnotateAll = new RadioButton
            {
                Text = "Annotate all sprinklers",
                Location = new Point(270, 22),
                Size = new Size(150, 20)
            };
            grpAnnot.Controls.AddRange(new Control[] { rbAnnotateExceeds, rbAnnotateAll });
            Controls.Add(grpAnnot);
            y += 65;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Check",
                DialogResult = DialogResult.OK,
                Location = new Point(220, y),
                Size = new Size(80, 30)
            };
            btnOK.Click += (s, e) =>
            {
                if (rbUnobstructed.Checked)
                    MaxDistance = 12.0 / 12.0;
                else if (rbObstructed.Checked)
                    MaxDistance = 22.0 / 12.0;
                else
                    MaxDistance = (double)nudCustom.Value / 12.0;

                HeadHeight = (double)nudHeadHeight.Value / 12.0;
                AnnotateAll = rbAnnotateAll.Checked;
            };
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(305, y),
                Size = new Size(80, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }
    }
}
