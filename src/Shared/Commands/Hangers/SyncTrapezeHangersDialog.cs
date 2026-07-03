using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the AutoSync Trapeze Hangers command.
    ///
    /// Collects:
    ///   - Minimum clearance distance from structural to pipe (inches)
    ///   - Rod position: closest side or middle of structural elements
    ///
    /// Both inputs persist between runs via <see cref="DialogMemory"/>.
    /// </summary>
    public class SyncTrapezeHangersDialog : DpiAwareForm
    {
        private const string MemKey = "SyncTrapezeHangers";

        // ── Results ──
        public double MinClearanceInches { get; private set; } = 7.0;
        public bool UseClosestSide { get; private set; } = true;

        // ── Controls ──
        private NumericUpDown numClearance;
        private RadioButton rbClosest;
        private RadioButton rbMiddle;

        private readonly int _hangerCount;

        public SyncTrapezeHangersDialog(int hangerCount)
        {
            _hangerCount = hangerCount;
            MinClearanceInches = DialogMemory.GetDouble(MemKey, "MinClearanceInches", 7.0);
            UseClosestSide = DialogMemory.GetBool(MemKey, "UseClosestSide", true);
            AllowResize = false;   // fixed stack of options — resizing adds nothing
            InitializeComponent();
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void InitializeComponent()
        {
            Text = "AutoSync Trapeze Hangers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 320);

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
                Value = Clamp((decimal)MinClearanceInches, 0, 48),
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
                Checked = UseClosestSide
            };
            rbMiddle = new RadioButton
            {
                Text = "Middle of structural elements",
                Location = new Point(10, 40),
                Size = new Size(400, 20),
                Checked = !UseClosestSide
            };
            grpPosition.Controls.AddRange(new Control[] { rbClosest, rbMiddle });
            Controls.Add(grpPosition);
            y += 75;

            // ── Buttons (right-aligned) ──
            // Form width 460, margin 15 → Cancel right edge at 445.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(370, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Sync Trapeze",
                DialogResult = DialogResult.OK,
                Location = new Point(260, y),
                Size = new Size(100, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            MinClearanceInches = (double)numClearance.Value;
            UseClosestSide = rbClosest.Checked;

            // Remember for next time.
            DialogMemory.SetDouble(MemKey, "MinClearanceInches", MinClearanceInches);
            DialogMemory.SetBool(MemKey, "UseClosestSide", UseClosestSide);
            DialogMemory.Flush();
        }
    }
}

