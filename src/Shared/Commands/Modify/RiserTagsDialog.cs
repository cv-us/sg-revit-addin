using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify
{
    /// <summary>
    /// Options for the Riser Tags command. Places a chosen pipe-tag family (your
    /// riser-nipple symbol, with its mask) at the TOP of vertical pipes, centered in
    /// plan and auto-rotated to the branch it comes from. Scope, filters and a couple
    /// of family-calibration nudges (center offset + rotation offset) are exposed so
    /// the tag lands exactly on the riser regardless of the family's origin.
    /// </summary>
    public class RiserTagsDialog : DpiAwareForm
    {
        private const string MemKey = "RiserTags";

        public enum RiserScope { Selection, ActiveView, WholeModel }
        public enum RiserAction { Cancel, Place, Remove }

        // ── Results ──
        public RiserAction Action { get; private set; } = RiserAction.Cancel;
        public string SelFamily { get; private set; }
        public string SelType { get; private set; }
        public RiserScope Scope { get; private set; } = RiserScope.Selection;
        public bool VerticalOnly { get; private set; } = true;
        public bool DropsOnly { get; private set; } = false;
        public bool AutoRotate { get; private set; } = true;
        public double CenterNudgeXin { get; private set; }
        public double CenterNudgeYin { get; private set; }
        public double RotationOffsetDeg { get; private set; }

        // ── Inputs ──
        private readonly List<string> _families;
        private readonly Dictionary<string, IList<string>> _famToTypes;

        private ComboBox _cboFamily, _cboType;
        private RadioButton _rbSel, _rbView, _rbModel;
        private CheckBox _chkVertical, _chkDrops, _chkRotate;
        private TextBox _txtNudgeX, _txtNudgeY, _txtRot;

        public RiserTagsDialog(List<string> families, Dictionary<string, IList<string>> famToTypes)
        {
            _families = families ?? new List<string>();
            _famToTypes = famToTypes ?? new Dictionary<string, IList<string>>();
            AllowResize = false;
            RememberSize = false;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Riser Tags";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 404);

            const int M = 15, W = 470;
            int y = M;

            Controls.Add(new Label
            {
                Text = "Places your riser-nipple pipe tag at the TOP of vertical pipes, centered in plan and " +
                       "rotated to the branch it comes from. Pick the tag family/type and scope below.",
                Location = new Point(M, y), Size = new Size(W, 34), ForeColor = SystemColors.GrayText
            });
            y += 40;

            // ── Tag family / type ──
            Controls.Add(new Label { Text = "Tag family:", Location = new Point(M, y + 3), AutoSize = true });
            _cboFamily = new ComboBox
            {
                Location = new Point(M + 80, y), Size = new Size(W - 80, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cboFamily.Items.AddRange(_families.Cast<object>().ToArray());
            _cboFamily.SelectedIndexChanged += (s, e) => RefillTypes();
            Controls.Add(_cboFamily);
            y += 30;

            Controls.Add(new Label { Text = "Type:", Location = new Point(M, y + 3), AutoSize = true });
            _cboType = new ComboBox
            {
                Location = new Point(M + 80, y), Size = new Size(W - 80, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            Controls.Add(_cboType);
            y += 34;

            // Restore last family/type, else first.
            string savedFam = DialogMemory.Get(MemKey, "Family", null);
            _cboFamily.SelectedItem = (savedFam != null && _families.Contains(savedFam)) ? savedFam
                                     : (_families.Count > 0 ? _families[0] : null);
            if (_cboFamily.SelectedIndex < 0 && _cboFamily.Items.Count > 0) _cboFamily.SelectedIndex = 0;
            RefillTypes();
            string savedType = DialogMemory.Get(MemKey, "Type", null);
            if (savedType != null && _cboType.Items.Contains(savedType)) _cboType.SelectedItem = savedType;

            // ── Scope ──
            var grpScope = new GroupBox { Text = "Scope", Location = new Point(M, y), Size = new Size(W, 50) };
            _rbSel = new RadioButton { Text = "Selected pipes", Location = new Point(12, 20), AutoSize = true };
            _rbView = new RadioButton { Text = "Active view", Location = new Point(140, 20), AutoSize = true };
            _rbModel = new RadioButton { Text = "Whole model", Location = new Point(260, 20), AutoSize = true };
            int savedScope = DialogMemory.GetInt(MemKey, "Scope", 0);
            _rbSel.Checked = savedScope == 0; _rbView.Checked = savedScope == 1; _rbModel.Checked = savedScope == 2;
            if (!_rbSel.Checked && !_rbView.Checked && !_rbModel.Checked) _rbSel.Checked = true;
            grpScope.Controls.AddRange(new Control[] { _rbSel, _rbView, _rbModel });
            Controls.Add(grpScope);
            y += 58;

            // ── Filters ──
            _chkVertical = new CheckBox
            {
                Text = "Vertical pipes only", Location = new Point(M, y), AutoSize = true,
                Checked = DialogMemory.GetBool(MemKey, "VerticalOnly", true)
            };
            _chkDrops = new CheckBox
            {
                Text = "Only drops (reach a sprinkler)", Location = new Point(M + 180, y), AutoSize = true,
                Checked = DialogMemory.GetBool(MemKey, "DropsOnly", false)
            };
            Controls.Add(_chkVertical); Controls.Add(_chkDrops);
            y += 26;

            _chkRotate = new CheckBox
            {
                Text = "Auto-rotate the tag to the branch direction", Location = new Point(M, y), AutoSize = true,
                Checked = DialogMemory.GetBool(MemKey, "AutoRotate", true)
            };
            Controls.Add(_chkRotate);
            y += 32;

            // ── Calibration ──
            var grpCal = new GroupBox { Text = "Fine-tune (for your tag family)", Location = new Point(M, y), Size = new Size(W, 54) };
            grpCal.Controls.Add(new Label { Text = "Center nudge  X", Location = new Point(12, 24), AutoSize = true });
            _txtNudgeX = new TextBox { Location = new Point(105, 21), Size = new Size(48, 22), Text = DialogMemory.Get(MemKey, "NudgeX", "0") };
            grpCal.Controls.Add(_txtNudgeX);
            grpCal.Controls.Add(new Label { Text = "Y", Location = new Point(162, 24), AutoSize = true });
            _txtNudgeY = new TextBox { Location = new Point(180, 21), Size = new Size(48, 22), Text = DialogMemory.Get(MemKey, "NudgeY", "0") };
            grpCal.Controls.Add(_txtNudgeY);
            grpCal.Controls.Add(new Label { Text = "in", Location = new Point(232, 24), AutoSize = true });
            grpCal.Controls.Add(new Label { Text = "Rotate +", Location = new Point(300, 24), AutoSize = true });
            _txtRot = new TextBox { Location = new Point(358, 21), Size = new Size(48, 22), Text = DialogMemory.Get(MemKey, "RotOff", "0") };
            grpCal.Controls.Add(_txtRot);
            grpCal.Controls.Add(new Label { Text = "°", Location = new Point(410, 24), AutoSize = true });
            Controls.Add(grpCal);
            y += 62;

            // ── Buttons ──
            var btnRemove = new Button { Text = "Remove Riser Tags", Location = new Point(M, y), Size = new Size(150, 30) };
            btnRemove.Click += (s, e) => { Action = RiserAction.Remove; Capture(persist: false); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btnRemove);

            var btnPlace = new Button { Text = "Place", Location = new Point(500 - M - 90 - 10 - 90, y), Size = new Size(90, 30) };
            btnPlace.Click += (s, e) => OnPlace();
            AcceptButton = btnPlace;
            Controls.Add(btnPlace);

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(500 - M - 90, y), Size = new Size(90, 30) };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void RefillTypes()
        {
            _cboType.Items.Clear();
            string fam = _cboFamily.SelectedItem as string;
            if (fam != null && _famToTypes.TryGetValue(fam, out var types))
                _cboType.Items.AddRange(types.Cast<object>().ToArray());
            if (_cboType.Items.Count > 0) _cboType.SelectedIndex = 0;
        }

        private static double ParseNum(TextBox tb)
        {
            return double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        }

        private void OnPlace()
        {
            if (_cboFamily.SelectedItem == null)
            {
                MessageBox.Show(this, "Pick a tag family.", "Riser Tags", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Action = RiserAction.Place;
            Capture(persist: true);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Capture(bool persist)
        {
            SelFamily = _cboFamily.SelectedItem as string;
            SelType = _cboType.SelectedItem as string;
            Scope = _rbView.Checked ? RiserScope.ActiveView : _rbModel.Checked ? RiserScope.WholeModel : RiserScope.Selection;
            VerticalOnly = _chkVertical.Checked;
            DropsOnly = _chkDrops.Checked;
            AutoRotate = _chkRotate.Checked;
            CenterNudgeXin = ParseNum(_txtNudgeX);
            CenterNudgeYin = ParseNum(_txtNudgeY);
            RotationOffsetDeg = ParseNum(_txtRot);

            if (!persist) return;
            DialogMemory.Set(MemKey, "Family", SelFamily ?? "");
            DialogMemory.Set(MemKey, "Type", SelType ?? "");
            DialogMemory.SetInt(MemKey, "Scope", (int)Scope);
            DialogMemory.SetBool(MemKey, "VerticalOnly", VerticalOnly);
            DialogMemory.SetBool(MemKey, "DropsOnly", DropsOnly);
            DialogMemory.SetBool(MemKey, "AutoRotate", AutoRotate);
            DialogMemory.Set(MemKey, "NudgeX", _txtNudgeX.Text);
            DialogMemory.Set(MemKey, "NudgeY", _txtNudgeY.Text);
            DialogMemory.Set(MemKey, "RotOff", _txtRot.Text);
            DialogMemory.Flush();
        }
    }
}
