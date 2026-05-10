using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Dialog for ImportASSprinklersCommand.
    /// Collects: level, sprinkler family type, and host floor offset.
    /// </summary>
    public class ImportASSprinklersDialog : Form
    {
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
            ClientSize = new Size(420, 220);

            int margin = 15;
            int y = margin;
            int labelW = 130;
            int ctrlW = 255;

            // Level
            Controls.Add(new Label { Text = "Associate with Level:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboLevel = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in levelNames) _cboLevel.Items.Add(n);
            if (_cboLevel.Items.Count > 0) _cboLevel.SelectedIndex = 0;
            Controls.Add(_cboLevel);
            y += 35;

            // Family Type
            Controls.Add(new Label { Text = "Sprinkler Family Type:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboFamilyType = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in familyTypeNames) _cboFamilyType.Items.Add(n);
            if (_cboFamilyType.Items.Count > 0) _cboFamilyType.SelectedIndex = 0;
            Controls.Add(_cboFamilyType);
            y += 35;

            // Offset
            Controls.Add(new Label { Text = "Z Offset from Level (in):", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _txtOffset = new TextBox { Location = new Point(margin + labelW, y), Size = new Size(80, 22), Text = "0" };
            Controls.Add(_txtOffset);
            y += 50;

            // Note
            var note = new Label
            {
                Text = "CSV columns expected: [name, X, Y, Z] in inches. Header row is skipped.",
                Location = new Point(margin, y),
                Size = new Size(390, 32),
                ForeColor = Color.Gray
            };
            Controls.Add(note);
            y += 40;

            // Buttons
            var btnOk = new Button { Text = "Import", DialogResult = DialogResult.OK, Location = new Point(margin + 140, y), Size = new Size(90, 28) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(margin + 240, y), Size = new Size(90, 28) };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                SelectedLevelName      = _cboLevel.SelectedItem?.ToString() ?? "";
                SelectedFamilyTypeName = _cboFamilyType.SelectedItem?.ToString() ?? "";

                if (!double.TryParse(_txtOffset.Text.Trim(), out double offsetIn))
                    offsetIn = 0.0;
                OffsetFromLevel = offsetIn / 12.0; // convert inches → feet
            }
            base.OnFormClosing(e);
        }
    }
}

