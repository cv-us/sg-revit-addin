using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Pipe Sleeves at Intersecting Walls command.
    ///
    /// Collects:
    ///   - Linked model selection
    ///   - Seismic area (Non-Seismic / Seismic)
    ///   - Wall type filtering (All, or filter by Interior/Exterior/Fire Rated/Structural)
    ///   - Selected wall types from filtered results
    /// </summary>
    public class PipeSleevesAtWallsDialog : Form
    {
        // ── Results ──
        public int SelectedLinkIndex { get; private set; } = -1;
        public bool IsSeismic { get; private set; } = false;
        public bool UseAllWalls { get; private set; } = true;
        public List<string> SelectedWallTypes { get; private set; } = new List<string>();

        // ── Controls ──
        private ComboBox cboLink;
        private RadioButton rbNonSeismic, rbSeismic;
        private RadioButton rbAllWalls, rbFilterWalls;
        private CheckBox chkInterior, chkExterior, chkFireRated, chkStructural;
        private CheckedListBox clbWallTypes;
        private Button btnApplyFilter, btnSelectAll, btnSelectNone;
        private Panel pnlFilters;

        private readonly List<string> _linkNames;
        private readonly List<string> _allWallTypeNames;

        public PipeSleevesAtWallsDialog(List<string> linkNames, List<string> wallTypeNames)
        {
            _linkNames = linkNames;
            _allWallTypeNames = wallTypeNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Auto-Populate Pipe Sleeves at Intersecting Walls";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 530);

            int margin = 15;
            int y = margin;

            // ── Link Selection ──
            var grpLink = new GroupBox
            {
                Text = "Linked Model",
                Location = new Point(margin, y),
                Size = new Size(470, 55)
            };
            var lblLink = new Label
            {
                Text = "Structural/Arch Link:",
                Location = new Point(10, 23),
                Size = new Size(130, 20)
            };
            cboLink = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(145, 20),
                Size = new Size(310, 24)
            };
            foreach (var name in _linkNames)
                cboLink.Items.Add(name);
            if (cboLink.Items.Count > 0) cboLink.SelectedIndex = 0;
            grpLink.Controls.AddRange(new Control[] { lblLink, cboLink });
            Controls.Add(grpLink);
            y += 65;

            // ── Seismic Area ──
            var grpSeismic = new GroupBox
            {
                Text = "Seismic Area",
                Location = new Point(margin, y),
                Size = new Size(470, 50)
            };
            rbNonSeismic = new RadioButton
            {
                Text = "Non-Seismic",
                Location = new Point(15, 20),
                Size = new Size(120, 20),
                Checked = true
            };
            rbSeismic = new RadioButton
            {
                Text = "Seismic",
                Location = new Point(160, 20),
                Size = new Size(120, 20)
            };
            grpSeismic.Controls.AddRange(new Control[] { rbNonSeismic, rbSeismic });
            Controls.Add(grpSeismic);
            y += 60;

            // ── Wall Types ──
            var grpWalls = new GroupBox
            {
                Text = "Wall Types",
                Location = new Point(margin, y),
                Size = new Size(470, 310)
            };

            rbAllWalls = new RadioButton
            {
                Text = "All intersecting walls",
                Location = new Point(15, 22),
                Size = new Size(200, 20),
                Checked = true
            };
            rbFilterWalls = new RadioButton
            {
                Text = "Filter by wall type",
                Location = new Point(15, 44),
                Size = new Size(200, 20)
            };
            rbAllWalls.CheckedChanged += (s, e) => ToggleFilterPanel();
            rbFilterWalls.CheckedChanged += (s, e) => ToggleFilterPanel();

            // Filter panel (shown when "Filter" is selected)
            pnlFilters = new Panel
            {
                Location = new Point(10, 70),
                Size = new Size(450, 230),
                Visible = false
            };

            var lblSearchFilters = new Label
            {
                Text = "Search Filters:",
                Location = new Point(5, 0),
                Size = new Size(100, 18),
                Font = new Font(Font, FontStyle.Bold)
            };
            chkInterior = new CheckBox
            {
                Text = "Interior",
                Location = new Point(5, 20),
                Size = new Size(100, 20),
                Checked = true
            };
            chkExterior = new CheckBox
            {
                Text = "Exterior",
                Location = new Point(115, 20),
                Size = new Size(100, 20),
                Checked = true
            };
            chkFireRated = new CheckBox
            {
                Text = "Fire Rated (HR, HOUR)",
                Location = new Point(225, 20),
                Size = new Size(160, 20)
            };
            chkStructural = new CheckBox
            {
                Text = "Structural (CONCRETE, CMU)",
                Location = new Point(5, 42),
                Size = new Size(200, 20)
            };

            btnApplyFilter = new Button
            {
                Text = "Apply Filters",
                Location = new Point(340, 38),
                Size = new Size(100, 25)
            };
            btnApplyFilter.Click += BtnApplyFilter_Click;

            var lblWallTypes = new Label
            {
                Text = "Matching wall types:",
                Location = new Point(5, 68),
                Size = new Size(200, 18)
            };
            clbWallTypes = new CheckedListBox
            {
                Location = new Point(5, 88),
                Size = new Size(435, 105),
                CheckOnClick = true
            };

            btnSelectAll = new Button
            {
                Text = "Select All",
                Location = new Point(260, 196),
                Size = new Size(85, 24)
            };
            btnSelectAll.Click += (s, e) =>
            {
                for (int i = 0; i < clbWallTypes.Items.Count; i++)
                    clbWallTypes.SetItemChecked(i, true);
            };

            btnSelectNone = new Button
            {
                Text = "Select None",
                Location = new Point(350, 196),
                Size = new Size(90, 24)
            };
            btnSelectNone.Click += (s, e) =>
            {
                for (int i = 0; i < clbWallTypes.Items.Count; i++)
                    clbWallTypes.SetItemChecked(i, false);
            };

            pnlFilters.Controls.AddRange(new Control[]
            {
                lblSearchFilters, chkInterior, chkExterior, chkFireRated, chkStructural,
                btnApplyFilter, lblWallTypes, clbWallTypes, btnSelectAll, btnSelectNone
            });

            grpWalls.Controls.AddRange(new Control[] { rbAllWalls, rbFilterWalls, pnlFilters });
            Controls.Add(grpWalls);
            y += 320;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Select Pipes",
                DialogResult = DialogResult.OK,
                Location = new Point(295, y),
                Size = new Size(110, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(410, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void ToggleFilterPanel()
        {
            pnlFilters.Visible = rbFilterWalls.Checked;
        }

        private void BtnApplyFilter_Click(object sender, EventArgs e)
        {
            var filtered = FilterWallTypes();
            clbWallTypes.Items.Clear();
            foreach (var name in filtered.OrderBy(n => n))
            {
                clbWallTypes.Items.Add(name, true); // Checked by default
            }
        }

        private List<string> FilterWallTypes()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in _allWallTypeNames)
            {
                string upper = name.ToUpperInvariant();

                if (chkInterior.Checked && upper.Contains("INTERIOR"))
                    result.Add(name);
                if (chkExterior.Checked && upper.Contains("EXTERIOR"))
                    result.Add(name);
                if (chkFireRated.Checked && (upper.Contains("HR") || upper.Contains("HOUR")))
                    result.Add(name);
                if (chkStructural.Checked && (upper.Contains("CONCRETE") || upper.Contains("CMU")))
                    result.Add(name);
            }

            return result.ToList();
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (_linkNames.Count == 0)
            {
                MessageBox.Show("No loaded Revit links found in the project.",
                    "No Links", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedLinkIndex = cboLink.SelectedIndex;
            IsSeismic = rbSeismic.Checked;
            UseAllWalls = rbAllWalls.Checked;

            if (!UseAllWalls)
            {
                SelectedWallTypes = new List<string>();
                foreach (var item in clbWallTypes.CheckedItems)
                    SelectedWallTypes.Add(item.ToString());

                if (SelectedWallTypes.Count == 0)
                {
                    MessageBox.Show("No wall types selected.\n\n" +
                        "Click 'Apply Filters' first, then select wall types from the list,\n" +
                        "or choose 'All intersecting walls'.",
                        "No Wall Types", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
            }
        }
    }
}
