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

        private CheckBox chkCustomPrefix;
        private TextBox txtPrefix;
        private Label lblPrefixHint;

        public PlaceTrimbleMarkersDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Trimble Markers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(420, 310);

            int y = 15;

            // ── Action buttons group ──
            var grpActions = new GroupBox
            {
                Text = "Actions",
                Location = new Point(12, y),
                Size = new Size(380, 125)
            };
            Controls.Add(grpActions);

            var btnPlace = new Button
            {
                Text = "Place Markers",
                Location = new Point(15, 25),
                Size = new Size(350, 28),
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnPlace.Click += (s, e) =>
            {
                SelectedMode = TrimbleMode.PlaceOnly;
                CollectPrefixSettings();
                DialogResult = DialogResult.OK;
            };
            grpActions.Controls.Add(btnPlace);

            var lblPlaceHint = new Label
            {
                Text = "Place new markers at selected hangers/braces (keeps existing markers)",
                Location = new Point(15, 55),
                Size = new Size(350, 15),
                ForeColor = Color.FromArgb(110, 110, 110),
                Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic)
            };
            grpActions.Controls.Add(lblPlaceHint);

            var btnClearAndPlace = new Button
            {
                Text = "Clear && Place Markers",
                Location = new Point(15, 73),
                Size = new Size(170, 28),
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnClearAndPlace.Click += (s, e) =>
            {
                SelectedMode = TrimbleMode.ClearAndPlace;
                CollectPrefixSettings();
                DialogResult = DialogResult.OK;
            };
            grpActions.Controls.Add(btnClearAndPlace);

            var btnClearOnly = new Button
            {
                Text = "Clear Markers Only",
                Location = new Point(195, 73),
                Size = new Size(170, 28),
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnClearOnly.Click += (s, e) =>
            {
                SelectedMode = TrimbleMode.ClearOnly;
                CollectPrefixSettings();
                DialogResult = DialogResult.OK;
            };
            grpActions.Controls.Add(btnClearOnly);

            var lblClearHint = new Label
            {
                Text = "Clear removes Trimble families from the active view before or instead of placing",
                Location = new Point(15, 103),
                Size = new Size(350, 15),
                ForeColor = Color.FromArgb(110, 110, 110),
                Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic)
            };
            grpActions.Controls.Add(lblClearHint);

            y += 135;

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

            // ── Cancel button ──
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(305, y),
                Size = new Size(85, 28)
            };
            Controls.Add(btnCancel);

            CancelButton = btnCancel;
        }

        private void CollectPrefixSettings()
        {
            ClearCustomPrefix = chkCustomPrefix.Checked;
            ClearFamilyPrefix = (txtPrefix.ForeColor == Color.Gray) ? "" : txtPrefix.Text.Trim();
        }
    }
}
