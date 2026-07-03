using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Dialog for the Export Trimble Points command.
    ///
    /// Collects:
    ///   - Scope: current selection, active view, or by level
    ///   - Coordinate basis: Shared (survey) or Project
    ///   - Coordinate order: Northing-Easting or Easting-Northing
    ///   - Units: US Feet or Meters
    ///   - Point name prefix
    ///   - Code/description value for the Code column
    ///   - Optional elevation datum offset
    /// </summary>
    public class ExportTrimblePointsDialog : DpiAwareForm
    {
        private const string MemKey = "ExportTrimblePoints";

        // ── Results ──
        public ScopeMode Scope { get; private set; } = ScopeMode.ActiveView;
        public int SelectedLevelIndex { get; private set; } = -1;
        public bool UseSharedCoordinates { get; private set; } = true;
        public bool NorthingFirst { get; private set; } = true;
        public bool UseFeet { get; private set; } = true;
        public string PointPrefix { get; private set; } = "H";
        public string PointCode { get; private set; } = "HANGER-INSERT";
        public double ElevationOffset { get; private set; } = 0;

        public enum ScopeMode { Selection, ActiveView, ByLevel }

        // ── Controls ──
        private RadioButton rbSelection;
        private RadioButton rbActiveView;
        private RadioButton rbByLevel;
        private ComboBox cboLevel;
        private RadioButton rbShared;
        private RadioButton rbProject;
        private RadioButton rbNorthingEasting;
        private RadioButton rbEastingNorthing;
        private RadioButton rbFeet;
        private RadioButton rbMeters;
        private TextBox txtPrefix;
        private TextBox txtCode;
        private TextBox txtElevOffset;

        private readonly int _preSelectionCount;
        private readonly List<string> _levelNames;

        public ExportTrimblePointsDialog(int preSelectionCount, List<string> levelNames)
        {
            _preSelectionCount = preSelectionCount;
            _levelNames = levelNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Export Trimble Layout Points";
            AllowResize = false;   // fixed option stack — resizing adds nothing
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 460);

            int margin = 15;
            int y = margin;

            // ── Scope ──
            var grpScope = new GroupBox
            {
                Text = "Hanger Scope",
                Location = new Point(margin, y),
                Size = new Size(450, 90)
            };

            rbSelection = new RadioButton
            {
                Text = $"Current selection ({_preSelectionCount} hanger{(_preSelectionCount != 1 ? "s" : "")})",
                Location = new Point(10, 18),
                Size = new Size(280, 20),
                Enabled = _preSelectionCount > 0,
                Checked = _preSelectionCount > 0
            };
            rbActiveView = new RadioButton
            {
                Text = "All hangers in active view",
                Location = new Point(10, 40),
                Size = new Size(200, 20),
                Checked = _preSelectionCount == 0
            };
            rbByLevel = new RadioButton
            {
                Text = "All hangers on level:",
                Location = new Point(10, 62),
                Size = new Size(150, 20)
            };
            cboLevel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(165, 60),
                Size = new Size(270, 24),
                Enabled = false
            };
            foreach (var name in _levelNames)
                cboLevel.Items.Add(name);
            if (cboLevel.Items.Count > 0) cboLevel.SelectedIndex = 0;

            rbByLevel.CheckedChanged += (s, e) => { cboLevel.Enabled = rbByLevel.Checked; };

            // Restore remembered scope (selection scope only if a selection exists)
            // and level (only if that level still exists in this model). The
            // radios aren't parented yet, so uncheck siblings explicitly.
            int remScope = DialogMemory.GetInt(MemKey, "Scope", -1);
            if (remScope == (int)ScopeMode.ActiveView)
            {
                rbSelection.Checked = false; rbByLevel.Checked = false; rbActiveView.Checked = true;
            }
            else if (remScope == (int)ScopeMode.ByLevel)
            {
                rbSelection.Checked = false; rbActiveView.Checked = false; rbByLevel.Checked = true;
            }
            else if (remScope == (int)ScopeMode.Selection && rbSelection.Enabled)
            {
                rbActiveView.Checked = false; rbByLevel.Checked = false; rbSelection.Checked = true;
            }
            int remLevel = cboLevel.Items.IndexOf(DialogMemory.Get(MemKey, "Level", ""));
            if (remLevel >= 0) cboLevel.SelectedIndex = remLevel;

            grpScope.Controls.AddRange(new Control[] { rbSelection, rbActiveView, rbByLevel, cboLevel });
            Controls.Add(grpScope);
            y += 100;

            // ── Coordinates ──
            var grpCoords = new GroupBox
            {
                Text = "Coordinate System",
                Location = new Point(margin, y),
                Size = new Size(450, 72)
            };

            bool remShared = DialogMemory.GetBool(MemKey, "Shared", true);
            rbShared = new RadioButton { Text = "Shared Coordinates (survey/real-world)", Location = new Point(10, 18), Size = new Size(280, 20), Checked = remShared };
            rbProject = new RadioButton { Text = "Project Internal Coordinates", Location = new Point(10, 40), Size = new Size(280, 20), Checked = !remShared };
            grpCoords.Controls.AddRange(new Control[] { rbShared, rbProject });
            Controls.Add(grpCoords);
            y += 82;

            // ── Order + Units (side by side) ──
            var grpOrder = new GroupBox
            {
                Text = "Coordinate Order",
                Location = new Point(margin, y),
                Size = new Size(220, 65)
            };
            bool remNorthing = DialogMemory.GetBool(MemKey, "NorthingFirst", true);
            rbNorthingEasting = new RadioButton { Text = "Northing, Easting", Location = new Point(10, 18), Size = new Size(180, 20), Checked = remNorthing };
            rbEastingNorthing = new RadioButton { Text = "Easting, Northing", Location = new Point(10, 40), Size = new Size(180, 20), Checked = !remNorthing };
            grpOrder.Controls.AddRange(new Control[] { rbNorthingEasting, rbEastingNorthing });
            Controls.Add(grpOrder);

            var grpUnits = new GroupBox
            {
                Text = "Units",
                Location = new Point(245, y),
                Size = new Size(220, 65)
            };
            bool remFeet = DialogMemory.GetBool(MemKey, "Feet", true);
            rbFeet = new RadioButton { Text = "US Feet", Location = new Point(10, 18), Size = new Size(120, 20), Checked = remFeet };
            rbMeters = new RadioButton { Text = "Meters", Location = new Point(10, 40), Size = new Size(120, 20), Checked = !remFeet };
            grpUnits.Controls.AddRange(new Control[] { rbFeet, rbMeters });
            Controls.Add(grpUnits);
            y += 75;

            // ── Point Naming ──
            var grpNaming = new GroupBox
            {
                Text = "Point Naming",
                Location = new Point(margin, y),
                Size = new Size(450, 55)
            };
            grpNaming.Controls.Add(new Label { Text = "Prefix:", Location = new Point(10, 22), Size = new Size(45, 18) });
            txtPrefix = new TextBox { Text = DialogMemory.Get(MemKey, "Prefix", "H"), Location = new Point(60, 19), Size = new Size(60, 22) };
            grpNaming.Controls.Add(txtPrefix);

            grpNaming.Controls.Add(new Label { Text = "Code:", Location = new Point(140, 22), Size = new Size(40, 18) });
            txtCode = new TextBox { Text = DialogMemory.Get(MemKey, "Code", "HANGER-INSERT"), Location = new Point(185, 19), Size = new Size(250, 22) };
            grpNaming.Controls.Add(txtCode);
            Controls.Add(grpNaming);
            y += 65;

            // ── Elevation Offset ──
            var grpElev = new GroupBox
            {
                Text = "Elevation Datum Offset (optional)",
                Location = new Point(margin, y),
                Size = new Size(450, 70)
            };
            grpElev.Controls.Add(new Label
            {
                Text = "Add to all elevations:",
                Location = new Point(10, 22),
                Size = new Size(130, 18)
            });
            txtElevOffset = new TextBox { Text = DialogMemory.Get(MemKey, "ElevOffset", "0"), Location = new Point(145, 19), Size = new Size(80, 22) };
            grpElev.Controls.Add(txtElevOffset);
            grpElev.Controls.Add(new Label
            {
                Text = "feet",
                Location = new Point(230, 22),
                Size = new Size(40, 18)
            });
            grpElev.Controls.Add(new Label
            {
                Text = "e.g. if project 0'-0\" = real-world 1272.35', enter 1272.35",
                Location = new Point(10, 45),
                Size = new Size(430, 18),
                ForeColor = SystemColors.GrayText
            });
            Controls.Add(grpElev);
            y += 78;

            // ── Buttons (right-aligned with 10px gap, added left→right for tab order) ──
            // Form width 480, margin 15 → Cancel right edge at 465.
            var btnOK = new Button
            {
                Text = "Export CSV",
                DialogResult = DialogResult.OK,
                Location = new Point(280, y),
                Size = new Size(100, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(390, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            // Validate the elevation offset before anything else — a typo here
            // would silently shift every exported point.
            string offsetText = txtElevOffset.Text.Trim();
            double offset = 0;
            if (offsetText.Length > 0 && !double.TryParse(offsetText, out offset))
            {
                MessageBox.Show(this,
                    "Elevation datum offset must be a number (e.g. 1272.35), or blank for 0.",
                    "Export Trimble Points", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;   // keep the dialog open
                return;
            }
            ElevationOffset = offset;

            if (rbSelection.Checked) Scope = ScopeMode.Selection;
            else if (rbActiveView.Checked) Scope = ScopeMode.ActiveView;
            else Scope = ScopeMode.ByLevel;

            SelectedLevelIndex = cboLevel.SelectedIndex;
            UseSharedCoordinates = rbShared.Checked;
            NorthingFirst = rbNorthingEasting.Checked;
            UseFeet = rbFeet.Checked;
            PointPrefix = txtPrefix.Text.Trim();
            PointCode = txtCode.Text.Trim();

            if (string.IsNullOrEmpty(PointPrefix))
                PointPrefix = "H";

            // Remember for next time.
            DialogMemory.SetInt(MemKey, "Scope", (int)Scope);
            DialogMemory.Set(MemKey, "Level", cboLevel.SelectedItem?.ToString() ?? "");
            DialogMemory.SetBool(MemKey, "Shared", UseSharedCoordinates);
            DialogMemory.SetBool(MemKey, "NorthingFirst", NorthingFirst);
            DialogMemory.SetBool(MemKey, "Feet", UseFeet);
            DialogMemory.Set(MemKey, "Prefix", PointPrefix);
            DialogMemory.Set(MemKey, "Code", PointCode);
            DialogMemory.Set(MemKey, "ElevOffset", offsetText.Length > 0 ? offsetText : "0");
            DialogMemory.Flush();
        }
    }
}

