using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// Dialog for Tag Pipes — SG-blue custom title bar (ChromeDpiAwareForm).
    /// Recreates HydraCAD's Tag Pipes layout: a pipe-tag-type picker (each with its
    /// own family dropdown), a selection method, drops handling, and options.
    /// Every control is remembered between runs via DialogMemory.
    /// </summary>
    public class TagPipesDialog : ChromeDpiAwareForm
    {
        private const string MemKey = "TagPipes";

        // ── Results ──
        public int TagTypeIndex { get; private set; }        // 0=C-C 1=Cut 2=Dynamic 3=Stocklisting
        public string TagFamily { get; private set; } = "";  // family for the chosen type
        public bool UseSystemWalker { get; private set; }
        public bool TagDropsOnly { get; private set; }
        public bool IncludeDrops { get; private set; }
        public string DropFamily { get; private set; } = "";
        public bool ResetTakeOut { get; private set; }
        public bool ResetCut { get; private set; }
        public bool RunCleanup { get; private set; }
        public bool Transparent { get; private set; }
        public bool Homogenize { get; private set; }

        // ── Controls ──
        private readonly RadioButton[] _rbType = new RadioButton[4];
        private readonly ComboBox[] _cboType = new ComboBox[4];
        private RadioButton _rbUser, _rbWalker;
        private CheckBox _chkDropsOnly, _chkIncludeDrops;
        private ComboBox _cboDrops;
        private CheckBox _chkResetTakeOut, _chkResetCut, _chkCleanup, _chkTransparent, _chkHomogenize;

        private static readonly string[] TypeLabels =
        {
            "Center to Center Length",
            "Cut Length",
            "Dynamic Length",
            "Stocklisting Tags"
        };
        private static readonly string[] TypeKeys = { "CC", "Cut", "Dyn", "Stock" };

        private readonly IList<string> _pipeTagFamilies;

        public TagPipesDialog(IList<string> pipeTagFamilies)
        {
            _pipeTagFamilies = pipeTagFamilies ?? new List<string>();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Tag Pipes";
            ClientSize = new Size(636, 394);

            int margin = 12;
            int top = HeaderHeight + margin;         // clear the blue title band
            const int colW = 300;
            int leftX = margin;
            int rightX = margin + colW + margin;

            // ── Pipe Tag Type (left) ──
            var grpType = new GroupBox
            {
                Text = "Pipe Tag Type",
                Location = new Point(leftX, top),
                Size = new Size(colW, 214)
            };
            int gy = 20;
            for (int i = 0; i < 4; i++)
            {
                _rbType[i] = new RadioButton
                {
                    Text = TypeLabels[i],
                    Location = new Point(10, gy),
                    Size = new Size(colW - 24, 18),
                    Checked = i == 0
                };
                _cboType[i] = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(26, gy + 20),
                    Size = new Size(colW - 44, 22)
                };
                FillFamilies(_cboType[i], DialogMemory.Get(MemKey, "Fam_" + TypeKeys[i], ""));
                grpType.Controls.Add(_rbType[i]);
                grpType.Controls.Add(_cboType[i]);
                gy += 48;
            }
            Controls.Add(grpType);

            // ── Selection Method (left) ──
            var grpSel = new GroupBox
            {
                Text = "Selection Method",
                Location = new Point(leftX, top + 214 + margin),
                Size = new Size(colW, 74)
            };
            _rbUser = new RadioButton
            {
                Text = "User Selection",
                Location = new Point(10, 22),
                Size = new Size(colW - 24, 20),
                Checked = true
            };
            _rbWalker = new RadioButton
            {
                Text = "System Walker Selection",
                Location = new Point(10, 44),
                Size = new Size(colW - 24, 20)
            };
            grpSel.Controls.AddRange(new Control[] { _rbUser, _rbWalker });
            Controls.Add(grpSel);

            // ── Options (right) ──
            var grpOpt = new GroupBox
            {
                Text = "Options",
                Location = new Point(rightX, top),
                Size = new Size(colW, 160)
            };
            _chkResetTakeOut = MakeCheck("Reset Length Adjustment (Take Out)", 10, 22, colW);
            _chkResetCut = MakeCheck("Reset Cut Lengths", 10, 44, colW);
            _chkCleanup = MakeCheck("Run Cleanup on Selected Tags", 10, 66, colW);
            _chkTransparent = MakeCheck("Transparent Tag Backgrounds", 10, 96, colW);
            _chkHomogenize = MakeCheck("Homogenize Tags", 10, 118, colW);
            grpOpt.Controls.AddRange(new Control[]
                { _chkResetTakeOut, _chkResetCut, _chkCleanup, _chkTransparent, _chkHomogenize });
            Controls.Add(grpOpt);

            // ── Drops (right) ──
            var grpDrops = new GroupBox
            {
                Text = "Drops",
                Location = new Point(rightX, top + 160 + margin),
                Size = new Size(colW, 118)
            };
            _chkDropsOnly = MakeCheck("Tag Drops Only", 10, 22, colW);
            _chkIncludeDrops = MakeCheck("Include Drops with Selection", 10, 44, colW);
            grpDrops.Controls.Add(new Label
            {
                Text = "Drop family:",
                Location = new Point(10, 74),
                Size = new Size(80, 18)
            });
            _cboDrops = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(94, 71),
                Size = new Size(colW - 108, 22)
            };
            FillFamilies(_cboDrops, DialogMemory.Get(MemKey, "DropFam", ""));
            grpDrops.Controls.AddRange(new Control[] { _chkDropsOnly, _chkIncludeDrops, _cboDrops });
            Controls.Add(grpDrops);

            // ── Buttons ──
            int by = top + 298 + margin - 4;
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(636 - margin - 85, by),
                Size = new Size(85, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Continue",
                DialogResult = DialogResult.OK,
                Location = new Point(636 - margin - 85 - 10 - 110, by),
                Size = new Size(110, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            // ── Restore remembered radios / checkboxes ──
            int t = DialogMemory.GetInt(MemKey, "TagType", 0);
            if (t >= 0 && t < 4) _rbType[t].Checked = true;
            _rbWalker.Checked = DialogMemory.GetBool(MemKey, "Walker", false);
            _rbUser.Checked = !_rbWalker.Checked;
            _chkDropsOnly.Checked = DialogMemory.GetBool(MemKey, "DropsOnly", false);
            _chkIncludeDrops.Checked = DialogMemory.GetBool(MemKey, "IncludeDrops", false);
            _chkResetTakeOut.Checked = DialogMemory.GetBool(MemKey, "ResetTakeOut", false);
            _chkResetCut.Checked = DialogMemory.GetBool(MemKey, "ResetCut", false);
            _chkCleanup.Checked = DialogMemory.GetBool(MemKey, "Cleanup", false);
            _chkTransparent.Checked = DialogMemory.GetBool(MemKey, "Transparent", false);
            _chkHomogenize.Checked = DialogMemory.GetBool(MemKey, "Homogenize", false);
        }

        private CheckBox MakeCheck(string text, int x, int y, int w)
            => new CheckBox { Text = text, Location = new Point(x, y), Size = new Size(w - 24, 20) };

        private void FillFamilies(ComboBox cbo, string remembered)
        {
            foreach (var f in _pipeTagFamilies) cbo.Items.Add(f);
            if (cbo.Items.Count == 0) return;
            int idx = string.IsNullOrEmpty(remembered) ? 0 : cbo.Items.IndexOf(remembered);
            cbo.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            TagTypeIndex = 0;
            for (int i = 0; i < 4; i++) if (_rbType[i].Checked) { TagTypeIndex = i; break; }
            TagFamily = _cboType[TagTypeIndex].SelectedItem?.ToString() ?? "";

            UseSystemWalker = _rbWalker.Checked;
            TagDropsOnly = _chkDropsOnly.Checked;
            IncludeDrops = _chkIncludeDrops.Checked;
            DropFamily = _cboDrops.SelectedItem?.ToString() ?? "";
            ResetTakeOut = _chkResetTakeOut.Checked;
            ResetCut = _chkResetCut.Checked;
            RunCleanup = _chkCleanup.Checked;
            Transparent = _chkTransparent.Checked;
            Homogenize = _chkHomogenize.Checked;

            // Persist everything.
            DialogMemory.SetInt(MemKey, "TagType", TagTypeIndex);
            for (int i = 0; i < 4; i++)
                DialogMemory.Set(MemKey, "Fam_" + TypeKeys[i], _cboType[i].SelectedItem?.ToString() ?? "");
            DialogMemory.SetBool(MemKey, "Walker", UseSystemWalker);
            DialogMemory.SetBool(MemKey, "DropsOnly", TagDropsOnly);
            DialogMemory.SetBool(MemKey, "IncludeDrops", IncludeDrops);
            DialogMemory.Set(MemKey, "DropFam", DropFamily);
            DialogMemory.SetBool(MemKey, "ResetTakeOut", ResetTakeOut);
            DialogMemory.SetBool(MemKey, "ResetCut", ResetCut);
            DialogMemory.SetBool(MemKey, "Cleanup", RunCleanup);
            DialogMemory.SetBool(MemKey, "Transparent", Transparent);
            DialogMemory.SetBool(MemKey, "Homogenize", Homogenize);
            DialogMemory.Flush();
        }
    }
}
