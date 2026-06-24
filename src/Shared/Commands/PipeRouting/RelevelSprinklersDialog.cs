using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>
    /// Target-level picker for Re-Level Sprinklers. Lists every level (sorted by
    /// elevation) and remembers the last chosen level by name.
    /// </summary>
    public class RelevelSprinklersDialog : Form
    {
        private const string MemKey = "RelevelSprinklers";

        public int SelectedLevelId { get; private set; }

        private readonly List<(int id, string name, double elev)> _levels;
        private readonly int _count;
        private ComboBox _cbo;

        public RelevelSprinklersDialog(List<(int id, string name, double elev)> levels, int count)
        {
            _levels = levels ?? new List<(int, string, double)>();
            _count = count;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Re-Level Sprinklers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 196);

            const int M = 16, W = 428;
            int y = M;

            var lblInfo = new Label
            {
                Text = $"Move the {_count} selected sprinkler(s) to a new level, keeping each head in its\n" +
                       "EXACT world location. \"Elevation from Level\" / \"Offset from Host\" is\n" +
                       "recomputed automatically from the level-elevation difference.",
                Location = new Point(M, y), Size = new Size(W, 56), ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblInfo);
            y += 62;

            Controls.Add(new Label { Text = "Target level:", Location = new Point(M, y + 4), Size = new Size(80, 20) });
            _cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(M + 84, y), Size = new Size(W - 84, 24) };
            foreach (var lv in _levels)
                _cbo.Items.Add(new Item(lv.id, $"{lv.name}   ({UnitConversion.FormatFeetInches(lv.elev)})"));
            if (_cbo.Items.Count > 0) _cbo.SelectedIndex = 0;
            // Restore last-used level by name.
            string savedName = DialogMemory.Get(MemKey, "LevelName", null);
            if (!string.IsNullOrEmpty(savedName))
                for (int i = 0; i < _levels.Count; i++)
                    if (_levels[i].name == savedName) { _cbo.SelectedIndex = i; break; }
            Controls.Add(_cbo);
            y += 40;

            var lblNote = new Label
            {
                Text = "Face/work-plane-hosted heads can't be re-leveled in place and are skipped + reported.",
                Location = new Point(M, y), Size = new Size(W, 20), ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblNote);
            y += 30;

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(460 - M - 90, y), Size = new Size(90, 30) };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
            var btnOK = new Button { Text = "Re-Level", Location = new Point(460 - M - 90 - 10 - 100, y), Size = new Size(100, 30) };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (!(_cbo.SelectedItem is Item it))
            {
                MessageBox.Show(this, "Pick a target level.", "Re-Level Sprinklers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            SelectedLevelId = it.Id;
            DialogMemory.Set(MemKey, "LevelName", it.LevelName);
            DialogMemory.Flush();
            DialogResult = DialogResult.OK;
        }

        private class Item
        {
            public int Id;
            public string LevelName;
            private readonly string _display;
            public Item(int id, string display) { Id = id; _display = display; LevelName = display.Split(new[] { "   (" }, StringSplitOptions.None)[0]; }
            public override string ToString() => _display;
        }
    }
}
