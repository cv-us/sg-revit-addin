using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers.PlaceHangers
{
    /// <summary>
    /// Unified dialog for the Place Hangers command. A Method dropdown at the
    /// top selects one of four placement strategies; the relevant option
    /// groups show/hide and re-stack. Every input is remembered between runs
    /// via <see cref="DialogMemory"/>, namespaced per method.
    /// </summary>
    public class PlaceHangersDialog : Form
    {
        private const string MemRoot = "PlaceHangers";

        // ── Result ──
        public PlacementMethod Method { get; private set; } = PlacementMethod.TypicalSpacing;

        // ── Feed data ──
        private readonly IList<string> _families;
        private readonly IList<string> _pipeTypes;
        private readonly IList<string> _linkNames;

        // ── Controls ──
        private ComboBox cboMethod;
        private ComboBox cboFamily;
        private ComboBox cboPipeFilter;
        private Label lblPipeFilter;

        // spacing
        private GroupBox grpSpacing;
        private RadioButton rbEven, rbExact;
        private RadioButton rb10_6, rb12, rb15, rbCustom;
        private TextBox txtCustom;

        // structural source
        private GroupBox grpStructural;
        private ComboBox cboSource;     // Local + link names
        private ComboBox cboAttach;     // Bottom / Top

        // raybounce
        private GroupBox grpRaybounce;
        private TextBox txtMaxClash;

        // type codes
        private GroupBox grpTypeCodes;
        private Label lblType; private TextBox txtType;
        private Label lblWide; private TextBox txtWide;
        private Label lblRoof; private TextBox txtRoof;
        private Label lblFloor; private TextBox txtFloor;
        private Label lblFraming; private TextBox txtFraming;
        private Label lblStairs; private TextBox txtStairs;

        // downstream
        private GroupBox grpDownstream;
        private TextBox txtDistEnd, txtMinLen;

        // c-clamp
        private GroupBox grpCClamp;
        private ComboBox cboCClamp;

        // layout bookkeeping
        private GroupBox grpMethod, grpCommon;
        private Button btnOK, btnCancel;
        private readonly List<(GroupBox grp, PlacementMethod[] methods)> _optionGroups
            = new List<(GroupBox, PlacementMethod[])>();

        private const int FormW = 540;
        private const int Margin = 15;
        private const int GroupW = FormW - Margin * 2;

        public PlaceHangersDialog(IList<string> families, IList<string> pipeTypes, IList<string> linkNames)
        {
            _families = families ?? new List<string>();
            _pipeTypes = pipeTypes ?? new List<string>();
            _linkNames = linkNames ?? new List<string>();
            InitializeComponent();
            RestoreForMethod();
            Relayout();
        }

        private void InitializeComponent()
        {
            Text = "Place Hangers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // ── Method (always) ──
            grpMethod = new GroupBox { Text = "Placement Method", Location = new Point(Margin, 0), Size = new Size(GroupW, 52) };
            cboMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 20), Size = new Size(GroupW - 24, 24) };
            cboMethod.Items.AddRange(new object[]
            {
                "Auto-spaced (decks / raybounce)",
                "Auto-spaced (parallel framing)",
                "Downstream ends (threaded lines)",
                "At structural steel"
            });
            cboMethod.SelectedIndex = DialogMemory.GetInt(MemRoot, "Method", 0);
            cboMethod.SelectedIndexChanged += (s, e) => { RestoreForMethod(); Relayout(); };
            grpMethod.Controls.Add(cboMethod);
            Controls.Add(grpMethod);

            // ── Common: family + pipe filter (always; pipe filter only shown for spacing methods) ──
            grpCommon = new GroupBox { Text = "Hanger Family / Pipe Filter", Location = new Point(Margin, 0), Size = new Size(GroupW, 80) };
            grpCommon.Controls.Add(new Label { Text = "Hanger family:", Location = new Point(12, 24), Size = new Size(110, 18) });
            cboFamily = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, 21), Size = new Size(GroupW - 145, 24) };
            foreach (var f in _families) cboFamily.Items.Add(f);
            if (cboFamily.Items.Count > 0) cboFamily.SelectedIndex = 0;
            grpCommon.Controls.Add(cboFamily);
            lblPipeFilter = new Label { Text = "Pipe filter:", Location = new Point(12, 52), Size = new Size(110, 18) };
            grpCommon.Controls.Add(lblPipeFilter);
            cboPipeFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, 49), Size = new Size(GroupW - 145, 24) };
            cboPipeFilter.Items.Add("ALL Pipes");
            foreach (var p in _pipeTypes) cboPipeFilter.Items.Add(p);
            cboPipeFilter.SelectedIndex = 0;
            grpCommon.Controls.Add(cboPipeFilter);
            Controls.Add(grpCommon);

            // ── Spacing (decks, parallel) ──
            grpSpacing = new GroupBox { Text = "Spacing", Location = new Point(Margin, 0), Size = new Size(GroupW, 150) };
            rbEven = new RadioButton { Text = "Evenly spaced along run", Location = new Point(12, 22), AutoSize = true, Checked = true };
            rbExact = new RadioButton { Text = "Exact spacing distance", Location = new Point(260, 22), AutoSize = true };
            grpSpacing.Controls.AddRange(new Control[] { rbEven, rbExact });
            rb10_6 = new RadioButton { Text = "10'-6\" (default)", Location = new Point(12, 50), AutoSize = true, Checked = true };
            rb12 = new RadioButton { Text = "12'-0\"", Location = new Point(160, 50), AutoSize = true };
            rb15 = new RadioButton { Text = "15'-0\"", Location = new Point(260, 50), AutoSize = true };
            rbCustom = new RadioButton { Text = "Custom (ft):", Location = new Point(12, 78), AutoSize = true };
            txtCustom = new TextBox { Location = new Point(110, 76), Size = new Size(70, 22), Enabled = false };
            rbCustom.CheckedChanged += (s, e) => txtCustom.Enabled = rbCustom.Checked;
            grpSpacing.Controls.AddRange(new Control[] { rb10_6, rb12, rb15, rbCustom, txtCustom });
            grpSpacing.Controls.Add(new Label { Text = "Max / exact spacing:", Location = new Point(12, 110), Size = new Size(GroupW - 24, 18), ForeColor = SystemColors.GrayText });
            Controls.Add(grpSpacing);
            _optionGroups.Add((grpSpacing, new[] { PlacementMethod.TypicalSpacing, PlacementMethod.ParallelStructural }));

            // ── Structural source (parallel, at-steel) ──
            grpStructural = new GroupBox { Text = "Structural Source", Location = new Point(Margin, 0), Size = new Size(GroupW, 90) };
            grpStructural.Controls.Add(new Label { Text = "Framing from:", Location = new Point(12, 24), Size = new Size(110, 18) });
            cboSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, 21), Size = new Size(GroupW - 145, 24) };
            cboSource.Items.Add("Local structural framing");
            foreach (var l in _linkNames) cboSource.Items.Add(l);
            cboSource.SelectedIndex = 0;
            grpStructural.Controls.Add(cboSource);
            grpStructural.Controls.Add(new Label { Text = "Attach to:", Location = new Point(12, 54), Size = new Size(110, 18) });
            cboAttach = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, 51), Size = new Size(GroupW - 145, 24) };
            cboAttach.Items.AddRange(new object[] { "BOTTOM of structural (default)", "TOP of structural" });
            cboAttach.SelectedIndex = 0;
            grpStructural.Controls.Add(cboAttach);
            Controls.Add(grpStructural);
            _optionGroups.Add((grpStructural, new[] { PlacementMethod.ParallelStructural, PlacementMethod.AtStructural }));

            // ── Raybounce clash height (decks, at-steel) ──
            grpRaybounce = new GroupBox { Text = "Raybounce", Location = new Point(Margin, 0), Size = new Size(GroupW, 52) };
            grpRaybounce.Controls.Add(new Label { Text = "Max clash height (ft):", Location = new Point(12, 22), Size = new Size(150, 18) });
            txtMaxClash = new TextBox { Location = new Point(170, 19), Size = new Size(70, 22), Text = "10" };
            grpRaybounce.Controls.Add(txtMaxClash);
            Controls.Add(grpRaybounce);
            _optionGroups.Add((grpRaybounce, new[] { PlacementMethod.TypicalSpacing, PlacementMethod.AtStructural }));

            // ── Type codes (contents vary per method) ──
            grpTypeCodes = new GroupBox { Text = "Hanger Type Codes (Hydratec)", Location = new Point(Margin, 0), Size = new Size(GroupW, 170) };
            int rowY = 22, rowH = 28, lblW = 170, tx = 190, tw = 70;
            lblType = new Label { Text = "Type Code:", Location = new Point(12, rowY + 3), Size = new Size(lblW, 18) };
            txtType = new TextBox { Location = new Point(tx, rowY), Size = new Size(tw, 22), Text = "01" };
            lblWide = new Label { Text = "Widemouth Type:", Location = new Point(12, rowY + 3), Size = new Size(lblW, 18) };
            txtWide = new TextBox { Location = new Point(tx, rowY), Size = new Size(tw, 22), Text = "01A" };
            lblRoof = new Label { Text = "Roofs:", Location = new Point(12, rowY + 3), Size = new Size(lblW, 18) };
            txtRoof = new TextBox { Location = new Point(tx, rowY), Size = new Size(tw, 22), Text = "03A" };
            lblFloor = new Label { Text = "Floor Decks:", Location = new Point(12, rowY + 3), Size = new Size(lblW, 18) };
            txtFloor = new TextBox { Location = new Point(tx, rowY), Size = new Size(tw, 22), Text = "05" };
            lblFraming = new Label { Text = "Structural Framing:", Location = new Point(12, rowY + 3), Size = new Size(lblW, 18) };
            txtFraming = new TextBox { Location = new Point(tx, rowY), Size = new Size(tw, 22), Text = "01" };
            lblStairs = new Label { Text = "Stairs:", Location = new Point(12, rowY + 3), Size = new Size(lblW, 18) };
            txtStairs = new TextBox { Location = new Point(tx, rowY), Size = new Size(tw, 22), Text = "" };
            grpTypeCodes.Controls.AddRange(new Control[]
            {
                lblType, txtType, lblWide, txtWide, lblRoof, txtRoof,
                lblFloor, txtFloor, lblFraming, txtFraming, lblStairs, txtStairs
            });
            Controls.Add(grpTypeCodes);
            _optionGroups.Add((grpTypeCodes, new[]
            {
                PlacementMethod.TypicalSpacing, PlacementMethod.ParallelStructural,
                PlacementMethod.Downstream, PlacementMethod.AtStructural
            }));

            // ── Downstream extras ──
            grpDownstream = new GroupBox { Text = "Downstream Placement", Location = new Point(Margin, 0), Size = new Size(GroupW, 90) };
            grpDownstream.Controls.Add(new Label { Text = "Distance from end (in):", Location = new Point(12, 24), Size = new Size(180, 18) });
            txtDistEnd = new TextBox { Location = new Point(200, 21), Size = new Size(70, 22), Text = "12" };
            grpDownstream.Controls.Add(txtDistEnd);
            grpDownstream.Controls.Add(new Label { Text = "Min pipe length (in):", Location = new Point(12, 54), Size = new Size(180, 18) });
            txtMinLen = new TextBox { Location = new Point(200, 51), Size = new Size(70, 22), Text = "18" };
            grpDownstream.Controls.Add(txtMinLen);
            Controls.Add(grpDownstream);
            _optionGroups.Add((grpDownstream, new[] { PlacementMethod.Downstream }));

            // ── C-Clamp (parallel, downstream, at-steel) ──
            grpCClamp = new GroupBox { Text = "C-Clamp", Location = new Point(Margin, 0), Size = new Size(GroupW, 52) };
            grpCClamp.Controls.Add(new Label { Text = "C-Clamp visibility:", Location = new Point(12, 22), Size = new Size(130, 18) });
            cboCClamp = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 19), Size = new Size(200, 24) };
            cboCClamp.Items.AddRange(new object[] { "Hide (default)", "Show" });
            cboCClamp.SelectedIndex = 0;
            grpCClamp.Controls.Add(cboCClamp);
            Controls.Add(grpCClamp);
            _optionGroups.Add((grpCClamp, new[]
            {
                PlacementMethod.ParallelStructural, PlacementMethod.Downstream, PlacementMethod.AtStructural
            }));

            // ── Buttons ──
            btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(80, 30) };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
            btnOK = new Button { Text = "Place Hangers", DialogResult = DialogResult.OK, Size = new Size(120, 30) };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private PlacementMethod CurrentMethod => (PlacementMethod)cboMethod.SelectedIndex;

        /// <summary>Show only the groups for the current method, stacked, and size the form.</summary>
        private void Relayout()
        {
            var m = CurrentMethod;
            int y = Margin;

            grpMethod.Location = new Point(Margin, y); y += grpMethod.Height + 8;
            grpCommon.Location = new Point(Margin, y);
            // Pipe filter row only for spacing methods — hide it (and shrink group) otherwise.
            bool showPipe = m == PlacementMethod.TypicalSpacing || m == PlacementMethod.ParallelStructural;
            cboPipeFilter.Visible = showPipe;
            lblPipeFilter.Visible = showPipe;
            grpCommon.Height = showPipe ? 80 : 52;
            y += grpCommon.Height + 8;

            // Type-code rows per method.
            LayoutTypeRows(m);

            foreach (var (grp, methods) in _optionGroups)
            {
                bool show = methods.Contains(m);
                grp.Visible = show;
                if (!show) continue;
                grp.Location = new Point(Margin, y);
                y += grp.Height + 8;
            }

            // Buttons
            btnCancel.Location = new Point(FormW - Margin - 80, y);
            btnOK.Location = new Point(FormW - Margin - 80 - 10 - 120, y);
            y += 30 + Margin;

            ClientSize = new Size(FormW, y);
        }

        /// <summary>Show/position only the type-code rows relevant to the method, and size grpTypeCodes.</summary>
        private void LayoutTypeRows(PlacementMethod m)
        {
            var rows = new List<(Label l, TextBox t)>();
            switch (m)
            {
                case PlacementMethod.TypicalSpacing:
                    rows.Add((lblType, txtType));
                    break;
                case PlacementMethod.ParallelStructural:
                    rows.Add((lblType, txtType));
                    rows.Add((lblWide, txtWide));
                    break;
                case PlacementMethod.AtStructural:
                    rows.Add((lblWide, txtWide));
                    break;
                case PlacementMethod.Downstream:
                    rows.Add((lblRoof, txtRoof));
                    rows.Add((lblFloor, txtFloor));
                    rows.Add((lblFraming, txtFraming));
                    rows.Add((lblStairs, txtStairs));
                    break;
            }
            // hide all first
            foreach (var c in new Control[] { lblType, txtType, lblWide, txtWide, lblRoof, txtRoof, lblFloor, txtFloor, lblFraming, txtFraming, lblStairs, txtStairs })
                c.Visible = false;

            int ry = 22, rh = 28;
            foreach (var (l, t) in rows)
            {
                l.Visible = true; t.Visible = true;
                l.Location = new Point(12, ry + 3);
                t.Location = new Point(190, ry);
                ry += rh;
            }
            grpTypeCodes.Height = ry + 10;
        }

        // ── Memory: per-method dialog key ──
        private string MethodKey => $"{MemRoot}.{CurrentMethod}";

        private void RestoreForMethod()
        {
            string k = MethodKey;
            SelectComboText(cboFamily, DialogMemory.Get(k, "Family", null));
            SelectComboText(cboPipeFilter, DialogMemory.Get(k, "PipeFilter", null));
            SelectComboText(cboSource, DialogMemory.Get(k, "Source", null));
            cboAttach.SelectedIndex = DialogMemory.GetInt(k, "Attach", 0).Clamp(0, 1);
            cboCClamp.SelectedIndex = DialogMemory.GetInt(k, "CClamp", 0).Clamp(0, 1);
            txtMaxClash.Text = DialogMemory.Get(k, "MaxClash", "10");
            txtType.Text = DialogMemory.Get(k, "TypeCode", "01");
            txtWide.Text = DialogMemory.Get(k, "Widemouth", "01A");
            txtRoof.Text = DialogMemory.Get(k, "Roof", "03A");
            txtFloor.Text = DialogMemory.Get(k, "Floor", "05");
            txtFraming.Text = DialogMemory.Get(k, "Framing", "01");
            txtStairs.Text = DialogMemory.Get(k, "Stairs", "");
            txtDistEnd.Text = DialogMemory.Get(k, "DistEnd", "12");
            txtMinLen.Text = DialogMemory.Get(k, "MinLen", "18");

            bool even = DialogMemory.GetBool(k, "Even", true);
            rbEven.Checked = even; rbExact.Checked = !even;
            int spIdx = DialogMemory.GetInt(k, "SpacingPreset", 0);
            rb10_6.Checked = spIdx == 0; rb12.Checked = spIdx == 1; rb15.Checked = spIdx == 2; rbCustom.Checked = spIdx == 3;
            txtCustom.Enabled = rbCustom.Checked;
            txtCustom.Text = DialogMemory.Get(k, "CustomSpacing", "");
        }

        private static void SelectComboText(ComboBox cbo, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = cbo.FindStringExact(text);
            if (i >= 0) cbo.SelectedIndex = i;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Method = CurrentMethod;

            // Validate family
            if (cboFamily.SelectedItem == null)
            {
                MessageBox.Show(this, "Select a hanger family.", "Place Hangers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            // Persist everything for this method.
            string k = MethodKey;
            DialogMemory.SetInt(MemRoot, "Method", cboMethod.SelectedIndex);
            DialogMemory.Set(k, "Family", cboFamily.SelectedItem?.ToString());
            DialogMemory.Set(k, "PipeFilter", cboPipeFilter.SelectedItem?.ToString());
            DialogMemory.Set(k, "Source", cboSource.SelectedItem?.ToString());
            DialogMemory.SetInt(k, "Attach", cboAttach.SelectedIndex);
            DialogMemory.SetInt(k, "CClamp", cboCClamp.SelectedIndex);
            DialogMemory.Set(k, "MaxClash", txtMaxClash.Text);
            DialogMemory.Set(k, "TypeCode", txtType.Text);
            DialogMemory.Set(k, "Widemouth", txtWide.Text);
            DialogMemory.Set(k, "Roof", txtRoof.Text);
            DialogMemory.Set(k, "Floor", txtFloor.Text);
            DialogMemory.Set(k, "Framing", txtFraming.Text);
            DialogMemory.Set(k, "Stairs", txtStairs.Text);
            DialogMemory.Set(k, "DistEnd", txtDistEnd.Text);
            DialogMemory.Set(k, "MinLen", txtMinLen.Text);
            DialogMemory.SetBool(k, "Even", rbEven.Checked);
            DialogMemory.SetInt(k, "SpacingPreset", rb10_6.Checked ? 0 : rb12.Checked ? 1 : rb15.Checked ? 2 : 3);
            DialogMemory.Set(k, "CustomSpacing", txtCustom.Text);
            DialogMemory.Flush();
        }

        // ── Config builders (called by the command after OK) ──

        private double MaxSpacing()
        {
            if (rb10_6.Checked) return 10.5;
            if (rb12.Checked) return 12.0;
            if (rb15.Checked) return 15.0;
            return double.TryParse(txtCustom.Text, out double v) && v > 0 ? v : 10.5;
        }

        private double Dbl(TextBox t, double fallback) => double.TryParse(t.Text, out double v) && v > 0 ? v : fallback;

        public TypicalSpacingConfig BuildTypicalSpacing() => new TypicalSpacingConfig
        {
            SelectedFamily = cboFamily.SelectedItem?.ToString(),
            PipeTypeFilter = cboPipeFilter.SelectedItem?.ToString() ?? "ALL Pipes",
            EvenlyDistributed = rbEven.Checked,
            MaxSpacingFeet = MaxSpacing(),
            HangerTypeCode = txtType.Text.Trim(),
            MaxClashHeightFeet = Dbl(txtMaxClash, 10.0)
        };

        public ParallelStructuralConfig BuildParallel() => new ParallelStructuralConfig
        {
            SelectedFamily = cboFamily.SelectedItem?.ToString(),
            PipeTypeFilter = cboPipeFilter.SelectedItem?.ToString() ?? "ALL Pipes",
            EvenlyDistributed = rbEven.Checked,
            MaxSpacingFeet = MaxSpacing(),
            HangerTypeCode = txtType.Text.Trim(),
            WidemouthTypeCode = txtWide.Text.Trim(),
            AttachToBottom = cboAttach.SelectedIndex == 0,
            ShowCClamp = cboCClamp.SelectedIndex == 1,
            UseLocalFraming = cboSource.SelectedIndex == 0,
            SelectedLinkName = cboSource.SelectedIndex > 0 ? cboSource.SelectedItem?.ToString() : null
        };

        public DownstreamConfig BuildDownstream() => new DownstreamConfig
        {
            SelectedFamily = cboFamily.SelectedItem?.ToString(),
            RoofTypeCode = txtRoof.Text.Trim(),
            FloorDeckTypeCode = txtFloor.Text.Trim(),
            FramingTypeCode = txtFraming.Text.Trim(),
            StairsTypeCode = txtStairs.Text.Trim(),
            DistanceFromEndInches = Dbl(txtDistEnd, 12.0),
            MinPipeLengthInches = Dbl(txtMinLen, 18.0),
            ShowCClamp = cboCClamp.SelectedIndex == 1
        };

        public AtStructuralConfig BuildAtStructural() => new AtStructuralConfig
        {
            SelectedFamily = cboFamily.SelectedItem?.ToString(),
            TypeCode = txtWide.Text.Trim(),
            WidemouthTypeCode = txtWide.Text.Trim(),
            AttachToBottom = cboAttach.SelectedIndex == 0,
            ShowCClamp = cboCClamp.SelectedIndex == 1,
            UseLocalFraming = cboSource.SelectedIndex == 0,
            SelectedLinkName = cboSource.SelectedIndex > 0 ? cboSource.SelectedItem?.ToString() : null,
            MaxClashHeightFeet = Dbl(txtMaxClash, 10.0)
        };
    }

    internal static class IntClampExt
    {
        public static int Clamp(this int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
