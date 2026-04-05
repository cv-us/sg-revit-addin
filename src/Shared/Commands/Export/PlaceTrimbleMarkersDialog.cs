using System;
using System.Drawing;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Export
{
    public class PlaceTrimbleMarkersDialog : Form
    {
        public enum TrimbleMode { PlaceOnly, ClearAndPlace, ClearOnly }

        public TrimbleMode SelectedMode { get; private set; } = TrimbleMode.ClearAndPlace;
        public string ClearFamilyPrefix { get; private set; } = "";
        public bool ClearCustomPrefix { get; private set; } = false;

        private RadioButton rbPlaceOnly;
        private RadioButton rbClearAndPlace;
        private RadioButton rbClearOnly;
        private CheckBox chkCustomPrefix;
        private TextBox txtPrefix;
        private Label lblPrefixHint;

        public PlaceTrimbleMarkersDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Place Trimble Markers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(420, 340);

            int y = 15;

            // ── Mode group ──
            var grpMode = new GroupBox
            {
                Text = "Action",
                Location = new Point(12, y),
                Size = new Size(380, 115)
            };
            Controls.Add(grpMode);

            rbClearAndPlace = new RadioButton
            {
                Text = "Clear existing markers, then place new ones",
                Location = new Point(15, 22),
                Size = new Size(350, 22),
                Checked = true
            };
            grpMode.Controls.Add(rbClearAndPlace);

            rbPlaceOnly = new RadioButton
            {
                Text = "Place new markers only (keep existing)",
                Location = new Point(15, 48),
                Size = new Size(350, 22)
            };
            grpMode.Controls.Add(rbPlaceOnly);

            rbClearOnly = new RadioButton
            {
                Text = "Clear markers only (don't place new ones)",
                Location = new Point(15, 74),
                Size = new Size(350, 22)
            };
            grpMode.Controls.Add(rbClearOnly);

            y += 125;

            // ── Custom prefix filter ──
            var grpFilter = new GroupBox
            {
                Text = "Clear Filter",
                Location = new Point(12, y),
                Size = new Size(380, 105)
            };
            Controls.Add(grpFilter);

            var lblDefault = new Label
            {
                Text = "By default, clears families starting with \"-Trimble-\" and \"Trmb_FieldPoints_\"",
                Location = new Point(15, 22),
                Size = new Size(350, 18),
                ForeColor = Color.FromArgb(100, 100, 100)
            };
            grpFilter.Controls.Add(lblDefault);

            chkCustomPrefix = new CheckBox
            {
                Text = "Also clear families starting with:",
                Location = new Point(15, 46),
                Size = new Size(220, 22)
            };
            chkCustomPrefix.CheckedChanged += (s, e) =>
            {
                txtPrefix.Enabled = chkCustomPrefix.Checked;
                lblPrefixHint.Visible = chkCustomPrefix.Checked;
            };
            grpFilter.Controls.Add(chkCustomPrefix);

            txtPrefix = new TextBox
            {
                Location = new Point(240, 45),
                Size = new Size(125, 22),
                Enabled = false,
                Text = "",
                ForeColor = Color.Gray
            };
            txtPrefix.GotFocus += (s, e) =>
            {
                if (txtPrefix.ForeColor == Color.Gray)
                {
                    txtPrefix.Text = "";
                    txtPrefix.ForeColor = Color.Black;
                }
            };
            txtPrefix.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtPrefix.Text))
                {
                    txtPrefix.ForeColor = Color.Gray;
                    txtPrefix.Text = "e.g. FP-";
                }
            };
            txtPrefix.Text = "e.g. FP-";
            grpFilter.Controls.Add(txtPrefix);

            lblPrefixHint = new Label
            {
                Text = "Comma-separate multiple prefixes: FP-, SSG-",
                Location = new Point(15, 74),
                Size = new Size(350, 18),
                ForeColor = Color.FromArgb(130, 130, 130),
                Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic),
                Visible = false
            };
            grpFilter.Controls.Add(lblPrefixHint);

            y += 115;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(210, y),
                Size = new Size(85, 28)
            };
            btnOK.Click += (s, e) =>
            {
                if (rbPlaceOnly.Checked) SelectedMode = TrimbleMode.PlaceOnly;
                else if (rbClearAndPlace.Checked) SelectedMode = TrimbleMode.ClearAndPlace;
                else SelectedMode = TrimbleMode.ClearOnly;

                ClearCustomPrefix = chkCustomPrefix.Checked;
                ClearFamilyPrefix = (txtPrefix.ForeColor == Color.Gray) ? "" : txtPrefix.Text.Trim();
            };
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(305, y),
                Size = new Size(85, 28)
            };
            Controls.Add(btnCancel);

            AcceptButton = btnOK;
            CancelButton = btnCancel;
        }
    }
}
