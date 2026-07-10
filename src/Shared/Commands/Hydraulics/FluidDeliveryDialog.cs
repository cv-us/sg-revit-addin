using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hydraulics
{
    /// <summary>How the user marks the flowing / remote-area heads for the calc.</summary>
    public enum RegionPickMode { Rectangle, Polygon, ExistingFlowing }

    /// <summary>
    /// Inputs for the Fluid Delivery (water-delivery-time) calculation.
    /// Fixed-size DpiAwareForm; remembers values via DialogMemory("FluidDelivery").
    /// </summary>
    public class FluidDeliveryDialog : DpiAwareForm
    {
        private const string MemKey = "FluidDelivery";

        // Region
        private RadioButton rbRect, rbPoly, rbExisting;
        // Hazard
        private ComboBox cboHazard;
        private TextBox txtTarget;
        // Supply
        private TextBox txtStatic, txtResidual, txtFlow;
        // System / air
        private RadioButton rbPreaction, rbDry;
        private TextBox txtLatency, txtSuperv, txtTrip, txtC, txtTemp;
        private CheckBox chkNitrogen;
        // Sprinkler
        private TextBox txtK;
        private CheckBox chkReadK;

        private Label lblLatency, lblSuperv, lblTrip;

        // ── Results ──
        public RegionPickMode RegionMode { get; private set; }
        public HazardClass Hazard { get; private set; }
        public double TargetSec { get; private set; }
        public double SupplyStaticPsi { get; private set; }
        public double SupplyResidualPsi { get; private set; }
        public double SupplyResidualFlowGpm { get; private set; }
        public bool ModelBlowdown { get; private set; }   // true = dry-pipe differential
        public double LatencySec { get; private set; }
        public double SupervisoryPsi { get; private set; }
        public double TripPsi { get; private set; }
        public double CFactor { get; private set; }
        public bool Nitrogen { get; private set; }
        public double GasTempF { get; private set; }
        public double KFactor { get; private set; }
        public bool ReadKFromHeads { get; private set; }

        private static readonly string[] HazardNames =
        {
            "Dwelling units (1 head, 15 s)",
            "Light hazard (1 head, 60 s)",
            "Ordinary hazard I (2 heads, 50 s)",
            "Ordinary hazard II (2 heads, 50 s)",
            "Extra hazard I (4 heads, 45 s)",
            "Extra hazard II (4 heads, 45 s)",
            "High-piled storage (4 heads, 40 s)",
            "Custom / other"
        };
        private static readonly HazardClass[] HazardVals =
        {
            HazardClass.Dwelling, HazardClass.Light, HazardClass.OrdinaryI, HazardClass.OrdinaryII,
            HazardClass.ExtraI, HazardClass.ExtraII, HazardClass.HighPiledStorage, HazardClass.Custom
        };

        public FluidDeliveryDialog()
        {
            AllowResize = false;
            RememberSize = false;

            Text = "Fluid Delivery — Water Delivery Time";
            Font = new Font("Segoe UI", 9f);
            const int W = 548;
            ClientSize = new Size(W, 592);

            // ── Group 1: Remote area ──
            var g1 = Group("Remote area (flowing heads)", 14, 10, 520, 96);
            rbRect = Radio("Draw a rectangle (pick 2 corners)", 14, 22, 300, g1);
            rbPoly = Radio("Draw a polygon (click corners, Esc to finish)", 14, 44, 340, g1);
            rbExisting = Radio("Use heads already flagged Flowing (Hydratec)", 14, 66, 340, g1);
            rbRect.Checked = true;

            // ── Group 2: Hazard & target ──
            var g2 = Group("Hazard class && target", 14, 114, 520, 84);
            Lbl("Hazard class:", 14, 26, 90, g2);
            cboHazard = new ComboBox { Left = 108, Top = 23, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            cboHazard.Items.AddRange(HazardNames);
            cboHazard.SelectedIndexChanged += (s, e) => ApplyHazardTarget();
            g2.Controls.Add(cboHazard);
            Lbl("Target time (s):", 312, 26, 95, g2);
            txtTarget = Txt("60", 410, 23, 80, g2);
            Note("NFPA 13 Table 8.2.3.6.1 — sets the max time and expected # of remote heads.", 14, 54, 500, g2);

            // ── Group 3: Water supply ──
            var g3 = Group("Water supply at the riser", 14, 206, 520, 84);
            Lbl("Static (psi):", 14, 26, 80, g3);
            txtStatic = Txt("60", 96, 23, 70, g3);
            Lbl("Residual (psi):", 180, 26, 90, g3);
            txtResidual = Txt("50", 274, 23, 70, g3);
            Lbl("@ Flow (gpm):", 356, 26, 90, g3);
            txtFlow = Txt("500", 448, 23, 62, g3);
            Note("Two-point supply curve (a flow test at the supply).", 14, 54, 500, g3);

            // ── Group 4: System & air ──
            var g4 = Group("System && air", 14, 298, 520, 176);
            rbPreaction = Radio("Preaction — electric double-interlock", 14, 22, 300, g4);
            rbDry = Radio("Dry-pipe — differential valve (model air blowdown)", 14, 44, 360, g4);
            rbPreaction.Checked = true;
            rbPreaction.CheckedChanged += (s, e) => ApplyValveMode();
            rbDry.CheckedChanged += (s, e) => ApplyValveMode();

            lblLatency = Lbl("Detection + valve latency (s):", 30, 72, 168, g4);
            txtLatency = Txt("0", 202, 69, 60, g4);
            lblSuperv = Lbl("Supervisory (psi):", 30, 100, 108, g4);
            txtSuperv = Txt("7", 146, 97, 60, g4);
            lblTrip = Lbl("Trip (psi):", 224, 100, 60, g4);
            txtTrip = Txt("3", 288, 97, 60, g4);

            Lbl("C-factor:", 14, 132, 58, g4);
            txtC = Txt("100", 76, 129, 56, g4);
            chkNitrogen = new CheckBox { Text = "Nitrogen (use 120)", Left = 146, Top = 130, Width = 150 };
            chkNitrogen.CheckedChanged += (s, e) => { if (chkNitrogen.Checked) txtC.Text = "120"; };
            g4.Controls.Add(chkNitrogen);
            Lbl("Gas temp (°F):", 330, 132, 90, g4);
            txtTemp = Txt("70", 424, 129, 60, g4);

            // ── Group 5: Sprinkler ──
            var g5 = Group("Sprinkler", 14, 482, 520, 64);
            Lbl("K-factor:", 14, 28, 58, g5);
            txtK = Txt("5.6", 76, 25, 56, g5);
            chkReadK = new CheckBox { Text = "Read K-factor from the flowing heads", Left = 146, Top = 27, Width = 320, Checked = true };
            g5.Controls.Add(chkReadK);

            // ── Buttons ──
            var btnOK = new Button { Text = "Pick && Calculate", Left = 284, Top = 558, Width = 140, Height = 26 };
            var btnCancel = new Button { Text = "Cancel", Left = 434, Top = 558, Width = 100, Height = 26 };
            btnOK.Click += BtnOK_Click;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnOK);
            Controls.Add(btnCancel);
            AcceptButton = btnOK;
            CancelButton = btnCancel;

            LoadMemory();
            ApplyValveMode();
        }

        // ── restore / persist ──
        private void LoadMemory()
        {
            int rm = DialogMemory.GetInt(MemKey, "RegionMode", 0);
            rbRect.Checked = rm == 0; rbPoly.Checked = rm == 1; rbExisting.Checked = rm == 2;

            int hz = DialogMemory.GetInt(MemKey, "Hazard", 1);   // default Light
            cboHazard.SelectedIndex = Math.Max(0, Math.Min(HazardVals.Length - 1, hz));

            txtTarget.Text = DialogMemory.Get(MemKey, "Target", txtTarget.Text);
            txtStatic.Text = DialogMemory.Get(MemKey, "Static", txtStatic.Text);
            txtResidual.Text = DialogMemory.Get(MemKey, "Residual", txtResidual.Text);
            txtFlow.Text = DialogMemory.Get(MemKey, "Flow", txtFlow.Text);

            bool dry = DialogMemory.GetBool(MemKey, "Dry", false);
            rbDry.Checked = dry; rbPreaction.Checked = !dry;

            txtLatency.Text = DialogMemory.Get(MemKey, "Latency", txtLatency.Text);
            txtSuperv.Text = DialogMemory.Get(MemKey, "Superv", txtSuperv.Text);
            txtTrip.Text = DialogMemory.Get(MemKey, "Trip", txtTrip.Text);
            txtC.Text = DialogMemory.Get(MemKey, "CFactor", txtC.Text);
            chkNitrogen.Checked = DialogMemory.GetBool(MemKey, "Nitrogen", false);
            txtTemp.Text = DialogMemory.Get(MemKey, "Temp", txtTemp.Text);
            txtK.Text = DialogMemory.Get(MemKey, "K", txtK.Text);
            chkReadK.Checked = DialogMemory.GetBool(MemKey, "ReadK", true);
        }

        private void SaveMemory()
        {
            DialogMemory.SetInt(MemKey, "RegionMode", rbPoly.Checked ? 1 : rbExisting.Checked ? 2 : 0);
            DialogMemory.SetInt(MemKey, "Hazard", Math.Max(0, cboHazard.SelectedIndex));
            DialogMemory.Set(MemKey, "Target", txtTarget.Text);
            DialogMemory.Set(MemKey, "Static", txtStatic.Text);
            DialogMemory.Set(MemKey, "Residual", txtResidual.Text);
            DialogMemory.Set(MemKey, "Flow", txtFlow.Text);
            DialogMemory.SetBool(MemKey, "Dry", rbDry.Checked);
            DialogMemory.Set(MemKey, "Latency", txtLatency.Text);
            DialogMemory.Set(MemKey, "Superv", txtSuperv.Text);
            DialogMemory.Set(MemKey, "Trip", txtTrip.Text);
            DialogMemory.Set(MemKey, "CFactor", txtC.Text);
            DialogMemory.SetBool(MemKey, "Nitrogen", chkNitrogen.Checked);
            DialogMemory.Set(MemKey, "Temp", txtTemp.Text);
            DialogMemory.Set(MemKey, "K", txtK.Text);
            DialogMemory.SetBool(MemKey, "ReadK", chkReadK.Checked);
            DialogMemory.Flush();
        }

        private void ApplyHazardTarget()
        {
            int i = cboHazard.SelectedIndex;
            if (i < 0 || HazardVals[i] == HazardClass.Custom) return;
            FluidDeliverySolver.HazardDefaults(HazardVals[i], out int _, out double t);
            txtTarget.Text = t.ToString(CultureInfo.InvariantCulture);
        }

        private void ApplyValveMode()
        {
            bool dry = rbDry.Checked;
            lblSuperv.Enabled = txtSuperv.Enabled = dry;
            lblTrip.Enabled = txtTrip.Enabled = dry;
            lblLatency.Enabled = txtLatency.Enabled = !dry;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            RegionMode = rbPoly.Checked ? RegionPickMode.Polygon
                       : rbExisting.Checked ? RegionPickMode.ExistingFlowing
                       : RegionPickMode.Rectangle;
            int hi = Math.Max(0, cboHazard.SelectedIndex);
            Hazard = HazardVals[hi];
            TargetSec = D(txtTarget, 60);
            SupplyStaticPsi = D(txtStatic, 60);
            SupplyResidualPsi = D(txtResidual, 50);
            SupplyResidualFlowGpm = D(txtFlow, 500);
            ModelBlowdown = rbDry.Checked;
            LatencySec = Math.Max(0, D(txtLatency, 0));
            SupervisoryPsi = D(txtSuperv, 7);
            TripPsi = D(txtTrip, 3);
            CFactor = D(txtC, 100);
            Nitrogen = chkNitrogen.Checked;
            GasTempF = D(txtTemp, 70);
            KFactor = D(txtK, 5.6);
            ReadKFromHeads = chkReadK.Checked;

            SaveMemory();
            DialogResult = DialogResult.OK;
            Close();
        }

        private static double D(TextBox t, double fallback) =>
            double.TryParse(t.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        // ── tiny control builders ──
        private GroupBox Group(string title, int x, int y, int w, int h)
        {
            var g = new GroupBox { Text = title, Left = x, Top = y, Width = w, Height = h };
            Controls.Add(g);
            return g;
        }
        private Label Lbl(string text, int x, int y, int w, Control parent)
        {
            var l = new Label { Text = text, Left = x, Top = y, Width = w, Height = 18, TextAlign = ContentAlignment.MiddleLeft };
            parent.Controls.Add(l);
            return l;
        }
        private void Note(string text, int x, int y, int w, Control parent)
        {
            var l = new Label { Text = text, Left = x, Top = y, Width = w, Height = 16, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8f) };
            parent.Controls.Add(l);
        }
        private TextBox Txt(string val, int x, int y, int w, Control parent)
        {
            var t = new TextBox { Text = val, Left = x, Top = y, Width = w };
            parent.Controls.Add(t);
            return t;
        }
        private RadioButton Radio(string text, int x, int y, int w, Control parent)
        {
            var r = new RadioButton { Text = text, Left = x, Top = y, Width = w, Height = 20 };
            parent.Controls.Add(r);
            return r;
        }
    }
}
