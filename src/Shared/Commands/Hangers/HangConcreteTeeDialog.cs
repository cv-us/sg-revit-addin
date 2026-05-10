using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the AutoHang Concrete Tee (Side of Stems) command.
    ///
    /// Collects:
    ///   - Pipe type filter (or "ALL Pipes")
    ///   - Hanger family name (pipe accessory families containing "-Pipe Hanger")
    ///   - Hanger type code (Hydratec)
    ///   - Rod offset from stem face (inches, default 0.5)
    ///   - Anchor distance above bottom of stem (inches, default 4)
    ///   - Linked model keyword for finding double tee model
    /// </summary>
    public class HangConcreteTeeDialog : Form
    {
        // ── Results ──
        public string PipeTypeFilter { get; private set; } = "ALL Pipes";
        public string SelectedFamily { get; private set; } = "";
        public string HangerTypeCode { get; private set; } = "01";
        public double RodOffsetFromStemInches { get; private set; } = 0.5;
        public double AnchorAboveBottomInches { get; private set; } = 4.0;
        public string LinkedModelKeyword { get; private set; } = "DOUBLE_TEE";

        // ── Controls ──
        private ComboBox cboPipeFilter;
        private ComboBox cboHangerFamily;
        private TextBox txtTypeCode;
        private NumericUpDown numRodOffset;
        private NumericUpDown numAnchorAbove;
        private TextBox txtLinkKeyword;

        private readonly int _pipeCount;
        private readonly int _lineCount;
        private readonly IList<string> _hangerFamilies;
        private readonly IList<string> _pipeTypeNames;

        public HangConcreteTeeDialog(
            int pipeCount, int lineCount,
            IList<string> hangerFamilies, IList<string> pipeTypeNames)
        {
            _pipeCount = pipeCount;
            _lineCount = lineCount;
            _hangerFamilies = hangerFamilies;
            _pipeTypeNames = pipeTypeNames;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Auto Hang — Concrete Tee Stems (User Locations)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 500);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(470, 55)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Places pipe hangers on the sides of concrete double tee stems.\n" +
                       "User draws detail lines across pipes at desired hanger locations.",
                Location = new Point(10, 18),
                Size = new Size(450, 32)
            });
            Controls.Add(grpInfo);
            y += 60;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(470, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_pipeCount} pipe{(_pipeCount != 1 ? "s" : "")} and " +
                       $"{_lineCount} detail line{(_lineCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 20),
                Size = new Size(450, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 55;

            // ── Pipe Filter ──
            var grpFilter = new GroupBox
            {
                Text = "Pipe Filter",
                Location = new Point(margin, y),
                Size = new Size(470, 50)
            };
            grpFilter.Controls.Add(new Label
            {
                Text = "Pipe type:",
                Location = new Point(10, 22),
                Size = new Size(80, 18)
            });
            cboPipeFilter = new ComboBox
            {
                Location = new Point(95, 19),
                Size = new Size(350, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboPipeFilter.Items.Add("ALL Pipes");
            foreach (var name in _pipeTypeNames)
                cboPipeFilter.Items.Add(name);
            cboPipeFilter.SelectedIndex = 0;
            grpFilter.Controls.Add(cboPipeFilter);
            Controls.Add(grpFilter);
            y += 55;

            // ── Hanger Family ──
            var grpFamily = new GroupBox
            {
                Text = "Hanger Settings",
                Location = new Point(margin, y),
                Size = new Size(470, 85)
            };
            grpFamily.Controls.Add(new Label
            {
                Text = "Family:",
                Location = new Point(10, 22),
                Size = new Size(80, 18)
            });
            cboHangerFamily = new ComboBox
            {
                Location = new Point(95, 19),
                Size = new Size(350, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var name in _hangerFamilies)
                cboHangerFamily.Items.Add(name);
            if (cboHangerFamily.Items.Count > 0)
                cboHangerFamily.SelectedIndex = 0;
            // Pre-select "-Basic Adjustable Ring Hanger" if present
            for (int i = 0; i < cboHangerFamily.Items.Count; i++)
            {
                if (cboHangerFamily.Items[i].ToString().Contains("-Basic Adjustable"))
                {
                    cboHangerFamily.SelectedIndex = i;
                    break;
                }
            }
            grpFamily.Controls.Add(cboHangerFamily);

            grpFamily.Controls.Add(new Label
            {
                Text = "Type code:",
                Location = new Point(10, 52),
                Size = new Size(80, 18)
            });
            txtTypeCode = new TextBox
            {
                Text = "01",
                Location = new Point(95, 49),
                Size = new Size(60, 22)
            };
            grpFamily.Controls.Add(txtTypeCode);
            Controls.Add(grpFamily);
            y += 90;

            // ── Stem Settings ──
            var grpStem = new GroupBox
            {
                Text = "Stem Placement Settings",
                Location = new Point(margin, y),
                Size = new Size(470, 85)
            };
            grpStem.Controls.Add(new Label
            {
                Text = "Rod offset from stem (in):",
                Location = new Point(10, 22),
                Size = new Size(180, 18)
            });
            numRodOffset = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 12,
                DecimalPlaces = 2,
                Increment = 0.25m,
                Value = 0.5m,
                Location = new Point(195, 19),
                Size = new Size(70, 22)
            };
            grpStem.Controls.Add(numRodOffset);

            grpStem.Controls.Add(new Label
            {
                Text = "Anchor above stem bottom (in):",
                Location = new Point(10, 52),
                Size = new Size(180, 18)
            });
            numAnchorAbove = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 48,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 4m,
                Location = new Point(195, 49),
                Size = new Size(70, 22)
            };
            grpStem.Controls.Add(numAnchorAbove);
            Controls.Add(grpStem);
            y += 90;

            // ── Link Keyword ──
            var grpLink = new GroupBox
            {
                Text = "Linked Model (Structural Double Tees)",
                Location = new Point(margin, y),
                Size = new Size(470, 50)
            };
            grpLink.Controls.Add(new Label
            {
                Text = "Link keyword:",
                Location = new Point(10, 22),
                Size = new Size(90, 18)
            });
            txtLinkKeyword = new TextBox
            {
                Text = "DOUBLE_TEE",
                Location = new Point(105, 19),
                Size = new Size(200, 22)
            };
            grpLink.Controls.Add(txtLinkKeyword);
            grpLink.Controls.Add(new Label
            {
                Text = "(matches link file name)",
                Location = new Point(315, 22),
                Size = new Size(140, 18),
                ForeColor = SystemColors.GrayText
            });
            Controls.Add(grpLink);
            y += 60;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Place Hangers",
                DialogResult = DialogResult.OK,
                Location = new Point(310, y),
                Size = new Size(100, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(415, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            PipeTypeFilter = cboPipeFilter.SelectedItem?.ToString() ?? "ALL Pipes";
            SelectedFamily = cboHangerFamily.SelectedItem?.ToString() ?? "";
            HangerTypeCode = txtTypeCode.Text.Trim();
            RodOffsetFromStemInches = (double)numRodOffset.Value;
            AnchorAboveBottomInches = (double)numAnchorAbove.Value;
            LinkedModelKeyword = txtLinkKeyword.Text.Trim();
        }
    }
}
