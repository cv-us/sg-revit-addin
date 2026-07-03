using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Setup
{
    /// <summary>
    /// Dialog for selecting a folder of .rfa family files to load.
    /// </summary>
    public class LoadFamiliesDialog : DpiAwareForm
    {
        private const string MemKey = "LoadFamilies";

        public string FolderPath { get; private set; }
        public bool IncludeSubfolders { get; private set; }

        private TextBox _txtFolder;
        private Button _btnBrowse;
        private CheckBox _chkSubfolders;
        private Button _btnOk;
        private Button _btnCancel;

        public LoadFamiliesDialog(string defaultFolder)
        {
            // Last-used folder wins over the command default, if it still exists.
            string remembered = DialogMemory.Get(MemKey, "Folder", null);
            FolderPath = !string.IsNullOrEmpty(remembered) && Directory.Exists(remembered)
                ? remembered : defaultFolder;
            IncludeSubfolders = DialogMemory.GetBool(MemKey, "Subfolders", true);
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "Load Custom Families";
            ClientSize = new Size(520, 170);
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
                Width = 350,
                // Explicit: widen with the dialog (long paths are the point of resizing).
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_txtFolder);

            _btnBrowse = new Button
            {
                Text = "...",
                Location = new Point(468, y - 1),
                Size = new Size(35, 24),
                // Explicit: stay glued to the folder row (auto-flex would bottom-pin it).
                Anchor = AnchorStyles.Top | AnchorStyles.Right
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
                Size = new Size(75, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            _btnOk.Click += BtnOk_Click;
            Controls.Add(_btnOk);

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(425, y),
                Size = new Size(75, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
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
                MessageBox.Show(this, "Please select a folder.", "Load Families",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (!Directory.Exists(FolderPath))
            {
                MessageBox.Show(this, "Folder does not exist:\n" + FolderPath, "Load Families",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            DialogMemory.Set(MemKey, "Folder", FolderPath);
            DialogMemory.SetBool(MemKey, "Subfolders", IncludeSubfolders);
            DialogMemory.Flush();
        }
    }
}

