using System;
using System.Drawing;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Dialog for the AutoSync Trapeze Hangers command.
    ///
    /// Collects:
    ///   - Minimum clearance distance from structural to pipe (inches)
    ///   - Rod position: closest side or middle of structural elements
    ///
    /// Migrated from: "AutoSync - Trapeze Hangers.dyn"
    /// </summary>
    public class AutoSyncTrapezeHangersDialog : Form
    {
        // ── Results ──
        public double MinClearanceInches { get; private set; } = 7.0;
        public bool UseClosestSide { get; private set; } = true;

        // ── Controls ──
        private NumericUpDown numClearance;
        private RadioButton rbClosest;
        private RadioButton rbMiddle;

        private readonly int _hangerCount;

        public AutoSyncTrapezeHangersDialog(int hangerCount)
        {
            _hangerCount = hangerCount;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "AutoSync Trapeze Hangers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 310);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(430, 55)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Synchronizes trapeze hanger parameters (rod lengths, offsets, rotation,\n" +
                       "pipe diameter) to the closest pipe and structural elements above.",
                Location = new Point(10, 18),
                Size = new Size(410, 32)
            });
            Controls.Add(grpInfo);
            y += 65;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(430, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_hangerCount} trapeze hanger{(_hangerCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 20),
                Size = new Size(410, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 60;

            // ── Min Clearance ──
            var grpClearance = new GroupBox
            {
                Text = "Minimum Distance Down to Trapeze Pipe",
                Location = new Point(margin, y),
                Size = new Size(430, 50)
            };
            grpClearance.Controls.Add(new Label
            {
                Text = "Clearance (inches):",
                Location = new Point(10, 22),
                Size = new Size(120, 18)
            });
            numClearance = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 48,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 7,
                Location = new Point(135, 19),
                Size = new Size(70, 22)
            };
            grpClearance.Controls.Add(numClearance);
            Controls.Add(grpClearance);
            y += 60;

            // ── Rod Position ──
            var grpPosition = new GroupBox
            {
                Text = "Rod Position on Structural Elements",
                Location = new Point(margin, y),
                Size = new Size(430, 65)
            };
            rbClosest = new RadioButton
            {
                Text = "Closest side of structural elements (default)",
                Location = new Point(10, 18),
                Size = new Size(400, 20),
                Checked = true
            };
            rbMiddle = new RadioButton
            {
                Text = "Middle of structural elements",
                Location = new Point(10, 40),
                Size = new Size(400, 20)
            };
            grpPosition.Controls.AddRange(new Control[] { rbClosest, rbMiddle });
            Controls.Add(grpPosition);
            y += 75;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Sync Trapeze",
                DialogResult = DialogResult.OK,
                Location = new Point(270, y),
                Size = new Size(100, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(375, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            MinClearanceInches = (double)numClearance.Value;
            UseClosestSide = rbClosest.Checked;
        }
    }
}
