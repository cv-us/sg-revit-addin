using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Room Name/Number Text Notes command.
    ///
    /// Collects:
    ///   - Linked model selection
    ///   - Level filter (from linked rooms)
    ///   - TextNoteType selection
    ///   - Whether to delete existing text notes of the selected type
    /// </summary>
    public class RoomTextNotesDialog : Form
    {
        // ── Results ──
        public int SelectedLinkIndex { get; private set; } = -1;
        public string SelectedLevelName { get; private set; }
        public int SelectedTextNoteTypeIndex { get; private set; } = -1;
        public bool DeleteExisting { get; private set; } = true;

        // ── Controls ──
        private ComboBox cboLink, cboLevel, cboTextType;
        private CheckBox chkDelete;

        private readonly List<string> _linkNames;
        private readonly List<string> _levelNames;
        private readonly List<string> _textNoteTypeNames;

        public RoomTextNotesDialog(
            List<string> linkNames,
            List<string> levelNames,
            List<string> textNoteTypeNames)
        {
            _linkNames = linkNames;
            _levelNames = levelNames;
            _textNoteTypeNames = textNoteTypeNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Room Name TextNotes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 320);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(450, 55)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Places TextNotes for linked rooms in the active view. Room names are stacked\n" +
                       "with each word on a new line, followed by the room number.",
                Location = new Point(10, 16),
                Size = new Size(430, 32)
            });
            Controls.Add(grpInfo);
            y += 63;

            // ── Settings ──
            var grpSettings = new GroupBox
            {
                Text = "Settings",
                Location = new Point(margin, y),
                Size = new Size(450, 155)
            };

            grpSettings.Controls.Add(new Label { Text = "Linked Model:", Location = new Point(10, 25), Size = new Size(90, 18) });
            cboLink = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 22),
                Size = new Size(330, 24)
            };
            foreach (var n in _linkNames) cboLink.Items.Add(n);
            if (cboLink.Items.Count > 0) cboLink.SelectedIndex = 0;
            grpSettings.Controls.Add(cboLink);

            grpSettings.Controls.Add(new Label { Text = "Level:", Location = new Point(10, 58), Size = new Size(90, 18) });
            cboLevel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 55),
                Size = new Size(330, 24)
            };
            foreach (var n in _levelNames) cboLevel.Items.Add(n);
            if (cboLevel.Items.Count > 0) cboLevel.SelectedIndex = 0;
            grpSettings.Controls.Add(cboLevel);

            grpSettings.Controls.Add(new Label { Text = "Text Style:", Location = new Point(10, 91), Size = new Size(90, 18) });
            cboTextType = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 88),
                Size = new Size(330, 24)
            };
            foreach (var n in _textNoteTypeNames) cboTextType.Items.Add(n);
            if (cboTextType.Items.Count > 0) cboTextType.SelectedIndex = 0;
            grpSettings.Controls.Add(cboTextType);

            chkDelete = new CheckBox
            {
                Text = "Delete existing text notes of selected type in active view first",
                Location = new Point(10, 122),
                Size = new Size(420, 20),
                Checked = true
            };
            grpSettings.Controls.Add(chkDelete);

            Controls.Add(grpSettings);
            y += 165;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Place Text Notes",
                DialogResult = DialogResult.OK,
                Location = new Point(260, y),
                Size = new Size(120, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(385, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (_linkNames.Count == 0)
            {
                MessageBox.Show("No loaded Revit links found.",
                    "No Links", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (_levelNames.Count == 0)
            {
                MessageBox.Show("No rooms with levels found in the linked model.",
                    "No Rooms", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (_textNoteTypeNames.Count == 0)
            {
                MessageBox.Show("No TextNoteTypes found in the project.",
                    "No Text Styles", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedLinkIndex = cboLink.SelectedIndex;
            SelectedLevelName = cboLevel.SelectedItem?.ToString() ?? "";
            SelectedTextNoteTypeIndex = cboTextType.SelectedIndex;
            DeleteExisting = chkDelete.Checked;
        }
    }
}
