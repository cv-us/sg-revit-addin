using System;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Flexible Drop Lengths (Dalmatian Fire Style) command.
    ///
    /// Collects:
    ///   - System type: Wet or Dry (changes length thresholds)
    ///   - Tag orientation (N, NE, E, SE, S, SW, W, NW)
    ///
    /// Unlike the standard InsertFlexDropLengthsDialog, this does NOT ask the user
    /// to pick a length — lengths are auto-calculated from the actual flex pipe
    /// connected to each sprinkler.
    ///
    /// Wet system thresholds:
    ///   Flex pipe <= 3'-6"  → "48"
    ///   Flex pipe <= 4'-6"  → "60"
    ///   Flex pipe <= 5'-6"  → "72"
    ///   Flex pipe > 5'-6"   → flagged as exceeding max
    ///
    /// Dry system thresholds:
    ///   Flex pipe <= 2'-8"  → "38"
    ///   Flex pipe <= 3'-8"  → "50"
    ///   Flex pipe <= 4'-4"  → "58"
    ///   Flex pipe > 4'-4"   → flagged as exceeding max
    /// </summary>
    public class FlexDropLengthsDalmatianDialog : Form
    {
        // ── Results ──
        public bool IsWetSystem { get; private set; } = true;
        public string TagOrientation { get; private set; } = "NE";

        // ── Controls ──
        private RadioButton rbWet;
        private RadioButton rbDry;
        private ComboBox cboOrientation;

        private readonly int _sprinklerCount;

        public FlexDropLengthsDalmatianDialog(int sprinklerCount)
        {
            _sprinklerCount = sprinklerCount;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Auto-Populate Flexible Drop Lengths (Dalmatian)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(450, 400);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(420, 55)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Reads the actual flex pipe length connected to each sprinkler and\n" +
                       "assigns the correct standard drop length based on Wet or Dry thresholds.",
                Location = new Point(10, 18),
                Size = new Size(400, 32)
            });
            Controls.Add(grpInfo);
            y += 60;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(420, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_sprinklerCount} sprinkler{(_sprinklerCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 20),
                Size = new Size(400, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 55;

            // ── System Type ──
            var grpSystem = new GroupBox
            {
                Text = "System Type",
                Location = new Point(margin, y),
                Size = new Size(420, 65)
            };
            rbWet = new RadioButton
            {
                Text = "Wet  (48\" / 60\" / 72\"  —  max 5'-6\")",
                Location = new Point(10, 18),
                Size = new Size(400, 20),
                Checked = true
            };
            rbDry = new RadioButton
            {
                Text = "Dry  (38\" / 50\" / 58\"  —  max 4'-4\")",
                Location = new Point(10, 40),
                Size = new Size(400, 20)
            };
            grpSystem.Controls.AddRange(new Control[] { rbWet, rbDry });
            Controls.Add(grpSystem);
            y += 70;

            // ── Length Reference ──
            var grpRef = new GroupBox
            {
                Text = "Standard Length Thresholds",
                Location = new Point(margin, y),
                Size = new Size(420, 80)
            };
            grpRef.Controls.Add(new Label
            {
                Text = "Wet:  pipe <= 3'-6\" → 48\"   |   <= 4'-6\" → 60\"   |   <= 5'-6\" → 72\"\n" +
                       "Dry:   pipe <= 2'-8\" → 38\"   |   <= 3'-8\" → 50\"   |   <= 4'-4\" → 58\"\n\n" +
                       "Pipes exceeding the max length will be flagged for review.",
                Location = new Point(10, 18),
                Size = new Size(400, 55),
                ForeColor = SystemColors.GrayText
            });
            Controls.Add(grpRef);
            y += 85;

            // ── Tag Orientation ──
            var grpOrient = new GroupBox
            {
                Text = "Tag Orientation",
                Location = new Point(margin, y),
                Size = new Size(420, 50)
            };
            grpOrient.Controls.Add(new Label
            {
                Text = "Direction:",
                Location = new Point(10, 22),
                Size = new Size(70, 18)
            });
            cboOrientation = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(85, 19),
                Size = new Size(80, 22)
            };
            cboOrientation.Items.AddRange(new object[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" });
            cboOrientation.SelectedIndex = 1; // NE default
            grpOrient.Controls.Add(cboOrientation);
            Controls.Add(grpOrient);
            y += 60;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Insert Tags",
                DialogResult = DialogResult.OK,
                Location = new Point(260, y),
                Size = new Size(100, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(365, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            IsWetSystem = rbWet.Checked;
            TagOrientation = cboOrientation.SelectedItem?.ToString() ?? "NE";
        }
    }
}

