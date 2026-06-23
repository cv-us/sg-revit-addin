using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>
    /// Configuration dialog for the Sprinkler Drops command. Generous
    /// spacing; numerics are inches with a units suffix; settings persist
    /// via <see cref="DialogMemory"/> and are restored on reopen.
    /// </summary>
    public class SprinklerDropDialog : Form
    {
        private const string MemKey = "SprinklerDrops";

        public enum ConnectionMode { Continuous, Batch }

        // ── Results ──
        public ConnectionMode Mode { get; private set; } = ConnectionMode.Continuous;
        public int DropPipeTypeId { get; private set; }
        public int ArmPipeTypeId { get; private set; }
        public int FlexTypeId { get; private set; }
        public double SizeInches { get; private set; }
        public double FlexSizeInches { get; private set; }
        public double RiseInches { get; private set; }
        public double TermHeightInches { get; private set; }
        public double StubInches { get; private set; }
        public double MaxFlexInches { get; private set; }
        public bool SwallowWarnings { get; private set; }

        private readonly List<(int id, string name)> _pipeTypes;
        private readonly List<(int id, string name)> _flexTypes;
        private readonly int _defaultPipeTypeId;

        private RadioButton _rbContinuous, _rbBatch;
        private ComboBox _cboDrop, _cboArm, _cboFlex;
        private NumericUpDown _numSize, _numFlexSize, _numRise, _numTerm, _numStub, _numMaxFlex;
        private CheckBox _chkSwallow;

        public SprinklerDropDialog(List<(int id, string name)> pipeTypes, List<(int id, string name)> flexTypes,
            int defaultPipeTypeId)
        {
            _pipeTypes = pipeTypes ?? new List<(int, string)>();
            _flexTypes = flexTypes ?? new List<(int, string)>();
            _defaultPipeTypeId = defaultPipeTypeId;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Place Sprinkler Drops (Flex to Pendent Heads)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(580, 645);

            const int M = 18, W = 544;
            int y = M;

            var lblInfo = new Label
            {
                Text = "Hard-pipe drop ending in a REAL elbow at the base (a stub forces the 90° turn so\n" +
                       "the BOM lists an elbow, not a union), then a flex hose to the head. Each head gets\n" +
                       "its own perpendicular armover + connection.",
                Location = new Point(M, y), Size = new Size(W, 50), ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblInfo);
            y += 56;

            // ── Mode ──
            var grpMode = new GroupBox { Text = "Connection Mode", Location = new Point(M, y), Size = new Size(W, 72) };
            _rbContinuous = new RadioButton
            {
                Text = "Continuous — click a head, then its pipe; repeat until Esc",
                Location = new Point(12, 22), Size = new Size(W - 24, 20)
            };
            _rbBatch = new RadioButton
            {
                Text = "Batch — select heads first, then click one pipe (each head gets its own perpendicular tie-in)",
                Location = new Point(12, 44), Size = new Size(W - 24, 20)
            };
            grpMode.Controls.AddRange(new Control[] { _rbContinuous, _rbBatch });
            Controls.Add(grpMode);
            y += 80;
            bool batch = DialogMemory.GetInt(MemKey, "Mode", 0) == 1;
            _rbBatch.Checked = batch; _rbContinuous.Checked = !batch;

            // ── Types ──
            var grpTypes = new GroupBox { Text = "Pipe & Flex Types", Location = new Point(M, y), Size = new Size(W, 120) };
            int gy = 26;
            AddCombo(grpTypes, "Drop pipe type:", ref gy, out _cboDrop, _pipeTypes);
            AddCombo(grpTypes, "Armover pipe type:", ref gy, out _cboArm, _pipeTypes);
            AddCombo(grpTypes, "Flex pipe type:", ref gy, out _cboFlex, _flexTypes);
            Controls.Add(grpTypes);
            y += 128;

            SelectById(_cboDrop, DialogMemory.GetInt(MemKey, "DropType", _defaultPipeTypeId));
            SelectById(_cboArm, DialogMemory.GetInt(MemKey, "ArmType", _defaultPipeTypeId));
            SelectById(_cboFlex, DialogMemory.GetInt(MemKey, "FlexType", -1));

            // ── Geometry ──
            var grpGeo = new GroupBox { Text = "Geometry (inches)", Location = new Point(M, y), Size = new Size(W, 228) };
            gy = 28;
            _numSize = AddNum(grpGeo, "Drop / armover pipe size:", ref gy, 0.25m, 12, DialogMemory.GetDouble(MemKey, "Size", 1.0), 0.25m);
            _numFlexSize = AddNum(grpGeo, "Flex pipe size:", ref gy, 0.25m, 12, DialogMemory.GetDouble(MemKey, "FlexSize", 1.0), 0.25m);
            _numRise = AddNum(grpGeo, "Return-bend rise above branch (0 = none):", ref gy, 0, 240, DialogMemory.GetDouble(MemKey, "Rise", 0), 0.5m);
            _numTerm = AddNum(grpGeo, "Hard-pipe termination above head:", ref gy, 0, 120, DialogMemory.GetDouble(MemKey, "Term", 12), 0.5m);
            _numStub = AddNum(grpGeo, "Elbow stub length (forces the turn):", ref gy, 1, 24, DialogMemory.GetDouble(MemKey, "Stub", 3), 0.5m);
            _numMaxFlex = AddNum(grpGeo, "Max flex length (0 = no check):", ref gy, 0, 120, DialogMemory.GetDouble(MemKey, "MaxFlex", 0), 1m);
            Controls.Add(grpGeo);
            y += 236;

            _chkSwallow = new CheckBox
            {
                Text = "Swallow recoverable warnings during placement",
                Location = new Point(M + 4, y), Size = new Size(W, 22),
                Checked = DialogMemory.GetBool(MemKey, "Swallow", true)
            };
            Controls.Add(_chkSwallow);
            y += 32;

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(580 - M - 90, y), Size = new Size(90, 32) };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
            var btnOK = new Button { Text = "Start Placing", Location = new Point(580 - M - 90 - 10 - 120, y), Size = new Size(120, 32) };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void AddCombo(GroupBox grp, string label, ref int gy, out ComboBox cbo, List<(int id, string name)> items)
        {
            grp.Controls.Add(new Label { Text = label, Location = new Point(12, gy + 3), Size = new Size(160, 18) });
            cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(178, gy), Size = new Size(350, 24) };
            foreach (var it in items) cbo.Items.Add(new Item(it.id, it.name));
            if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
            grp.Controls.Add(cbo);
            gy += 30;
        }

        private NumericUpDown AddNum(GroupBox grp, string label, ref int gy, decimal min, decimal max, double val, decimal inc)
        {
            grp.Controls.Add(new Label { Text = label, Location = new Point(12, gy + 3), Size = new Size(320, 18) });
            var num = new NumericUpDown
            {
                Location = new Point(340, gy), Size = new Size(80, 24),
                Minimum = min, Maximum = max, DecimalPlaces = 2, Increment = inc,
                Value = (decimal)Math.Max((double)min, Math.Min((double)max, val))
            };
            grp.Controls.Add(num);
            grp.Controls.Add(new Label { Text = "in", Location = new Point(426, gy + 3), Size = new Size(20, 18) });
            gy += 32;
            return num;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (!(_cboDrop.SelectedItem is Item drop) || !(_cboFlex.SelectedItem is Item flex))
            {
                MessageBox.Show(this, "Select a drop pipe type and a flex pipe type.", "Sprinkler Drops",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            var arm = _cboArm.SelectedItem as Item ?? drop;

            Mode = _rbBatch.Checked ? ConnectionMode.Batch : ConnectionMode.Continuous;
            DropPipeTypeId = drop.Id;
            ArmPipeTypeId = arm.Id;
            FlexTypeId = flex.Id;
            SizeInches = (double)_numSize.Value;
            FlexSizeInches = (double)_numFlexSize.Value;
            RiseInches = (double)_numRise.Value;
            TermHeightInches = (double)_numTerm.Value;
            StubInches = (double)_numStub.Value;
            MaxFlexInches = (double)_numMaxFlex.Value;
            SwallowWarnings = _chkSwallow.Checked;

            DialogMemory.SetInt(MemKey, "Mode", Mode == ConnectionMode.Batch ? 1 : 0);
            DialogMemory.SetInt(MemKey, "DropType", DropPipeTypeId);
            DialogMemory.SetInt(MemKey, "ArmType", ArmPipeTypeId);
            DialogMemory.SetInt(MemKey, "FlexType", FlexTypeId);
            DialogMemory.SetDouble(MemKey, "Size", SizeInches);
            DialogMemory.SetDouble(MemKey, "FlexSize", FlexSizeInches);
            DialogMemory.SetDouble(MemKey, "Rise", RiseInches);
            DialogMemory.SetDouble(MemKey, "Term", TermHeightInches);
            DialogMemory.SetDouble(MemKey, "Stub", StubInches);
            DialogMemory.SetDouble(MemKey, "MaxFlex", MaxFlexInches);
            DialogMemory.SetBool(MemKey, "Swallow", SwallowWarnings);
            DialogMemory.Flush();

            DialogResult = DialogResult.OK;
        }

        private static void SelectById(ComboBox cbo, int id)
        {
            if (id < 0) return;
            for (int i = 0; i < cbo.Items.Count; i++)
                if (cbo.Items[i] is Item it && it.Id == id) { cbo.SelectedIndex = i; return; }
        }

        private class Item
        {
            public int Id;
            private readonly string _name;
            public Item(int id, string name) { Id = id; _name = name; }
            public override string ToString() => _name;
        }
    }
}
