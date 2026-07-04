using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Setup
{
    /// <summary>
    /// Dialog for the Copy Link Levels and Grids command.
    ///
    /// Collects:
    ///   - Which linked model to copy from
    ///   - Import mode: Levels and Grids, Levels only, or Grids only
    ///   - Grid type to assign to imported grids
    ///   - Whether to copy ALL or let user select specific levels/grids
    ///   - Whether to pin the imported elements
    /// </summary>
    public class CopyLinkLevelsGridsDialog : DpiAwareForm
    {
        private const string MemKey = "CopyLinkLevelsGrids";

        // ── Results ──
        public enum ImportMode { LevelsAndGrids, LevelsOnly, GridsOnly }
        public enum SelectionMode { CopyAll, SelectSpecific }

        public string SelectedLinkName { get; private set; } = "";
        public ImportMode Mode { get; private set; } = ImportMode.LevelsAndGrids;
        public string SelectedGridTypeName { get; private set; } = "";
        public SelectionMode LevelSelectionMode { get; private set; } = SelectionMode.CopyAll;
        public SelectionMode GridSelectionMode { get; private set; } = SelectionMode.CopyAll;
        public bool PinElements { get; private set; } = true;
        /// <summary>
        /// When true, the command skips its own recreate-import and instead
        /// launches Revit's native Copy/Monitor tool for the chosen link — the
        /// only way to establish a real monitor relationship (the API can't).
        /// </summary>
        public bool LaunchCopyMonitor { get; private set; } = false;

        // ── Controls ──
        private ComboBox cboLink;
        private RadioButton rbBoth, rbLevelsOnly, rbGridsOnly;
        private ComboBox cboGridType;
        private RadioButton rbAllLevels, rbSelectLevels;
        private RadioButton rbAllGrids, rbSelectGrids;
        private CheckBox chkPin;
        private CheckBox chkMonitor;

        private readonly IList<string> _linkNames;
        private readonly IList<string> _gridTypeNames;
        private const string DefaultGridType = "SS Grid Head - 3/8\" Bubble";

        public CopyLinkLevelsGridsDialog(
            IList<string> linkNames, IList<string> gridTypeNames)
        {
            _linkNames = linkNames;
            _gridTypeNames = gridTypeNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Setup: Import Link Levels and Grids";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 432);

            int margin = 15;
            int y = margin;

            // ── Link Selection ──
            var grpLink = new GroupBox
            {
                Text = "Select Link to Import From",
                Location = new Point(margin, y),
                Size = new Size(430, 50)
            };
            cboLink = new ComboBox
            {
                Location = new Point(10, 20),
                Size = new Size(410, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var name in _linkNames)
                cboLink.Items.Add(name);
            if (cboLink.Items.Count > 0)
                cboLink.SelectedIndex = 0;
            grpLink.Controls.Add(cboLink);
            Controls.Add(grpLink);
            y += 55;

            // ── Import Options ──
            var grpImport = new GroupBox
            {
                Text = "Import Options",
                Location = new Point(margin, y),
                Size = new Size(430, 86)
            };
            rbBoth = new RadioButton
            {
                Text = "Levels and Grids",
                Location = new Point(10, 18),
                Size = new Size(200, 20),
                Checked = true
            };
            rbLevelsOnly = new RadioButton
            {
                Text = "Levels only",
                Location = new Point(10, 38),
                Size = new Size(200, 20)
            };
            rbGridsOnly = new RadioButton
            {
                Text = "Grids only",
                Location = new Point(10, 58),
                Size = new Size(200, 20)
            };
            grpImport.Controls.AddRange(new Control[] { rbBoth, rbLevelsOnly, rbGridsOnly });
            Controls.Add(grpImport);
            y += 91;

            // Restore remembered import mode.
            int mode = DialogMemory.GetInt(MemKey, "Mode", 0);
            rbLevelsOnly.Checked = mode == 1;
            rbGridsOnly.Checked = mode == 2;
            rbBoth.Checked = mode != 1 && mode != 2;

            // ── Grid Type ──
            var grpGridType = new GroupBox
            {
                Text = "Grid Type for Imported Grids",
                Location = new Point(margin, y),
                Size = new Size(430, 50)
            };
            cboGridType = new ComboBox
            {
                Location = new Point(10, 20),
                Size = new Size(410, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var name in _gridTypeNames)
                cboGridType.Items.Add(name);
            // Pre-select: remembered grid type (if still present), else the SS default.
            int defaultIdx = -1;
            string savedGridType = DialogMemory.Get(MemKey, "GridType", null);
            if (!string.IsNullOrEmpty(savedGridType))
                defaultIdx = cboGridType.Items.IndexOf(savedGridType);
            if (defaultIdx < 0)
            {
                for (int i = 0; i < cboGridType.Items.Count; i++)
                {
                    if (cboGridType.Items[i].ToString().Contains("SS Grid Head"))
                    {
                        defaultIdx = i;
                        break;
                    }
                }
            }
            cboGridType.SelectedIndex = defaultIdx >= 0 ? defaultIdx :
                (cboGridType.Items.Count > 0 ? 0 : -1);
            grpGridType.Controls.Add(cboGridType);
            Controls.Add(grpGridType);
            y += 55;

            // ── Level Selection Mode ──
            var grpLevels = new GroupBox
            {
                Text = "Levels",
                Location = new Point(margin, y),
                Size = new Size(210, 64)
            };
            rbAllLevels = new RadioButton
            {
                Text = "Copy ALL levels",
                Location = new Point(10, 18),
                Size = new Size(190, 20),
                Checked = true
            };
            rbSelectLevels = new RadioButton
            {
                Text = "Select levels to copy",
                Location = new Point(10, 38),
                Size = new Size(190, 20)
            };
            grpLevels.Controls.AddRange(new Control[] { rbAllLevels, rbSelectLevels });
            Controls.Add(grpLevels);

            // ── Grid Selection Mode ──
            var grpGrids = new GroupBox
            {
                Text = "Grids",
                Location = new Point(margin + 220, y),
                Size = new Size(210, 64)
            };
            rbAllGrids = new RadioButton
            {
                Text = "Copy ALL grids",
                Location = new Point(10, 18),
                Size = new Size(190, 20),
                Checked = true
            };
            rbSelectGrids = new RadioButton
            {
                Text = "Select grids to copy",
                Location = new Point(10, 38),
                Size = new Size(190, 20)
            };
            grpGrids.Controls.AddRange(new Control[] { rbAllGrids, rbSelectGrids });
            Controls.Add(grpGrids);
            y += 69;

            // Restore remembered selection modes.
            rbSelectLevels.Checked = DialogMemory.GetBool(MemKey, "SelectLevels", false);
            rbAllLevels.Checked = !rbSelectLevels.Checked;
            rbSelectGrids.Checked = DialogMemory.GetBool(MemKey, "SelectGrids", false);
            rbAllGrids.Checked = !rbSelectGrids.Checked;

            // ── Pin ──
            chkPin = new CheckBox
            {
                Text = "Pin imported levels and grids",
                Location = new Point(margin + 5, y),
                Size = new Size(300, 20),
                Checked = DialogMemory.GetBool(MemKey, "Pin", true)
            };
            Controls.Add(chkPin);
            y += 26;

            // ── Copy/Monitor (native tool) ──
            chkMonitor = new CheckBox
            {
                Text = "Set up Copy/Monitor instead (uses Revit's native tool)",
                Location = new Point(margin + 5, y),
                Size = new Size(420, 20),
                Checked = false
            };
            chkMonitor.CheckedChanged += (s, e) =>
            {
                bool mon = chkMonitor.Checked;
                grpImport.Enabled = !mon;
                grpGridType.Enabled = !mon;
                grpLevels.Enabled = !mon;
                grpGrids.Enabled = !mon;
                chkPin.Enabled = !mon;
            };
            Controls.Add(chkMonitor);
            y += 22;

            var lblMonitor = new Label
            {
                Text = "Skips the import above and opens Revit's Copy/Monitor for the\n" +
                       "chosen link so the copied levels/grids are monitored. You finish\n" +
                       "picking elements in Revit.",
                Location = new Point(margin + 22, y),
                Size = new Size(408, 48),
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblMonitor);
            y += 56;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Import",
                DialogResult = DialogResult.OK,
                Location = new Point(255, y),
                Size = new Size(100, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            // Reflect the action: in monitor mode the button launches the native tool.
            chkMonitor.CheckedChanged += (s, e) =>
                btnOK.Text = chkMonitor.Checked ? "Copy/Monitor" : "Import";

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(365, y),
                Size = new Size(80, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SelectedLinkName = cboLink.SelectedItem?.ToString() ?? "";
            Mode = rbBoth.Checked ? ImportMode.LevelsAndGrids :
                   rbLevelsOnly.Checked ? ImportMode.LevelsOnly :
                   ImportMode.GridsOnly;
            SelectedGridTypeName = cboGridType.SelectedItem?.ToString() ?? "";
            LevelSelectionMode = rbAllLevels.Checked ? SelectionMode.CopyAll : SelectionMode.SelectSpecific;
            GridSelectionMode = rbAllGrids.Checked ? SelectionMode.CopyAll : SelectionMode.SelectSpecific;
            PinElements = chkPin.Checked;
            LaunchCopyMonitor = chkMonitor.Checked;

            // Remember options (link name is model-specific — intentionally not saved).
            DialogMemory.SetInt(MemKey, "Mode",
                Mode == ImportMode.LevelsOnly ? 1 : Mode == ImportMode.GridsOnly ? 2 : 0);
            if (!string.IsNullOrEmpty(SelectedGridTypeName))
                DialogMemory.Set(MemKey, "GridType", SelectedGridTypeName);
            DialogMemory.SetBool(MemKey, "SelectLevels", LevelSelectionMode == SelectionMode.SelectSpecific);
            DialogMemory.SetBool(MemKey, "SelectGrids", GridSelectionMode == SelectionMode.SelectSpecific);
            DialogMemory.SetBool(MemKey, "Pin", PinElements);
            DialogMemory.Flush();
        }
    }

    /// <summary>
    /// Secondary dialog for selecting specific levels or grids to import.
    /// Shows a checklist and returns the selected names.
    /// </summary>
    public class SelectItemsDialog : DpiAwareForm
    {
        public List<string> SelectedItems { get; private set; } = new List<string>();

        private CheckedListBox checkedList;

        public SelectItemsDialog(string title, IList<string> items)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 350);

            checkedList = new CheckedListBox
            {
                Location = new Point(15, 15),
                Size = new Size(370, 260),
                CheckOnClick = true,
                // Primary list grows with the dialog; buttons below pin to the bottom.
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            foreach (var item in items)
                checkedList.Items.Add(item, true); // all checked by default
            Controls.Add(checkedList);

            var btnAll = new Button
            {
                Text = "Select All",
                Location = new Point(15, 285),
                Size = new Size(80, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnAll.Click += (s, e) =>
            {
                for (int i = 0; i < checkedList.Items.Count; i++)
                    checkedList.SetItemChecked(i, true);
            };
            Controls.Add(btnAll);

            var btnNone = new Button
            {
                Text = "Select None",
                Location = new Point(100, 285),
                Size = new Size(80, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnNone.Click += (s, e) =>
            {
                for (int i = 0; i < checkedList.Items.Count; i++)
                    checkedList.SetItemChecked(i, false);
            };
            Controls.Add(btnNone);

            var btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(225, 285),
                Size = new Size(75, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnOK.Click += (s, e) =>
            {
                SelectedItems = checkedList.CheckedItems.Cast<string>().ToList();
                if (SelectedItems.Count == 0)
                {
                    MessageBox.Show(this, "Check at least one item to copy (or Cancel).", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(310, 285),
                Size = new Size(75, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }
    }
}

