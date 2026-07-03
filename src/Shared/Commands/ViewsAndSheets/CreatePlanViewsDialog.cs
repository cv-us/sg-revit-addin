using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.ViewsAndSheets
{
    /// <summary>
    /// Dialog for the Create Plan Views command.
    ///
    /// Source mode:
    ///   • This model — pick levels; one floor/ceiling view per level.
    ///   • Another model — pick an open or linked document and which of its plan
    ///     views to replicate into this model.
    ///
    /// Shared controls (both modes): view-type radios (create / filter), view
    /// templates, Sub-Discipline, and name suffix.
    /// </summary>
    public class CreatePlanViewsDialog : DpiAwareForm
    {
        private const string MemKey = "CreatePlanViews";

        // ── Results ──
        public enum ViewTypeOption { FloorAndCeiling, FloorOnly, CeilingOnly }
        public enum SourceModeOption { ThisModel, AnotherModel }

        public SourceModeOption SourceMode { get; private set; } = SourceModeOption.ThisModel;
        public ViewTypeOption SelectedViewType { get; private set; } = ViewTypeOption.FloorAndCeiling;
        public List<string> SelectedLevelNames { get; private set; } = new List<string>();
        public int SelectedSourceModelIndex { get; private set; } = -1;
        public List<int> SelectedSourceViewIndices { get; private set; } = new List<int>();
        public string FloorTemplate { get; private set; } = "";
        public string CeilingTemplate { get; private set; } = "";
        public string SubDiscipline { get; private set; } = "";
        public string ViewNameSuffix { get; private set; } = "OVERALL";

        private const string NoSubDiscipline = "(leave unset)";

        // ── Controls ──
        private RadioButton rbThisModel, rbAnotherModel;
        private GroupBox grpLevels, grpSource, grpType;
        private RadioButton rbBoth, rbFloor, rbCeiling;
        private CheckedListBox chkLevels;
        private ComboBox cboSourceModel;
        private CheckedListBox chkSourceViews;
        private ComboBox cboFloorTemplate, cboCeilingTemplate;
        private ComboBox cboSubDiscipline;
        private RadioButton rbOverall, rbRefOnly, rbCustom;
        private TextBox txtCustom;

        private readonly IList<string> _levelDisplayNames;
        private readonly IList<string> _levelNames;
        private readonly IList<string> _floorTemplates;
        private readonly IList<string> _ceilingTemplates;
        private readonly IList<string> _sourceModelDisplays;
        private readonly IList<IList<string>> _sourceViewDisplays;
        private readonly IList<string> _subDisciplineValues;

        public CreatePlanViewsDialog(
            IList<string> levelDisplayNames,
            IList<string> levelNames,
            IList<string> floorTemplates,
            IList<string> ceilingTemplates,
            IList<string> sourceModelDisplays,
            IList<IList<string>> sourceViewDisplays,
            IList<string> subDisciplineValues)
        {
            _levelDisplayNames = levelDisplayNames;
            _levelNames = levelNames;
            _floorTemplates = floorTemplates;
            _ceilingTemplates = ceilingTemplates;
            _sourceModelDisplays = sourceModelDisplays ?? new List<string>();
            _sourceViewDisplays = sourceViewDisplays ?? new List<IList<string>>();
            _subDisciplineValues = subDisciplineValues ?? new List<string>();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Create Plan Views";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(470, 600);

            int margin = 15;
            int y = margin;
            const int GroupW = 440;
            var growAnchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            var belowAnchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            bool hasSources = _sourceModelDisplays.Count > 0;

            // ── Source mode ──
            var grpMode = new GroupBox
            {
                Text = "Create Views From",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 50)
            };
            rbThisModel = new RadioButton
            {
                Text = "This model (by level)",
                Location = new Point(10, 20),
                Size = new Size(200, 20),
                Checked = true
            };
            rbAnotherModel = new RadioButton
            {
                Text = "Another model (copy views)",
                Location = new Point(215, 20),
                Size = new Size(215, 20),
                Enabled = hasSources
            };
            rbThisModel.CheckedChanged += (s, e) => UpdateMode();
            rbAnotherModel.CheckedChanged += (s, e) => UpdateMode();
            grpMode.Controls.AddRange(new Control[] { rbThisModel, rbAnotherModel });
            Controls.Add(grpMode);
            y += 55;

            // ── Shared slot: Levels (this model)  OR  Source (another model) ──
            int slotY = y;

            grpLevels = new GroupBox
            {
                Text = "Select Levels to Create Views From",
                Location = new Point(margin, slotY),
                Size = new Size(GroupW, 150),
                Anchor = growAnchor   // primary list slot grows with the dialog
            };
            chkLevels = new CheckedListBox
            {
                Location = new Point(10, 18),
                Size = new Size(340, 120),
                CheckOnClick = true,
                Anchor = growAnchor
            };
            foreach (var name in _levelDisplayNames) chkLevels.Items.Add(name, true);
            grpLevels.Controls.Add(chkLevels);
            grpLevels.Controls.Add(MakeSmallButton("All", 360, 18,
                () => SetAllChecked(chkLevels, true)));
            grpLevels.Controls.Add(MakeSmallButton("None", 360, 48,
                () => SetAllChecked(chkLevels, false)));
            Controls.Add(grpLevels);

            grpSource = new GroupBox
            {
                Text = "Copy Plan Views From Another Model",
                Location = new Point(margin, slotY),
                Size = new Size(GroupW, 150),
                Visible = false,
                Anchor = growAnchor
            };
            grpSource.Controls.Add(new Label
            {
                Text = "Model:",
                Location = new Point(10, 23),
                Size = new Size(50, 18)
            });
            cboSourceModel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(60, 20),
                Size = new Size(GroupW - 75, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            foreach (var m in _sourceModelDisplays) cboSourceModel.Items.Add(m);
            cboSourceModel.SelectedIndexChanged += (s, e) => PopulateSourceViews(cboSourceModel.SelectedIndex);
            grpSource.Controls.Add(cboSourceModel);

            chkSourceViews = new CheckedListBox
            {
                Location = new Point(10, 50),
                Size = new Size(340, 88),
                CheckOnClick = true,
                Anchor = growAnchor
            };
            grpSource.Controls.Add(chkSourceViews);
            grpSource.Controls.Add(MakeSmallButton("All", 360, 50,
                () => SetAllChecked(chkSourceViews, true)));
            grpSource.Controls.Add(MakeSmallButton("None", 360, 80,
                () => SetAllChecked(chkSourceViews, false)));
            if (cboSourceModel.Items.Count > 0) cboSourceModel.SelectedIndex = 0;
            Controls.Add(grpSource);
            y += 155;

            // ── View types (create / filter) ──
            grpType = new GroupBox
            {
                Text = "View Types to Create",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 80),
                Anchor = belowAnchor
            };
            rbBoth = new RadioButton { Text = "Floor and Ceiling Plans", Location = new Point(10, 18), Size = new Size(250, 20), Checked = true };
            rbFloor = new RadioButton { Text = "Floor Plans only", Location = new Point(10, 38), Size = new Size(250, 20) };
            rbCeiling = new RadioButton { Text = "Ceiling Plans only", Location = new Point(10, 58), Size = new Size(250, 20) };
            grpType.Controls.AddRange(new Control[] { rbBoth, rbFloor, rbCeiling });
            Controls.Add(grpType);
            y += 85;

            // Restore remembered view-type choice.
            int viewType = DialogMemory.GetInt(MemKey, "ViewType", 0);
            rbFloor.Checked = viewType == 1;
            rbCeiling.Checked = viewType == 2;
            rbBoth.Checked = viewType != 1 && viewType != 2;

            // ── Templates ──
            var grpTemplate = new GroupBox
            {
                Text = "View Templates",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 80),
                Anchor = belowAnchor
            };
            grpTemplate.Controls.Add(new Label { Text = "Floor Plans:", Location = new Point(10, 22), Size = new Size(80, 18) });
            cboFloorTemplate = new ComboBox { Location = new Point(95, 19), Size = new Size(330, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            cboFloorTemplate.Items.Add("(none)");
            foreach (var t in _floorTemplates) cboFloorTemplate.Items.Add(t);
            RestoreOrDefaultTemplate(cboFloorTemplate, "FloorTemplate", "00 Working Floor Fine");
            grpTemplate.Controls.Add(cboFloorTemplate);

            grpTemplate.Controls.Add(new Label { Text = "Ceiling Plans:", Location = new Point(10, 52), Size = new Size(80, 18) });
            cboCeilingTemplate = new ComboBox { Location = new Point(95, 49), Size = new Size(330, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            cboCeilingTemplate.Items.Add("(none)");
            foreach (var t in _ceilingTemplates) cboCeilingTemplate.Items.Add(t);
            RestoreOrDefaultTemplate(cboCeilingTemplate, "CeilingTemplate", "00 Working Ceiling Fine");
            grpTemplate.Controls.Add(cboCeilingTemplate);
            Controls.Add(grpTemplate);
            y += 85;

            // ── Sub-Discipline ──
            var grpSubDisc = new GroupBox
            {
                Text = "Sub-Discipline  (browser organization)",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 55),
                Anchor = belowAnchor
            };
            grpSubDisc.Controls.Add(new Label { Text = "Sub-Discipline:", Location = new Point(10, 24), Size = new Size(95, 18) });
            cboSubDiscipline = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,   // editable: pick or type
                Location = new Point(110, 21),
                Size = new Size(GroupW - 125, 22)
            };
            cboSubDiscipline.Items.Add(NoSubDiscipline);
            foreach (var v in _subDisciplineValues) cboSubDiscipline.Items.Add(v);
            string rememberedSub = DialogMemory.Get(MemKey, "SubDiscipline", NoSubDiscipline);
            int subIdx = cboSubDiscipline.Items.IndexOf(rememberedSub);
            if (subIdx >= 0) cboSubDiscipline.SelectedIndex = subIdx;
            else if (!string.IsNullOrEmpty(rememberedSub) && rememberedSub != NoSubDiscipline) cboSubDiscipline.Text = rememberedSub;
            else cboSubDiscipline.SelectedIndex = 0;
            grpSubDisc.Controls.Add(cboSubDiscipline);
            Controls.Add(grpSubDisc);
            y += 60;

            // ── View name suffix ──
            var grpSuffix = new GroupBox
            {
                Text = "View Name Suffix  (appended to the view name)",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 95),
                Anchor = belowAnchor
            };
            rbOverall = new RadioButton { Text = "OVERALL", Location = new Point(10, 18), Size = new Size(200, 20), Checked = true };
            rbRefOnly = new RadioButton { Text = "FOR REFERENCE ONLY", Location = new Point(10, 38), Size = new Size(200, 20) };
            rbCustom = new RadioButton { Text = "Custom:", Location = new Point(10, 62), Size = new Size(70, 20) };
            rbCustom.CheckedChanged += (s, e) => txtCustom.Enabled = rbCustom.Checked;
            txtCustom = new TextBox { Location = new Point(85, 60), Size = new Size(340, 22), Enabled = false };
            grpSuffix.Controls.AddRange(new Control[] { rbOverall, rbRefOnly, rbCustom, txtCustom });
            Controls.Add(grpSuffix);
            y += 100;

            // Restore remembered suffix choice.
            int suffixMode = DialogMemory.GetInt(MemKey, "SuffixMode", 0);
            txtCustom.Text = DialogMemory.Get(MemKey, "SuffixCustom", "");
            rbRefOnly.Checked = suffixMode == 1;
            rbCustom.Checked = suffixMode == 2;
            rbOverall.Checked = suffixMode != 1 && suffixMode != 2;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Create Views",
                DialogResult = DialogResult.OK,
                Location = new Point(270, y),
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(380, y),
                Size = new Size(75, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            UpdateMode();
        }

        private void UpdateMode()
        {
            bool thisModel = rbThisModel.Checked;
            grpLevels.Visible = thisModel;
            grpSource.Visible = !thisModel;
            grpType.Text = thisModel ? "View Types to Create" : "View Types to Copy  (filter)";
        }

        private void PopulateSourceViews(int idx)
        {
            chkSourceViews.Items.Clear();
            if (idx < 0 || idx >= _sourceViewDisplays.Count) return;
            foreach (var d in _sourceViewDisplays[idx])
                chkSourceViews.Items.Add(d, false);   // default unchecked — user picks
        }

        private Button MakeSmallButton(string text, int x, int y, Action onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(65, 25),
                // Explicit: hug the top-right of the group while its list grows.
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private static void SetAllChecked(CheckedListBox clb, bool value)
        {
            for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, value);
        }

        /// <summary>Remembered template wins if still present; else the named default; else "(none)".</summary>
        private void RestoreOrDefaultTemplate(ComboBox cbo, string memField, string defaultName)
        {
            string saved = DialogMemory.Get(MemKey, memField, null);
            if (!string.IsNullOrEmpty(saved))
            {
                int idx = cbo.Items.IndexOf(saved);
                if (idx >= 0) { cbo.SelectedIndex = idx; return; }
            }
            for (int i = 0; i < cbo.Items.Count; i++)
            {
                if (cbo.Items[i].ToString().Contains(defaultName)) { cbo.SelectedIndex = i; return; }
            }
            cbo.SelectedIndex = 0; // (none)
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            SourceMode = rbThisModel.Checked ? SourceModeOption.ThisModel : SourceModeOption.AnotherModel;

            SelectedViewType = rbBoth.Checked ? ViewTypeOption.FloorAndCeiling :
                               rbFloor.Checked ? ViewTypeOption.FloorOnly :
                               ViewTypeOption.CeilingOnly;

            // This-model: checked levels.
            SelectedLevelNames = new List<string>();
            for (int i = 0; i < chkLevels.Items.Count; i++)
                if (chkLevels.GetItemChecked(i) && i < _levelNames.Count)
                    SelectedLevelNames.Add(_levelNames[i]);

            // Another-model: source model + checked views.
            SelectedSourceModelIndex = cboSourceModel.SelectedIndex;
            SelectedSourceViewIndices = new List<int>();
            for (int i = 0; i < chkSourceViews.Items.Count; i++)
                if (chkSourceViews.GetItemChecked(i)) SelectedSourceViewIndices.Add(i);

            // Validate per mode.
            if (SourceMode == SourceModeOption.ThisModel && SelectedLevelNames.Count == 0)
            {
                MessageBox.Show("Select at least one level.", "Create Plan Views",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (SourceMode == SourceModeOption.AnotherModel)
            {
                if (SelectedSourceModelIndex < 0)
                {
                    MessageBox.Show("Pick a source model.", "Create Plan Views",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                if (SelectedSourceViewIndices.Count == 0)
                {
                    MessageBox.Show("Check at least one source view to copy.", "Create Plan Views",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
            }

            FloorTemplate = cboFloorTemplate.SelectedItem?.ToString() ?? "";
            if (FloorTemplate == "(none)") FloorTemplate = "";
            CeilingTemplate = cboCeilingTemplate.SelectedItem?.ToString() ?? "";
            if (CeilingTemplate == "(none)") CeilingTemplate = "";

            string sub = cboSubDiscipline.Text?.Trim() ?? "";
            SubDiscipline = (sub == NoSubDiscipline || string.IsNullOrEmpty(sub)) ? "" : sub;

            if (rbOverall.Checked) ViewNameSuffix = "OVERALL";
            else if (rbRefOnly.Checked) ViewNameSuffix = "FOR REFERENCE ONLY";
            else ViewNameSuffix = txtCustom.Text.Trim();

            DialogMemory.SetInt(MemKey, "ViewType",
                SelectedViewType == ViewTypeOption.FloorOnly ? 1 :
                SelectedViewType == ViewTypeOption.CeilingOnly ? 2 : 0);
            DialogMemory.Set(MemKey, "FloorTemplate", cboFloorTemplate.SelectedItem?.ToString() ?? "");
            DialogMemory.Set(MemKey, "CeilingTemplate", cboCeilingTemplate.SelectedItem?.ToString() ?? "");
            DialogMemory.SetInt(MemKey, "SuffixMode", rbRefOnly.Checked ? 1 : rbCustom.Checked ? 2 : 0);
            DialogMemory.Set(MemKey, "SuffixCustom", txtCustom.Text.Trim());
            DialogMemory.Set(MemKey, "SubDiscipline",
                string.IsNullOrEmpty(SubDiscipline) ? NoSubDiscipline : SubDiscipline);
            DialogMemory.Flush();
        }
    }
}
