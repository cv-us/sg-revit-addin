using System;
using System.Drawing;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// Dialog for the AutoSync Hangers to Structural Elements (Surface) command.
    ///
    /// Collects:
    ///   - Hanger type codes per structural category
    ///   - Clash height distance (vertical search range in feet)
    ///   - Framing offset distance (in inches)
    ///   - Framing sync direction (top or bottom)
    ///   - Whether to keep existing hanger types
    ///
    /// Migrated from: "AutoSync - Hangers To Structural Elements.dyn" (V43)
    /// </summary>
    public class AutoSyncHangersToStructuralSurfaceDialog : Form
    {
        // ── Results ──
        public string TypeCodeFloors { get; private set; }
        public string TypeCodeStairs { get; private set; }
        public string TypeCodeRoofs { get; private set; }
        public string TypeCodeFraming { get; private set; }
        public double ClashHeightFeet { get; private set; }
        public double FramingOffsetInches { get; private set; }
        public bool FramingSyncToBottom { get; private set; }
        public bool KeepHangerTypes { get; private set; }

        // ── Controls ──
        private TextBox txtFloors;
        private TextBox txtStairs;
        private TextBox txtRoofs;
        private TextBox txtFraming;
        private NumericUpDown nudClashHeight;
        private NumericUpDown nudFramingOffset;
        private ComboBox cboFramingSync;
        private CheckBox chkKeepTypes;

        private readonly int _hangerCount;

        public AutoSyncHangersToStructuralSurfaceDialog(
            int hangerCount,
            string defaultFloors, string defaultStairs,
            string defaultRoofs, string defaultFraming,
            double defaultClashHeight, double defaultFramingOffset,
            bool defaultSyncToBottom, bool defaultKeepTypes)
        {
            _hangerCount = hangerCount;
            TypeCodeFloors = defaultFloors;
            TypeCodeStairs = defaultStairs;
            TypeCodeRoofs = defaultRoofs;
            TypeCodeFraming = defaultFraming;
            ClashHeightFeet = defaultClashHeight;
            FramingOffsetInches = defaultFramingOffset;
            FramingSyncToBottom = defaultSyncToBottom;
            KeepHangerTypes = defaultKeepTypes;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "AutoSync Hangers to Structural (Surface Intersection)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 530);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(470, 55)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Syncs hanger rod lengths to the closest underside surface of structural\n" +
                       "elements above (floors, roofs, framing, stairs — including linked models).",
                Location = new Point(10, 18),
                Size = new Size(450, 32)
            });
            Controls.Add(grpInfo);
            y += 65;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(470, 45)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_hangerCount} pipe hanger{(_hangerCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 18),
                Size = new Size(450, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 55;

            // ── Search Parameters ──
            var grpSearch = new GroupBox
            {
                Text = "Search Parameters",
                Location = new Point(margin, y),
                Size = new Size(470, 100)
            };

            int lx = 10, tx = 250;
            int gy = 22;

            grpSearch.Controls.Add(new Label
            {
                Text = "Clash Height Distance (feet):",
                Location = new Point(lx, gy + 3),
                Size = new Size(230, 18)
            });
            nudClashHeight = new NumericUpDown
            {
                Location = new Point(tx, gy),
                Size = new Size(80, 22),
                Minimum = 1, Maximum = 50, DecimalPlaces = 1,
                Value = (decimal)ClashHeightFeet
            };
            grpSearch.Controls.Add(nudClashHeight);

            gy += 28;
            grpSearch.Controls.Add(new Label
            {
                Text = "Framing Offset Distance (inches):",
                Location = new Point(lx, gy + 3),
                Size = new Size(230, 18)
            });
            nudFramingOffset = new NumericUpDown
            {
                Location = new Point(tx, gy),
                Size = new Size(80, 22),
                Minimum = 0, Maximum = 24, DecimalPlaces = 1,
                Value = (decimal)FramingOffsetInches
            };
            grpSearch.Controls.Add(nudFramingOffset);

            gy += 28;
            grpSearch.Controls.Add(new Label
            {
                Text = "Framing Hangers Sync'd To:",
                Location = new Point(lx, gy + 3),
                Size = new Size(230, 18)
            });
            cboFramingSync = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(tx, gy),
                Size = new Size(200, 22)
            };
            cboFramingSync.Items.Add("BOTTOM where possible (Default)");
            cboFramingSync.Items.Add("TOP of Structural Elements");
            cboFramingSync.SelectedIndex = FramingSyncToBottom ? 0 : 1;
            grpSearch.Controls.Add(cboFramingSync);

            Controls.Add(grpSearch);
            y += 110;

            // ── Hanger Type Codes ──
            var grpTypes = new GroupBox
            {
                Text = "Hanger Assembly Type Codes (Hydratec)",
                Location = new Point(margin, y),
                Size = new Size(470, 120)
            };

            int tw = 60, rowH = 24;
            gy = 22;
            grpTypes.Controls.Add(new Label { Text = "Floors:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtFloors = new TextBox { Text = TypeCodeFloors, Location = new Point(150, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtFloors);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Stairs:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtStairs = new TextBox { Text = TypeCodeStairs, Location = new Point(150, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtStairs);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Roofs:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtRoofs = new TextBox { Text = TypeCodeRoofs, Location = new Point(150, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtRoofs);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Structural Framing:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtFraming = new TextBox { Text = TypeCodeFraming, Location = new Point(150, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtFraming);

            Controls.Add(grpTypes);
            y += 130;

            // ── Keep Types ──
            chkKeepTypes = new CheckBox
            {
                Text = "Keep existing Hanger Types and Comments — only adjust Rod Lengths",
                Location = new Point(margin + 5, y),
                Size = new Size(460, 20),
                Checked = KeepHangerTypes
            };
            chkKeepTypes.CheckedChanged += (s, e) =>
            {
                bool disabled = chkKeepTypes.Checked;
                txtFloors.Enabled = !disabled;
                txtStairs.Enabled = !disabled;
                txtRoofs.Enabled = !disabled;
                txtFraming.Enabled = !disabled;
            };
            // Apply initial state
            if (KeepHangerTypes)
            {
                txtFloors.Enabled = false;
                txtStairs.Enabled = false;
                txtRoofs.Enabled = false;
                txtFraming.Enabled = false;
            }
            Controls.Add(chkKeepTypes);
            y += 35;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Sync Rod Lengths",
                DialogResult = DialogResult.OK,
                Location = new Point(275, y),
                Size = new Size(120, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(400, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            TypeCodeFloors = txtFloors.Text.Trim();
            TypeCodeStairs = txtStairs.Text.Trim();
            TypeCodeRoofs = txtRoofs.Text.Trim();
            TypeCodeFraming = txtFraming.Text.Trim();
            ClashHeightFeet = (double)nudClashHeight.Value;
            FramingOffsetInches = (double)nudFramingOffset.Value;
            FramingSyncToBottom = cboFramingSync.SelectedIndex == 0;
            KeepHangerTypes = chkKeepTypes.Checked;
        }
    }
}
