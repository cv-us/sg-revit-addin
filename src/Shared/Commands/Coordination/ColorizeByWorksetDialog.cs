using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Coordination
{
    /// <summary>
    /// Configuration dialog for Colorize Pipes by Workset / Construction
    /// Status. Gathers a workset→status mapping (with name-keyword
    /// auto-suggest), per-status colors, apply mode(s), scope, and an
    /// optional extra-categories toggle. Also exposes a "Clear All Coloring"
    /// action that reverts everything the command applied.
    /// </summary>
    public class ColorizeByWorksetDialog : DpiAwareForm
    {
        private const string MemKey = "ColorizeByWorkset";

        public enum ColorizeAction { Cancel, Apply, Clear }

        // ── Results ──
        public ColorizeAction Action { get; private set; } = ColorizeAction.Cancel;
        public Dictionary<int, StatusBucket> WorksetStatus { get; } = new Dictionary<int, StatusBucket>();
        public Dictionary<StatusBucket, Color> StatusColors { get; } = new Dictionary<StatusBucket, Color>();
        public bool AssignMaterial { get; private set; } = true;
        public bool ApplyViewOverride { get; private set; } = false;
        public bool DeepColor { get; private set; } = true;
        public ColorizeScope Scope { get; private set; } = ColorizeScope.EntireModel;
        public bool IncludeExtraCategories { get; private set; } = false;

        // ── Inputs ──
        private readonly List<(int id, string name)> _worksets;
        private readonly Dictionary<int, int> _counts; // worksetIdValue → element count (whole model)

        // ── Controls ──
        private DataGridView _grid;
        private readonly Dictionary<StatusBucket, Button> _colorBtns = new Dictionary<StatusBucket, Button>();
        private CheckBox _chkMaterial;
        private CheckBox _chkDeep;
        private CheckBox _chkViewOverride;
        private RadioButton _rbModel, _rbView, _rbSel;
        private CheckBox _chkExtra;
        private Label _lblPreview;

        private static readonly string[] StatusItems =
        {
            "Existing", "Demo", "Modify", "New", "Ignore / skip"
        };

        public ColorizeByWorksetDialog(List<(int id, string name)> worksets, Dictionary<int, int> counts)
        {
            _worksets = worksets ?? new List<(int, string)>();
            _counts = counts ?? new Dictionary<int, int>();
            // seed colors from memory or defaults
            foreach (var s in ColorizeStatusInfo.Buckets)
            {
                int argb = DialogMemory.GetInt(MemKey, "Color_" + s, ColorizeStatusInfo.DefaultColor(s).ToArgb());
                StatusColors[s] = Color.FromArgb(argb);
            }
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Colorize Pipes by Workset / Construction Status";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(740, 770);

            const int M = 15, W = 710;
            int y = M;

            // ── Info ──
            var lblInfo = new Label
            {
                Text = "Colors pipes & fittings by the construction status on their workset, so it EXPORTS to NWC.\n" +
                       "• Assign material → pipes get a colored per-status duplicate type (e.g. \"Welded - New\"),\n" +
                       "   fittings get a material. Bakes into the body → survives NWC. (Face paint does NOT export.)\n" +
                       "• View graphic override → in-Revit visualization only, does NOT export.",
                Location = new Point(M, y),
                Size = new Size(W, 76),
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblInfo);
            y += 80;

            // ── Loud DO-NOT-SAVE warning ──
            var lblWarn = new Label
            {
                Text = "⚠️  DO NOT SAVE OR SYNCHRONIZE AFTER RUNNING  ⚠️\n" +
                       "This bakes colored types into the model and edits fab families in-memory.\n" +
                       "Export the NWC, then CLOSE the model WITHOUT saving / syncing.",
                Location = new Point(M, y),
                Size = new Size(W, 58),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 0, 0),
                BackColor = Color.FromArgb(255, 244, 150),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(lblWarn);
            y += 66;

            // ── Workset → Status grid ──
            var grpMap = new GroupBox { Text = "Workset → Status", Location = new Point(M, y), Size = new Size(W, 220) };
            _grid = new DataGridView
            {
                Location = new Point(10, 22),
                Size = new Size(W - 20, 158),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            var colName = new DataGridViewTextBoxColumn
            {
                HeaderText = "Workset", ReadOnly = true, Width = 360,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            var colCount = new DataGridViewTextBoxColumn
            {
                HeaderText = "Elems", ReadOnly = true, Width = 55
            };
            var colStatus = new DataGridViewComboBoxColumn
            {
                HeaderText = "Status", Width = 150, FlatStyle = FlatStyle.Flat
            };
            colStatus.Items.AddRange(StatusItems);
            _grid.Columns.Add(colName);
            _grid.Columns.Add(colCount);
            _grid.Columns.Add(colStatus);

            foreach (var ws in _worksets)
            {
                int rowIdx = _grid.Rows.Add();
                var row = _grid.Rows[rowIdx];
                row.Cells[0].Value = ws.name;
                row.Cells[1].Value = _counts.TryGetValue(ws.id, out int c) ? c : 0;
                // Restore the last status chosen for this workset (keyed by name,
                // which is stable across runs); else auto-suggest from the name.
                string savedLabel = DialogMemory.Get(MemKey, WsField(ws.name), null);
                row.Cells[2].Value = (!string.IsNullOrEmpty(savedLabel) && StatusItems.Contains(savedLabel))
                    ? savedLabel
                    : StatusLabelFor(ColorizeStatusInfo.Suggest(ws.name));
                row.Tag = ws.id;
            }
            _grid.CellValueChanged += (s, e) => UpdatePreview();
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grpMap.Controls.Add(_grid);

            var btnAuto = new Button
            {
                Text = "Auto-suggest from names",
                Location = new Point(10, 184),
                Size = new Size(180, 26)
            };
            btnAuto.Click += (s, e) => { AutoSuggest(); UpdatePreview(); };
            grpMap.Controls.Add(btnAuto);
            Controls.Add(grpMap);
            y += 228;

            // ── Status colors ──
            var grpColors = new GroupBox { Text = "Status Colors", Location = new Point(M, y), Size = new Size(W, 56) };
            int cx = 10;
            foreach (var st in ColorizeStatusInfo.Buckets)
            {
                grpColors.Controls.Add(new Label
                {
                    Text = ColorizeStatusInfo.Label(st) + ":",
                    Location = new Point(cx, 24), Size = new Size(58, 18)
                });
                var swatch = new Button
                {
                    Location = new Point(cx + 56, 20), Size = new Size(40, 24),
                    BackColor = StatusColors[st], FlatStyle = FlatStyle.Flat
                };
                var captured = st;
                swatch.Click += (s, e) =>
                {
                    using (var cd = new ColorDialog { Color = StatusColors[captured], FullOpen = true })
                    {
                        if (cd.ShowDialog(this) == DialogResult.OK)
                        {
                            StatusColors[captured] = cd.Color;
                            swatch.BackColor = cd.Color;
                        }
                    }
                };
                _colorBtns[st] = swatch;
                grpColors.Controls.Add(swatch);
                cx += 145;
            }
            Controls.Add(grpColors);
            y += 64;

            // ── Apply mode ──
            var grpMode = new GroupBox { Text = "Apply", Location = new Point(M, y), Size = new Size(W, 104) };
            _chkMaterial = new CheckBox
            {
                Text = "Assign material — colored pipe-type swap + fitting materials, EXPORTS to NWC (recommended)",
                Location = new Point(12, 22), Size = new Size(W - 24, 20),
                Checked = DialogMemory.GetBool(MemKey, "Material", true)
            };
            grpMode.Controls.Add(_chkMaterial);
            _chkDeep = new CheckBox
            {
                Text = "Deep-color By-Category fittings & flex (edits fab families in-memory; flex = one global color)",
                Location = new Point(12, 48), Size = new Size(W - 24, 20),
                Checked = DialogMemory.GetBool(MemKey, "DeepColor", true)
            };
            grpMode.Controls.Add(_chkDeep);
            _chkViewOverride = new CheckBox
            {
                Text = "Apply view graphic override (active view only — does NOT export to NWC)",
                Location = new Point(12, 74), Size = new Size(W - 24, 20),
                Checked = DialogMemory.GetBool(MemKey, "ViewOverride", false)
            };
            grpMode.Controls.Add(_chkViewOverride);
            Controls.Add(grpMode);
            y += 112;

            // ── Scope + extra categories ──
            var grpScope = new GroupBox { Text = "Scope", Location = new Point(M, y), Size = new Size(W, 78) };
            _rbModel = new RadioButton { Text = "Entire model", Location = new Point(12, 22), AutoSize = true, Checked = true };
            _rbView = new RadioButton { Text = "Active view's visible elements", Location = new Point(140, 22), AutoSize = true };
            _rbSel = new RadioButton { Text = "Current selection", Location = new Point(380, 22), AutoSize = true };
            int savedScope = DialogMemory.GetInt(MemKey, "Scope", 0);
            _rbView.Checked = savedScope == 1; _rbSel.Checked = savedScope == 2; _rbModel.Checked = savedScope == 0;
            grpScope.Controls.AddRange(new Control[] { _rbModel, _rbView, _rbSel });
            _chkExtra = new CheckBox
            {
                Text = "Also include sprinklers & pipe accessories (flex pipes are always included)",
                Location = new Point(12, 48), Size = new Size(W - 24, 20),
                Checked = DialogMemory.GetBool(MemKey, "Extra", true)
            };
            grpScope.Controls.Add(_chkExtra);
            Controls.Add(grpScope);
            y += 86;

            // ── Preview ──
            _lblPreview = new Label
            {
                Location = new Point(M, y), Size = new Size(W, 20),
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(_lblPreview);
            y += 26;
            UpdatePreview();

            // ── Buttons ──
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(740 - M - 80, y), Size = new Size(80, 30) };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnApply = new Button { Text = "Apply", Location = new Point(740 - M - 80 - 10 - 90, y), Size = new Size(90, 30) };
            btnApply.Click += (s, e) => { Action = ColorizeAction.Apply; CaptureAndClose(); };
            AcceptButton = btnApply;
            Controls.Add(btnApply);

            var btnClear = new Button { Text = "Clear All Coloring", Location = new Point(M, y), Size = new Size(150, 30) };
            btnClear.Click += (s, e) => { Action = ColorizeAction.Clear; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btnClear);

            var btnPreview = new Button { Text = "Preview Count", Location = new Point(M + 160, y), Size = new Size(120, 30) };
            btnPreview.Click += (s, e) => UpdatePreview(showZero: true);
            Controls.Add(btnPreview);
        }

        /// <summary>Per-workset memory field key (status remembered by workset name).</summary>
        private static string WsField(string worksetName) => "WS::" + worksetName;

        private static string StatusLabelFor(StatusBucket s)
            => s == StatusBucket.Ignore ? "Ignore / skip" : ColorizeStatusInfo.Label(s);

        private static StatusBucket BucketFromLabel(string label)
        {
            switch (label)
            {
                case "Existing": return StatusBucket.Existing;
                case "Demo": return StatusBucket.Demo;
                case "Modify": return StatusBucket.Modify;
                case "New": return StatusBucket.New;
                default: return StatusBucket.Ignore;
            }
        }

        private void AutoSuggest()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                string name = row.Cells[0].Value?.ToString() ?? "";
                row.Cells[2].Value = StatusLabelFor(ColorizeStatusInfo.Suggest(name));
            }
        }

        private void UpdatePreview(bool showZero = false)
        {
            var totals = new Dictionary<StatusBucket, int>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var st = BucketFromLabel(row.Cells[2].Value?.ToString());
                if (st == StatusBucket.Ignore) continue;
                int cnt = 0;
                int.TryParse(row.Cells[1].Value?.ToString(), out cnt);
                if (!totals.ContainsKey(st)) totals[st] = 0;
                totals[st] += cnt;
            }
            var parts = new List<string>();
            foreach (var st in ColorizeStatusInfo.Buckets)
            {
                int n = totals.TryGetValue(st, out int v) ? v : 0;
                if (n > 0 || showZero) parts.Add($"{ColorizeStatusInfo.Label(st)}: {n}");
            }
            _lblPreview.Text = parts.Count > 0
                ? "Preview (whole model): " + string.Join("   ", parts)
                : "Preview: no worksets mapped to a status yet.";
        }

        private void CaptureAndClose()
        {
            AssignMaterial = _chkMaterial.Checked;
            DeepColor = _chkDeep.Checked;
            ApplyViewOverride = _chkViewOverride.Checked;

            if (!AssignMaterial && !ApplyViewOverride)
            {
                MessageBox.Show(this, "Pick at least one apply mode (material and/or view override).",
                    "Colorize", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Action = ColorizeAction.Cancel;
                return;
            }

            Scope = _rbView.Checked ? ColorizeScope.ActiveView
                  : _rbSel.Checked ? ColorizeScope.Selection
                  : ColorizeScope.EntireModel;
            IncludeExtraCategories = _chkExtra.Checked;

            WorksetStatus.Clear();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!(row.Tag is int wsId)) continue;
                string label = row.Cells[2].Value?.ToString();
                WorksetStatus[wsId] = BucketFromLabel(label);
                // Remember this workset's status by name for next time.
                string wsName = row.Cells[0].Value?.ToString();
                if (!string.IsNullOrEmpty(wsName))
                    DialogMemory.Set(MemKey, WsField(wsName), label ?? "");
            }

            // Persist colors + modes + scope.
            foreach (var st in ColorizeStatusInfo.Buckets)
                DialogMemory.SetInt(MemKey, "Color_" + st, StatusColors[st].ToArgb());
            DialogMemory.SetBool(MemKey, "Material", AssignMaterial);
            DialogMemory.SetBool(MemKey, "DeepColor", DeepColor);
            DialogMemory.SetBool(MemKey, "ViewOverride", ApplyViewOverride);
            DialogMemory.SetInt(MemKey, "Scope", (int)Scope);
            DialogMemory.SetBool(MemKey, "Extra", IncludeExtraCategories);
            DialogMemory.Flush();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
