using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

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
    public class CreateDependentViewsDialog : Form
    {
        // ── Results ──
        public List<string> SelectedFloorViewNames { get; private set; } = new List<string>();
        public List<string> SelectedCeilingViewNames { get; private set; } = new List<string>();
        public bool ApplyScopeBoxes { get; private set; } = true;
        public List<string> SelectedScopeBoxNames { get; private set; } = new List<string>();
        public int CopyCount { get; private set; } = 1;

        // ── Controls ──
        private CheckedListBox chkFloorViews, chkCeilingViews, chkScopeBoxes;
        private RadioButton rbScopeBoxes, rbBlankCopies;
        private NumericUpDown nudCopies;
        private Label lblCopies;

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
            ClientSize = new Size(520, 640);

            int margin = 15;
            int y = margin;
            int listHeight = 100;

            // ── Floor Plan Views ──
            var grpFloor = new GroupBox
            {
                Text = $"Floor Plan Views ({_floorViewNames.Count})",
                Location = new Point(margin, y),
                Size = new Size(490, listHeight + 30)
            };
            chkFloorViews = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(390, listHeight),
                CheckOnClick = true
            };
            foreach (var name in _floorViewNames)
                chkFloorViews.Items.Add(name);
            grpFloor.Controls.Add(chkFloorViews);
            AddAllNoneButtons(grpFloor, chkFloorViews, 410, 18);
            Controls.Add(grpFloor);
            y += listHeight + 35;

            // ── Ceiling Plan Views ──
            var grpCeiling = new GroupBox
            {
                Text = $"Ceiling Plan Views ({_ceilingViewNames.Count})",
                Location = new Point(margin, y),
                Size = new Size(490, listHeight + 30)
            };
            chkCeilingViews = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(390, listHeight),
                CheckOnClick = true
            };
            foreach (var name in _ceilingViewNames)
                chkCeilingViews.Items.Add(name);
            grpCeiling.Controls.Add(chkCeilingViews);
            AddAllNoneButtons(grpCeiling, chkCeilingViews, 410, 18);
            Controls.Add(grpCeiling);
            y += listHeight + 35;

            // ── Mode ──
            var grpMode = new GroupBox
            {
                Text = "Dependent View Mode",
                Location = new Point(margin, y),
                Size = new Size(490, 55)
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
            var grpScopeBoxes = new GroupBox
            {
                Text = $"Scope Boxes ({_scopeBoxNames.Count})",
                Location = new Point(margin, y),
                Size = new Size(490, listHeight + 30)
            };
            chkScopeBoxes = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(390, listHeight),
                CheckOnClick = true
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
                Enabled = false
            };
            Controls.Add(lblCopies);

            nudCopies = new NumericUpDown
            {
                Location = new Point(200, y),
                Size = new Size(60, 22),
                Minimum = 1,
                Maximum = 50,
                Value = 1,
                Enabled = false
            };
            Controls.Add(nudCopies);
            y += 35;

            // ── Info label ──
            var lblInfo = new Label
            {
                Text = "Dependent views inherit the parent view's template, filters, and annotations.",
                Location = new Point(margin + 10, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            Controls.Add(lblInfo);
            y += 25;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Create",
                DialogResult = DialogResult.OK,
                Location = new Point(340, y),
                Size = new Size(80, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(425, y),
                Size = new Size(80, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void AddAllNoneButtons(GroupBox grp, CheckedListBox clb, int x, int y)
        {
            var btnAll = new Button
            {
                Text = "All",
                Location = new Point(x, y),
                Size = new Size(55, 25)
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
                Size = new Size(55, 25)
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
        }
    }
}
