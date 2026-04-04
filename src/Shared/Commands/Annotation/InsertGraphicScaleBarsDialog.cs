using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Graphic Scale Bars command.
    ///
    /// Collects:
    ///   - Sheet selection mode (all sheets or specific sheets)
    ///   - Selected sheets (if specific)
    ///
    /// Migrated from: "AutoInsert - Graphic Scale Bars To Sheets.dyn"
    /// </summary>
    public class InsertGraphicScaleBarsDialog : Form
    {
        // ── Results ──
        public bool ProcessAllSheets { get; private set; }
        public List<string> SelectedSheetNumbers { get; private set; } = new List<string>();

        // ── Controls ──
        private RadioButton rbAllSheets;
        private RadioButton rbSelectedSheets;
        private CheckedListBox lstSheets;
        private Button btnSelectAll;
        private Button btnSelectNone;
        private Button btnOK;
        private Button btnCancel;

        private readonly List<SheetItem> _sheets;

        public class SheetItem
        {
            public string Number { get; set; }
            public string Name { get; set; }
            public override string ToString() => $"{Number} - {Name}";
        }

        public InsertGraphicScaleBarsDialog(List<SheetItem> sheets)
        {
            _sheets = sheets;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Insert Graphic Scale Bars";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 430);

            int margin = 15;
            int y = margin;

            // ── Description ──
            var lblDesc = new Label
            {
                Text = "Inserts graphic scale bar annotations on sheets based on\nthe scale of each view placed on the sheet.",
                Location = new Point(margin, y),
                Size = new Size(370, 36),
                AutoSize = false
            };
            Controls.Add(lblDesc);
            y += 44;

            // ── Mode selection ──
            rbAllSheets = new RadioButton
            {
                Text = "All sheets in the project",
                Location = new Point(margin, y),
                Size = new Size(370, 20),
                Checked = true
            };
            rbAllSheets.CheckedChanged += (s, e) => lstSheets.Enabled = !rbAllSheets.Checked;
            Controls.Add(rbAllSheets);
            y += 24;

            rbSelectedSheets = new RadioButton
            {
                Text = "Selected sheets only:",
                Location = new Point(margin, y),
                Size = new Size(370, 20)
            };
            Controls.Add(rbSelectedSheets);
            y += 28;

            // ── Sheet list ──
            lstSheets = new CheckedListBox
            {
                Location = new Point(margin, y),
                Size = new Size(370, 220),
                CheckOnClick = true,
                Enabled = false
            };
            foreach (var sheet in _sheets.OrderBy(s => s.Number))
                lstSheets.Items.Add(sheet);
            Controls.Add(lstSheets);
            y += 225;

            // ── Select all/none buttons ──
            btnSelectAll = new Button
            {
                Text = "Select All",
                Location = new Point(margin, y),
                Size = new Size(80, 25)
            };
            btnSelectAll.Click += (s, e) =>
            {
                for (int i = 0; i < lstSheets.Items.Count; i++)
                    lstSheets.SetItemChecked(i, true);
            };
            Controls.Add(btnSelectAll);

            btnSelectNone = new Button
            {
                Text = "Select None",
                Location = new Point(100, y),
                Size = new Size(80, 25)
            };
            btnSelectNone.Click += (s, e) =>
            {
                for (int i = 0; i < lstSheets.Items.Count; i++)
                    lstSheets.SetItemChecked(i, false);
            };
            Controls.Add(btnSelectNone);
            y += 35;

            // ── Buttons ──
            btnOK = new Button
            {
                Text = "Insert Scale Bars",
                DialogResult = DialogResult.OK,
                Location = new Point(185, y),
                Size = new Size(115, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(305, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            ProcessAllSheets = rbAllSheets.Checked;

            if (!ProcessAllSheets)
            {
                SelectedSheetNumbers = lstSheets.CheckedItems
                    .Cast<SheetItem>()
                    .Select(s => s.Number)
                    .ToList();

                if (SelectedSheetNumbers.Count == 0)
                {
                    MessageBox.Show("Please select at least one sheet.", "No Sheets Selected",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            }
        }
    }
}
