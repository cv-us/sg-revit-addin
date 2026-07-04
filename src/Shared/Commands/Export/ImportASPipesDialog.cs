using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Export
{
    /// <summary>
    /// Dialog for ImportASPipesCommand.
    /// Collects: level, pipe type, and piping system type to use when creating pipes.
    /// Selections are remembered between runs via <see cref="DialogMemory"/>.
    /// </summary>
    public class ImportASPipesDialog : DpiAwareForm
    {
        private const string MemKey = "ImportASPipes";

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
            ClientSize = new Size(440, 220);

            int margin = 15;
            int y = margin;
            int labelW = 160;
            int ctrlW = 245;

            // Level
            Controls.Add(new Label { Text = "Associate with Level:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboLevel = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in levelNames) _cboLevel.Items.Add(n);
            if (_cboLevel.Items.Count > 0) _cboLevel.SelectedIndex = 0;
            RestoreRemembered(_cboLevel, "Level");
            Controls.Add(_cboLevel);
            y += 35;

            // Pipe Type
            Controls.Add(new Label { Text = "Pipe Type:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboPipeType = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in pipeTypeNames) _cboPipeType.Items.Add(n);
            SelectDefault(_cboPipeType, pipeTypeNames, "-SS FP Mains");
            RestoreRemembered(_cboPipeType, "PipeType");
            Controls.Add(_cboPipeType);
            y += 35;

            // System Type
            Controls.Add(new Label { Text = "Piping System:", Location = new Point(margin, y + 3), Size = new Size(labelW, 20) });
            _cboSystem = new ComboBox { Location = new Point(margin + labelW, y), Size = new Size(ctrlW, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in systemTypeNames) _cboSystem.Items.Add(n);
            SelectDefault(_cboSystem, systemTypeNames, "Fire Protection Wet");
            RestoreRemembered(_cboSystem, "System");
            Controls.Add(_cboSystem);
            y += 50;

            // Note
            var note = new Label
            {
                Text = "CSV columns expected: [name, X1, Y1, Z1, X2, Y2, Z2] in inches. Header row is skipped.",
                Location = new Point(margin, y),
                Size = new Size(410, 32),
                ForeColor = Color.Gray
            };
            Controls.Add(note);
            y += 40;

            // Buttons (right-aligned with 10px gap)
            // Form width 440, margin 15 → Cancel right edge at 425.
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(335, y), Size = new Size(90, 28) };
            var btnOk = new Button { Text = "Import", DialogResult = DialogResult.OK, Location = new Point(235, y), Size = new Size(90, 28) };
            btnOk.Click += BtnOk_Click;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            SelectedLevelName    = _cboLevel.SelectedItem?.ToString() ?? "";
            SelectedPipeTypeName = _cboPipeType.SelectedItem?.ToString() ?? "";
            SelectedSystemName   = _cboSystem.SelectedItem?.ToString() ?? "";

            // Remember for next time.
            DialogMemory.Set(MemKey, "Level", SelectedLevelName);
            DialogMemory.Set(MemKey, "PipeType", SelectedPipeTypeName);
            DialogMemory.Set(MemKey, "System", SelectedSystemName);
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

