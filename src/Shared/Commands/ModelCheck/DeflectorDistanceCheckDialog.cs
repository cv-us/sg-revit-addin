using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.ModelCheck
{
    /// <summary>
    /// Dialog for the Deflector Distance Check command.
    ///
    /// Collects:
    ///   - Distance type: Unobstructed (1-12"), Obstructed (1-22"), or Custom
    ///   - Sprinkler head height (inches)
    ///   - Annotation mode: All sprinklers or Exceeding only
    ///
    /// All inputs persist between runs via <see cref="DialogMemory"/>.
    /// </summary>
    public class DeflectorDistanceCheckDialog : DpiAwareForm
    {
        private const string MemKey = "DeflectorDistanceCheck";

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
            AllowResize = false;   // fixed option stack — resizing adds nothing
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 350);

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
                Size = new Size(510, 105)
            };

            rbUnobstructed = new RadioButton
            {
                Text = "Unobstructed construction — 1\" to 12\" (NFPA 13 Table 8.6.2.1.1)",
                Location = new Point(15, 22),
                Size = new Size(485, 20),
                Checked = true
            };
            rbObstructed = new RadioButton
            {
                Text = "Obstructed construction — 1\" to 22\" (NFPA 13 Table 8.6.2.1.1)",
                Location = new Point(15, 46),
                Size = new Size(485, 20)
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

            // Restore remembered distance mode + custom value.
            int remCustom = DialogMemory.GetInt(MemKey, "CustomMax", 12);
            if (remCustom >= (int)nudCustom.Minimum && remCustom <= (int)nudCustom.Maximum)
                nudCustom.Value = remCustom;
            int remMode = DialogMemory.GetInt(MemKey, "DistMode", 0);
            rbObstructed.Checked = remMode == 1;
            rbCustom.Checked = remMode == 2;   // fires CheckedChanged → enables nud
            rbUnobstructed.Checked = !rbObstructed.Checked && !rbCustom.Checked;

            grpDist.Controls.AddRange(new Control[] {
                rbUnobstructed, rbObstructed, rbCustom, nudCustom, lblInches });
            Controls.Add(grpDist);
            y += 115;

            // ── Head Height ──
            var grpHead = new GroupBox
            {
                Text = "Sprinkler Head Height (pipe center to deflector top)",
                Location = new Point(15, y),
                Size = new Size(510, 55)
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
            // Restore remembered head height (stored in tenths of an inch).
            int remHead = DialogMemory.GetInt(MemKey, "HeadHeightTenths", 30);
            decimal remHeadIn = remHead / 10m;
            if (remHeadIn >= nudHeadHeight.Minimum && remHeadIn <= nudHeadHeight.Maximum)
                nudHeadHeight.Value = remHeadIn;

            grpHead.Controls.AddRange(new Control[] { lblHead, nudHeadHeight, lblIn2 });
            Controls.Add(grpHead);
            y += 65;

            // ── Annotation Mode ──
            var grpAnnot = new GroupBox
            {
                Text = "Annotation Mode",
                Location = new Point(15, y),
                Size = new Size(510, 55)
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
            rbAnnotateAll.Checked = DialogMemory.GetBool(MemKey, "AnnotateAll", false);
            rbAnnotateExceeds.Checked = !rbAnnotateAll.Checked;
            grpAnnot.Controls.AddRange(new Control[] { rbAnnotateExceeds, rbAnnotateAll });
            Controls.Add(grpAnnot);
            y += 65;

            // ── Buttons (right-aligned with 10px gap, added left→right for tab order) ──
            // Form width 540, margin 15 → Cancel right edge at 525.
            var btnOK = new Button
            {
                Text = "Check",
                DialogResult = DialogResult.OK,
                Location = new Point(355, y),
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

                // Remember for next time.
                DialogMemory.SetInt(MemKey, "DistMode", rbObstructed.Checked ? 1 : rbCustom.Checked ? 2 : 0);
                DialogMemory.SetInt(MemKey, "CustomMax", (int)nudCustom.Value);
                DialogMemory.SetInt(MemKey, "HeadHeightTenths", (int)(nudHeadHeight.Value * 10m));
                DialogMemory.SetBool(MemKey, "AnnotateAll", AnnotateAll);
                DialogMemory.Flush();
            };
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(445, y),
                Size = new Size(80, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }
    }
}

