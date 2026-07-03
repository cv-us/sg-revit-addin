using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;
using static SgRevitAddin.Commands.Coordination.MarkFamilyInstancesCommand;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Dialog for the Mark Family Instances command.
    ///
    /// Layout:
    ///   • Search box for filtering the family list (matches family name OR
    ///     category, case-insensitive).
    ///   • Side-by-side row:
    ///       - Families ListBox showing "Name [Category] ×count", with the
    ///         count reflecting the currently-selected worksets.
    ///       - Worksets CheckedListBox + Select All / None buttons. Default
    ///         all checked. Changes refresh the family list.
    ///   • Scope radios — active view or whole project.
    ///   • Place / Delete All / Close buttons.
    ///
    /// For non-workshared projects the workset section shows a hint message
    /// and is disabled; the workset filter falls through (every instance
    /// included).
    /// </summary>
    public class MarkFamilyInstancesDialog : DpiAwareForm
    {
        private const string MemKey = "MarkFamilyInstances";

        public enum MarkAction { None, Place, DeleteAll }

        // ── Results ──
        public MarkAction Action { get; private set; } = MarkAction.None;
        public FamilyMarkerInfo SelectedFamily { get; private set; }
        public bool ActiveViewOnly { get; private set; }

        /// <summary>
        /// Workset ids checked at OK time. Null if the project isn't
        /// workshared — the command treats null as "no workset filter".
        /// </summary>
        public HashSet<int> SelectedWorksetIds { get; private set; }

        // ── Controls ──
        private TextBox txtSearch;
        private Label lblCount;
        private ListBox lstFamilies;
        private CheckedListBox clbWorksets;
        private Button btnAllWorksets;
        private Button btnNoneWorksets;
        private Label lblNoWorksetsHint;
        private RadioButton rbView;
        private RadioButton rbProject;
        private Button btnPlace;
        private Button btnDeleteAll;
        private Button btnClose;

        private readonly List<FamilyMarkerInfo> _allFamilies;
        private readonly List<WorksetInfo> _allWorksets;
        private readonly int _existingMarkerCount;
        private bool _suppressWorksetRefresh;

        /// <summary>Wraps a family with its currently-displayed count so the ListBox text reflects the workset filter.</summary>
        private class DisplayItem
        {
            public FamilyMarkerInfo Info;
            public int VisibleCount;
            public override string ToString()
                => $"{Info.FamilyName}    [{Info.CategoryName}]    ×{VisibleCount}";
        }

        public MarkFamilyInstancesDialog(
            List<FamilyMarkerInfo> allFamilies,
            List<WorksetInfo> allWorksets,
            int existingMarkerCount)
        {
            _allFamilies = allFamilies ?? new List<FamilyMarkerInfo>();
            _allWorksets = allWorksets ?? new List<WorksetInfo>();
            _existingMarkerCount = existingMarkerCount;

            InitializeComponent();
            PopulateWorksets();
            RefreshList();
        }

        private void InitializeComponent()
        {
            // Layout constants — generous spacing so nothing clips.
            const int Margin = 15;
            const int FormWidth = 740;
            const int FullW = FormWidth - Margin * 2;

            // Side-by-side row
            const int FamiliesW = 470;
            const int WorksetsW = 215;
            const int RowH = 320;

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
                       "instance of a chosen family. Search to narrow the family list, " +
                       "pick which worksets to include, and choose whether to mark only " +
                       "the active view or the whole project.",
                Location = new Point(Margin, y),
                Size = new Size(FullW, 50),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 58;

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
                Size = new Size(FullW - 75, 24)
            };
            txtSearch.TextChanged += (s, e) => RefreshList();
            Controls.Add(txtSearch);
            y += 32;

            // ── Side-by-side: Families | Worksets ──
            // Both groups are the dialog's vertical-flex elements: enlarging
            // the window grows the lists. Everything below is bottom-anchored.
            var grpFamilies = new GroupBox
            {
                Text = "Families (click to select)",
                Location = new Point(Margin, y),
                Size = new Size(FamiliesW, RowH),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(grpFamilies);

            lblCount = new Label
            {
                Text = "",
                Location = new Point(10, 22),
                Size = new Size(FamiliesW - 25, 18),
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            grpFamilies.Controls.Add(lblCount);

            lstFamilies = new ListBox
            {
                Location = new Point(10, 46),
                Size = new Size(FamiliesW - 25, RowH - 56),
                IntegralHeight = false,
                Font = new Font(FontFamily.GenericSansSerif, 9f),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            grpFamilies.Controls.Add(lstFamilies);

            var grpWorksets = new GroupBox
            {
                Text = "Worksets",
                Location = new Point(Margin + FamiliesW + 15, y),
                Size = new Size(WorksetsW, RowH),
                Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(grpWorksets);

            bool isWorkshared = _allWorksets.Count > 0;

            if (isWorkshared)
            {
                clbWorksets = new CheckedListBox
                {
                    Location = new Point(10, 22),
                    Size = new Size(WorksetsW - 25, RowH - 64),
                    CheckOnClick = true,
                    IntegralHeight = false,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
                };
                clbWorksets.ItemCheck += (s, e) =>
                {
                    // ItemCheck fires before the new state is applied; defer
                    // the refresh so we read the post-click state.
                    if (_suppressWorksetRefresh) return;
                    BeginInvoke(new Action(RefreshList));
                };
                grpWorksets.Controls.Add(clbWorksets);

                btnAllWorksets = new Button
                {
                    Text = "Select All",
                    Location = new Point(10, RowH - 36),
                    Size = new Size(85, 26),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Left
                };
                btnAllWorksets.Click += (s, e) => SetAllWorksets(true);
                grpWorksets.Controls.Add(btnAllWorksets);

                btnNoneWorksets = new Button
                {
                    Text = "Select None",
                    Location = new Point(100, RowH - 36),
                    Size = new Size(95, 26),
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Left
                };
                btnNoneWorksets.Click += (s, e) => SetAllWorksets(false);
                grpWorksets.Controls.Add(btnNoneWorksets);
            }
            else
            {
                lblNoWorksetsHint = new Label
                {
                    Text = "Project is not workshared.\n\nWorkset filter has no effect — " +
                           "every instance is included.",
                    Location = new Point(10, 30),
                    Size = new Size(WorksetsW - 25, RowH - 40),
                    ForeColor = SystemColors.GrayText
                };
                grpWorksets.Controls.Add(lblNoWorksetsHint);
            }
            y += RowH + 10;

            // ── Scope group ──
            var grpScope = new GroupBox
            {
                Text = "Scope",
                Location = new Point(Margin, y),
                Size = new Size(FullW, 55),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(grpScope);

            bool remWholeProject = DialogMemory.GetBool(MemKey, "WholeProject", true);
            rbView = new RadioButton
            {
                Text = "Active view only",
                Location = new Point(15, 22),
                Size = new Size(180, 22),
                Checked = !remWholeProject
            };
            rbProject = new RadioButton
            {
                Text = "Whole project",
                Location = new Point(200, 22),
                Size = new Size(180, 22),
                Checked = remWholeProject
            };
            grpScope.Controls.AddRange(new Control[] { rbView, rbProject });
            y += 65;

            // ── Markers group ──
            var grpMarkers = new GroupBox
            {
                Text = "Markers",
                Location = new Point(Margin, y),
                Size = new Size(FullW, 75),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(grpMarkers);

            grpMarkers.Controls.Add(new Label
            {
                Text = $"Existing markers in project: {_existingMarkerCount}    " +
                       "(Place adds markers without clearing prior ones)",
                Location = new Point(10, 22),
                Size = new Size(FullW - 25, 18),
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
            AcceptButton = btnPlace;   // Enter = primary action

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
                Size = new Size(90, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            CancelButton = btnClose;
            Controls.Add(btnClose);

            y += 28 + Margin;
            ClientSize = new Size(FormWidth, y);
        }

        // ── Workset checklist ──

        private void PopulateWorksets()
        {
            if (clbWorksets == null) return;
            _suppressWorksetRefresh = true;
            try
            {
                foreach (var ws in _allWorksets)
                    clbWorksets.Items.Add(ws, true); // default all checked
            }
            finally
            {
                _suppressWorksetRefresh = false;
            }
        }

        private void SetAllWorksets(bool checkedState)
        {
            if (clbWorksets == null) return;
            _suppressWorksetRefresh = true;
            try
            {
                for (int i = 0; i < clbWorksets.Items.Count; i++)
                    clbWorksets.SetItemChecked(i, checkedState);
            }
            finally
            {
                _suppressWorksetRefresh = false;
            }
            RefreshList();
        }

        /// <summary>
        /// Returns the set of checked workset ids. Null if the project isn't
        /// workshared (no filter applies).
        /// </summary>
        private HashSet<int> CurrentlyCheckedWorksets()
        {
            if (clbWorksets == null) return null;
            var set = new HashSet<int>();
            foreach (var item in clbWorksets.CheckedItems)
            {
                if (item is WorksetInfo ws)
                    set.Add(ws.Id);
            }
            return set;
        }

        // ── Family list ──

        private void RefreshList()
        {
            var checkedWorksets = CurrentlyCheckedWorksets();
            string filter = (txtSearch?.Text ?? "").Trim();

            lstFamilies.BeginUpdate();
            lstFamilies.Items.Clear();

            int totalMatch = 0;
            foreach (var f in _allFamilies)
            {
                int count = f.CountInSelectedWorksets(checkedWorksets);
                if (count == 0) continue;

                if (filter.Length > 0)
                {
                    bool nameHit = (f.FamilyName ?? "")
                        .IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool catHit = (f.CategoryName ?? "")
                        .IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!nameHit && !catHit) continue;
                }

                lstFamilies.Items.Add(new DisplayItem { Info = f, VisibleCount = count });
                totalMatch++;
            }

            lstFamilies.EndUpdate();

            string scopeNote = checkedWorksets == null
                ? ""
                : "  (counts reflect selected worksets)";

            lblCount.Text = filter.Length == 0
                ? $"Showing {totalMatch} of {_allFamilies.Count} families{scopeNote}"
                : $"Filter matches {totalMatch} of {_allFamilies.Count} families{scopeNote}";
        }

        private void BtnPlace_Click(object sender, EventArgs e)
        {
            if (!(lstFamilies.SelectedItem is DisplayItem item))
            {
                MessageBox.Show(this,
                    "Pick a family from the list.",
                    "Mark Family Instances",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Validate at least one workset checked (for workshared projects).
            if (clbWorksets != null && clbWorksets.CheckedItems.Count == 0)
            {
                MessageBox.Show(this,
                    "Check at least one workset, or click Select All.",
                    "Mark Family Instances",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SelectedFamily = item.Info;
            ActiveViewOnly = rbView.Checked;
            SelectedWorksetIds = CurrentlyCheckedWorksets(); // null when not workshared
            Action = MarkAction.Place;

            // Remember scope for next time (family/worksets are model-specific).
            DialogMemory.SetBool(MemKey, "WholeProject", rbProject.Checked);
            DialogMemory.Flush();

            DialogResult = DialogResult.OK;
        }
    }
}
