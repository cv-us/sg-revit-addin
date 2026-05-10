using System;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Setup
{
    /// <summary>
    /// Dialog for selecting a folder of .rfa family files to load.
    /// </summary>
    public class LoadFamiliesDialog : Form
    {
        public string FolderPath { get; private set; }
        public bool IncludeSubfolders { get; private set; }

        private TextBox _txtFolder;
        private Button _btnBrowse;
        private CheckBox _chkSubfolders;
        private Button _btnOk;
        private Button _btnCancel;

        public LoadFamiliesDialog(string defaultFolder)
        {
            FolderPath = defaultFolder;
            IncludeSubfolders = true;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "Load Custom Families";
            Size = new Size(560, 200);
            MinimumSize = new Size(460, 200);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;

            // ── Folder path ──
            var lblFolder = new Label
            {
                Text = "Family Folder:",
                Location = new Point(15, y + 3),
                AutoSize = true
            };
            Controls.Add(lblFolder);

            _txtFolder = new TextBox
            {
                Text = FolderPath,
                Location = new Point(110, y),
                Width = 350
            };
            Controls.Add(_txtFolder);

            _btnBrowse = new Button
            {
                Text = "...",
                Location = new Point(468, y - 1),
                Size = new Size(35, 24)
            };
            _btnBrowse.Click += BtnBrowse_Click;
            Controls.Add(_btnBrowse);

            y += 40;

            // ── Include subfolders ──
            _chkSubfolders = new CheckBox
            {
                Text = "Include subfolders (recursive)",
                Location = new Point(110, y),
                AutoSize = true,
                Checked = IncludeSubfolders
            };
            Controls.Add(_chkSubfolders);

            y += 40;

            // ── Info label ──
            var lblInfo = new Label
            {
                Text = "Families already loaded in the project will be skipped.",
                Location = new Point(110, y),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            Controls.Add(lblInfo);

            y += 30;

            // ── OK / Cancel ──
            _btnOk = new Button
            {
                Text = "Load",
                DialogResult = DialogResult.OK,
                Location = new Point(340, y),
                Size = new Size(75, 28)
            };
            _btnOk.Click += BtnOk_Click;
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(425, y),
                Size = new Size(75, 28)
            };
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder containing .rfa family files";
                fbd.SelectedPath = _txtFolder.Text;
                fbd.ShowNewFolderButton = false;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _txtFolder.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            FolderPath = _txtFolder.Text.Trim();
            IncludeSubfolders = _chkSubfolders.Checked;

            if (string.IsNullOrEmpty(FolderPath))
            {
                MessageBox.Show("Please select a folder.", "Load Families",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        }
    }
}
