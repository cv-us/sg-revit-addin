using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Export
{
    public class PlaceTrimbleMarkersDialog : DpiAwareForm
    {
        private const string MemKey = "PlaceTrimbleMarkers";

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
            ClientSize = new Size(480, 315);

            int y = 15;

            // ── Action buttons group ──
            var grpActions = new GroupBox
            {
                Text = "Actions",
                Location = new Point(15, y),
                Size = new Size(450, 125)
            };
            Controls.Add(grpActions);

            var btnPlace = new Button
            {
                Text = "Place Markers",
                Location = new Point(15, 25),
                Size = new Size(420, 28),
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
                Size = new Size(420, 15),
                ForeColor = Color.FromArgb(110, 110, 110),
                Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic)
            };
            grpActions.Controls.Add(lblPlaceHint);

            var btnClearAndPlace = new Button
            {
                Text = "Clear && Place Markers",
                Location = new Point(15, 73),
                Size = new Size(205, 28),
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnClearAndPlace.Click += (s, e) =>
            {
                SelectedMode = TrimbleMode.ClearAndPlace;
                CollectPrefixSettings();
                DialogResult = DialogResult.OK;
            };
            grpActions.Controls.Add(btnClearAndPlace);
            AcceptButton = btnClearAndPlace;   // Enter = default action

            var btnClearOnly = new Button
            {
                Text = "Clear Markers Only",
                Location = new Point(230, 73),
                Size = new Size(205, 28),
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
                Size = new Size(420, 15),
                ForeColor = Color.FromArgb(110, 110, 110),
                Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic)
            };
            grpActions.Controls.Add(lblClearHint);

            y += 135;

            // ── Custom prefix filter ──
            var grpFilter = new GroupBox
            {
                Text = "Clear Filter",
                Location = new Point(15, y),
                Size = new Size(450, 105)
            };
            Controls.Add(grpFilter);

            var lblDefault = new Label
            {
                Text = "By default, clears families starting with \"-Trimble-\" and \"Trmb_FieldPoints_\"",
                Location = new Point(15, 22),
                Size = new Size(420, 18),
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
                Size = new Size(195, 22),
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
                Text = "Comma-separate multiple prefixes: FP-, SG-",
                Location = new Point(15, 74),
                Size = new Size(420, 18),
                ForeColor = Color.FromArgb(130, 130, 130),
                Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic),
                Visible = false
            };
            grpFilter.Controls.Add(lblPrefixHint);

            // Restore the remembered custom-prefix filter (after all the
            // controls the CheckedChanged handler touches exist).
            string remPrefix = DialogMemory.Get(MemKey, "PrefixText", "");
            if (!string.IsNullOrEmpty(remPrefix))
            {
                txtPrefix.Text = remPrefix;
                txtPrefix.ForeColor = Color.Black;
            }
            chkCustomPrefix.Checked = DialogMemory.GetBool(MemKey, "CustomPrefix", false);

            y += 115;

            // ── Cancel button (right-aligned) ──
            // Form width 480, margin 15 → Cancel right edge at 465.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(380, y),
                Size = new Size(85, 28)
            };
            Controls.Add(btnCancel);

            CancelButton = btnCancel;
        }

        private void CollectPrefixSettings()
        {
            ClearCustomPrefix = chkCustomPrefix.Checked;
            ClearFamilyPrefix = (txtPrefix.ForeColor == Color.Gray) ? "" : txtPrefix.Text.Trim();

            // Remember for next time.
            DialogMemory.SetBool(MemKey, "CustomPrefix", ClearCustomPrefix);
            DialogMemory.Set(MemKey, "PrefixText", ClearFamilyPrefix);
            DialogMemory.Flush();
        }
    }
}

