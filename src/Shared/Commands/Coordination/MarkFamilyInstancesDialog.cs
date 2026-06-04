using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static SgRevitAddin.Commands.Coordination.MarkFamilyInstancesCommand;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Dialog for the Mark Family Instances command.
    ///
    /// Layout:
    ///   • Search box for filtering the family list (matches family name OR
    ///     category, case-insensitive).
    ///   • ListBox of families, formatted "Name [Category] ×count".
    ///   • Scope radios: Active View / Whole Project.
    ///   • Place Markers and Delete All Markers as separate explicit actions
    ///     (Place does NOT auto-clear prior markers).
    ///   • Existing-marker count shown next to the action buttons.
    /// </summary>
    public class MarkFamilyInstancesDialog : Form
    {
        public enum MarkAction { None, Place, DeleteAll }

        // ── Results ──
        public MarkAction Action { get; private set; } = MarkAction.None;
        public FamilyMarkerInfo SelectedFamily { get; private set; }
        public bool ActiveViewOnly { get; private set; }

        // ── Controls ──
        private TextBox txtSearch;
        private Label lblCount;
        private ListBox lstFamilies;
        private RadioButton rbView;
        private RadioButton rbProject;
        private Button btnPlace;
        private Button btnDeleteAll;
        private Button btnClose;

        private readonly List<FamilyMarkerInfo> _allFamilies;
        private readonly int _existingMarkerCount;

        public MarkFamilyInstancesDialog(List<FamilyMarkerInfo> allFamilies, int existingMarkerCount)
        {
            _allFamilies = allFamilies ?? new List<FamilyMarkerInfo>();
            _existingMarkerCount = existingMarkerCount;
            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            // Layout constants — generous spacing so nothing clips on small monitors.
            const int Margin = 15;
            const int FormWidth = 640;
            const int GroupW = FormWidth - Margin * 2;

            Text = "Mark Family Instances";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = Margin;

            // ── Description ──
            var lblDesc = new Label
            {
                Text = "Places a bright orange 12-inch sphere at the center of every " +
                       "instance of a chosen family. Use the search box to narrow the " +
                       "family list; choose whether to mark only the active view or " +
                       "the whole project.",
                Location = new Point(Margin, y),
                Size = new Size(GroupW, 50),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 60;

            // ── Search row ──
            var lblSearch = new Label
            {
                Text = "Search:",
                Location = new Point(Margin, y + 4),
                Size = new Size(70, 22),
                AutoSize = false,
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblSearch);

            txtSearch = new TextBox
            {
                Location = new Point(Margin + 75, y),
                Size = new Size(GroupW - 75, 24)
            };
            txtSearch.TextChanged += (s, e) => RefreshList();
            Controls.Add(txtSearch);
            y += 32;

            // ── Family list group ──
            var grpFamilies = new GroupBox
            {
                Text = "Families (click to select)",
                Location = new Point(Margin, y),
                Size = new Size(GroupW, 320)
            };
            Controls.Add(grpFamilies);

            lblCount = new Label
            {
                Text = "",
                Location = new Point(10, 22),
                Size = new Size(GroupW - 25, 18),
                ForeColor = SystemColors.GrayText
            };
            grpFamilies.Controls.Add(lblCount);

            lstFamilies = new ListBox
            {
                Location = new Point(10, 46),
                Size = new Size(GroupW - 25, 260),
                IntegralHeight = false,
                Font = new Font(FontFamily.GenericSansSerif, 9f)
            };
            grpFamilies.Controls.Add(lstFamilies);
            y += 330;

            // ── Scope group ──
            var grpScope = new GroupBox
            {
                Text = "Scope",
                Location = new Point(Margin, y),
                Size = new Size(GroupW, 55)
            };
            Controls.Add(grpScope);

            rbView = new RadioButton
            {
                Text = "Active view only",
                Location = new Point(15, 22),
                Size = new Size(180, 22),
                Checked = false
            };
            rbProject = new RadioButton
            {
                Text = "Whole project",
                Location = new Point(200, 22),
                Size = new Size(180, 22),
                Checked = true
            };
            grpScope.Controls.AddRange(new Control[] { rbView, rbProject });
            y += 65;

            // ── Markers group ──
            var grpMarkers = new GroupBox
            {
                Text = "Markers",
                Location = new Point(Margin, y),
                Size = new Size(GroupW, 75)
            };
            Controls.Add(grpMarkers);

            grpMarkers.Controls.Add(new Label
            {
                Text = $"Existing markers in project: {_existingMarkerCount}    " +
                       "(Place adds markers without clearing prior ones)",
                Location = new Point(10, 22),
                Size = new Size(GroupW - 25, 18),
                ForeColor = SystemColors.GrayText
            });

            btnPlace = new Button
            {
                Text = "Place Markers",
                Location = new Point(10, 44),
                Size = new Size(140, 28)
            };
            btnPlace.Click += BtnPlace_Click;
            grpMarkers.Controls.Add(btnPlace);

            btnDeleteAll = new Button
            {
                Text = "Delete All Markers",
                Location = new Point(160, 44),
                Size = new Size(160, 28),
                Enabled = _existingMarkerCount > 0
            };
            btnDeleteAll.Click += (s, e) =>
            {
                Action = MarkAction.DeleteAll;
                DialogResult = DialogResult.OK;
            };
            grpMarkers.Controls.Add(btnDeleteAll);
            y += 85;

            // ── Close ──
            btnClose = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Location = new Point(FormWidth - Margin - 90, y),
                Size = new Size(90, 28)
            };
            CancelButton = btnClose;
            Controls.Add(btnClose);

            y += 28 + Margin;
            ClientSize = new Size(FormWidth, y);
        }

        private void RefreshList()
        {
            string filter = (txtSearch?.Text ?? "").Trim();
            var matches = filter.Length == 0
                ? _allFamilies
                : _allFamilies.Where(f =>
                    (f.FamilyName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                 || (f.CategoryName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                  .ToList();

            lstFamilies.BeginUpdate();
            lstFamilies.Items.Clear();
            foreach (var f in matches)
                lstFamilies.Items.Add(f);
            lstFamilies.EndUpdate();

            lblCount.Text = filter.Length == 0
                ? $"Showing all {_allFamilies.Count} families in the project"
                : $"Filter matches {matches.Count} of {_allFamilies.Count} families";
        }

        private void BtnPlace_Click(object sender, EventArgs e)
        {
            if (!(lstFamilies.SelectedItem is FamilyMarkerInfo info))
            {
                MessageBox.Show(this,
                    "Pick a family from the list.",
                    "Mark Family Instances",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SelectedFamily = info;
            ActiveViewOnly = rbView.Checked;
            Action = MarkAction.Place;
            DialogResult = DialogResult.OK;
        }
    }
}
