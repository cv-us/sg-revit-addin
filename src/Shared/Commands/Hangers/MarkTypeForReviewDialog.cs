using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Mark Type for Review command. Drives three actions:
    ///   • Place Markers       — flag selected hangers of a chosen Type Code.
    ///   • Delete by Type Code — remove markers for one Type Code.
    ///   • Delete All Markers  — remove every review marker in the project.
    ///
    /// The "Add Markers" group is disabled when no hangers are selected;
    /// the "Delete Markers" group is disabled when no markers exist.
    /// </summary>
    public class MarkTypeForReviewDialog : DpiAwareForm
    {
        private const string MemKey = "MarkTypeForReview";

        public enum MarkAction { None, Place, DeleteAll, DeleteByType }

        // ── Results ──
        public MarkAction Action { get; private set; } = MarkAction.None;
        public string TypeCode { get; private set; }
        public double ReachFeet { get; private set; }
        public string DeleteTypeCode { get; private set; }

        // ── Controls ──
        private ComboBox cbTypeCode;
        private NumericUpDown nudReach;
        private ComboBox cbDeleteType;
        private Button btnPlace, btnDeleteType, btnDeleteAll, btnClose;

        private readonly int _hangerCount;

        /// <summary>Combo item that displays "code (count)" but yields the bare code.</summary>
        private class MarkerTypeItem
        {
            public string Code;
            public int Count;
            public override string ToString()
                => $"{(Code.Length == 0 ? "(untagged)" : Code)}  ({Count})";
        }

        public MarkTypeForReviewDialog(int hangerCount, List<string> availableCodes,
            Dictionary<string, int> markerCounts)
        {
            _hangerCount = hangerCount;
            InitializeComponent(availableCodes, markerCounts);
        }

        private void InitializeComponent(List<string> availableCodes, Dictionary<string, int> markerCounts)
        {
            const int Margin = 15;
            const int FormWidth = 460;
            const int GroupW = FormWidth - Margin * 2;
            const int LabelW = 150;
            const int InputX = 25 + LabelW;

            Text = "Mark Type for Review";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AllowResize = false;   // fixed action dialog — nothing gains from resizing

            int y = Margin;

            bool hasSelection = _hangerCount > 0 && availableCodes.Count > 0;
            int totalMarkers = markerCounts?.Values.Sum() ?? 0;
            bool hasMarkers = totalMarkers > 0;

            // ── Summary ──
            var lblSummary = new Label
            {
                Text = hasSelection
                    ? $"Selected hangers: {_hangerCount}"
                    : "No hangers selected — select hangers to add markers.",
                Location = new Point(Margin, y),
                Size = new Size(GroupW, 20),
                AutoSize = false,
                Font = new Font(Font, FontStyle.Bold)
            };
            Controls.Add(lblSummary);
            y += 28;

            // ── Add Markers group ──
            var grpAdd = new GroupBox
            {
                Text = "Add Markers",
                Location = new Point(Margin, y),
                Size = new Size(GroupW, 130),
                Enabled = hasSelection
            };
            Controls.Add(grpAdd);

            grpAdd.Controls.Add(new Label
            {
                Text = "Type Code:",
                Location = new Point(10, 28),
                Size = new Size(LabelW, 20)
            });
            cbTypeCode = new ComboBox
            {
                Location = new Point(InputX - Margin, 25),
                Size = new Size(GroupW - (InputX - Margin) - 15, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var code in availableCodes)
                cbTypeCode.Items.Add(code);
            if (cbTypeCode.Items.Count > 0) cbTypeCode.SelectedIndex = 0;
            grpAdd.Controls.Add(cbTypeCode);

            grpAdd.Controls.Add(new Label
            {
                Text = "Reach above/below (ft):",
                Location = new Point(10, 60),
                Size = new Size(LabelW, 20)
            });
            nudReach = new NumericUpDown
            {
                Location = new Point(InputX - Margin, 57),
                Size = new Size(90, 24),
                Minimum = 0.5m,
                Maximum = 50m,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 5.0m
            };
            double rememberedReach = DialogMemory.GetDouble(MemKey, "ReachFt", 5.0);
            if (rememberedReach >= 0.5 && rememberedReach <= 50.0)
                nudReach.Value = (decimal)rememberedReach;
            grpAdd.Controls.Add(nudReach);

            var tips = new ToolTip();
            tips.SetToolTip(nudReach,
                "Vertical distance the review marker extends above and below the hanger.");

            btnPlace = new Button
            {
                Text = "Place Markers",
                Location = new Point(GroupW - 15 - 130, 92),
                Size = new Size(130, 28)
            };
            btnPlace.Click += BtnPlace_Click;
            grpAdd.Controls.Add(btnPlace);
            AcceptButton = btnPlace;   // Enter = the primary (Place) action
            y += 140;

            // ── Delete Markers group ──
            var grpDelete = new GroupBox
            {
                Text = "Delete Markers",
                Location = new Point(Margin, y),
                Size = new Size(GroupW, 120),
                Enabled = hasMarkers
            };
            Controls.Add(grpDelete);

            grpDelete.Controls.Add(new Label
            {
                Text = $"Existing review markers: {totalMarkers}",
                Location = new Point(10, 24),
                Size = new Size(GroupW - 25, 18)
            });

            grpDelete.Controls.Add(new Label
            {
                Text = "For Type Code:",
                Location = new Point(10, 52),
                Size = new Size(110, 20)
            });
            cbDeleteType = new ComboBox
            {
                Location = new Point(125, 49),
                Size = new Size(160, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            if (markerCounts != null)
            {
                foreach (var kvp in markerCounts.OrderBy(k => k.Key))
                    cbDeleteType.Items.Add(new MarkerTypeItem { Code = kvp.Key, Count = kvp.Value });
            }
            if (cbDeleteType.Items.Count > 0) cbDeleteType.SelectedIndex = 0;
            grpDelete.Controls.Add(cbDeleteType);

            btnDeleteType = new Button
            {
                Text = "Delete These",
                Location = new Point(295, 48),
                Size = new Size(120, 26)
            };
            btnDeleteType.Click += BtnDeleteType_Click;
            grpDelete.Controls.Add(btnDeleteType);

            btnDeleteAll = new Button
            {
                Text = "Delete All Markers",
                Location = new Point(GroupW - 15 - 150, 84),
                Size = new Size(150, 28)
            };
            btnDeleteAll.Click += (s, e) =>
            {
                Action = MarkAction.DeleteAll;
                DialogResult = DialogResult.OK;
            };
            grpDelete.Controls.Add(btnDeleteAll);
            y += 130;

            // ── Close ──
            btnClose = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Location = new Point(FormWidth - Margin - 85, y),
                Size = new Size(85, 28)
            };
            CancelButton = btnClose;
            Controls.Add(btnClose);

            y += 28 + Margin;
            ClientSize = new Size(FormWidth, y);
        }

        private void BtnPlace_Click(object sender, EventArgs e)
        {
            TypeCode = cbTypeCode.SelectedItem as string;
            ReachFeet = (double)nudReach.Value;

            if (string.IsNullOrWhiteSpace(TypeCode))
            {
                MessageBox.Show(this, "Pick a Type Code.", "Mark Type for Review",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogMemory.SetDouble(MemKey, "ReachFt", ReachFeet);
            DialogMemory.Flush();

            Action = MarkAction.Place;
            DialogResult = DialogResult.OK;
        }

        private void BtnDeleteType_Click(object sender, EventArgs e)
        {
            if (!(cbDeleteType.SelectedItem is MarkerTypeItem item))
            {
                MessageBox.Show(this, "Pick a Type Code to delete.", "Mark Type for Review",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DeleteTypeCode = item.Code;
            Action = MarkAction.DeleteByType;
            DialogResult = DialogResult.OK;
        }
    }
}
