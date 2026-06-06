using System;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Flex Drops Auto command — auto-sizes each sprinkler's
    /// flex drop tag to the standard that fits its measured flex pipe length.
    ///
    /// Collects:
    ///   - System type: Wet or Dry (changes length thresholds and max).
    ///   - Tag orientation (N, NE, E, SE, S, SW, W, NW).
    ///
    /// Unlike <see cref="FlexDropLengthsDialog"/> (the "Set" command), this
    /// does NOT ask the user to pick one length — each sprinkler's tag is
    /// auto-sized from its actual connected flex pipe.
    ///
    /// Wet thresholds (5'-6" max):
    ///   ≤ 3'-6" → "48"   ≤ 4'-6" → "60"   ≤ 5'-6" → "72"   > flagged
    ///
    /// Dry thresholds (4'-4" max):
    ///   ≤ 2'-8" → "38"   ≤ 3'-8" → "50"   ≤ 4'-4" → "58"   > flagged
    /// </summary>
    public class FlexDropLengthsAutoDialog : Form
    {
        // ── Results ──
        public bool IsWetSystem { get; private set; } = true;
        public string TagOrientation { get; private set; } = "NE";

        // ── Controls ──
        private RadioButton rbWet;
        private RadioButton rbDry;
        private ComboBox cboOrientation;

        private readonly int _sprinklerCount;

        public FlexDropLengthsAutoDialog(int sprinklerCount)
        {
            _sprinklerCount = sprinklerCount;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Flex Drops Auto — Auto-Size from Connected Flex Pipe";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 400);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(490, 55)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Reads each sprinkler's actual connected flex pipe and auto-sizes\n" +
                       "its drop-length tag to the matching Wet or Dry standard.",
                Location = new Point(10, 18),
                Size = new Size(470, 32)
            });
            Controls.Add(grpInfo);
            y += 60;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(490, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_sprinklerCount} sprinkler{(_sprinklerCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 20),
                Size = new Size(470, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 55;

            // ── System Type ──
            var grpSystem = new GroupBox
            {
                Text = "System Type",
                Location = new Point(margin, y),
                Size = new Size(490, 65)
            };
            rbWet = new RadioButton
            {
                Text = "Wet  (48\" / 60\" / 72\"  —  max 5'-6\")",
                Location = new Point(10, 18),
                Size = new Size(470, 20),
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
                Size = new Size(490, 80)
            };
            grpRef.Controls.Add(new Label
            {
                Text = "Wet:  pipe <= 3'-6\" → 48\"   |   <= 4'-6\" → 60\"   |   <= 5'-6\" → 72\"\n" +
                       "Dry:   pipe <= 2'-8\" → 38\"   |   <= 3'-8\" → 50\"   |   <= 4'-4\" → 58\"\n\n" +
                       "Pipes exceeding the max length will be flagged for review.",
                Location = new Point(10, 18),
                Size = new Size(470, 55),
                ForeColor = SystemColors.GrayText
            });
            Controls.Add(grpRef);
            y += 85;

            // ── Tag Orientation ──
            var grpOrient = new GroupBox
            {
                Text = "Tag Orientation",
                Location = new Point(margin, y),
                Size = new Size(490, 50)
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

            // ── Buttons (right-aligned) ──
            // Form width 520, margin 15 → Cancel right edge at 505.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(430, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Insert Tags",
                DialogResult = DialogResult.OK,
                Location = new Point(320, y),
                Size = new Size(100, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            IsWetSystem = rbWet.Checked;
            TagOrientation = cboOrientation.SelectedItem?.ToString() ?? "NE";
        }
    }
}

