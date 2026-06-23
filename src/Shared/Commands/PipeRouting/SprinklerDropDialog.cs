using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.PipeRouting
{
    /// <summary>
    /// Configuration dialog for the Sprinkler Drops command (hard-pipe
    /// up-over-down drop ending in a real elbow at the drop base, with a
    /// flex hose to the pendent head). Generous spacing; all numerics are
    /// inches with a units suffix and validation; settings persist via
    /// <see cref="DialogMemory"/>.
    /// </summary>
    public class SprinklerDropDialog : Form
    {
        private const string MemKey = "SprinklerDrops";

        // ── Results ──
        public int DropPipeTypeId { get; private set; }
        public int ArmPipeTypeId { get; private set; }
        public int FlexTypeId { get; private set; }
        public double RiseInches { get; private set; }
        public double TermHeightInches { get; private set; }
        public double StubInches { get; private set; }
        public double MaxFlexInches { get; private set; }
        public bool SwallowWarnings { get; private set; }

        private readonly List<(int id, string name)> _pipeTypes;
        private readonly List<(int id, string name)> _flexTypes;

        private ComboBox _cboDrop, _cboArm, _cboFlex;
        private NumericUpDown _numRise, _numTerm, _numStub, _numMaxFlex;
        private CheckBox _chkSwallow;

        public SprinklerDropDialog(List<(int id, string name)> pipeTypes, List<(int id, string name)> flexTypes)
        {
            _pipeTypes = pipeTypes ?? new List<(int, string)>();
            _flexTypes = flexTypes ?? new List<(int, string)>();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Place Sprinkler Drops (Flex to Pendent Heads)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 530);

            const int M = 18, W = 524, LblW = 250, InX = 280, InW = 246;
            int y = M;

            var lblInfo = new Label
            {
                Text = "Builds a hard-pipe up-over-down drop that ends in a REAL elbow at the\n" +
                       "drop base (a short stub forces a genuine 90° turn, so the BOM lists an\n" +
                       "elbow, not a union), then runs a flex hose from the elbow to the head.",
                Location = new Point(M, y), Size = new Size(W, 50), ForeColor = SystemColors.GrayText
            };
            Controls.Add(lblInfo);
            y += 58;

            // ── Pipe / flex types ──
            var grpTypes = new GroupBox { Text = "Pipe & Flex Types", Location = new Point(M, y), Size = new Size(W, 120) };
            int gy = 26;
            AddCombo(grpTypes, "Drop pipe type:", ref gy, out _cboDrop, _pipeTypes, LblW: 160, inX: 175, inW: 335);
            AddCombo(grpTypes, "Armover pipe type:", ref gy, out _cboArm, _pipeTypes, LblW: 160, inX: 175, inW: 335);
            AddCombo(grpTypes, "Flex pipe type:", ref gy, out _cboFlex, _flexTypes, LblW: 160, inX: 175, inW: 335);
            Controls.Add(grpTypes);
            y += 128;

            // restore type selections
            SelectById(_cboDrop, DialogMemory.GetInt(MemKey, "DropType", -1));
            SelectById(_cboArm, DialogMemory.GetInt(MemKey, "ArmType", -1));
            SelectById(_cboFlex, DialogMemory.GetInt(MemKey, "FlexType", -1));

            // ── Geometry ──
            var grpGeo = new GroupBox { Text = "Drop Geometry (inches)", Location = new Point(M, y), Size = new Size(W, 168) };
            gy = 28;
            _numRise = AddNum(grpGeo, "Return-bend rise above branch:", ref gy, 0, 240, DialogMemory.GetDouble(MemKey, "Rise", 12));
            _numTerm = AddNum(grpGeo, "Hard-pipe termination above head:", ref gy, 0, 120, DialogMemory.GetDouble(MemKey, "Term", 12));
            _numStub = AddNum(grpGeo, "Elbow stub length (forces the turn):", ref gy, 1, 24, DialogMemory.GetDouble(MemKey, "Stub", 3));
            _numMaxFlex = AddNum(grpGeo, "Max flex length (0 = no check):", ref gy, 0, 120, DialogMemory.GetDouble(MemKey, "MaxFlex", 0));
            Controls.Add(grpGeo);
            y += 176;

            // ── Options ──
            _chkSwallow = new CheckBox
            {
                Text = "Swallow recoverable warnings during placement",
                Location = new Point(M + 4, y), Size = new Size(W, 22),
                Checked = DialogMemory.GetBool(MemKey, "Swallow", true)
            };
            Controls.Add(_chkSwallow);
            y += 32;

            // ── Buttons ──
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(560 - M - 90, y), Size = new Size(90, 32) };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
            var btnOK = new Button { Text = "Place Drops", Location = new Point(560 - M - 90 - 10 - 120, y), Size = new Size(120, 32) };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void AddCombo(GroupBox grp, string label, ref int gy, out ComboBox cbo,
            List<(int id, string name)> items, int LblW, int inX, int inW)
        {
            grp.Controls.Add(new Label { Text = label, Location = new Point(12, gy + 3), Size = new Size(LblW, 18) });
            cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(inX, gy), Size = new Size(inW, 24) };
            foreach (var it in items) cbo.Items.Add(new Item(it.id, it.name));
            if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
            grp.Controls.Add(cbo);
            gy += 30;
        }

        private NumericUpDown AddNum(GroupBox grp, string label, ref int gy, decimal min, decimal max, double val)
        {
            grp.Controls.Add(new Label { Text = label, Location = new Point(12, gy + 3), Size = new Size(290, 18) });
            var num = new NumericUpDown
            {
                Location = new Point(310, gy), Size = new Size(80, 24),
                Minimum = min, Maximum = max, DecimalPlaces = 2, Increment = 0.5m,
                Value = (decimal)Math.Max((double)min, Math.Min((double)max, val))
            };
            grp.Controls.Add(num);
            grp.Controls.Add(new Label { Text = "in", Location = new Point(396, gy + 3), Size = new Size(20, 18) });
            gy += 34;
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

            DropPipeTypeId = drop.Id;
            ArmPipeTypeId = arm.Id;
            FlexTypeId = flex.Id;
            RiseInches = (double)_numRise.Value;
            TermHeightInches = (double)_numTerm.Value;
            StubInches = (double)_numStub.Value;
            MaxFlexInches = (double)_numMaxFlex.Value;
            SwallowWarnings = _chkSwallow.Checked;

            DialogMemory.SetInt(MemKey, "DropType", DropPipeTypeId);
            DialogMemory.SetInt(MemKey, "ArmType", ArmPipeTypeId);
            DialogMemory.SetInt(MemKey, "FlexType", FlexTypeId);
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
