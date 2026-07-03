using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Dialog for the Create Dependent Views command.
    ///
    /// Collects:
    ///   - Floor plan views to create dependents from (checklist)
    ///   - Ceiling plan views to create dependents from (checklist)
    ///   - Mode: Apply Scope Boxes or Blank Copies
    ///   - Scope boxes to apply (checklist, enabled only in scope box mode)
    ///   - Number of copies (enabled only in blank copies mode)
    /// </summary>
    public class CreateDependentViewsDialog : DpiAwareForm
    {
        private const string MemKey = "CreateDependentViews";

        // ── Results ──
        public List<string> SelectedFloorViewNames { get; private set; } = new List<string>();
        public List<string> SelectedCeilingViewNames { get; private set; } = new List<string>();
        public bool ApplyScopeBoxes { get; private set; } = true;
        public List<string> SelectedScopeBoxNames { get; private set; } = new List<string>();
        public int CopyCount { get; private set; } = 1;

        // ── Controls ──
        private CheckedListBox chkFloorViews, chkCeilingViews, chkScopeBoxes;
        private GroupBox grpFloor, grpCeiling, grpMode, grpScopeBoxes;
        private RadioButton rbScopeBoxes, rbBlankCopies;
        private NumericUpDown nudCopies;
        private Label lblCopies;

        // Natural (design) bounds captured for the three-way vertical flex.
        private bool _flexCaptured;
        private int _natHeight, _fH, _cTop, _cH, _mTop, _sTop, _sH;

        private readonly IList<string> _floorViewNames;
        private readonly IList<string> _ceilingViewNames;
        private readonly IList<string> _scopeBoxNames;

        public CreateDependentViewsDialog(
            IList<string> floorViewNames,
            IList<string> ceilingViewNames,
            IList<string> scopeBoxNames)
        {
            _floorViewNames = floorViewNames;
            _ceilingViewNames = ceilingViewNames;
            _scopeBoxNames = scopeBoxNames;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "Create Dependent Views";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 585);

            int margin = 15;
            int y = margin;
            int listHeight = 100;
            var listAnchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            var groupAnchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // ── Floor Plan Views ──
            grpFloor = new GroupBox
            {
                Text = $"Floor Plan Views ({_floorViewNames.Count})",
                Location = new Point(margin, y),
                Size = new Size(490, listHeight + 30),
                Anchor = groupAnchor
            };
            chkFloorViews = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(390, listHeight),
                CheckOnClick = true,
                Anchor = listAnchor
            };
            foreach (var name in _floorViewNames)
                chkFloorViews.Items.Add(name);
            grpFloor.Controls.Add(chkFloorViews);
            AddAllNoneButtons(grpFloor, chkFloorViews, 410, 18);
            Controls.Add(grpFloor);
            y += listHeight + 35;

            // ── Ceiling Plan Views ──
            grpCeiling = new GroupBox
            {
                Text = $"Ceiling Plan Views ({_ceilingViewNames.Count})",
                Location = new Point(margin, y),
                Size = new Size(490, listHeight + 30),
                Anchor = groupAnchor
            };
            chkCeilingViews = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(390, listHeight),
                CheckOnClick = true,
                Anchor = listAnchor
            };
            foreach (var name in _ceilingViewNames)
                chkCeilingViews.Items.Add(name);
            grpCeiling.Controls.Add(chkCeilingViews);
            AddAllNoneButtons(grpCeiling, chkCeilingViews, 410, 18);
            Controls.Add(grpCeiling);
            y += listHeight + 35;

            // ── Mode ──
            grpMode = new GroupBox
            {
                Text = "Dependent View Mode",
                Location = new Point(margin, y),
                Size = new Size(490, 55),
                Anchor = groupAnchor
            };
            rbScopeBoxes = new RadioButton
            {
                Text = "Apply Scope Boxes",
                Location = new Point(10, 22),
                Size = new Size(200, 20),
                Checked = true
            };
            rbScopeBoxes.CheckedChanged += (s, e) => UpdateModeUI();
            rbBlankCopies = new RadioButton
            {
                Text = "Blank Copies (no scope boxes)",
                Location = new Point(250, 22),
                Size = new Size(230, 20)
            };
            grpMode.Controls.AddRange(new Control[] { rbScopeBoxes, rbBlankCopies });
            Controls.Add(grpMode);
            y += 60;

            // ── Scope Boxes ──
            grpScopeBoxes = new GroupBox
            {
                Text = $"Scope Boxes ({_scopeBoxNames.Count})",
                Location = new Point(margin, y),
                Size = new Size(490, listHeight + 30),
                Anchor = groupAnchor
            };
            chkScopeBoxes = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(390, listHeight),
                CheckOnClick = true,
                Anchor = listAnchor
            };
            foreach (var name in _scopeBoxNames)
                chkScopeBoxes.Items.Add(name, true);
            grpScopeBoxes.Controls.Add(chkScopeBoxes);
            AddAllNoneButtons(grpScopeBoxes, chkScopeBoxes, 410, 18);
            Controls.Add(grpScopeBoxes);
            y += listHeight + 35;

            // ── Copy Count (for blank copies mode) ──
            lblCopies = new Label
            {
                Text = "Number of copies per view:",
                Location = new Point(margin + 10, y + 3),
                AutoSize = true,
                Enabled = false,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Controls.Add(lblCopies);

            nudCopies = new NumericUpDown
            {
                Location = new Point(200, y),
                Size = new Size(60, 22),
                Minimum = 1,
                Maximum = 50,
                Value = Math.Max(1, Math.Min(50, DialogMemory.GetInt(MemKey, "CopyCount", 1))),
                Enabled = false,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Controls.Add(nudCopies);
            y += 35;

            // ── Info label ──
            var lblInfo = new Label
            {
                Text = "Dependent views inherit the parent view's template, filters, and annotations.",
                Location = new Point(margin + 10, y),
                AutoSize = true,
                ForeColor = Color.Gray,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Controls.Add(lblInfo);
            y += 25;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Create",
                DialogResult = DialogResult.OK,
                Location = new Point(335, y),
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(425, y),
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            // Restore remembered mode (fires UpdateModeUI via CheckedChanged).
            bool scopeMode = DialogMemory.GetBool(MemKey, "ScopeMode", true);
            rbScopeBoxes.Checked = scopeMode;
            rbBlankCopies.Checked = !scopeMode;

            // Enlarging the dialog splits the extra height across the three lists.
            Resize += (s, e) => FlexLists();
        }

        /// <summary>
        /// Distributes any height beyond the natural size equally across the three
        /// checklist groups (the base class only supports one vertical grower via
        /// anchors, so the distribution is done manually from captured baselines).
        /// Controls below the lists ride down on their Bottom anchors.
        /// </summary>
        private void FlexLists()
        {
            if (MinimumSize.Height <= 0) return;   // base chrome not built yet
            if (!_flexCaptured)
            {
                // First call happens while controls still sit at their natural
                // (DPI-scaled) positions — Top|Left/T|L|R anchors never move them.
                _natHeight = MinimumSize.Height;
                _fH = grpFloor.Height;
                _cTop = grpCeiling.Top; _cH = grpCeiling.Height;
                _mTop = grpMode.Top;
                _sTop = grpScopeBoxes.Top; _sH = grpScopeBoxes.Height;
                _flexCaptured = true;
            }
            int extra = Math.Max(0, Height - _natHeight);
            int each = extra / 3;
            grpFloor.Height = _fH + each;
            grpCeiling.Top = _cTop + each;
            grpCeiling.Height = _cH + each;
            grpMode.Top = _mTop + 2 * each;
            grpScopeBoxes.Top = _sTop + 2 * each;
            grpScopeBoxes.Height = _sH + (extra - 2 * each);
        }

        private void AddAllNoneButtons(GroupBox grp, CheckedListBox clb, int x, int y)
        {
            var btnAll = new Button
            {
                Text = "All",
                Location = new Point(x, y),
                Size = new Size(55, 25),
                // Explicit: stay at the top of the group while its list grows.
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnAll.Click += (s, e) =>
            {
                for (int i = 0; i < clb.Items.Count; i++)
                    clb.SetItemChecked(i, true);
            };
            grp.Controls.Add(btnAll);

            var btnNone = new Button
            {
                Text = "None",
                Location = new Point(x, y + 30),
                Size = new Size(55, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnNone.Click += (s, e) =>
            {
                for (int i = 0; i < clb.Items.Count; i++)
                    clb.SetItemChecked(i, false);
            };
            grp.Controls.Add(btnNone);
        }

        private void UpdateModeUI()
        {
            bool scopeMode = rbScopeBoxes.Checked;
            chkScopeBoxes.Enabled = scopeMode;
            nudCopies.Enabled = !scopeMode;
            lblCopies.Enabled = !scopeMode;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SelectedFloorViewNames = new List<string>();
            for (int i = 0; i < chkFloorViews.Items.Count; i++)
            {
                if (chkFloorViews.GetItemChecked(i))
                    SelectedFloorViewNames.Add(chkFloorViews.Items[i].ToString());
            }

            SelectedCeilingViewNames = new List<string>();
            for (int i = 0; i < chkCeilingViews.Items.Count; i++)
            {
                if (chkCeilingViews.GetItemChecked(i))
                    SelectedCeilingViewNames.Add(chkCeilingViews.Items[i].ToString());
            }

            ApplyScopeBoxes = rbScopeBoxes.Checked;

            SelectedScopeBoxNames = new List<string>();
            for (int i = 0; i < chkScopeBoxes.Items.Count; i++)
            {
                if (chkScopeBoxes.GetItemChecked(i))
                    SelectedScopeBoxNames.Add(chkScopeBoxes.Items[i].ToString());
            }

            CopyCount = (int)nudCopies.Value;

            if (SelectedFloorViewNames.Count == 0 && SelectedCeilingViewNames.Count == 0)
            {
                MessageBox.Show(this, "Check at least one floor or ceiling plan view.",
                    "Create Dependent Views", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (ApplyScopeBoxes && SelectedScopeBoxNames.Count == 0)
            {
                MessageBox.Show(this, "Check at least one scope box, or switch to Blank Copies.",
                    "Create Dependent Views", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            DialogMemory.SetBool(MemKey, "ScopeMode", ApplyScopeBoxes);
            DialogMemory.SetInt(MemKey, "CopyCount", CopyCount);
            DialogMemory.Flush();
        }
    }
}

