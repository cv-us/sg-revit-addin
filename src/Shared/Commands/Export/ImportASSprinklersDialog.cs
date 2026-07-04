using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Dialog for ImportASSprinklersCommand.
    /// Collects: level, sprinkler family type, and host floor offset.
    /// Selections are remembered between runs via <see cref="DialogMemory"/>.
    /// </summary>
    public class ImportASSprinklersDialog : DpiAwareForm
    {
        private const string MemKey = "ImportASSprinklers";

        public string SelectedLevelName        { get; private set; } = "";
        public string SelectedFamilyTypeName   { get; private set; } = "";
        public double OffsetFromLevel          { get; private set; } = 0.0;

        private readonly ComboBox _cboLevel;
        private readonly ComboBox _cboFamilyType;
        private readonly TextBox  _txtOffset;

        public ImportASSprinklersDialog(
            IList<string> levelNames,
            IList<string> familyTypeNames)
        {
            Text = "Import AutoSPRINK Sprinklers from CSV";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 220);

            int margin = 15;
            int y = margin;
            int labelW = 160;
            int ctrlW = 265;

            // Level
            Controls.Add(new Label { Text = "Associate with Level:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboLevel = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in levelNames) _cboLevel.Items.Add(n);
            if (_cboLevel.Items.Count > 0) _cboLevel.SelectedIndex = 0;
            RestoreRemembered(_cboLevel, "Level");
            Controls.Add(_cboLevel);
            y += 35;

            // Family Type
            Controls.Add(new Label { Text = "Sprinkler Family Type:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboFamilyType = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in familyTypeNames) _cboFamilyType.Items.Add(n);
            if (_cboFamilyType.Items.Count > 0) _cboFamilyType.SelectedIndex = 0;
            RestoreRemembered(_cboFamilyType, "FamilyType");
            Controls.Add(_cboFamilyType);
            y += 35;

            // Offset
            Controls.Add(new Label { Text = "Z Offset from Level (in):", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _txtOffset = new TextBox { Location = new Point(margin + labelW, y), Size = new Size(80, 22), Text = DialogMemory.Get(MemKey, "OffsetIn", "0") };
            Controls.Add(_txtOffset);
            y += 50;

            // Note
            var note = new Label
            {
                Text = "CSV columns expected: [name, X, Y, Z] in inches. Header row is skipped.",
                Location = new Point(margin, y),
                Size = new Size(430, 32),
                ForeColor = Color.Gray
            };
            Controls.Add(note);
            y += 40;

            // Buttons (right-aligned with 10px gap)
            // Form width 460, margin 15 → Cancel right edge at 445.
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(355, y), Size = new Size(90, 28) };
            var btnOk = new Button { Text = "Import", DialogResult = DialogResult.OK, Location = new Point(255, y), Size = new Size(90, 28) };
            btnOk.Click += BtnOk_Click;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string offsetText = _txtOffset.Text.Trim();
            double offsetIn = 0.0;
            if (offsetText.Length > 0 && !double.TryParse(offsetText, out offsetIn))
            {
                MessageBox.Show(this,
                    "Z offset must be a number in inches (e.g. 1.5), or blank for 0.",
                    "Import AutoSPRINK Sprinklers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;   // keep the dialog open
                return;
            }

            SelectedLevelName      = _cboLevel.SelectedItem?.ToString() ?? "";
            SelectedFamilyTypeName = _cboFamilyType.SelectedItem?.ToString() ?? "";
            OffsetFromLevel        = offsetIn / 12.0; // convert inches → feet

            // Remember for next time.
            DialogMemory.Set(MemKey, "Level", SelectedLevelName);
            DialogMemory.Set(MemKey, "FamilyType", SelectedFamilyTypeName);
            DialogMemory.Set(MemKey, "OffsetIn", offsetText.Length > 0 ? offsetText : "0");
            DialogMemory.Flush();
        }

        /// <summary>Re-select the remembered item, but only if it still exists in the model.</summary>
        private static void RestoreRemembered(ComboBox cbo, string field)
        {
            string remembered = DialogMemory.Get(MemKey, field, "");
            if (string.IsNullOrEmpty(remembered)) return;
            int i = cbo.Items.IndexOf(remembered);
            if (i >= 0) cbo.SelectedIndex = i;
        }
    }
}

