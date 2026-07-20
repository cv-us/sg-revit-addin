using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>Options for <see cref="TraceCadPipeCommand"/>. Reports what was found in the
    /// link up front so the sizes can be sanity-checked before anything is placed.</summary>
    public class TraceCadPipeDialog : DpiAwareForm
    {
        private const string MemKey = nameof(TraceCadPipeDialog);

        private ComboBox _cmbPipeType, _cmbSystem, _cmbLevel;
        private RadioButton _rbSnap, _rbAsFit, _rbForce;
        private ComboBox _cmbForce;
        private TextBox _txtMinLen;
        private CheckBox _chkFlatten;

        private readonly IList<(int id, string name)> _pipeTypes;
        private readonly IList<(int id, string name)> _systems;
        private readonly IList<string> _levels;
        private readonly int _runCount;
        private readonly double _totalFt;
        private readonly string _sizeSummary;

        public int PipeTypeId { get; private set; }
        public int SystemTypeId { get; private set; }
        public string LevelName { get; private set; } = "";
        public bool SnapToNominal { get; private set; } = true;
        public double ForceSizeIn { get; private set; }      // 0 = don't force
        public double MinLengthFt { get; private set; }
        public bool FlattenSlope { get; private set; }

        private static readonly (string label, double inches)[] SizeList =
        {
            ("1", 1.315), ("1-1/4", 1.660), ("1-1/2", 1.900), ("2", 2.375), ("2-1/2", 2.875),
            ("3", 3.500), ("4", 4.500), ("6", 6.625), ("8", 8.625), ("10", 10.750), ("12", 12.750)
        };

        public TraceCadPipeDialog(IList<(int id, string name)> pipeTypes,
                                  IList<(int id, string name)> systems,
                                  IList<string> levels,
                                  int runCount, double totalFt, string sizeSummary)
        {
            _pipeTypes = pipeTypes; _systems = systems; _levels = levels;
            _runCount = runCount; _totalFt = totalFt; _sizeSummary = sizeSummary;
            AllowResize = false; RememberSize = false;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Trace CAD Pipe";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 396);
            Font = new Font("Segoe UI", 9f);
            const int M = 15;

            // ── what was found ──
            var grpFound = new GroupBox { Text = "Found in the linked CAD", Location = new Point(M, 12), Size = new Size(530, 84) };
            grpFound.Controls.Add(new Label
            {
                Location = new Point(12, 24),
                Size = new Size(505, 50),
                Text = $"{_runCount} pipe runs,  {_totalFt:0} linear ft\r\n" +
                       $"Fitted sizes:  {_sizeSummary}"
            });
            Controls.Add(grpFound);

            int y = 106;
            var grp = new GroupBox { Text = "Place as", Location = new Point(M, y), Size = new Size(530, 116) };
            int gy = 24;
            grp.Controls.Add(new Label { Text = "Pipe type:", Location = new Point(12, gy + 3), AutoSize = true });
            _cmbPipeType = AddCombo(grp, new Point(96, gy), 415, _pipeTypes.Select(t => t.name),
                DialogMemory.Get(MemKey, "PipeType", ""));
            gy += 28;
            grp.Controls.Add(new Label { Text = "System:", Location = new Point(12, gy + 3), AutoSize = true });
            _cmbSystem = AddCombo(grp, new Point(96, gy), 415, _systems.Select(t => t.name),
                DialogMemory.Get(MemKey, "System", ""));
            gy += 28;
            grp.Controls.Add(new Label { Text = "Level:", Location = new Point(12, gy + 3), AutoSize = true });
            _cmbLevel = AddCombo(grp, new Point(96, gy), 220, _levels, DialogMemory.Get(MemKey, "Level", ""));
            Controls.Add(grp);

            y += 126;
            var grpSize = new GroupBox { Text = "Sizing", Location = new Point(M, y), Size = new Size(530, 104) };
            _rbSnap = new RadioButton { Text = "Snap each run to the nearest nominal pipe size (recommended)", Location = new Point(12, 22), AutoSize = true };
            _rbAsFit = new RadioButton { Text = "Use the measured diameter exactly", Location = new Point(12, 46), AutoSize = true };
            _rbForce = new RadioButton { Text = "Force every pipe to:", Location = new Point(12, 70), AutoSize = true };
            _cmbForce = new ComboBox { Location = new Point(160, 68), Size = new Size(90, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var s in SizeList) _cmbForce.Items.Add(s.label);
            _cmbForce.SelectedIndex = Math.Max(0, DialogMemory.GetInt(MemKey, "ForceIdx", 9));
            grpSize.Controls.Add(new Label { Text = "in", Location = new Point(256, 71), AutoSize = true });
            grpSize.Controls.Add(_rbSnap); grpSize.Controls.Add(_rbAsFit);
            grpSize.Controls.Add(_rbForce); grpSize.Controls.Add(_cmbForce);
            Controls.Add(grpSize);

            int mode = DialogMemory.GetInt(MemKey, "SizeMode", 0);
            _rbSnap.Checked = mode == 0; _rbAsFit.Checked = mode == 1; _rbForce.Checked = mode == 2;
            EventHandler sync = (s, e) => _cmbForce.Enabled = _rbForce.Checked;
            _rbSnap.CheckedChanged += sync; _rbAsFit.CheckedChanged += sync; _rbForce.CheckedChanged += sync;
            sync(null, EventArgs.Empty);

            y += 114;
            Controls.Add(new Label { Text = "Skip runs shorter than", Location = new Point(M + 2, y + 3), AutoSize = true });
            _txtMinLen = new TextBox { Location = new Point(M + 140, y), Size = new Size(50, 22), Text = DialogMemory.Get(MemKey, "MinLen", "2") };
            Controls.Add(_txtMinLen);
            Controls.Add(new Label { Text = "ft", Location = new Point(M + 195, y + 3), AutoSize = true });
            _chkFlatten = new CheckBox
            {
                Text = "Flatten to level (ignore the traced slope)",
                Location = new Point(M + 232, y + 1),
                AutoSize = true,
                Checked = DialogMemory.GetBool(MemKey, "Flatten", false)
            };
            Controls.Add(_chkFlatten);

            y += 34;
            var btnOk = new Button { Text = "Place", Location = new Point(560 - M - 90 - 10 - 90, y), Size = new Size(90, 30) };
            btnOk.Click += (s, e) => OnPlace();
            Controls.Add(btnOk); AcceptButton = btnOk;
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(560 - M - 90, y), Size = new Size(90, 30) };
            Controls.Add(btnCancel); CancelButton = btnCancel;
        }

        private void OnPlace()
        {
            if (_cmbPipeType.SelectedIndex < 0 || _cmbSystem.SelectedIndex < 0 || _cmbLevel.SelectedIndex < 0)
            { MessageBox.Show("Pick a pipe type, system and level.", "Trace CAD Pipe"); return; }

            PipeTypeId = _pipeTypes[_cmbPipeType.SelectedIndex].id;
            SystemTypeId = _systems[_cmbSystem.SelectedIndex].id;
            LevelName = (string)_cmbLevel.SelectedItem;
            SnapToNominal = _rbSnap.Checked;
            ForceSizeIn = _rbForce.Checked ? SizeList[Math.Max(0, _cmbForce.SelectedIndex)].inches : 0.0;
            MinLengthFt = ParseNum(_txtMinLen);
            FlattenSlope = _chkFlatten.Checked;

            DialogMemory.Set(MemKey, "PipeType", (string)_cmbPipeType.SelectedItem);
            DialogMemory.Set(MemKey, "System", (string)_cmbSystem.SelectedItem);
            DialogMemory.Set(MemKey, "Level", LevelName);
            DialogMemory.SetInt(MemKey, "SizeMode", _rbSnap.Checked ? 0 : _rbAsFit.Checked ? 1 : 2);
            DialogMemory.SetInt(MemKey, "ForceIdx", Math.Max(0, _cmbForce.SelectedIndex));
            DialogMemory.Set(MemKey, "MinLen", _txtMinLen.Text);
            DialogMemory.SetBool(MemKey, "Flatten", FlattenSlope);
            DialogMemory.Flush();

            DialogResult = DialogResult.OK;
            Close();
        }

        private static ComboBox AddCombo(Control parent, Point loc, int width, IEnumerable<string> items, string selected)
        {
            var cmb = new ComboBox { Location = loc, Size = new Size(width, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string s in items) cmb.Items.Add(s);
            if (!string.IsNullOrEmpty(selected))
            {
                int i = cmb.Items.IndexOf(selected);
                if (i >= 0) cmb.SelectedIndex = i;
            }
            if (cmb.SelectedIndex < 0 && cmb.Items.Count > 0) cmb.SelectedIndex = 0;
            parent.Controls.Add(cmb);
            return cmb;
        }

        private static double ParseNum(TextBox tb)
        {
            double d;
            return double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : 0.0;
        }
    }
}
