using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.Hangers
{
    /// <summary>
    /// WinForms dialog for the Auto Hang at CAD Lines command.
    /// Collects: hanger family, type code, rod length, min CAD line length, and layer selection.
    /// </summary>
    public class HangAtCADLinesDialog : Form
    {
        // ── Results (read these after ShowDialog() == OK) ──
        public string SelectedFamily { get; private set; }
        public string TypeCode { get; private set; }
        public double RodLengthInches { get; private set; }
        public double MinLineLengthFeet { get; private set; }
        public List<string> SelectedLayers { get; private set; }

        // ── Controls ──
        private ComboBox cboFamily;
        private TextBox txtTypeCode;
        private TextBox txtRodLength;
        private TextBox txtMinLength;
        private CheckedListBox lstLayers;
        private Button btnOK;
        private Button btnCancel;
        private Button btnSelectAll;
        private Button btnSelectNone;

        public HangAtCADLinesDialog(
            IList<string> hangerFamilyNames,
            Dictionary<string, int> layersWithCounts,
            string defaultFamily = "",
            string defaultTypeCode = "01",
            double defaultRodLength = 12,
            double defaultMinLength = 4)
        {
            InitializeForm();
            PopulateFamilies(hangerFamilyNames, defaultFamily);
            PopulateLayers(layersWithCounts);

            txtTypeCode.Text = defaultTypeCode;
            txtRodLength.Text = defaultRodLength.ToString();
            txtMinLength.Text = defaultMinLength.ToString();
        }

        private void InitializeForm()
        {
            Text = "Auto Hang — Pipes Crossing CAD Lines";
            Size = new Size(480, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;
            int labelW = 140;
            int inputX = 155;
            int inputW = 290;

            // ── Hanger Family ──
            AddLabel("Hanger Family:", 15, y);
            cboFamily = new ComboBox { Left = inputX, Top = y - 2, Width = inputW, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(cboFamily);
            y += 35;

            // ── Type Code (Hydratec) ──
            AddLabel("Type Code (Hydratec):", 15, y);
            txtTypeCode = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = "01" };
            Controls.Add(txtTypeCode);
            y += 35;

            // ── Rod Length ──
            AddLabel("Rod Length (inches):", 15, y);
            txtRodLength = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = "12" };
            Controls.Add(txtRodLength);
            y += 35;

            // ── Min CAD Line Length ──
            AddLabel("Min Line Length (ft):", 15, y);
            txtMinLength = new TextBox { Left = inputX, Top = y - 2, Width = 80, Text = "4" };
            Controls.Add(txtMinLength);
            y += 40;

            // ── Layer Selection ──
            AddLabel("CAD Layers:", 15, y);
            y += 20;

            lstLayers = new CheckedListBox { Left = 15, Top = y, Width = inputW + labelW, Height = 250, CheckOnClick = true };
            Controls.Add(lstLayers);
            y += 255;

            // ── Select All / None ──
            btnSelectAll = new Button { Text = "Select All", Left = 15, Top = y, Width = 90, Height = 28 };
            btnSelectAll.Click += (s, e) => { for (int i = 0; i < lstLayers.Items.Count; i++) lstLayers.SetItemChecked(i, true); };
            Controls.Add(btnSelectAll);

            btnSelectNone = new Button { Text = "Select None", Left = 115, Top = y, Width = 90, Height = 28 };
            btnSelectNone.Click += (s, e) => { for (int i = 0; i < lstLayers.Items.Count; i++) lstLayers.SetItemChecked(i, false); };
            Controls.Add(btnSelectNone);
            y += 40;

            // ── OK / Cancel ──
            btnOK = new Button { Text = "Place Hangers", Left = 230, Top = y, Width = 110, Height = 32, DialogResult = DialogResult.OK };
            btnOK.Click += BtnOK_Click;
            Controls.Add(btnOK);
            AcceptButton = btnOK;

            btnCancel = new Button { Text = "Cancel", Left = 350, Top = y, Width = 90, Height = 32, DialogResult = DialogResult.Cancel };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
        }

        private void AddLabel(string text, int x, int y)
        {
            var lbl = new Label { Text = text, Left = x, Top = y + 2, AutoSize = true };
            lbl.Font = new Font(lbl.Font, FontStyle.Bold);
            Controls.Add(lbl);
        }

        private void PopulateFamilies(IList<string> names, string defaultFamily)
        {
            foreach (string name in names.OrderBy(n => n))
                cboFamily.Items.Add(name);

            if (!string.IsNullOrEmpty(defaultFamily) && cboFamily.Items.Contains(defaultFamily))
                cboFamily.SelectedItem = defaultFamily;
            else if (cboFamily.Items.Count > 0)
                cboFamily.SelectedIndex = 0;
        }

        private void PopulateLayers(Dictionary<string, int> layersWithCounts)
        {
            foreach (var kvp in layersWithCounts.OrderBy(k => k.Key))
            {
                lstLayers.Items.Add($"{kvp.Key}  ({kvp.Value} lines)", false);
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            // Validate inputs
            if (cboFamily.SelectedItem == null)
            {
                MessageBox.Show("Please select a hanger family.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!double.TryParse(txtRodLength.Text, out double rodLen) || rodLen <= 0)
            {
                MessageBox.Show("Rod length must be a positive number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (!double.TryParse(txtMinLength.Text, out double minLen) || minLen < 0)
            {
                MessageBox.Show("Min line length must be a non-negative number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (lstLayers.CheckedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one CAD layer.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            // Capture results
            SelectedFamily = cboFamily.SelectedItem.ToString();
            TypeCode = txtTypeCode.Text;
            RodLengthInches = rodLen;
            MinLineLengthFeet = minLen;

            // Extract layer names (strip the count suffix)
            SelectedLayers = new List<string>();
            foreach (string item in lstLayers.CheckedItems)
            {
                string layerName = item.Substring(0, item.LastIndexOf("  ("));
                SelectedLayers.Add(layerName);
            }
        }
    }
}
