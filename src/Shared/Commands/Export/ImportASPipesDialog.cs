using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Dialog for ImportASPipesCommand.
    /// Collects: level, pipe type, and piping system type to use when creating pipes.
    /// </summary>
    public class ImportASPipesDialog : Form
    {
        public string SelectedLevelName    { get; private set; } = "";
        public string SelectedPipeTypeName { get; private set; } = "";
        public string SelectedSystemName   { get; private set; } = "";

        private readonly ComboBox _cboLevel;
        private readonly ComboBox _cboPipeType;
        private readonly ComboBox _cboSystem;

        public ImportASPipesDialog(
            IList<string> levelNames,
            IList<string> pipeTypeNames,
            IList<string> systemTypeNames)
        {
            Text = "Import AutoSPRINK Pipes from CSV";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 220);

            int margin = 15;
            int y = margin;
            int labelW = 120;
            int ctrlW = 245;

            // Level
            Controls.Add(new Label { Text = "Associate with Level:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboLevel = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in levelNames) _cboLevel.Items.Add(n);
            if (_cboLevel.Items.Count > 0) _cboLevel.SelectedIndex = 0;
            Controls.Add(_cboLevel);
            y += 35;

            // Pipe Type
            Controls.Add(new Label { Text = "Pipe Type:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboPipeType = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in pipeTypeNames) _cboPipeType.Items.Add(n);
            SelectDefault(_cboPipeType, pipeTypeNames, "-SS FP Mains");
            Controls.Add(_cboPipeType);
            y += 35;

            // System Type
            Controls.Add(new Label { Text = "Piping System:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboSystem = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in systemTypeNames) _cboSystem.Items.Add(n);
            SelectDefault(_cboSystem, systemTypeNames, "Fire Protection Wet");
            Controls.Add(_cboSystem);
            y += 50;

            // Note
            var note = new Label
            {
                Text = "CSV columns expected: [name, X1, Y1, Z1, X2, Y2, Z2] in inches. Header row is skipped.",
                Location = new Point(margin, y),
                Size = new Size(370, 32),
                ForeColor = Color.Gray
            };
            Controls.Add(note);
            y += 40;

            // Buttons
            var btnOk = new Button { Text = "Import", DialogResult = DialogResult.OK, Location = new Point(margin + 130, y), Size = new Size(90, 28) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(margin + 230, y), Size = new Size(90, 28) };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                SelectedLevelName    = _cboLevel.SelectedItem?.ToString() ?? "";
                SelectedPipeTypeName = _cboPipeType.SelectedItem?.ToString() ?? "";
                SelectedSystemName   = _cboSystem.SelectedItem?.ToString() ?? "";
            }
            base.OnFormClosing(e);
        }

        private static void SelectDefault(ComboBox cbo, IList<string> names, string fragment)
        {
            if (cbo.Items.Count == 0) return;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cbo.SelectedIndex = i;
                    return;
                }
            }
            cbo.SelectedIndex = 0;
        }
    }
}

