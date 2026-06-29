using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

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
        // ── Results ──
        public enum ImportMode { LevelsAndGrids, LevelsOnly, GridsOnly }
        public enum SelectionMode { CopyAll, SelectSpecific }

        public string SelectedLinkName { get; private set; } = "";
        public ImportMode Mode { get; private set; } = ImportMode.LevelsAndGrids;
        public string SelectedGridTypeName { get; private set; } = "";
        public SelectionMode LevelSelectionMode { get; private set; } = SelectionMode.CopyAll;
        public SelectionMode GridSelectionMode { get; private set; } = SelectionMode.CopyAll;
        public bool PinElements { get; private set; } = true;

        // ── Controls ──
        private ComboBox cboLink;
        private RadioButton rbBoth, rbLevelsOnly, rbGridsOnly;
        private ComboBox cboGridType;
        private RadioButton rbAllLevels, rbSelectLevels;
        private RadioButton rbAllGrids, rbSelectGrids;
        private CheckBox chkPin;

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
            ClientSize = new Size(460, 430);

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
            y += 85;

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
            // Pre-select default grid type if available
            int defaultIdx = -1;
            for (int i = 0; i < cboGridType.Items.Count; i++)
            {
                if (cboGridType.Items[i].ToString().Contains("SS Grid Head"))
                {
                    defaultIdx = i;
                    break;
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
            y += 65;

            // ── Pin ──
            chkPin = new CheckBox
            {
                Text = "Pin imported levels and grids",
                Location = new Point(margin + 5, y),
                Size = new Size(300, 20),
                Checked = true
            };
            Controls.Add(chkPin);
            y += 30;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Import",
                DialogResult = DialogResult.OK,
                Location = new Point(280, y),
                Size = new Size(90, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(375, y),
                Size = new Size(75, 30)
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
                CheckOnClick = true
            };
            foreach (var item in items)
                checkedList.Items.Add(item, true); // all checked by default
            Controls.Add(checkedList);

            var btnAll = new Button
            {
                Text = "Select All",
                Location = new Point(15, 285),
                Size = new Size(80, 25)
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
                Size = new Size(80, 25)
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
                Location = new Point(230, 285),
                Size = new Size(75, 25)
            };
            btnOK.Click += (s, e) =>
            {
                SelectedItems = checkedList.CheckedItems.Cast<string>().ToList();
            };
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(310, 285),
                Size = new Size(75, 25)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }
    }
}

