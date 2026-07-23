using SgRevitAddin.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.SprinklerLayout
{
    /// <summary>
    /// Settings dialog for <see cref="LayoutCommand"/> — variable-spacing branch-line
    /// and sprinkler layout that fills a picked area. Line spacings live in numbered
    /// slots (1-6) and head spacings in lettered slots (A-F); the sequence strings
    /// CYCLE to fill the area (line "112112" tiles across the width, head "AABA" tiles
    /// along each line's length). Two pick modes: "Fill area" (pick two corners) and
    /// "Area + central main" (pick two corners and a main line — branches slope down
    /// toward the main and tie in with riser nipples + tees, the main slopes toward its
    /// riser end). All values are remembered.
    /// </summary>
    public class LayoutDialog : DpiAwareForm
    {
        private const string MemKey = "Layout";
        private const int SlotCount = 6;

        public enum PickModeKind { FillArea = 0, AreaMain = 1, TwoMains = 2 }

        private readonly TextBox[] _lnFt = new TextBox[SlotCount];
        private readonly TextBox[] _lnIn = new TextBox[SlotCount];
        private readonly TextBox[] _hdFt = new TextBox[SlotCount];
        private readonly TextBox[] _hdIn = new TextBox[SlotCount];
        private TextBox _txtLineSeq, _txtHeadSeq;

        private ComboBox _cmbPickMode;
        private Panel _dirToggle;           // clickable arrows: branch-line direction
        private bool _linesAlongX = true;

        private ComboBox _cmbPipeType, _cmbSystem, _cmbLevel, _cmbHead, _cmbMainType, _cmbSprigType;
        private ComboBox _cmbLineSize, _cmbSprigSize, _cmbMainSize, _cmbRiserSize;
        private TextBox _txtElevFt, _txtElevIn, _txtSlope, _txtOffFt, _txtOffIn, _txtEndFt, _txtEndIn;
        private GroupBox _grpMain;
        private TextBox _txtMainElevFt, _txtMainElevIn, _txtMainSlope, _txtHeadClear;
        private Label _lblMainSlope;
        private Panel _mainToggle;          // clickable main image: orientation + HIGH/LOW slope
        private bool _mainReversed;
        private CheckBox _chkTailback;
        private ComboBox _cmbTieIn;         // riser nipple above main vs side outlet at main elevation
        private Label _lblStartElevRef;     // dynamic note: where the branch Start elev is measured
        private Label _lblMainElevRef;      // dynamic note: where the Main elev is measured
        private Label _lblGuidance;
        private Button _btnPlace;
        private RadioButton _rbOutlets, _rbSprigs, _rbTermElev, _rbSprigLen;
        private TextBox _txtTermFt, _txtTermIn, _txtLenFt, _txtLenIn;
        private CheckBox _chkCap;
        private TextBox _txtCapFt, _txtCapIn;

        private readonly IList<(int id, string name)> _pipeTypes;
        private readonly IList<(int id, string name)> _systemTypes;
        private readonly IList<(int id, string name)> _headTypes;
        private readonly IList<(int id, string name)> _fittings;
        private readonly IList<string> _levels;
        private readonly string _defaultLevel;
        private readonly string _defaultOutlet;
        private readonly string _defaultRiserTee;

        // ── Results (valid after DialogResult.OK) ──
        public double[] LineSlotFt { get; } = new double[SlotCount];
        public double[] HeadSlotFt { get; } = new double[SlotCount];
        public string LineSequence { get; private set; } = "";
        public string HeadSequence { get; private set; } = "";
        public PickModeKind PickMode { get; private set; }
        public bool LinesAlongX => _linesAlongX;
        public int PipeTypeId { get; private set; }        // line (branch) pipe type
        public int MainPipeTypeId { get; private set; }
        public int SprigPipeTypeId { get; private set; }
        public int SystemTypeId { get; private set; }
        public int HeadSymbolId { get; private set; }
        public string LevelName { get; private set; } = "";
        public double StartElevFt { get; private set; }
        public double SlopeFtPerFt { get; private set; }        // branch slope, ft rise per ft run
        public double LineSizeIn { get; private set; }
        public double StartOffsetFt { get; private set; }       // 1st corner -> 1st line inset (0 = old behavior)
        public double EndOffsetFt { get; private set; }         // 1st corner -> last head (fill mode; cap runs past it toward the corner)
        public double SprigSizeIn { get; private set; }
        public bool UseSprigs { get; private set; }
        public bool SprigsToCommonElev { get; private set; }
        public double TermElevFt { get; private set; }
        public double SprigLenFt { get; private set; }
        public bool CapEnds { get; private set; }
        public double ExtendToCapFt { get; private set; }
        public double MainSizeIn { get; private set; }
        public double RiserSizeIn { get; private set; }
        public double MainElevFt { get; private set; }
        public double MainSlopeFtPerFt { get; private set; }    // main slope, ft fall per ft run
        public double MainHeadClearFt { get; private set; }     // min centerline gap main -> nearest head; the main shifts over
        public bool MainSlopeReversed { get; private set; }
        public bool Tailback { get; private set; } = true;      // two-mains: tee + stub (vs elbow) at each main
        public bool SideOutlet { get; private set; }            // branch at main elevation, side tap (vs riser nipple)
        public int OutletFittingId { get; private set; } = -1;  // -1 = routing-preference default
        public int RiserTeeFittingId { get; private set; } = -1;

        private const string DefaultLabel = "(routing-preference default)";
        private ComboBox _cmbOutlet, _cmbRiserTee;

        public LayoutDialog(IList<(int id, string name)> pipeTypes,
                            IList<(int id, string name)> systemTypes,
                            IList<(int id, string name)> headTypes,
                            IList<string> levels, string defaultLevel,
                            IList<(int id, string name)> fittings, string defaultOutlet, string defaultRiserTee)
        {
            _pipeTypes = pipeTypes;
            _systemTypes = systemTypes;
            _headTypes = headTypes;
            _levels = levels;
            _defaultLevel = defaultLevel;
            _fittings = fittings;
            _defaultOutlet = defaultOutlet;
            _defaultRiserTee = defaultRiserTee;

            AllowResize = false;    // fixed size — pins the buttons below the groups
            RememberSize = false;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Layout";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(740, 775);
            Font = new Font("Segoe UI", 9f);

            const int M = 15;

            // ── Spacing slots (two groups) + mode column, top row ──
            var grpLines = new GroupBox { Text = "Line spacing slots", Location = new Point(M, 12), Size = new Size(250, 250) };
            BuildSlots(grpLines, _lnFt, _lnIn, i => (i + 1).ToString(), "Ln");
            _txtLineSeq = BuildSequence(grpLines, "LineSeq", "112112",
                "One digit per line spacing; repeats to fill the area's width.");
            Controls.Add(grpLines);

            var grpHeads = new GroupBox { Text = "Head spacing slots", Location = new Point(M + 258, 12), Size = new Size(250, 250) };
            BuildSlots(grpHeads, _hdFt, _hdIn, i => ((char)('A' + i)).ToString(), "Hd");
            _txtHeadSeq = BuildSequence(grpHeads, "HeadSeq", "AABA",
                "One letter per head spacing; repeats along each line's length.");
            Controls.Add(grpHeads);

            BuildModeColumn(new Point(M + 516, 12), new Size(194, 250));

            int y = 270;

            // ── Branch lines ──
            var grpPipe = new GroupBox { Text = "Branch lines", Location = new Point(M, y), Size = new Size(350, 234) };
            int gy = 22;
            grpPipe.Controls.Add(new Label { Text = "Line type:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbPipeType = AddCombo(grpPipe, new Point(80, gy), 258,
                _pipeTypes.Select(t => t.name), DialogMemory.Get(MemKey, "PipeType", DefaultPipeTypeName()));
            gy += 27;
            grpPipe.Controls.Add(new Label { Text = "System:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbSystem = AddCombo(grpPipe, new Point(80, gy), 258,
                _systemTypes.Select(t => t.name), DialogMemory.Get(MemKey, "System", DefaultSystemName()));
            gy += 27;
            grpPipe.Controls.Add(new Label { Text = "Line size:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbLineSize = AddSizeCombo(grpPipe, new Point(80, gy), DialogMemory.GetDouble(MemKey, "LineSize", 1.25));
            grpPipe.Controls.Add(new Label { Text = "in", Location = new Point(146, gy + 3), AutoSize = true });
            grpPipe.Controls.Add(new Label { Text = "Level:", Location = new Point(178, gy + 3), AutoSize = true });
            _cmbLevel = AddCombo(grpPipe, new Point(218, gy), 120,
                _levels, DialogMemory.Get(MemKey, "Level", _defaultLevel ?? ""));
            gy += 27;
            grpPipe.Controls.Add(new Label { Text = "Start elev:", Location = new Point(10, gy + 3), AutoSize = true });
            AddFtIn(grpPipe, 80, gy, "ElevFt", "ElevIn", "10", "0", out _txtElevFt, out _txtElevIn);
            _lblStartElevRef = new Label { Text = "above level", Location = new Point(210, gy + 3), Size = new Size(132, 16), AutoEllipsis = true, ForeColor = SystemColors.GrayText };
            grpPipe.Controls.Add(_lblStartElevRef);
            var setip = new ToolTip();
            setip.SetToolTip(_txtElevFt,
                "Where this elevation is measured on the branch line:\n" +
                " • Area + central main: the branch's LOW point, at the main — it slopes UP from here to both ends.\n" +
                " • Fill area: the near (first-picked-corner) end — it slopes toward the far end.\n" +
                " • Two mains: the branch is flat at this elevation.\n" +
                "The Z on the branch image marks this point.");
            gy += 27;
            grpPipe.Controls.Add(new Label { Text = "Slope:", Location = new Point(10, gy + 3), AutoSize = true });
            _txtSlope = new TextBox { Location = new Point(80, gy), Size = new Size(48, 22), Text = DialogMemory.Get(MemKey, "BrSlope", "0.5") };
            grpPipe.Controls.Add(_txtSlope);
            grpPipe.Controls.Add(new Label { Text = "in / 10 ft  (↓ to main)", Location = new Point(134, gy + 3), AutoSize = true });
            gy += 27;
            grpPipe.Controls.Add(new Label { Text = "Start offset:", Location = new Point(10, gy + 3), AutoSize = true });
            AddFtIn(grpPipe, 90, gy, "OffFt", "OffIn", "0", "0", out _txtOffFt, out _txtOffIn);
            grpPipe.Controls.Add(new Label { Text = "1st corner → 1st line", Location = new Point(222, gy + 3), AutoSize = true });
            gy += 27;
            grpPipe.Controls.Add(new Label { Text = "End offset:", Location = new Point(10, gy + 3), AutoSize = true });
            AddFtIn(grpPipe, 90, gy, "EndFt", "EndIn", "0", "0", out _txtEndFt, out _txtEndIn);
            grpPipe.Controls.Add(new Label { Text = "1st corner → last head", Location = new Point(222, gy + 3), AutoSize = true });
            Controls.Add(grpPipe);

            // ── Main(s) — enabled in Area + central main (3-pt) and Two mains (4-pt) ──
            _grpMain = new GroupBox { Text = "Cross-main(s)", Location = new Point(M + 360, y), Size = new Size(350, 326) };
            gy = 22;
            _grpMain.Controls.Add(new Label { Text = "Main type:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbMainType = AddCombo(_grpMain, new Point(80, gy), 258,
                _pipeTypes.Select(t => t.name), DialogMemory.Get(MemKey, "MainType", DefaultMainTypeName()));
            gy += 27;
            _grpMain.Controls.Add(new Label { Text = "Main size:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbMainSize = AddSizeCombo(_grpMain, new Point(80, gy), DialogMemory.GetDouble(MemKey, "MainSize", 2.5));
            _grpMain.Controls.Add(new Label { Text = "in", Location = new Point(146, gy + 3), AutoSize = true });
            _grpMain.Controls.Add(new Label { Text = "Riser:", Location = new Point(178, gy + 3), AutoSize = true });
            _cmbRiserSize = AddSizeCombo(_grpMain, new Point(228, gy), DialogMemory.GetDouble(MemKey, "RiserSize", 1.5));
            _grpMain.Controls.Add(new Label { Text = "in", Location = new Point(294, gy + 3), AutoSize = true });
            gy += 27;
            _grpMain.Controls.Add(new Label { Text = "Main elev:", Location = new Point(10, gy + 3), AutoSize = true });
            AddFtIn(_grpMain, 80, gy, "MainElevFt", "MainElevIn", "9", "0", out _txtMainElevFt, out _txtMainElevIn);
            _lblMainElevRef = new Label { Text = "above level", Location = new Point(210, gy + 3), Size = new Size(132, 16), AutoEllipsis = true, ForeColor = SystemColors.GrayText };
            _grpMain.Controls.Add(_lblMainElevRef);
            var metip = new ToolTip();
            metip.SetToolTip(_txtMainElevFt,
                "Where this elevation is measured on the main:\n" +
                " • Area + central main: the HIGH end — the main slopes DOWN from here to the riser (low) end.\n" +
                " • Two mains: the mains are flat at this elevation.\n" +
                "The Z on the main image marks the HIGH end. Click the image to flip which physical end is high.");
            gy += 27;
            _lblMainSlope = new Label { Text = "Main slope:", Location = new Point(10, gy + 3), AutoSize = true };
            _grpMain.Controls.Add(_lblMainSlope);
            _txtMainSlope = new TextBox { Location = new Point(80, gy), Size = new Size(48, 22), Text = DialogMemory.Get(MemKey, "MainSlope", "0.25") };
            _grpMain.Controls.Add(_txtMainSlope);
            _grpMain.Controls.Add(new Label { Text = "in / 10 ft   down toward the riser", Location = new Point(134, gy + 3), AutoSize = true });
            gy += 27;
            var fitNames = new List<string> { DefaultLabel };
            fitNames.AddRange(_fittings.Select(f => f.name));
            _grpMain.Controls.Add(new Label { Text = "Branch outlet on main:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbOutlet = AddCombo(_grpMain, new Point(130, gy), 208, fitNames,
                DialogMemory.Get(MemKey, "Outlet", _defaultOutlet ?? DefaultLabel));
            var otip = new ToolTip();
            otip.SetToolTip(_cmbOutlet, "The fitting the branch lines tie into the main with " +
                "(a GOL / grooved outlet by default). Used for both tie-in styles below.");
            gy += 27;
            _grpMain.Controls.Add(new Label { Text = "Riser tee:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbRiserTee = AddCombo(_grpMain, new Point(80, gy), 258, fitNames,
                DialogMemory.Get(MemKey, "RiserTee", _defaultRiserTee ?? DefaultLabel));
            gy += 27;
            _grpMain.Controls.Add(new Label { Text = "Head clear:", Location = new Point(10, gy + 3), AutoSize = true });
            _txtHeadClear = new TextBox { Location = new Point(80, gy), Size = new Size(48, 22), Text = DialogMemory.Get(MemKey, "HeadClear", "6") };
            var hctip = new ToolTip();
            hctip.SetToolTip(_txtHeadClear,
                "Minimum centerline distance from the main to the nearest sprinkler.\n" +
                "Head spacing never changes — the main shifts over to make room.");
            _grpMain.Controls.Add(_txtHeadClear);
            _grpMain.Controls.Add(new Label { Text = "in   min main → head (main shifts)", Location = new Point(134, gy + 3), AutoSize = true });
            gy += 27;

            // Branch tie-in style: a riser nipple up from the main to the branch above it
            // (dry / pre-action, branches drain to the main), OR the branch sits at the
            // main's elevation and ties straight into its side with the outlet fitting.
            _grpMain.Controls.Add(new Label { Text = "Branch tie-in:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbTieIn = AddCombo(_grpMain, new Point(90, gy), 248,
                new[] { "Riser nipple above the main", "Side outlet at main elevation" },
                DialogMemory.GetInt(MemKey, "TieIn", 0) == 1 ? "Side outlet at main elevation" : "Riser nipple above the main");
            var titip = new ToolTip();
            titip.SetToolTip(_cmbTieIn,
                "Riser nipple: the branch runs above the main and a vertical nipple drops to it.\n" +
                "Side outlet: the branch sits at the main's elevation and ties into its side —\n" +
                "no nipple. Interior crossings become a 4-way outlet; ends and two-mains taps a tee.");
            _grpMain.Controls.Add(_cmbTieIn);
            gy += 30;

            // Clickable main image: shows the main ⊥ to the branches. In 3-pt mode it
            // labels HIGH/LOW (click to flip the slope direction); in two-mains it shows
            // both mains. Reorients when the branch-direction toggle flips.
            _grpMain.Controls.Add(new Label { Text = "Main / slope  (click to flip HIGH↔LOW):", Location = new Point(10, gy), AutoSize = true });
            _mainReversed = DialogMemory.GetBool(MemKey, "MainReverse", false);
            _mainToggle = new Panel { Location = new Point(10, gy + 20), Size = new Size(150, 56), Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle };
            _mainToggle.Paint += DrawMainSlope;
            _mainToggle.Click += (s, e) => { _mainReversed = !_mainReversed; _mainToggle.Invalidate(); };
            var mtip = new ToolTip();
            mtip.SetToolTip(_mainToggle, "Click to flip which end of the main is high / low (the slope direction).");
            _grpMain.Controls.Add(_mainToggle);

            _chkTailback = new CheckBox
            {
                Text = "Tailback at mains\n(tee + stub; off = elbow)",
                Location = new Point(178, gy + 22),
                Size = new Size(164, 40),
                Checked = DialogMemory.GetBool(MemKey, "Tailback", true)
            };
            _grpMain.Controls.Add(_chkTailback);
            Controls.Add(_grpMain);

            y += 334;

            // ── Sprinklers (full width) ──
            var grpSprk = new GroupBox { Text = "Sprinklers", Location = new Point(M, y), Size = new Size(710, 150) };
            gy = 22;
            grpSprk.Controls.Add(new Label { Text = "Head type:", Location = new Point(10, gy + 3), AutoSize = true });
            _cmbHead = AddCombo(grpSprk, new Point(80, gy), 290,
                _headTypes.Select(t => t.name), DialogMemory.Get(MemKey, "Head", ""));
            grpSprk.Controls.Add(new Label { Text = "Sprig/drop type:", Location = new Point(385, gy + 3), AutoSize = true });
            _cmbSprigType = AddCombo(grpSprk, new Point(490, gy), 210,
                _pipeTypes.Select(t => t.name), DialogMemory.Get(MemKey, "SprigType", DefaultSprigTypeName()));
            gy += 30;
            _rbOutlets = new RadioButton { Text = "Heads directly at outlets on the line", Location = new Point(10, gy), AutoSize = true };
            grpSprk.Controls.Add(_rbOutlets);
            _rbSprigs = new RadioButton { Text = "Sprigs up to heads", Location = new Point(330, gy), AutoSize = true };
            grpSprk.Controls.Add(_rbSprigs);
            _cmbSprigSize = AddSizeCombo(grpSprk, new Point(470, gy - 2), DialogMemory.GetDouble(MemKey, "SprigSize", 1.0));
            grpSprk.Controls.Add(new Label { Text = "in sprig size", Location = new Point(538, gy + 1), AutoSize = true });
            gy += 26;
            var pnlSprig = new Panel { Location = new Point(330, gy), Size = new Size(370, 52) };
            _rbTermElev = new RadioButton { Text = "Common head elevation:", Location = new Point(0, 2), AutoSize = true };
            pnlSprig.Controls.Add(_rbTermElev);
            AddFtIn(pnlSprig, 160, 0, "TermFt", "TermIn", "12", "0", out _txtTermFt, out _txtTermIn);
            pnlSprig.Controls.Add(new Label { Text = "above level", Location = new Point(288, 3), AutoSize = true });
            _rbSprigLen = new RadioButton { Text = "Fixed sprig length:", Location = new Point(0, 28), AutoSize = true };
            pnlSprig.Controls.Add(_rbSprigLen);
            AddFtIn(pnlSprig, 160, 26, "LenFt", "LenIn", "1", "0", out _txtLenFt, out _txtLenIn);
            grpSprk.Controls.Add(pnlSprig);
            Controls.Add(grpSprk);

            bool sprigs = DialogMemory.GetBool(MemKey, "UseSprigs", true);
            _rbSprigs.Checked = sprigs;
            _rbOutlets.Checked = !sprigs;
            bool term = DialogMemory.GetBool(MemKey, "CommonElev", true);
            _rbTermElev.Checked = term;
            _rbSprigLen.Checked = !term;

            EventHandler syncEnable = (s, e) =>
            {
                bool sp = _rbSprigs.Checked;
                _cmbSprigSize.Enabled = sp;
                _rbTermElev.Enabled = _rbSprigLen.Enabled = sp;
                _txtTermFt.Enabled = _txtTermIn.Enabled = sp && _rbTermElev.Checked;
                _txtLenFt.Enabled = _txtLenIn.Enabled = sp && _rbSprigLen.Checked;
            };
            _rbOutlets.CheckedChanged += syncEnable;
            _rbSprigs.CheckedChanged += syncEnable;
            _rbTermElev.CheckedChanged += syncEnable;
            _rbSprigLen.CheckedChanged += syncEnable;
            syncEnable(null, EventArgs.Empty);

            y += 158;

            _btnPlace = new Button { Text = "Place", Location = new Point(740 - M - 90 - 10 - 90, y), Size = new Size(90, 30) };
            _btnPlace.Click += (s, e) => OnPlace();
            Controls.Add(_btnPlace);
            AcceptButton = _btnPlace;

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(740 - M - 90, y), Size = new Size(90, 30) };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;

            // Guidance text sits to the LEFT of the buttons, along the bottom of the window.
            _lblGuidance = new Label
            {
                Location = new Point(M, y + 6),
                Size = new Size(740 - M - 90 - 10 - 90 - 10 - M, 22),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(_lblGuidance);

            // Pick-mode wiring: enable the main group + set guidance + reorient images.
            EventHandler modeChanged = (s, e) =>
            {
                int idx = _cmbPickMode.SelectedIndex;
                bool main = idx == (int)PickModeKind.AreaMain;
                bool two = idx == (int)PickModeKind.TwoMains;
                _grpMain.Enabled = main || two;
                _lblGuidance.Text = two
                    ? "Pick: 1st corner  →  opposite corner  →  primary main  →  secondary main"
                    : main
                        ? "Pick: 1st corner  →  opposite corner  →  main line"
                        : "Pick: 1st corner  →  opposite corner";
                _lblMainSlope.Enabled = _txtMainSlope.Enabled = main;   // slope is 3-pt only; two-mains is flat
                _chkTailback.Enabled = two;
                _mainToggle.Invalidate();
                _dirToggle?.Invalidate();   // the branch "Z" anchor depends on the mode
                UpdateElevRefs();
            };
            _cmbPickMode.SelectedIndexChanged += modeChanged;
            modeChanged(null, EventArgs.Empty);
        }

        // ── Mode column: pick-mode dropdown, direction arrows, cap options ──
        private void BuildModeColumn(Point loc, Size size)
        {
            var grp = new GroupBox { Text = "Mode", Location = loc, Size = size };

            grp.Controls.Add(new Label { Text = "Pick mode:", Location = new Point(10, 24), AutoSize = true });
            _cmbPickMode = new ComboBox { Location = new Point(10, 44), Size = new Size(170, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbPickMode.Items.Add("Fill area (2 corners)");
            _cmbPickMode.Items.Add("Area + central main (3 pts)");
            _cmbPickMode.Items.Add("Two mains (4 pts)");
            _cmbPickMode.SelectedIndex = Math.Max(0, Math.Min(2, DialogMemory.GetInt(MemKey, "PickMode", 0)));
            grp.Controls.Add(_cmbPickMode);

            grp.Controls.Add(new Label { Text = "Branch-line direction:", Location = new Point(10, 78), AutoSize = true });
            _linesAlongX = DialogMemory.GetBool(MemKey, "LinesAlongX", true);
            _dirToggle = new Panel { Location = new Point(20, 98), Size = new Size(154, 58), Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle };
            _dirToggle.Paint += DrawDirArrows;
            _dirToggle.Click += (s, e) => { _linesAlongX = !_linesAlongX; _dirToggle.Invalidate(); _mainToggle?.Invalidate(); };
            var tip = new ToolTip();
            tip.SetToolTip(_dirToggle, "Click to rotate the branch-line direction (X ⇄ Y). The main(s) stay perpendicular.");
            grp.Controls.Add(_dirToggle);

            _chkCap = new CheckBox
            {
                Text = "Cap branch-line ends",
                Location = new Point(10, 168),
                AutoSize = true,
                Checked = DialogMemory.GetBool(MemKey, "CapEnds", true)
            };
            grp.Controls.Add(_chkCap);

            grp.Controls.Add(new Label { Text = "Extend to cap:", Location = new Point(10, 196), AutoSize = true });
            AddFtIn(grp, 10, 216, "CapFt6", "CapIn6", "0", "6", out _txtCapFt, out _txtCapIn);

            Controls.Add(grp);
        }

        private void DrawDirArrows(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = _dirToggle.ClientRectangle;
            g.Clear(SystemColors.Window);
            using (var pen = new Pen(Color.FromArgb(30, 90, 168), 3f))
            using (var brush = new SolidBrush(Color.FromArgb(30, 90, 168)))
            {
                int cx = rc.Width / 2, cy = rc.Height / 2;
                if (_linesAlongX)
                {
                    int x0 = 14, x1 = rc.Width - 14;
                    g.DrawLine(pen, x0, cy, x1, cy);
                    FillArrowHead(g, brush, new Point(x0, cy), true, -1);
                    FillArrowHead(g, brush, new Point(x1, cy), true, +1);
                }
                else
                {
                    int y0 = 12, y1 = rc.Height - 12;
                    g.DrawLine(pen, cx, y0, cx, y1);
                    FillArrowHead(g, brush, new Point(cx, y0), false, -1);
                    FillArrowHead(g, brush, new Point(cx, y1), false, +1);
                }
            }
            TextRenderer.DrawText(g, _linesAlongX ? "X" : "Y", Font,
                new Point(rc.Width / 2 - 6, rc.Height / 2 - 20), Color.Gray);

            // "Z" marks where the branch Start elev is measured:
            //  • central-main mode: the low point where the branch crosses the main (centre)
            //  • fill mode: the near (first-corner) end
            //  • two-mains: flat, no anchor to mark
            int mode = _cmbPickMode != null ? _cmbPickMode.SelectedIndex : 0;
            var zcol = Color.FromArgb(20, 130, 60);
            if (mode == (int)PickModeKind.AreaMain)
                DrawZ(g, zcol, new Point(rc.Width / 2 + 4, rc.Height / 2 - 2));
            else if (mode == (int)PickModeKind.FillArea)
            {
                if (_linesAlongX) DrawZ(g, zcol, new Point(6, rc.Height / 2 - 2));
                else DrawZ(g, zcol, new Point(rc.Width / 2 + 4, 2));
            }
        }

        private static void FillArrowHead(Graphics g, Brush b, Point tip, bool horizontal, int dir)
        {
            const int s = 9;
            Point[] pts = horizontal
                ? new[] { tip, new Point(tip.X - dir * s, tip.Y - s), new Point(tip.X - dir * s, tip.Y + s) }
                : new[] { tip, new Point(tip.X - s, tip.Y - dir * s), new Point(tip.X + s, tip.Y - dir * s) };
            g.FillPolygon(b, pts);
        }

        /// <summary>Refresh the "where is this elevation measured" notes for the current mode.</summary>
        private void UpdateElevRefs()
        {
            int mode = _cmbPickMode != null ? _cmbPickMode.SelectedIndex : 0;
            if (_lblStartElevRef != null)
                _lblStartElevRef.Text = mode == (int)PickModeKind.AreaMain ? "at main · low (Z)"
                                      : mode == (int)PickModeKind.TwoMains ? "flat"
                                      : "at start end (Z)";
            if (_lblMainElevRef != null)
                _lblMainElevRef.Text = mode == (int)PickModeKind.TwoMains ? "flat" : "HIGH end (Z)";
        }

        /// <summary>A bold green "Z" over a short datum tick — marks where the entered
        /// elevation is measured on a pipe image.</summary>
        private void DrawZ(Graphics g, Color col, Point at)
        {
            using (var f = new Font(Font.FontFamily, 9f, FontStyle.Bold))
            using (var pen = new Pen(col, 1.5f))
            {
                TextRenderer.DrawText(g, "Z", f, at, col);
                g.DrawLine(pen, at.X, at.Y + 15, at.X + 14, at.Y + 15);
            }
        }

        /// <summary>Paint the main image: the main runs perpendicular to the branch lines.
        /// In 3-pt mode it labels HIGH/LOW with a downhill arrow (click flips); in two-mains
        /// it shows both parallel mains (flat, no slope).</summary>
        private void DrawMainSlope(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = _mainToggle.ClientRectangle;
            g.Clear(SystemColors.Window);

            bool two = _cmbPickMode != null && _cmbPickMode.SelectedIndex == (int)PickModeKind.TwoMains;
            bool mainAlongY = _linesAlongX;              // main is perpendicular to the branches
            var col = Color.FromArgb(150, 40, 40);
            int cx = rc.Width / 2, cy = rc.Height / 2;

            using (var pen = new Pen(col, 3f))
            using (var brush = new SolidBrush(col))
            {
                if (two)
                {
                    if (mainAlongY)
                    {
                        g.DrawLine(pen, cx - 26, 10, cx - 26, rc.Height - 10);
                        g.DrawLine(pen, cx + 26, 10, cx + 26, rc.Height - 10);
                    }
                    else
                    {
                        g.DrawLine(pen, 12, cy - 14, rc.Width - 12, cy - 14);
                        g.DrawLine(pen, 12, cy + 14, rc.Width - 12, cy + 14);
                    }
                    TextRenderer.DrawText(g, "2 mains", Font, new Point(cx - 24, cy - 8), Color.Gray);
                    return;
                }

                // single sloped main with HIGH / LOW ends (3-pt mode). The "Z" marks the HIGH
                // end — that is where the Main elev is measured; the main slopes down to LOW.
                var zcol = Color.FromArgb(20, 130, 60);
                if (mainAlongY)
                {
                    int x = cx, y0 = 14, y1 = rc.Height - 14;
                    g.DrawLine(pen, x, y0, x, y1);
                    Point hi = _mainReversed ? new Point(x, y1) : new Point(x, y0);   // not reversed → HIGH at top
                    Point lo = _mainReversed ? new Point(x, y0) : new Point(x, y1);
                    FillArrowHead(g, brush, lo, false, lo.Y > hi.Y ? +1 : -1);
                    TextRenderer.DrawText(g, "HIGH", Font, new Point(x + 6, hi.Y - 7), col);
                    TextRenderer.DrawText(g, "LOW", Font, new Point(x + 6, lo.Y - 7), col);
                    DrawZ(g, zcol, new Point(x - 20, hi.Y - 7));
                }
                else
                {
                    int y = cy, x0 = 14, x1 = rc.Width - 14;
                    g.DrawLine(pen, x0, y, x1, y);
                    Point hi = _mainReversed ? new Point(x0, y) : new Point(x1, y);   // not reversed → HIGH at right
                    Point lo = _mainReversed ? new Point(x1, y) : new Point(x0, y);
                    FillArrowHead(g, brush, lo, true, lo.X > hi.X ? +1 : -1);
                    TextRenderer.DrawText(g, "HIGH", Font, new Point(hi.X - (hi.X > cx ? 34 : 2), y - 20), col);
                    TextRenderer.DrawText(g, "LOW", Font, new Point(lo.X - (lo.X > cx ? 30 : 0), y + 5), col);
                    DrawZ(g, zcol, new Point(hi.X - (hi.X > cx ? 12 : 0), y + 6));
                }
            }
        }

        // ── Control builders ──

        private void BuildSlots(GroupBox grp, TextBox[] ft, TextBox[] inch, Func<int, string> label, string memPrefix)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                int gy = 24 + i * 26;
                grp.Controls.Add(new Label { Text = label(i), Location = new Point(12, gy + 3), AutoSize = true });
                ft[i] = new TextBox { Location = new Point(38, gy), Size = new Size(44, 22), Text = DialogMemory.Get(MemKey, memPrefix + "Ft" + i, "") };
                grp.Controls.Add(ft[i]);
                grp.Controls.Add(new Label { Text = "ft", Location = new Point(86, gy + 3), AutoSize = true });
                inch[i] = new TextBox { Location = new Point(106, gy), Size = new Size(44, 22), Text = DialogMemory.Get(MemKey, memPrefix + "In" + i, "") };
                grp.Controls.Add(inch[i]);
                grp.Controls.Add(new Label { Text = "in", Location = new Point(154, gy + 3), AutoSize = true });
            }
        }

        private TextBox BuildSequence(GroupBox grp, string memField, string hint, string tip)
        {
            int gy = 24 + SlotCount * 26 + 6;
            grp.Controls.Add(new Label { Text = "Sequence:", Location = new Point(12, gy + 3), AutoSize = true });
            var txt = new TextBox { Location = new Point(82, gy), Size = new Size(150, 22), Text = DialogMemory.Get(MemKey, memField, hint) };
            grp.Controls.Add(txt);
            grp.Controls.Add(new Label
            {
                Text = tip,
                Location = new Point(12, gy + 28),
                Size = new Size(228, 30),
                ForeColor = SystemColors.GrayText
            });
            return txt;
        }

        private static ComboBox AddCombo(Control parent, Point loc, int width, IEnumerable<string> items, string remembered)
        {
            var cmb = new ComboBox { Location = loc, Size = new Size(width, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var it in items) cmb.Items.Add(it);
            int idx = cmb.Items.IndexOf(remembered);
            if (idx < 0 && cmb.Items.Count > 0) idx = 0;
            cmb.SelectedIndex = idx;
            parent.Controls.Add(cmb);
            return cmb;
        }

        // Standard sprinkler-pipe sizes (label, inches). Above 12" is underground only.
        private static readonly (string label, double inches)[] SizeList =
        {
            ("1/4", 0.25), ("1/2", 0.5), ("3/4", 0.75), ("1", 1.0), ("1¼", 1.25), ("1½", 1.5), ("2", 2.0),
            ("2½", 2.5), ("3", 3.0), ("3½", 3.5), ("4", 4.0), ("5", 5.0), ("6", 6.0),
            ("8", 8.0), ("10", 10.0), ("12", 12.0)
        };

        private static ComboBox AddSizeCombo(Control parent, Point loc, double rememberedIn)
        {
            var cmb = new ComboBox { Location = loc, Size = new Size(62, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var s in SizeList) cmb.Items.Add(s.label);
            int idx = 0; double best = double.MaxValue;
            for (int i = 0; i < SizeList.Length; i++)
            {
                double err = Math.Abs(SizeList[i].inches - rememberedIn);
                if (err <= best) { best = err; idx = i; }   // ties round up to the larger size (list is ascending)
            }
            cmb.SelectedIndex = idx;
            parent.Controls.Add(cmb);
            return cmb;
        }

        private static double ComboSizeIn(ComboBox cmb) =>
            cmb.SelectedIndex >= 0 && cmb.SelectedIndex < SizeList.Length ? SizeList[cmb.SelectedIndex].inches : 1.0;

        private void AddFtIn(Control parent, int x, int y, string memF, string memI,
                             string defF, string defI, out TextBox ft, out TextBox inch)
        {
            ft = new TextBox { Location = new Point(x, y), Size = new Size(40, 22), Text = DialogMemory.Get(MemKey, memF, defF) };
            parent.Controls.Add(ft);
            parent.Controls.Add(new Label { Text = "ft", Location = new Point(x + 44, y + 3), AutoSize = true });
            inch = new TextBox { Location = new Point(x + 62, y), Size = new Size(40, 22), Text = DialogMemory.Get(MemKey, memI, defI) };
            parent.Controls.Add(inch);
            parent.Controls.Add(new Label { Text = "in", Location = new Point(x + 106, y + 3), AutoSize = true });
        }

        private string DefaultPipeTypeName() => PipeTypeMatch("welded", "line");
        private string DefaultMainTypeName() => PipeTypeMatch("welded", "main");
        private string DefaultSprigTypeName() => PipeTypeMatch("thread", "line");

        /// <summary>First pipe type containing BOTH words; else either word; else first.</summary>
        private string PipeTypeMatch(string a, string b)
        {
            var both = _pipeTypes.FirstOrDefault(p =>
                p.name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0 &&
                p.name.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0);
            if (both.name != null) return both.name;
            var one = _pipeTypes.FirstOrDefault(p => p.name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0);
            return one.name ?? (_pipeTypes.Count > 0 ? _pipeTypes[0].name : "");
        }

        private string DefaultSystemName()
        {
            var t = _systemTypes.FirstOrDefault(p => p.name.IndexOf("wet", StringComparison.OrdinalIgnoreCase) >= 0);
            return t.name ?? (_systemTypes.Count > 0 ? _systemTypes[0].name : "");
        }

        // ── OK ──

        private void OnPlace()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                LineSlotFt[i] = ParseNum(_lnFt[i]) + ParseNum(_lnIn[i]) / 12.0;
                HeadSlotFt[i] = ParseNum(_hdFt[i]) + ParseNum(_hdIn[i]) / 12.0;
            }

            LineSequence = CleanSequence(_txtLineSeq.Text).ToUpperInvariant();
            HeadSequence = CleanSequence(_txtHeadSeq.Text).ToUpperInvariant();

            if (LineSequence.Length == 0)
            { Warn("Enter a line-spacing sequence (e.g. 112112) — it repeats to fill the area."); return; }
            foreach (char c in LineSequence)
            {
                if (c < '1' || c > '0' + SlotCount)
                { Warn($"Line sequence character \"{c}\" isn't a slot number (1-{SlotCount})."); return; }
                if (LineSlotFt[c - '1'] <= 1e-9)
                { Warn($"Line slot {c} is referenced by the sequence but has no spacing."); return; }
            }
            foreach (char c in HeadSequence)
            {
                if (c < 'A' || c >= 'A' + SlotCount)
                { Warn($"Head sequence character \"{c}\" isn't a slot letter (A-{(char)('A' + SlotCount - 1)})."); return; }
                if (HeadSlotFt[c - 'A'] <= 1e-9)
                { Warn($"Head slot {c} is referenced by the sequence but has no spacing."); return; }
            }

            if (_cmbPipeType.SelectedIndex < 0) { Warn("No pipe type selected."); return; }
            if (_cmbSystem.SelectedIndex < 0) { Warn("No piping system type selected."); return; }
            if (_cmbLevel.SelectedIndex < 0) { Warn("No level selected."); return; }
            if (HeadSequence.Length > 0 && _cmbHead.SelectedIndex < 0)
            { Warn("No sprinkler type selected."); return; }

            PickMode = _cmbPickMode.SelectedIndex == 1 ? PickModeKind.AreaMain
                     : _cmbPickMode.SelectedIndex == 2 ? PickModeKind.TwoMains
                     : PickModeKind.FillArea;
            PipeTypeId = _pipeTypes[_cmbPipeType.SelectedIndex].id;
            MainPipeTypeId = _cmbMainType.SelectedIndex >= 0 ? _pipeTypes[_cmbMainType.SelectedIndex].id : PipeTypeId;
            SprigPipeTypeId = _cmbSprigType.SelectedIndex >= 0 ? _pipeTypes[_cmbSprigType.SelectedIndex].id : PipeTypeId;
            SystemTypeId = _systemTypes[_cmbSystem.SelectedIndex].id;
            HeadSymbolId = _cmbHead.SelectedIndex >= 0 ? _headTypes[_cmbHead.SelectedIndex].id : -1;
            LevelName = (string)_cmbLevel.SelectedItem;
            StartElevFt = ParseNum(_txtElevFt) + ParseNum(_txtElevIn) / 12.0;
            SlopeFtPerFt = ParseNum(_txtSlope) / 120.0;         // in per 10 ft → ft per ft
            StartOffsetFt = ParseNum(_txtOffFt) + ParseNum(_txtOffIn) / 12.0;
            EndOffsetFt = ParseNum(_txtEndFt) + ParseNum(_txtEndIn) / 12.0;
            LineSizeIn = ComboSizeIn(_cmbLineSize);
            SprigSizeIn = ComboSizeIn(_cmbSprigSize);
            UseSprigs = _rbSprigs.Checked;
            SprigsToCommonElev = _rbTermElev.Checked;
            TermElevFt = ParseNum(_txtTermFt) + ParseNum(_txtTermIn) / 12.0;
            SprigLenFt = ParseNum(_txtLenFt) + ParseNum(_txtLenIn) / 12.0;
            CapEnds = _chkCap.Checked;
            ExtendToCapFt = ParseNum(_txtCapFt) + ParseNum(_txtCapIn) / 12.0;
            MainSizeIn = ComboSizeIn(_cmbMainSize);
            RiserSizeIn = ComboSizeIn(_cmbRiserSize);
            MainElevFt = ParseNum(_txtMainElevFt) + ParseNum(_txtMainElevIn) / 12.0;
            MainSlopeFtPerFt = ParseNum(_txtMainSlope) / 120.0;
            MainHeadClearFt = Math.Max(0.0, ParseNum(_txtHeadClear) / 12.0);
            MainSlopeReversed = _mainReversed;
            Tailback = _chkTailback.Checked;
            SideOutlet = _cmbTieIn.SelectedIndex == 1;
            OutletFittingId = FittingIdAt(_cmbOutlet);
            RiserTeeFittingId = FittingIdAt(_cmbRiserTee);

            for (int i = 0; i < SlotCount; i++)
            {
                DialogMemory.Set(MemKey, "LnFt" + i, _lnFt[i].Text);
                DialogMemory.Set(MemKey, "LnIn" + i, _lnIn[i].Text);
                DialogMemory.Set(MemKey, "HdFt" + i, _hdFt[i].Text);
                DialogMemory.Set(MemKey, "HdIn" + i, _hdIn[i].Text);
            }
            DialogMemory.Set(MemKey, "LineSeq", _txtLineSeq.Text);
            DialogMemory.Set(MemKey, "HeadSeq", _txtHeadSeq.Text);
            DialogMemory.SetInt(MemKey, "PickMode", (int)PickMode);
            DialogMemory.SetBool(MemKey, "LinesAlongX", _linesAlongX);
            DialogMemory.Set(MemKey, "PipeType", (string)_cmbPipeType.SelectedItem);
            DialogMemory.Set(MemKey, "MainType", _cmbMainType.SelectedIndex >= 0 ? (string)_cmbMainType.SelectedItem : "");
            DialogMemory.Set(MemKey, "SprigType", _cmbSprigType.SelectedIndex >= 0 ? (string)_cmbSprigType.SelectedItem : "");
            DialogMemory.Set(MemKey, "System", (string)_cmbSystem.SelectedItem);
            DialogMemory.Set(MemKey, "Level", LevelName);
            DialogMemory.Set(MemKey, "Head", _cmbHead.SelectedIndex >= 0 ? (string)_cmbHead.SelectedItem : "");
            DialogMemory.Set(MemKey, "ElevFt", _txtElevFt.Text);
            DialogMemory.Set(MemKey, "ElevIn", _txtElevIn.Text);
            DialogMemory.Set(MemKey, "BrSlope", _txtSlope.Text);
            DialogMemory.Set(MemKey, "OffFt", _txtOffFt.Text);
            DialogMemory.Set(MemKey, "OffIn", _txtOffIn.Text);
            DialogMemory.Set(MemKey, "EndFt", _txtEndFt.Text);
            DialogMemory.Set(MemKey, "EndIn", _txtEndIn.Text);
            DialogMemory.SetDouble(MemKey, "LineSize", LineSizeIn);
            DialogMemory.SetDouble(MemKey, "SprigSize", SprigSizeIn);
            DialogMemory.SetBool(MemKey, "UseSprigs", UseSprigs);
            DialogMemory.SetBool(MemKey, "CommonElev", SprigsToCommonElev);
            DialogMemory.Set(MemKey, "TermFt", _txtTermFt.Text);
            DialogMemory.Set(MemKey, "TermIn", _txtTermIn.Text);
            DialogMemory.Set(MemKey, "LenFt", _txtLenFt.Text);
            DialogMemory.Set(MemKey, "LenIn", _txtLenIn.Text);
            DialogMemory.SetBool(MemKey, "CapEnds", CapEnds);
            DialogMemory.Set(MemKey, "CapFt6", _txtCapFt.Text);
            DialogMemory.Set(MemKey, "CapIn6", _txtCapIn.Text);
            DialogMemory.SetDouble(MemKey, "MainSize", MainSizeIn);
            DialogMemory.SetDouble(MemKey, "RiserSize", RiserSizeIn);
            DialogMemory.Set(MemKey, "MainElevFt", _txtMainElevFt.Text);
            DialogMemory.Set(MemKey, "MainElevIn", _txtMainElevIn.Text);
            DialogMemory.Set(MemKey, "MainSlope", _txtMainSlope.Text);
            DialogMemory.Set(MemKey, "HeadClear", _txtHeadClear.Text);
            DialogMemory.SetBool(MemKey, "MainReverse", MainSlopeReversed);
            DialogMemory.SetBool(MemKey, "Tailback", Tailback);
            DialogMemory.SetInt(MemKey, "TieIn", SideOutlet ? 1 : 0);
            DialogMemory.Set(MemKey, "Outlet", (string)_cmbOutlet.SelectedItem ?? DefaultLabel);
            DialogMemory.Set(MemKey, "RiserTee", (string)_cmbRiserTee.SelectedItem ?? DefaultLabel);
            DialogMemory.Flush();

            DialogResult = DialogResult.OK;
            Close();
        }

        private static string CleanSequence(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            return new string(raw.Where(char.IsLetterOrDigit).ToArray());
        }

        private static double ParseNum(TextBox tb)
        {
            return double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        }

        /// <summary>Combo index 0 = the "(default)" row → -1; otherwise the fitting id.</summary>
        private int FittingIdAt(ComboBox cmb)
        {
            int i = cmb.SelectedIndex;
            return i <= 0 || i - 1 >= _fittings.Count ? -1 : _fittings[i - 1].id;
        }

        private static void Warn(string msg)
        {
            MessageBox.Show(msg, "Layout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
