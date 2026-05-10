using System;
using System.Drawing;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Flexible Drop Lengths command.
    ///
    /// Collects:
    ///   - Standard drop length (31", 36", 48", 60", 72")
    ///   - Tag orientation (N, NE, E, SE, S, SW, W, NW)
    /// </summary>
    public class FlexDropLengthsDialog : Form
    {
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
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Insert Flexible Drop Lengths";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(300, 310);

            int margin = 15;
            int y = margin;

            // ── Length group ──
            var grpLength = new GroupBox
            {
                Text = "Flexible Drop Standard Lengths",
                Location = new Point(margin, y),
                Size = new Size(270, 155)
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
                Size = new Size(165, 24)
            };
            cboOrientation.Items.AddRange(new object[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" });
            cboOrientation.SelectedIndex = 0;
            Controls.Add(cboOrientation);
            y += 40;

            // ── Buttons ──
            btnOK = new Button
            {
                Text = "Insert Tags",
                DialogResult = DialogResult.OK,
                Location = new Point(95, y),
                Size = new Size(95, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(195, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (rb31.Checked) SelectedLength = "31";
            else if (rb36.Checked) SelectedLength = "36";
            else if (rb48.Checked) SelectedLength = "48";
            else if (rb60.Checked) SelectedLength = "60";
            else SelectedLength = "72";

            TagOrientation = cboOrientation.SelectedItem?.ToString() ?? "N";
        }
    }
}

