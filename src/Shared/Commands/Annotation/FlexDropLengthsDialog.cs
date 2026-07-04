using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Flexible Drop Lengths command.
    ///
    /// Collects:
    ///   - Standard drop length (31", 36", 48", 60", 72")
    ///   - Tag orientation (N, NE, E, SE, S, SW, W, NW)
    /// </summary>
    public class FlexDropLengthsDialog : DpiAwareForm
    {
        private const string MemKey = "FlexDropSet";

        // ── Results ──
        public string SelectedLength { get; private set; }
        public string TagOrientation { get; private set; }

        // ── Controls ──
        private RadioButton rb31, rb36, rb48, rb60, rb72;
        private ComboBox cboOrientation;
        private Button btnOK;
        private Button btnCancel;

        public FlexDropLengthsDialog()
        {
            // Last-used values win so the dialog re-opens as it was left.
            SelectedLength = DialogMemory.Get(MemKey, "Length", "31");
            TagOrientation = DialogMemory.Get(MemKey, "Orientation", "N");
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Insert Flexible Drop Lengths";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(340, 265);

            int margin = 15;
            int y = margin;

            // ── Length group ──
            var grpLength = new GroupBox
            {
                Text = "Flexible Drop Standard Lengths",
                Location = new Point(margin, y),
                Size = new Size(310, 155)
            };

            int ry = 22;
            rb31 = new RadioButton { Text = "31 Inches", Location = new Point(15, ry), Size = new Size(120, 20), Checked = true };
            ry += 24;
            rb36 = new RadioButton { Text = "36 Inches", Location = new Point(15, ry), Size = new Size(120, 20) };
            ry += 24;
            rb48 = new RadioButton { Text = "48 Inches", Location = new Point(15, ry), Size = new Size(120, 20) };
            ry += 24;
            rb60 = new RadioButton { Text = "60 Inches", Location = new Point(15, ry), Size = new Size(120, 20) };
            ry += 24;
            rb72 = new RadioButton { Text = "72 Inches", Location = new Point(15, ry), Size = new Size(120, 20) };

            grpLength.Controls.AddRange(new Control[] { rb31, rb36, rb48, rb60, rb72 });

            // Restore the remembered length (after parenting so the radio
            // auto-uncheck works; rb31 stays checked otherwise).
            switch (SelectedLength)
            {
                case "36": rb36.Checked = true; break;
                case "48": rb48.Checked = true; break;
                case "60": rb60.Checked = true; break;
                case "72": rb72.Checked = true; break;
            }

            Controls.Add(grpLength);
            y += 165;

            // ── Tag Orientation ──
            var lblOrient = new Label
            {
                Text = "Tag Orientation:",
                Location = new Point(margin, y + 3),
                Size = new Size(100, 20)
            };
            Controls.Add(lblOrient);

            cboOrientation = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(120, y),
                Size = new Size(205, 24)
            };
            cboOrientation.Items.AddRange(new object[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" });
            int orientIdx = cboOrientation.Items.IndexOf(TagOrientation);
            cboOrientation.SelectedIndex = orientIdx >= 0 ? orientIdx : 0;
            Controls.Add(cboOrientation);
            y += 40;

            // ── Buttons (right-aligned with 10px gap) ──
            // Form width 340, margin 15 → Cancel right edge at 325.
            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(250, y),
                Size = new Size(75, 30),
                TabIndex = 101
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            btnOK = new Button
            {
                Text = "Tag Drops",
                DialogResult = DialogResult.OK,
                Location = new Point(145, y),
                Size = new Size(95, 30),
                TabIndex = 100
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (rb31.Checked) SelectedLength = "31";
            else if (rb36.Checked) SelectedLength = "36";
            else if (rb48.Checked) SelectedLength = "48";
            else if (rb60.Checked) SelectedLength = "60";
            else SelectedLength = "72";

            TagOrientation = cboOrientation.SelectedItem?.ToString() ?? "N";

            // Remember for next time.
            DialogMemory.Set(MemKey, "Length", SelectedLength);
            DialogMemory.Set(MemKey, "Orientation", TagOrientation);
            DialogMemory.Flush();
        }
    }
}

