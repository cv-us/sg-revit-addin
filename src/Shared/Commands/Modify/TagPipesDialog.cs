using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// Dialog for Tag Pipes — inherits the SG-blue custom title bar from DpiAwareForm.
    /// Each pipe-tag type picks a family AND a type within it. Stocklisting splits
    /// into a "line" tag and a "main" tag (chosen by pipe name). Every control is
    /// remembered between runs via DialogMemory.
    /// </summary>
    public class TagPipesDialog : DpiAwareForm
    {
        private const string MemKey = "TagPipes";

        // ── Results ──
        public int TagTypeIndex { get; private set; }        // 0=C-C 1=Cut 2=Dynamic 3=Stocklisting
        public string SelFamily { get; private set; } = "";  // chosen family for a non-stock type
        public string SelType { get; private set; } = "";    // chosen type name
        public string StockLineFamily { get; private set; } = "";
        public string StockLineType { get; private set; } = "";
        public string StockMainFamily { get; private set; } = "";
        public string StockMainType { get; private set; } = "";
        public bool UseSystemWalker { get; private set; }
        public bool TagDropsOnly { get; private set; }
        public bool IncludeDrops { get; private set; }
        public string DropFamily { get; private set; } = "";
        public string DropType { get; private set; } = "";
        public bool ResetTakeOut { get; private set; }
        public bool ResetCut { get; private set; }
        public bool RunCleanup { get; private set; }
        public bool Transparent { get; private set; }
        public bool Homogenize { get; private set; }

        // ── Controls ──
        private readonly RadioButton[] _rbType = new RadioButton[4];
        private readonly ComboBox[] _fam = new ComboBox[4];
        private readonly ComboBox[] _type = new ComboBox[4];
        private ComboBox _stockMainFam, _stockMainType;
        private RadioButton _rbUser, _rbWalker;
        private CheckBox _chkDropsOnly, _chkIncludeDrops;
        private ComboBox _dropFam, _dropType;
        private CheckBox _chkResetTakeOut, _chkResetCut, _chkCleanup, _chkTransparent, _chkHomogenize;

        // Two-column groups + scale factor, for proportional resize.
        private GroupBox _grpType, _grpSel, _grpOpt, _grpDrops;
        private float _sf = 1f;

        // Draggable divider between the family and type dropdown columns.
        private Panel _typeSplitter;
        private double _typeSplitRatio = 0.60;   // fraction of the row span given to family
        private bool _splitDragging;

        // Action buttons — positioned explicitly by LayoutColumns (always visible).
        private Button _btnOK, _btnCancel;

        private static readonly string[] TypeLabels =
        { "Center to Center Length", "Cut Length", "Dynamic Length", "Stocklisting Tags" };
        private static readonly string[] TypeKeys = { "CC", "Cut", "Dyn", "StockLine" };

        private readonly IList<string> _familyNames;
        private readonly IDictionary<string, IList<string>> _familyToTypes;

        public TagPipesDialog(IList<string> familyNames, IDictionary<string, IList<string>> familyToTypes)
        {
            _familyNames = familyNames ?? new List<string>();
            _familyToTypes = familyToTypes ?? new Dictionary<string, IList<string>>();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Tag Pipes";
            // Resizable: drag wider and both columns (and the family dropdowns
            // inside them) grow — see LayoutColumns. Wider default so the family
            // names aren't cramped out of the box.
            ClientSize = new Size(772, 396);

            int margin = 12;
            int colW = 372;
            int leftX = margin, rightX = margin + colW + margin;
            _typeSplitRatio = Math.Max(0.30, Math.Min(0.82, DialogMemory.GetDouble(MemKey, "TypeSplit", 0.60)));

            // ── Pipe Tag Type (left) ──
            // Family + type combos are laid out by LayoutTypeColumns (a draggable
            // divider between the two columns reapportions their widths). Radios are
            // AutoSize so their row doesn't run under the divider.
            _grpType = new GroupBox { Text = "Pipe Tag Type", Location = new Point(leftX, margin), Size = new Size(colW, 244) };
            int gy = 20;
            for (int i = 0; i < 3; i++)   // C-C, Cut, Dynamic
            {
                _rbType[i] = new RadioButton { Text = TypeLabels[i], Location = new Point(10, gy), AutoSize = true, Checked = i == 0 };
                _fam[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(26, gy + 20), Size = new Size(colW - 148, 22) };
                _type[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(colW - 116, gy + 20), Size = new Size(104, 22) };
                WireFamilyType(_fam[i], _type[i], DialogMemory.Get(MemKey, "Fam_" + TypeKeys[i], ""), DialogMemory.Get(MemKey, "Type_" + TypeKeys[i], ""));
                _grpType.Controls.AddRange(new Control[] { _rbType[i], _fam[i], _type[i] });
                gy += 48;
            }
            // Stocklisting (line + main)
            _rbType[3] = new RadioButton { Text = TypeLabels[3], Location = new Point(10, gy), AutoSize = true };
            _grpType.Controls.Add(_rbType[3]);
            _grpType.Controls.Add(new Label { Text = "Line:", Location = new Point(26, gy + 22), Size = new Size(34, 18) });
            _fam[3] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(62, gy + 20), Size = new Size(colW - 184, 22) };
            _type[3] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(colW - 118, gy + 20), Size = new Size(98, 22) };
            WireFamilyType(_fam[3], _type[3], DialogMemory.Get(MemKey, "Fam_StockLine", ""), DialogMemory.Get(MemKey, "Type_StockLine", ""));
            _grpType.Controls.Add(new Label { Text = "Main:", Location = new Point(26, gy + 46), Size = new Size(34, 18) });
            _stockMainFam = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(62, gy + 44), Size = new Size(colW - 184, 22) };
            _stockMainType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(colW - 118, gy + 44), Size = new Size(98, 22) };
            WireFamilyType(_stockMainFam, _stockMainType, DialogMemory.Get(MemKey, "Fam_StockMain", ""), DialogMemory.Get(MemKey, "Type_StockMain", ""));
            _grpType.Controls.AddRange(new Control[] { _fam[3], _type[3], _stockMainFam, _stockMainType });

            // Draggable divider between the family and type columns.
            _typeSplitter = new Panel { Cursor = Cursors.SizeWE, BackColor = _grpType.BackColor };
            _typeSplitter.Paint += TypeSplitter_Paint;
            _typeSplitter.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _splitDragging = true;
                _typeSplitter.Capture = true;   // keep events while the splitter moves under the drag
            };
            _typeSplitter.MouseMove += TypeSplitter_MouseMove;
            _typeSplitter.MouseUp += (s, e) =>
            {
                if (!_splitDragging) return;
                _splitDragging = false;
                _typeSplitter.Capture = false;
                DialogMemory.SetDouble(MemKey, "TypeSplit", _typeSplitRatio);
                DialogMemory.Flush();
            };
            _grpType.Controls.Add(_typeSplitter);
            Controls.Add(_grpType);

            // ── Selection Method (left) ──
            _grpSel = new GroupBox { Text = "Selection Method", Location = new Point(leftX, margin + 244 + margin), Size = new Size(colW, 74) };
            _rbUser = new RadioButton { Text = "User Selection", Location = new Point(10, 22), Size = new Size(colW - 24, 20), Checked = true };
            _rbWalker = new RadioButton { Text = "System Walker Selection", Location = new Point(10, 44), Size = new Size(colW - 24, 20) };
            _grpSel.Controls.AddRange(new Control[] { _rbUser, _rbWalker });
            Controls.Add(_grpSel);

            // ── Options (right) ──
            _grpOpt = new GroupBox { Text = "Options", Location = new Point(rightX, margin), Size = new Size(colW, 160) };
            _chkResetTakeOut = MakeCheck("Reset Length Adjustment (Take Out)", 10, 22, colW);
            _chkResetCut = MakeCheck("Reset Cut Lengths", 10, 44, colW);
            _chkCleanup = MakeCheck("Run Cleanup on Selected Tags", 10, 66, colW);
            _chkTransparent = MakeCheck("Transparent Tag Backgrounds", 10, 96, colW);
            _chkHomogenize = MakeCheck("Homogenize Tags", 10, 118, colW);
            _grpOpt.Controls.AddRange(new Control[] { _chkResetTakeOut, _chkResetCut, _chkCleanup, _chkTransparent, _chkHomogenize });
            Controls.Add(_grpOpt);

            // ── Drops (right) ──
            _grpDrops = new GroupBox { Text = "Drops", Location = new Point(rightX, margin + 160 + margin), Size = new Size(colW, 132) };
            _chkDropsOnly = MakeCheck("Tag Drops Only", 10, 22, colW);
            _chkIncludeDrops = MakeCheck("Include Drops with Selection", 10, 44, colW);
            _grpDrops.Controls.Add(new Label { Text = "Family:", Location = new Point(10, 72), Size = new Size(50, 18) });
            _dropFam = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(64, 70), Size = new Size(colW - 78, 22), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _grpDrops.Controls.Add(new Label { Text = "Type:", Location = new Point(10, 100), Size = new Size(50, 18) });
            _dropType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(64, 98), Size = new Size(colW - 78, 22), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            WireFamilyType(_dropFam, _dropType, DialogMemory.Get(MemKey, "DropFam", ""), DialogMemory.Get(MemKey, "DropType", ""));
            _grpDrops.Controls.AddRange(new Control[] { _chkDropsOnly, _chkIncludeDrops, _dropFam, _dropType });
            Controls.Add(_grpDrops);

            // ── Buttons (added left→right for tab order) ── positioned by LayoutColumns.
            int by = 354;
            _btnOK = new Button { Text = "Continue", DialogResult = DialogResult.OK, Location = new Point(565, by), Size = new Size(110, 30) };
            _btnOK.Click += BtnOK_Click;
            AcceptButton = _btnOK;
            Controls.Add(_btnOK);
            _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(685, by), Size = new Size(85, 30) };
            CancelButton = _btnCancel;
            Controls.Add(_btnCancel);

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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);   // base reparents the groups into its content panel
            _sf = DeviceDpi / 96f;

            // The two columns are positioned manually by LayoutColumns, so keep the
            // groups Top|Left-anchored (the base auto-flex may have stretched the
            // right column) — otherwise WinForms anchoring and LayoutColumns fight.
            const AnchorStyles pin = AnchorStyles.Top | AnchorStyles.Left;
            if (_grpType != null) _grpType.Anchor = pin;
            if (_grpSel != null) _grpSel.Anchor = pin;
            if (_grpOpt != null) _grpOpt.Anchor = pin;
            if (_grpDrops != null) _grpDrops.Anchor = pin;
            // Buttons are placed manually by LayoutColumns too — keep them Top|Left so
            // WinForms anchoring doesn't fight that (and never hides them).
            if (_btnOK != null) _btnOK.Anchor = pin;
            if (_btnCancel != null) _btnCancel.Anchor = pin;

            LayoutColumns();
            var host = _grpType != null ? _grpType.Parent : null;
            if (host != null) host.SizeChanged += (s, ev) => LayoutColumns();
        }

        /// <summary>
        /// Splits the content width into two equal columns and repositions the four
        /// groups, so dragging the dialog wider grows both columns evenly. The
        /// family combos inside (Left|Right anchored) then widen with their group,
        /// while the short type combos ride the right edge at a fixed width.
        /// </summary>
        private void LayoutColumns()
        {
            var host = _grpType != null ? _grpType.Parent : null;
            if (host == null || _grpSel == null || _grpOpt == null || _grpDrops == null) return;

            int m = (int)Math.Round(12 * _sf);
            int colW = (host.ClientSize.Width - 3 * m) / 2;
            int minCol = (int)Math.Round(300 * _sf);
            if (colW < minCol) colW = minCol;

            _grpType.Left = m; _grpType.Width = colW;
            _grpSel.Left = m; _grpSel.Width = colW;

            int rx = m + colW + m;
            _grpOpt.Left = rx; _grpOpt.Width = colW;
            _grpDrops.Left = rx; _grpDrops.Width = colW;

            // Buttons: pinned to the bottom-right of the content, always on-screen.
            if (_btnOK != null && _btnCancel != null)
            {
                int bm = (int)Math.Round(14 * _sf);
                int gap = (int)Math.Round(10 * _sf);
                int hostH = host.ClientSize.Height, hostW = host.ClientSize.Width;
                _btnCancel.Location = new Point(hostW - m - _btnCancel.Width, hostH - bm - _btnCancel.Height);
                _btnOK.Location = new Point(_btnCancel.Left - gap - _btnOK.Width, _btnCancel.Top);
                _btnOK.BringToFront();
                _btnCancel.BringToFront();
            }

            LayoutTypeColumns();   // reflow family/type columns to the new group width
        }

        /// <summary>
        /// Positions every family/type combo pair in the Pipe Tag Type group around a
        /// single vertical divider at <see cref="_typeSplitRatio"/> of the row span,
        /// and places the draggable splitter over it. Family combos fill from their
        /// left up to the divider; type combos fill from the divider to the right
        /// margin — so one drag reapportions all rows at once, within the current
        /// group width.
        /// </summary>
        private void LayoutTypeColumns()
        {
            if (_grpType == null || _fam[0] == null || _typeSplitter == null) return;

            int rightMargin = (int)Math.Round(12 * _sf);
            int gap = (int)Math.Round(8 * _sf);
            int minFam = (int)Math.Round(70 * _sf);
            int minType = (int)Math.Round(60 * _sf);

            int spanStart = _fam[0].Left;                               // top family left
            int spanEnd = _grpType.ClientSize.Width - rightMargin;
            if (spanEnd - spanStart < minFam + minType + gap) return;   // group too narrow

            int splitX = spanStart + (int)Math.Round((spanEnd - spanStart) * _typeSplitRatio);
            splitX = Math.Max(spanStart + minFam, Math.Min(spanEnd - minType, splitX));

            var pairs = new[]
            {
                new[] { _fam[0], _type[0] }, new[] { _fam[1], _type[1] }, new[] { _fam[2], _type[2] },
                new[] { _fam[3], _type[3] }, new[] { _stockMainFam, _stockMainType }
            };
            foreach (var p in pairs)
            {
                ComboBox fam = p[0], type = p[1];
                if (fam == null || type == null) continue;
                fam.Width = Math.Max(minFam, (splitX - gap) - fam.Left);
                type.Left = splitX;
                type.Width = Math.Max(minType, spanEnd - splitX);
            }

            int top = _fam[0].Top;
            int bottom = _stockMainType.Bottom;
            _typeSplitter.SetBounds(splitX - gap, top, gap, bottom - top);
            _typeSplitter.BringToFront();
        }

        private void TypeSplitter_Paint(object sender, PaintEventArgs e)
        {
            int cx = _typeSplitter.Width / 2;
            using (var pen = new Pen(Color.FromArgb(140, 140, 140)))
                e.Graphics.DrawLine(pen, cx, 2, cx, _typeSplitter.Height - 3);
            // three grip dots at the vertical centre so it reads as draggable
            int cy = _typeSplitter.Height / 2;
            using (var b = new SolidBrush(Color.FromArgb(110, 110, 110)))
                for (int k = -1; k <= 1; k++)
                    e.Graphics.FillRectangle(b, cx - 1, cy + k * 5 - 1, 2, 2);
        }

        private void TypeSplitter_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_splitDragging || _grpType == null) return;
            int gx = _grpType.PointToClient(Control.MousePosition).X;
            int rightMargin = (int)Math.Round(12 * _sf);
            int spanStart = _fam[0].Left, spanEnd = _grpType.ClientSize.Width - rightMargin;
            if (spanEnd <= spanStart) return;
            _typeSplitRatio = Math.Max(0.30, Math.Min(0.82, (double)(gx - spanStart) / (spanEnd - spanStart)));
            LayoutTypeColumns();
        }

        private CheckBox MakeCheck(string text, int x, int y, int w)
            => new CheckBox { Text = text, Location = new Point(x, y), Size = new Size(w - 24, 20), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

        /// <summary>Fill a family combo, wire it so its paired type combo repopulates on change.</summary>
        private void WireFamilyType(ComboBox fam, ComboBox type, string remFam, string remType)
        {
            foreach (var f in _familyNames) fam.Items.Add(f);
            fam.SelectedIndexChanged += (s, e) => PopulateTypes(fam, type, null);
            // The open dropdown list auto-widens to the longest entry, so full
            // family/type names are readable even when the combo box is narrow.
            fam.DropDown += (s, e) => FitDropWidth(fam);
            type.DropDown += (s, e) => FitDropWidth(type);
            int fi = string.IsNullOrEmpty(remFam) ? -1 : fam.Items.IndexOf(remFam);
            if (fam.Items.Count > 0) fam.SelectedIndex = fi >= 0 ? fi : 0;   // fires PopulateTypes
            if (!string.IsNullOrEmpty(remType))
            {
                int ti = type.Items.IndexOf(remType);
                if (ti >= 0) type.SelectedIndex = ti;
            }
        }

        /// <summary>Widen a combo's drop-down list to fit its longest item (so long
        /// family/type names are fully readable), clamped to a sane maximum.</summary>
        private static void FitDropWidth(ComboBox cb)
        {
            int max = cb.Width;
            try
            {
                using (var g = cb.CreateGraphics())
                {
                    foreach (var it in cb.Items)
                    {
                        int w = (int)Math.Ceiling(g.MeasureString(it == null ? "" : it.ToString(), cb.Font).Width) + 26;
                        if (w > max) max = w;
                    }
                }
            }
            catch { }
            cb.DropDownWidth = Math.Min(max, 640);
        }

        private void PopulateTypes(ComboBox fam, ComboBox type, string preferType)
        {
            type.Items.Clear();
            string famName = fam.SelectedItem?.ToString();
            if (famName != null && _familyToTypes.TryGetValue(famName, out var types))
                foreach (var tName in types) type.Items.Add(tName);
            if (type.Items.Count > 0)
            {
                int i = string.IsNullOrEmpty(preferType) ? 0 : type.Items.IndexOf(preferType);
                type.SelectedIndex = i >= 0 ? i : 0;
            }
        }

        private static string Val(ComboBox c) => c?.SelectedItem?.ToString() ?? "";

        private void BtnOK_Click(object sender, EventArgs e)
        {
            TagTypeIndex = 0;
            for (int i = 0; i < 4; i++) if (_rbType[i].Checked) { TagTypeIndex = i; break; }

            SelFamily = Val(_fam[TagTypeIndex]);
            SelType = Val(_type[TagTypeIndex]);
            StockLineFamily = Val(_fam[3]);
            StockLineType = Val(_type[3]);
            StockMainFamily = Val(_stockMainFam);
            StockMainType = Val(_stockMainType);
            UseSystemWalker = _rbWalker.Checked;
            TagDropsOnly = _chkDropsOnly.Checked;
            IncludeDrops = _chkIncludeDrops.Checked;
            DropFamily = Val(_dropFam);
            DropType = Val(_dropType);
            ResetTakeOut = _chkResetTakeOut.Checked;
            ResetCut = _chkResetCut.Checked;
            RunCleanup = _chkCleanup.Checked;
            Transparent = _chkTransparent.Checked;
            Homogenize = _chkHomogenize.Checked;

            // Persist everything.
            DialogMemory.SetInt(MemKey, "TagType", TagTypeIndex);
            for (int i = 0; i < 4; i++)
            {
                DialogMemory.Set(MemKey, "Fam_" + TypeKeys[i], Val(_fam[i]));
                DialogMemory.Set(MemKey, "Type_" + TypeKeys[i], Val(_type[i]));
            }
            DialogMemory.Set(MemKey, "Fam_StockMain", StockMainFamily);
            DialogMemory.Set(MemKey, "Type_StockMain", StockMainType);
            DialogMemory.Set(MemKey, "DropFam", DropFamily);
            DialogMemory.Set(MemKey, "DropType", DropType);
            DialogMemory.SetBool(MemKey, "Walker", UseSystemWalker);
            DialogMemory.SetBool(MemKey, "DropsOnly", TagDropsOnly);
            DialogMemory.SetBool(MemKey, "IncludeDrops", IncludeDrops);
            DialogMemory.SetBool(MemKey, "ResetTakeOut", ResetTakeOut);
            DialogMemory.SetBool(MemKey, "ResetCut", ResetCut);
            DialogMemory.SetBool(MemKey, "Cleanup", RunCleanup);
            DialogMemory.SetBool(MemKey, "Transparent", Transparent);
            DialogMemory.SetBool(MemKey, "Homogenize", Homogenize);
            DialogMemory.Flush();
        }
    }
}
