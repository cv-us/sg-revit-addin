using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.ViewsAndSheets
{
    /// <summary>
    /// Dialog for the Create Plan Views command.
    ///
    /// Collects:
    ///   - View type: Floor and Ceiling, Floor only, or Ceiling only
    ///   - Levels to create views from (checklist, sorted by elevation)
    ///   - Floor plan view template
    ///   - Ceiling plan view template
    ///   - View name suffix: OVERALL, FOR REFERENCE ONLY, or custom text
    /// </summary>
    public class CreatePlanViewsDialog : Form
    {
        // ── Results ──
        public enum ViewTypeOption { FloorAndCeiling, FloorOnly, CeilingOnly }
        public ViewTypeOption SelectedViewType { get; private set; } = ViewTypeOption.FloorAndCeiling;
        public List<string> SelectedLevelNames { get; private set; } = new List<string>();
        public string FloorTemplate { get; private set; } = "";
        public string CeilingTemplate { get; private set; } = "";
        public string ViewNameSuffix { get; private set; } = "OVERALL";

        // ── Controls ──
        private RadioButton rbBoth, rbFloor, rbCeiling;
        private CheckedListBox chkLevels;
        private ComboBox cboFloorTemplate, cboCeilingTemplate;
        private RadioButton rbOverall, rbRefOnly, rbCustom;
        private TextBox txtCustom;

        private readonly IList<string> _levelDisplayNames;
        private readonly IList<string> _levelNames;
        private readonly IList<string> _floorTemplates;
        private readonly IList<string> _ceilingTemplates;

        public CreatePlanViewsDialog(
            IList<string> levelDisplayNames,
            IList<string> levelNames,
            IList<string> floorTemplates,
            IList<string> ceilingTemplates)
        {
            _levelDisplayNames = levelDisplayNames;
            _levelNames = levelNames;
            _floorTemplates = floorTemplates;
            _ceilingTemplates = ceilingTemplates;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Create Plan Views By Level";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(470, 530);

            int margin = 15;
            int y = margin;

            // ── View Type ──
            var grpType = new GroupBox
            {
                Text = "View Types to Create",
                Location = new Point(margin, y),
                Size = new Size(440, 80)
            };
            rbBoth = new RadioButton
            {
                Text = "Floor and Ceiling Plans",
                Location = new Point(10, 18),
                Size = new Size(250, 20),
                Checked = true
            };
            rbFloor = new RadioButton
            {
                Text = "Floor Plans only",
                Location = new Point(10, 38),
                Size = new Size(250, 20)
            };
            rbCeiling = new RadioButton
            {
                Text = "Ceiling Plans only",
                Location = new Point(10, 58),
                Size = new Size(250, 20)
            };
            grpType.Controls.AddRange(new Control[] { rbBoth, rbFloor, rbCeiling });
            Controls.Add(grpType);
            y += 85;

            // ── Levels ──
            var grpLevels = new GroupBox
            {
                Text = "Select Levels to Create Views From",
                Location = new Point(margin, y),
                Size = new Size(440, 150)
            };
            chkLevels = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(340, 120),
                CheckOnClick = true
            };
            foreach (var name in _levelDisplayNames)
                chkLevels.Items.Add(name, true);
            grpLevels.Controls.Add(chkLevels);

            var btnAll = new Button
            {
                Text = "All",
                Location = new Point(360, 18),
                Size = new Size(65, 25)
            };
            btnAll.Click += (s, e) =>
            {
                for (int i = 0; i < chkLevels.Items.Count; i++)
                    chkLevels.SetItemChecked(i, true);
            };
            grpLevels.Controls.Add(btnAll);

            var btnNone = new Button
            {
                Text = "None",
                Location = new Point(360, 48),
                Size = new Size(65, 25)
            };
            btnNone.Click += (s, e) =>
            {
                for (int i = 0; i < chkLevels.Items.Count; i++)
                    chkLevels.SetItemChecked(i, false);
            };
            grpLevels.Controls.Add(btnNone);
            Controls.Add(grpLevels);
            y += 155;

            // ── Templates ──
            var grpTemplate = new GroupBox
            {
                Text = "View Templates",
                Location = new Point(margin, y),
                Size = new Size(440, 80)
            };
            grpTemplate.Controls.Add(new Label
            {
                Text = "Floor Plans:",
                Location = new Point(10, 22),
                Size = new Size(80, 18)
            });
            cboFloorTemplate = new ComboBox
            {
                Location = new Point(95, 19),
                Size = new Size(330, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboFloorTemplate.Items.Add("(none)");
            foreach (var t in _floorTemplates)
                cboFloorTemplate.Items.Add(t);
            SelectDefaultTemplate(cboFloorTemplate, "00 Working Floor Fine");
            grpTemplate.Controls.Add(cboFloorTemplate);

            grpTemplate.Controls.Add(new Label
            {
                Text = "Ceiling Plans:",
                Location = new Point(10, 52),
                Size = new Size(80, 18)
            });
            cboCeilingTemplate = new ComboBox
            {
                Location = new Point(95, 49),
                Size = new Size(330, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboCeilingTemplate.Items.Add("(none)");
            foreach (var t in _ceilingTemplates)
                cboCeilingTemplate.Items.Add(t);
            SelectDefaultTemplate(cboCeilingTemplate, "00 Working Ceiling Fine");
            grpTemplate.Controls.Add(cboCeilingTemplate);
            Controls.Add(grpTemplate);
            y += 85;

            // ── View Name Suffix ──
            var grpSuffix = new GroupBox
            {
                Text = "View Name Suffix  (e.g., LEVEL 1 - OVERALL)",
                Location = new Point(margin, y),
                Size = new Size(440, 95)
            };
            rbOverall = new RadioButton
            {
                Text = "OVERALL",
                Location = new Point(10, 18),
                Size = new Size(200, 20),
                Checked = true
            };
            rbRefOnly = new RadioButton
            {
                Text = "FOR REFERENCE ONLY",
                Location = new Point(10, 38),
                Size = new Size(200, 20)
            };
            rbCustom = new RadioButton
            {
                Text = "Custom:",
                Location = new Point(10, 62),
                Size = new Size(70, 20)
            };
            rbCustom.CheckedChanged += (s, e) => txtCustom.Enabled = rbCustom.Checked;
            txtCustom = new TextBox
            {
                Location = new Point(85, 60),
                Size = new Size(340, 22),
                Enabled = false
            };
            grpSuffix.Controls.AddRange(new Control[]
                { rbOverall, rbRefOnly, rbCustom, txtCustom });
            Controls.Add(grpSuffix);
            y += 100;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Create Views",
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
                Location = new Point(385, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void SelectDefaultTemplate(ComboBox cbo, string defaultName)
        {
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i].ToString().Contains(defaultName))
                {
                    cbo.SelectedIndex = i;
                    return;
                }
            }
            cbo.SelectedIndex = 0; // (none)
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SelectedViewType = rbBoth.Checked ? ViewTypeOption.FloorAndCeiling :
                               rbFloor.Checked ? ViewTypeOption.FloorOnly :
                               ViewTypeOption.CeilingOnly;

            // Map checked display names back to level names
            SelectedLevelNames = new List<string>();
            for (int i = 0; i < chkLevels.Items.Count; i++)
            {
                if (chkLevels.GetItemChecked(i) && i < _levelNames.Count)
                    SelectedLevelNames.Add(_levelNames[i]);
            }

            FloorTemplate = cboFloorTemplate.SelectedItem?.ToString() ?? "";
            if (FloorTemplate == "(none)") FloorTemplate = "";
            CeilingTemplate = cboCeilingTemplate.SelectedItem?.ToString() ?? "";
            if (CeilingTemplate == "(none)") CeilingTemplate = "";

            if (rbOverall.Checked) ViewNameSuffix = "OVERALL";
            else if (rbRefOnly.Checked) ViewNameSuffix = "FOR REFERENCE ONLY";
            else ViewNameSuffix = txtCustom.Text.Trim();
        }
    }
}
