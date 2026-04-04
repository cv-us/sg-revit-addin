using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.ViewsAndSheets
{
    /// <summary>
    /// Dialog for the Rotate Scope Box command.
    ///
    /// Collects:
    ///   - Angle source: local grid, linked grid, or manual angle
    ///   - Grid selection (for grid-based rotation)
    ///   - Manual angle (for manual entry)
    ///
    /// Migrated from: "RotateScopeBox.dyn"
    /// </summary>
    public class RotateScopeBoxDialog : Form
    {
        // ── Results ──
        public enum AngleSourceOption { LocalGrid, LinkedGrid, ManualAngle }
        public AngleSourceOption AngleSource { get; private set; } = AngleSourceOption.LocalGrid;
        public string SelectedGridName { get; private set; } = "";
        public double ManualAngleDegrees { get; private set; } = 0;

        // ── Controls ──
        private RadioButton rbLocalGrid;
        private RadioButton rbLinkedGrid;
        private RadioButton rbManual;
        private ComboBox cboGrids;
        private NumericUpDown numAngle;
        private Label lblGridNote;

        private readonly string _scopeBoxName;
        private readonly IList<string> _localGridNames;

        public RotateScopeBoxDialog(string scopeBoxName, IList<string> localGridNames)
        {
            _scopeBoxName = scopeBoxName;
            _localGridNames = localGridNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Rotate Scope Box";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 340);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(390, 50)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Rotates a scope box to match the angle of a selected grid line.\n" +
                       "Works with local grids, linked grids, or a manual angle.",
                Location = new Point(10, 16),
                Size = new Size(370, 30)
            });
            Controls.Add(grpInfo);
            y += 55;

            // ── Scope Box ──
            var grpScope = new GroupBox
            {
                Text = "Scope Box",
                Location = new Point(margin, y),
                Size = new Size(390, 45)
            };
            grpScope.Controls.Add(new Label
            {
                Text = $"Selected: {_scopeBoxName}",
                Location = new Point(10, 18),
                Size = new Size(370, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpScope);
            y += 50;

            // ── Angle Source ──
            var grpAngle = new GroupBox
            {
                Text = "Rotation Angle Source",
                Location = new Point(margin, y),
                Size = new Size(390, 140)
            };

            rbLocalGrid = new RadioButton
            {
                Text = "Match local grid:",
                Location = new Point(10, 20),
                Size = new Size(140, 20),
                Checked = true
            };
            rbLocalGrid.CheckedChanged += AngleSource_Changed;

            cboGrids = new ComboBox
            {
                Location = new Point(155, 18),
                Size = new Size(220, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var name in _localGridNames)
                cboGrids.Items.Add(name);
            if (cboGrids.Items.Count > 0)
                cboGrids.SelectedIndex = 0;

            rbLinkedGrid = new RadioButton
            {
                Text = "Match linked grid (select after OK)",
                Location = new Point(10, 50),
                Size = new Size(360, 20)
            };
            rbLinkedGrid.CheckedChanged += AngleSource_Changed;

            lblGridNote = new Label
            {
                Text = "You will be prompted to pick a linked grid element.",
                Location = new Point(30, 72),
                Size = new Size(350, 16),
                ForeColor = SystemColors.GrayText
            };

            rbManual = new RadioButton
            {
                Text = "Manual angle (degrees):",
                Location = new Point(10, 95),
                Size = new Size(160, 20)
            };
            rbManual.CheckedChanged += AngleSource_Changed;

            numAngle = new NumericUpDown
            {
                Minimum = -360,
                Maximum = 360,
                DecimalPlaces = 2,
                Increment = 0.5m,
                Value = 0,
                Location = new Point(175, 93),
                Size = new Size(80, 22),
                Enabled = false
            };

            grpAngle.Controls.AddRange(new Control[]
                { rbLocalGrid, cboGrids, rbLinkedGrid, lblGridNote, rbManual, numAngle });
            Controls.Add(grpAngle);
            y += 150;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Rotate",
                DialogResult = DialogResult.OK,
                Location = new Point(240, y),
                Size = new Size(90, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(335, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void AngleSource_Changed(object sender, EventArgs e)
        {
            cboGrids.Enabled = rbLocalGrid.Checked;
            numAngle.Enabled = rbManual.Checked;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (rbLocalGrid.Checked)
            {
                AngleSource = AngleSourceOption.LocalGrid;
                SelectedGridName = cboGrids.SelectedItem?.ToString() ?? "";
            }
            else if (rbLinkedGrid.Checked)
            {
                AngleSource = AngleSourceOption.LinkedGrid;
            }
            else
            {
                AngleSource = AngleSourceOption.ManualAngle;
                ManualAngleDegrees = (double)numAngle.Value;
            }
        }
    }
}
