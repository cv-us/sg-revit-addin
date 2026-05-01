using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SSG_FP_Suite.Commands.ModelCheck
{
    /// <summary>
    /// Dialog for the Hanger Gap Check command.
    /// Lets the user pick which Type Codes and pipe sizes to check, plus
    /// the gap threshold above which hangers are flagged. Also exposes
    /// a "Clear Markers Only" action for wiping existing markers without
    /// running a check.
    /// </summary>
    public class HangerGapCheckDialog : Form
    {
        public enum ActionMode { Check, ClearOnly }

        // ── Results ──
        public ActionMode Mode { get; private set; } = ActionMode.Check;
        public List<string> SelectedTypeCodes { get; private set; } = new List<string>();
        public List<double> SelectedSizes { get; private set; } = new List<double>();
        public double ThresholdInches { get; private set; } = 6.0;

        // ── Controls ──
        private CheckedListBox lstTypeCodes;
        private CheckedListBox lstSizes;
        private NumericUpDown nudThreshold;
        private Button btnAllTypes, btnNoneTypes, btnAllSizes, btnNoneSizes;

        private readonly List<string> _typeCodes;
        private readonly List<double> _sizesFt;

        public HangerGapCheckDialog(int hangerCount, List<string> availableTypeCodes,
            List<double> availableSizesFt)
        {
            _typeCodes = availableTypeCodes ?? new List<string>();
            _sizesFt = availableSizesFt ?? new List<double>();

            Text = "Hanger Gap Check";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 470);

            int margin = 15;
            int y = margin;

            // ── Header ──
            var lblHeader = new Label
            {
                Text = $"{hangerCount} hanger{(hangerCount != 1 ? "s" : "")} selected. " +
                       "Pick which to check below.",
                Location = new Point(margin, y),
                Size = new Size(510, 20)
            };
            Controls.Add(lblHeader);
            y += 28;

            // ── Type Code group ──
            var grpTypes = new GroupBox
            {
                Text = "Type Code (Hydratec) — check these hangers",
                Location = new Point(margin, y),
                Size = new Size(245, 200)
            };
            Controls.Add(grpTypes);

            lstTypeCodes = new CheckedListBox
            {
                Location = new Point(10, 22),
                Size = new Size(225, 135),
                CheckOnClick = true
            };
            foreach (var tc in _typeCodes)
                lstTypeCodes.Items.Add(tc, true); // all checked by default
            grpTypes.Controls.Add(lstTypeCodes);

            btnAllTypes = new Button
            {
                Text = "All",
                Location = new Point(10, 165),
                Size = new Size(60, 25)
            };
            btnAllTypes.Click += (s, e) => SetAllChecked(lstTypeCodes, true);
            grpTypes.Controls.Add(btnAllTypes);

            btnNoneTypes = new Button
            {
                Text = "None",
                Location = new Point(75, 165),
                Size = new Size(60, 25)
            };
            btnNoneTypes.Click += (s, e) => SetAllChecked(lstTypeCodes, false);
            grpTypes.Controls.Add(btnNoneTypes);

            // ── Pipe Size group ──
            var grpSizes = new GroupBox
            {
                Text = "Pipe Sizes — only check hangers on these",
                Location = new Point(margin + 255, y),
                Size = new Size(245, 200)
            };
            Controls.Add(grpSizes);

            lstSizes = new CheckedListBox
            {
                Location = new Point(10, 22),
                Size = new Size(225, 135),
                CheckOnClick = true
            };
            foreach (var sz in _sizesFt)
                lstSizes.Items.Add(new SizeItem(sz), true);
            grpSizes.Controls.Add(lstSizes);

            btnAllSizes = new Button
            {
                Text = "All",
                Location = new Point(10, 165),
                Size = new Size(60, 25)
            };
            btnAllSizes.Click += (s, e) => SetAllChecked(lstSizes, true);
            grpSizes.Controls.Add(btnAllSizes);

            btnNoneSizes = new Button
            {
                Text = "None",
                Location = new Point(75, 165),
                Size = new Size(60, 25)
            };
            btnNoneSizes.Click += (s, e) => SetAllChecked(lstSizes, false);
            grpSizes.Controls.Add(btnNoneSizes);

            y += 210;

            // ── Threshold group ──
            var grpThreshold = new GroupBox
            {
                Text = "Gap threshold",
                Location = new Point(margin, y),
                Size = new Size(510, 80)
            };
            Controls.Add(grpThreshold);

            grpThreshold.Controls.Add(new Label
            {
                Text = "Flag hangers whose top-of-pipe to structure gap exceeds:",
                Location = new Point(10, 25),
                Size = new Size(340, 18)
            });

            nudThreshold = new NumericUpDown
            {
                Location = new Point(355, 22),
                Size = new Size(70, 24),
                Minimum = 0.5M,
                Maximum = 48,
                Value = 6,
                DecimalPlaces = 1,
                Increment = 0.5M
            };
            grpThreshold.Controls.Add(nudThreshold);

            grpThreshold.Controls.Add(new Label
            {
                Text = "inches",
                Location = new Point(430, 25),
                Size = new Size(50, 18)
            });

            grpThreshold.Controls.Add(new Label
            {
                Text = "Math: gap = rod length − (pipe OD ÷ 2). Type 02 also subtracts 1.5\" hardware.",
                Location = new Point(10, 50),
                Size = new Size(490, 18),
                ForeColor = SystemColors.GrayText
            });

            y += 90;

            // ── Buttons ──
            var btnCheck = new Button
            {
                Text = "Check",
                Location = new Point(margin, y),
                Size = new Size(110, 30)
            };
            btnCheck.Click += (s, e) =>
            {
                Mode = ActionMode.Check;
                CollectFilters();
                DialogResult = DialogResult.OK;
            };
            AcceptButton = btnCheck;
            Controls.Add(btnCheck);

            var btnClear = new Button
            {
                Text = "Clear Markers Only",
                Location = new Point(margin + 120, y),
                Size = new Size(160, 30)
            };
            btnClear.Click += (s, e) =>
            {
                Mode = ActionMode.ClearOnly;
                // No filters needed for clear; we just leave the defaults
                DialogResult = DialogResult.OK;
            };
            Controls.Add(btnClear);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(445, y),
                Size = new Size(80, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void CollectFilters()
        {
            SelectedTypeCodes = lstTypeCodes.CheckedItems.Cast<string>().ToList();
            SelectedSizes = lstSizes.CheckedItems.Cast<SizeItem>()
                .Select(si => si.SizeFt).ToList();
            ThresholdInches = (double)nudThreshold.Value;
        }

        private void SetAllChecked(CheckedListBox list, bool checkedState)
        {
            for (int i = 0; i < list.Items.Count; i++)
                list.SetItemChecked(i, checkedState);
        }

        /// <summary>
        /// Wraps a pipe-size value (feet) with a friendly inch display string
        /// like "1\"" or "1-1/4\"" for the CheckedListBox.
        /// </summary>
        private class SizeItem
        {
            public double SizeFt { get; }
            public SizeItem(double sizeFt) { SizeFt = sizeFt; }

            public override string ToString()
            {
                double inches = SizeFt * 12.0;
                int whole = (int)Math.Floor(inches);
                double frac = inches - whole;

                // Common nominal fractions
                if (Math.Abs(frac) < 0.05) return $"{whole}\"";
                if (Math.Abs(frac - 0.25) < 0.05) return whole > 0 ? $"{whole}-1/4\"" : "1/4\"";
                if (Math.Abs(frac - 0.5)  < 0.05) return whole > 0 ? $"{whole}-1/2\"" : "1/2\"";
                if (Math.Abs(frac - 0.75) < 0.05) return whole > 0 ? $"{whole}-3/4\"" : "3/4\"";
                return $"{inches:F2}\"";
            }
        }
    }
}
