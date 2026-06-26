using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Insert Pipe and Fitting Elevations command.
    ///
    /// Collects:
    ///   - TOS (Top of Steel) reference method and parameters
    ///   - AFF (Above Finished Floor) reference method and parameters
    ///   - Element type to process (pipes, fittings, or both)
    /// </summary>
    public class PipeElevationsDialog : DpiAwareForm
    {
        // ── Results ──
        public string TOSMethod { get; private set; }       // "Deck", "Plane", "Z", "Level"
        public string TOSReferencePlaneName { get; private set; }
        public double TOSZElevationFeet { get; private set; }
        public string TOSLevelName { get; private set; }

        public string AFFMethod { get; private set; }       // "Deck", "Plane", "Z", "Level"
        public string AFFReferencePlaneName { get; private set; }
        public double AFFZElevationFeet { get; private set; }
        public string AFFLevelName { get; private set; }

        public bool ProcessPipes { get; private set; }
        public bool ProcessFittings { get; private set; }

        // ── Controls ──
        private ComboBox cboTOSMethod, cboAFFMethod;
        private ComboBox cboTOSPlane, cboAFFPlane;
        private TextBox txtTOSZ, txtAFFZ;
        private ComboBox cboTOSLevel, cboAFFLevel;
        private Panel pnlTOSPlane, pnlAFFPlane;
        private Panel pnlTOSZ, pnlAFFZ;
        private Panel pnlTOSLevel, pnlAFFLevel;
        private CheckBox chkPipes, chkFittings;

        private readonly List<string> _levelNames;
        private readonly List<string> _refPlaneNames;

        private static readonly string[] MethodOptions = new[]
        {
            "Structural Decks (RayBounce)",
            "Named Reference Plane",
            "User-Defined Z Elevation",
            "Reference Level"
        };

        private static readonly string[] MethodKeys = new[] { "Deck", "Plane", "Z", "Level" };

        public PipeElevationsDialog(List<string> levelNames, List<string> refPlaneNames)
        {
            _levelNames = levelNames;
            _refPlaneNames = refPlaneNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Insert Pipe & Fitting Elevations";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 450);

            int margin = 15;
            int y = margin;
            int labelW = 140;
            int ctrlX = margin + labelW + 5;
            int ctrlW = 260;

            // ── TOS Section ──
            var grpTOS = new GroupBox
            {
                Text = "TOS (Top of Steel/Deck) Reference",
                Location = new Point(margin, y),
                Size = new Size(410, 135)
            };
            BuildReferenceSection(grpTOS, "TOS", out cboTOSMethod,
                out pnlTOSPlane, out cboTOSPlane,
                out pnlTOSZ, out txtTOSZ,
                out pnlTOSLevel, out cboTOSLevel);
            Controls.Add(grpTOS);
            y += 145;

            // ── AFF Section ──
            var grpAFF = new GroupBox
            {
                Text = "AFF (Above Finished Floor) Reference",
                Location = new Point(margin, y),
                Size = new Size(410, 135)
            };
            BuildReferenceSection(grpAFF, "AFF", out cboAFFMethod,
                out pnlAFFPlane, out cboAFFPlane,
                out pnlAFFZ, out txtAFFZ,
                out pnlAFFLevel, out cboAFFLevel);
            Controls.Add(grpAFF);
            y += 145;

            // ── Element Types ──
            var grpType = new GroupBox
            {
                Text = "Elements to Process",
                Location = new Point(margin, y),
                Size = new Size(410, 55)
            };
            chkPipes = new CheckBox
            {
                Text = "Pipes",
                Location = new Point(15, 22),
                Size = new Size(100, 20),
                Checked = true
            };
            chkFittings = new CheckBox
            {
                Text = "Fittings && Accessories",
                Location = new Point(140, 22),
                Size = new Size(200, 20),
                Checked = true
            };
            grpType.Controls.AddRange(new Control[] { chkPipes, chkFittings });
            Controls.Add(grpType);
            y += 65;

            // ── Buttons (right-aligned with 10px gap) ──
            // Form width 440, margin 15 → Cancel right edge at 425.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(350, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Calculate Elevations",
                DialogResult = DialogResult.OK,
                Location = new Point(210, y),
                Size = new Size(130, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BuildReferenceSection(GroupBox grp, string prefix,
            out ComboBox cboMethod,
            out Panel pnlPlane, out ComboBox cboPlane,
            out Panel pnlZ, out TextBox txtZ,
            out Panel pnlLevel, out ComboBox cboLevel)
        {
            int gy = 20;

            // Method dropdown
            var lblMethod = new Label { Text = "Method:", Location = new Point(10, gy + 3), Size = new Size(55, 20) };
            cboMethod = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(70, gy),
                Size = new Size(325, 24)
            };
            cboMethod.Items.AddRange(MethodOptions);
            cboMethod.SelectedIndex = 0;
            grp.Controls.Add(lblMethod);
            grp.Controls.Add(cboMethod);
            gy += 30;

            // Reference Plane panel
            pnlPlane = new Panel { Location = new Point(10, gy), Size = new Size(390, 30), Visible = false };
            var lblPlane = new Label { Text = "Ref Plane:", Location = new Point(0, 5), Size = new Size(60, 20) };
            cboPlane = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(60, 2),
                Size = new Size(325, 24)
            };
            foreach (var name in _refPlaneNames)
                cboPlane.Items.Add(name);
            if (cboPlane.Items.Count > 0) cboPlane.SelectedIndex = 0;
            pnlPlane.Controls.Add(lblPlane);
            pnlPlane.Controls.Add(cboPlane);
            grp.Controls.Add(pnlPlane);

            // Z Elevation panel
            pnlZ = new Panel { Location = new Point(10, gy), Size = new Size(390, 30), Visible = false };
            var lblZ = new Label { Text = "Z Elev (ft):", Location = new Point(0, 5), Size = new Size(65, 20) };
            txtZ = new TextBox { Location = new Point(70, 2), Size = new Size(100, 24), Text = "0" };
            pnlZ.Controls.Add(lblZ);
            pnlZ.Controls.Add(txtZ);
            grp.Controls.Add(pnlZ);

            // Level panel
            pnlLevel = new Panel { Location = new Point(10, gy), Size = new Size(390, 30), Visible = false };
            var lblLevel = new Label { Text = "Level:", Location = new Point(0, 5), Size = new Size(40, 20) };
            cboLevel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(45, 2),
                Size = new Size(340, 24)
            };
            foreach (var name in _levelNames)
                cboLevel.Items.Add(name);
            if (cboLevel.Items.Count > 0) cboLevel.SelectedIndex = 0;
            pnlLevel.Controls.Add(lblLevel);
            pnlLevel.Controls.Add(cboLevel);
            grp.Controls.Add(pnlLevel);

            // Wire method change — capture out params in locals for lambda
            var localMethod = cboMethod;
            var localPnlPlane = pnlPlane;
            var localPnlZ = pnlZ;
            var localPnlLevel = pnlLevel;
            localMethod.SelectedIndexChanged += (s, e) =>
            {
                int idx = localMethod.SelectedIndex;
                localPnlPlane.Visible = (idx == 1);
                localPnlZ.Visible = (idx == 2);
                localPnlLevel.Visible = (idx == 3);
            };
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (!chkPipes.Checked && !chkFittings.Checked)
            {
                MessageBox.Show("Select at least one element type to process.",
                    "No Elements", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            TOSMethod = MethodKeys[cboTOSMethod.SelectedIndex];
            AFFMethod = MethodKeys[cboAFFMethod.SelectedIndex];

            TOSReferencePlaneName = cboTOSPlane.SelectedItem?.ToString() ?? "";
            AFFReferencePlaneName = cboAFFPlane.SelectedItem?.ToString() ?? "";

            double.TryParse(txtTOSZ.Text, out double tosZ);
            TOSZElevationFeet = tosZ;
            double.TryParse(txtAFFZ.Text, out double affZ);
            AFFZElevationFeet = affZ;

            TOSLevelName = cboTOSLevel.SelectedItem?.ToString() ?? "";
            AFFLevelName = cboAFFLevel.SelectedItem?.ToString() ?? "";

            ProcessPipes = chkPipes.Checked;
            ProcessFittings = chkFittings.Checked;
        }
    }
}

