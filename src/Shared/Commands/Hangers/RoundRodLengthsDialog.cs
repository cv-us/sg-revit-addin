using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Round Rods Up command. Confirms the operation and
    /// offers a single option (keep Y Grip in sync). Last-used option is
    /// remembered via <see cref="DialogMemory"/>.
    /// </summary>
    public class RoundRodLengthsDialog : DpiAwareForm
    {
        private const string MemKey = "RoundRods";

        // ── Results ──
        public bool UpdateYGrip { get; private set; } = true;

        private CheckBox chkYGrip;

        private readonly int _hangerCount;
        private readonly int _willChange;

        public RoundRodLengthsDialog(int hangerCount, int willChange)
        {
            _hangerCount = hangerCount;
            _willChange = willChange;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Round Rod Lengths Up";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 237);

            const int Margin = 15;
            int y = Margin;

            // ── About ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(Margin, y),
                Size = new Size(450, 75)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Rounds each hanger's Rod Length UP to the nearest half inch.\n" +
                       "Rods already on a full or half inch are left alone. Rods are\n" +
                       "never rounded down — e.g. 8 41/256\" → 8 1/2\", 11 17/32\" → 12\".",
                Location = new Point(10, 18),
                Size = new Size(430, 50)
            });
            Controls.Add(grpInfo);
            y += 85;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(Margin, y),
                Size = new Size(450, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_hangerCount} hanger{(_hangerCount != 1 ? "s" : "")} selected — " +
                       $"{_willChange} not on a half inch (will round up).",
                Location = new Point(10, 20),
                Size = new Size(430, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 60;

            // ── Option (with memory) ──
            chkYGrip = new CheckBox
            {
                Text = "Also set Y Grip to match the new Rod Length",
                Location = new Point(Margin + 5, y),
                Size = new Size(445, 22),
                Checked = DialogMemory.GetBool(MemKey, "UpdateYGrip", true)
            };
            Controls.Add(chkYGrip);
            y += 32;

            // ── Buttons ──
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(480 - Margin - 75, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Round Up",
                DialogResult = DialogResult.OK,
                Location = new Point(480 - Margin - 75 - 10 - 95, y),
                Size = new Size(95, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            UpdateYGrip = chkYGrip.Checked;
            DialogMemory.SetBool(MemKey, "UpdateYGrip", UpdateYGrip);
            DialogMemory.Flush();
        }
    }
}
