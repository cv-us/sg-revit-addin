using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Commands.Coordination;   // ColorizeStatusInfo (status → default color)
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// Options dialog for Pretty Sprinklers. A mode radio at the top picks:
    ///   • Standard — just place the opaque head-symbol overlays (as before).
    ///   • Workset → Color — place them AND color each by the WORKSET its sprinkler
    ///     head is on (a per-instance view graphic override), so three heads of the
    ///     same family on three worksets read three colors in the same view.
    ///
    /// Colors are picked per workset (click the color cell). "Auto-color from workset
    /// names" seeds each from the Colorize status palette (existing = grey, demo =
    /// red, modify = orange, new = green). Choices are remembered by workset name.
    /// </summary>
    public class PrettySprinklersDialog : DpiAwareForm
    {
        private const string MemKey = "PrettySprinklers";

        public enum PrettyAction { Cancel, Apply, ClearColoring }

        // ── Results ──
        public PrettyAction Action { get; private set; } = PrettyAction.Cancel;
        public bool ColorByWorkset { get; private set; } = true;
        public Dictionary<int, Color> WorksetColors { get; } = new Dictionary<int, Color>();

        // ── Inputs ──
        private readonly List<(int id, string name)> _worksets;
        private readonly Dictionary<int, int> _counts;   // worksetId → selected-head count

        private RadioButton _rbStandard, _rbWorkset;
        private GroupBox _grpMap;
        private DataGridView _grid;
        private Button _btnApply;

        public PrettySprinklersDialog(List<(int id, string name)> worksets, Dictionary<int, int> counts)
        {
            _worksets = worksets ?? new List<(int, string)>();
            _counts = counts ?? new Dictionary<int, int>();
            AllowResize = false;    // fixed size — pins the buttons below the grid (grid scrolls if many worksets)
            RememberSize = false;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Pretty Sprinklers";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 342);

            const int M = 15, W = 530;
            int y = M;

            var lblInfo = new Label
            {
                Text = "Places the opaque head symbol over each selected sprinkler. Optionally colors each " +
                       "overlay by its head's workset — a view graphic override in the active view.",
                Location = new Point(M, y),
                Size = new Size(W, 34),
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblInfo);
            y += 40;

            // ── Mode (two radios → one group; they're the only radios on the form) ──
            bool colorMode = DialogMemory.GetBool(MemKey, "ColorByWorkset", true);
            _rbStandard = new RadioButton
            {
                Text = "Standard — place head symbols only",
                Location = new Point(M, y), AutoSize = true, Checked = !colorMode
            };
            _rbWorkset = new RadioButton
            {
                Text = "Workset → Color — place symbols and color each by its head's workset",
                Location = new Point(M, y + 24), AutoSize = true, Checked = colorMode
            };
            _rbStandard.CheckedChanged += (s, e) => SyncMode();
            _rbWorkset.CheckedChanged += (s, e) => SyncMode();
            Controls.Add(_rbStandard);
            Controls.Add(_rbWorkset);
            y += 54;

            // ── Workset → color grid (enabled only in Workset mode) ──
            _grpMap = new GroupBox
            {
                Text = "Workset → Color",
                Location = new Point(M, y),
                Size = new Size(W, 188)
            };
            _grid = new DataGridView
            {
                Location = new Point(10, 22),
                Size = new Size(W - 20, 126),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Workset", ReadOnly = true, Width = 335 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Heads", ReadOnly = true, Width = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Color", ReadOnly = true, Width = 90 });

            foreach (var ws in _worksets)
            {
                int argb = DialogMemory.GetInt(MemKey, ColorField(ws.name),
                    ColorizeStatusInfo.DefaultColor(ColorizeStatusInfo.Suggest(ws.name)).ToArgb());
                int r = _grid.Rows.Add();
                var row = _grid.Rows[r];
                row.Cells[0].Value = ws.name;
                row.Cells[1].Value = _counts.TryGetValue(ws.id, out int c) ? c : 0;
                SetSwatch(row.Cells[2], Color.FromArgb(argb));
                row.Tag = ws.id;
            }
            _grid.CellClick += Grid_CellClick;
            _grpMap.Controls.Add(_grid);

            var btnAuto = new Button
            {
                Text = "Auto-color from workset names",
                Location = new Point(10, 154),
                Size = new Size(230, 26)
            };
            btnAuto.Click += (s, e) => AutoColor();
            _grpMap.Controls.Add(btnAuto);
            Controls.Add(_grpMap);
            y += 196;

            // ── Buttons ──
            var btnClear = new Button
            {
                Text = "Remove Coloring",
                Location = new Point(M, y),
                Size = new Size(140, 30)
            };
            btnClear.Click += (s, e) => { Action = PrettyAction.ClearColoring; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btnClear);

            _btnApply = new Button
            {
                Text = "Place + Color",
                Location = new Point(560 - M - 90 - 10 - 100, y),
                Size = new Size(100, 30)
            };
            _btnApply.Click += (s, e) => CaptureAndClose();
            AcceptButton = _btnApply;
            Controls.Add(_btnApply);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(560 - M - 90, y),
                Size = new Size(90, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            SyncMode();
        }

        private void SyncMode()
        {
            bool color = _rbWorkset.Checked;
            if (_grpMap != null) _grpMap.Enabled = color;
            if (_btnApply != null) _btnApply.Text = color ? "Place + Color" : "Place";
        }

        private static string ColorField(string wsName) => "Color_WS::" + wsName;

        private static void SetSwatch(DataGridViewCell cell, Color color)
        {
            cell.Value = "";
            cell.Style.BackColor = color;
            cell.Style.SelectionBackColor = color;
            cell.ToolTipText = "Click to pick a color";
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
            var cell = _grid.Rows[e.RowIndex].Cells[2];
            using (var cd = new ColorDialog { Color = cell.Style.BackColor, FullOpen = true })
                if (cd.ShowDialog(this) == DialogResult.OK)
                    SetSwatch(cell, cd.Color);
        }

        private void AutoColor()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                string name = row.Cells[0].Value?.ToString() ?? "";
                SetSwatch(row.Cells[2], ColorizeStatusInfo.DefaultColor(ColorizeStatusInfo.Suggest(name)));
            }
        }

        private void CaptureAndClose()
        {
            Action = PrettyAction.Apply;
            ColorByWorkset = _rbWorkset.Checked;

            WorksetColors.Clear();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!(row.Tag is int wsId)) continue;
                Color color = row.Cells[2].Style.BackColor;
                WorksetColors[wsId] = color;
                string name = row.Cells[0].Value?.ToString();
                if (!string.IsNullOrEmpty(name))
                    DialogMemory.SetInt(MemKey, ColorField(name), color.ToArgb());
            }
            DialogMemory.SetBool(MemKey, "ColorByWorkset", ColorByWorkset);
            DialogMemory.Flush();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
