using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Annotation
{
    /// <summary>
    /// Dialog for the Sprig Tags command.
    ///
    /// Collects:
    ///   • Max host pipe size (inches) — only tags on pipes this size or
    ///     smaller are converted (default 1").
    ///   • Optional "only convert tags of family X" guard so unrelated tags
    ///     on the same sprig pipe are left alone.
    ///   • The SPRIG tag type to convert qualifying sprig tags to.
    ///
    /// All inputs persist between runs via <see cref="DialogMemory"/>.
    /// </summary>
    public class SprigTagsDialog : DpiAwareForm
    {
        private const string MemKey = "SprigTags";

        // ── Results ──
        public double MaxSizeInches { get; private set; } = 1.0;
        public string FromFamily { get; private set; } = SprigTagsCommand.AnyFamily;
        public int TargetTypeIndex { get; private set; } = -1;

        // ── Controls ──
        private NumericUpDown nudSize;
        private ComboBox cboFromFamily;
        private ComboBox cboTarget;

        private readonly int _candidateCount;
        private readonly bool _fromSelection;
        private readonly List<string> _fromFamilies;
        private readonly List<string> _tagDisplays;

        public SprigTagsDialog(int candidateCount, bool fromSelection,
            List<string> fromFamilies, List<string> tagDisplays)
        {
            _candidateCount = candidateCount;
            _fromSelection = fromSelection;
            _fromFamilies = fromFamilies ?? new List<string>();
            _tagDisplays = tagDisplays ?? new List<string>();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Sprig Tags";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 368);

            int margin = 15;
            int y = margin;
            const int GroupW = 490;

            // ── About ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 64)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Converts the vertical-pipe direction tag (UP / DN / RN) to your SPRIG tag\n" +
                       "on small sprigs — vertical pipes at or below the chosen size with a sprinkler\n" +
                       "on top. Genuine drops and riser nipples are left unchanged.",
                Location = new Point(10, 18),
                Size = new Size(GroupW - 20, 42)
            });
            Controls.Add(grpInfo);
            y += 72;

            // ── Selection summary ──
            var grpSummary = new GroupBox
            {
                Text = "Tags Considered",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 46)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_candidateCount} pipe tag{(_candidateCount != 1 ? "s" : "")} " +
                       $"{(_fromSelection ? "from selection" : "in active view")}.",
                Location = new Point(10, 18),
                Size = new Size(GroupW - 20, 20),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 54;

            // ── Match ──
            var grpMatch = new GroupBox
            {
                Text = "Match",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 96)
            };

            grpMatch.Controls.Add(new Label
            {
                Text = "Max host pipe size (in):",
                Location = new Point(10, 27),
                Size = new Size(170, 20)
            });
            nudSize = new NumericUpDown
            {
                Location = new Point(185, 24),
                Size = new Size(70, 24),
                Minimum = 0.25m,
                Maximum = 12m,
                Increment = 0.25m,
                DecimalPlaces = 2,
                Value = (decimal)DialogMemory.GetDouble(MemKey, "MaxSizeIn", 1.0)
            };
            grpMatch.Controls.Add(nudSize);

            grpMatch.Controls.Add(new Label
            {
                Text = "Only convert tags of family:",
                Location = new Point(10, 61),
                Size = new Size(170, 20)
            });
            cboFromFamily = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(185, 58),
                Size = new Size(GroupW - 185 - 15, 24)
            };
            cboFromFamily.Items.Add(SprigTagsCommand.AnyFamily);
            foreach (var f in _fromFamilies) cboFromFamily.Items.Add(f);
            SelectCombo(cboFromFamily,
                DialogMemory.Get(MemKey, "FromFamily", SprigTagsCommand.AnyFamily), 0);
            grpMatch.Controls.Add(cboFromFamily);

            Controls.Add(grpMatch);
            y += 104;

            // ── Convert To ──
            var grpTarget = new GroupBox
            {
                Text = "Convert To",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 70)
            };
            grpTarget.Controls.Add(new Label
            {
                Text = "Convert matching sprig tags to this type:",
                Location = new Point(10, 22),
                Size = new Size(GroupW - 20, 18)
            });
            cboTarget = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(10, 42),
                Size = new Size(GroupW - 20, 24)
            };
            foreach (var d in _tagDisplays) cboTarget.Items.Add(d);
            SelectCombo(cboTarget,
                DialogMemory.Get(MemKey, "TargetDisplay", ""), DefaultTargetIndex());
            grpTarget.Controls.Add(cboTarget);
            Controls.Add(grpTarget);
            y += 78;

            // ── Buttons (right-aligned with 10px gap) ──
            // Form width 520, margin 15 → Cancel right edge at 505.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(430, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Convert",
                DialogResult = DialogResult.OK,
                Location = new Point(310, y),
                Size = new Size(110, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        /// <summary>Picks the first loaded type whose text mentions "sprig", else 0.</summary>
        private int DefaultTargetIndex()
        {
            for (int i = 0; i < _tagDisplays.Count; i++)
            {
                if (_tagDisplays[i].IndexOf("sprig", StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return _tagDisplays.Count > 0 ? 0 : -1;
        }

        private static void SelectCombo(ComboBox cbo, string value, int fallbackIndex)
        {
            if (!string.IsNullOrEmpty(value))
            {
                int idx = cbo.Items.IndexOf(value);
                if (idx >= 0) { cbo.SelectedIndex = idx; return; }
            }
            if (fallbackIndex >= 0 && fallbackIndex < cbo.Items.Count)
                cbo.SelectedIndex = fallbackIndex;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (cboTarget.SelectedIndex < 0)
            {
                MessageBox.Show("Pick the SPRIG tag type to convert to.",
                    "Sprig Tags", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            MaxSizeInches = (double)nudSize.Value;
            FromFamily = cboFromFamily.SelectedItem?.ToString() ?? SprigTagsCommand.AnyFamily;
            TargetTypeIndex = cboTarget.SelectedIndex;

            DialogMemory.SetDouble(MemKey, "MaxSizeIn", MaxSizeInches);
            DialogMemory.Set(MemKey, "FromFamily", FromFamily);
            DialogMemory.Set(MemKey, "TargetDisplay", cboTarget.SelectedItem?.ToString() ?? "");
            DialogMemory.Flush();
        }
    }
}
