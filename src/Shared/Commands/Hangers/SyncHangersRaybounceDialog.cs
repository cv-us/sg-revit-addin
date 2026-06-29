using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the AutoSync Hangers to Structural Elements command.
    ///
    /// Collects:
    ///   - Hanger type codes per structural category (Floors, Stairs, Roofs, Framing)
    ///   - Whether to keep existing hanger types (only update rod lengths)
    ///
    /// All inputs persist between runs via <see cref="DialogMemory"/>.
    /// </summary>
    public class SyncHangersRaybounceDialog : DpiAwareForm
    {
        private const string MemKey = "SyncRaybounce";

        // ── Results ──
        public string TypeCodeFloors { get; private set; } = "05";
        public string TypeCodeStairs { get; private set; } = "02";
        public string TypeCodeRoofs { get; private set; } = "03";
        public string TypeCodeFraming { get; private set; } = "02";
        public bool KeepHangerTypes { get; private set; } = false;
        /// <summary>
        /// When true, the raybounce filter also matches Generic Models and
        /// Masses — covers IFC imports, STEP/SAT/Inventor imports, and other
        /// non-structural geometry that the standard category list misses.
        /// </summary>
        public bool IncludeGenericGeometry { get; private set; } = false;

        /// <summary>
        /// When true, the raybounce filter also matches ImportInstance
        /// elements (linked DWG / DGN / SAT geometry — covers STEP and IFC
        /// brought through AutoCAD into a DWG). The intersector target is
        /// switched from Face-only to All, and the absolute closest hit of
        /// any reference type wins — face, mesh, or edge. CAD imports
        /// often only expose edge references (especially STEP-via-AutoCAD
        /// wireframes), so face-only mode would miss them entirely.
        /// </summary>
        public bool IncludeImportedCAD { get; private set; } = false;

        // ── Controls ──
        private TextBox txtFloors;
        private TextBox txtStairs;
        private TextBox txtRoofs;
        private TextBox txtFraming;
        private CheckBox chkKeepTypes;
        private CheckBox chkIncludeGeneric;
        private CheckBox chkIncludeCAD;

        private readonly int _hangerCount;

        public SyncHangersRaybounceDialog(int hangerCount,
            string defaultFloors = "05", string defaultStairs = "02",
            string defaultRoofs = "03", string defaultFraming = "02",
            bool defaultKeepTypes = false)
        {
            _hangerCount = hangerCount;
            // Last-used values win over the passed-in defaults so the dialog
            // re-opens with whatever was entered/checked last time.
            TypeCodeFloors = DialogMemory.Get(MemKey, "Floors", defaultFloors);
            TypeCodeStairs = DialogMemory.Get(MemKey, "Stairs", defaultStairs);
            TypeCodeRoofs = DialogMemory.Get(MemKey, "Roofs", defaultRoofs);
            TypeCodeFraming = DialogMemory.Get(MemKey, "Framing", defaultFraming);
            KeepHangerTypes = DialogMemory.GetBool(MemKey, "KeepTypes", defaultKeepTypes);
            IncludeGenericGeometry = DialogMemory.GetBool(MemKey, "IncludeGeneric", false);
            IncludeImportedCAD = DialogMemory.GetBool(MemKey, "IncludeCAD", false);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Raybounce Dev (under development)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 510);

            int margin = 15;
            int y = margin;

            const int GroupW = 510;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 70)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Shoots a ray upward from each hanger to find the closest element above\n" +
                       "(floors, stairs, roofs, structural framing — including linked models).\n" +
                       "Sets Rod Length to the vertical distance to the hit.",
                Location = new Point(10, 18),
                Size = new Size(GroupW - 20, 45)
            });
            Controls.Add(grpInfo);
            y += 80;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_hangerCount} pipe hanger{(_hangerCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 20),
                Size = new Size(GroupW - 20, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 60;

            // ── Hanger Type Codes (sized to fit all four rows + comfortable padding) ──
            var grpTypes = new GroupBox
            {
                Text = "Hanger Assembly Type Codes (Hydratec)",
                Location = new Point(margin, y),
                Size = new Size(GroupW, 140)
            };

            int lx = 10, tx = 195, tw = 60, rowH = 28, labelW = 175;
            int gy = 22;
            grpTypes.Controls.Add(new Label { Text = "Floors:", Location = new Point(lx, gy + 3), Size = new Size(labelW, 18) });
            txtFloors = new TextBox { Text = TypeCodeFloors, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtFloors);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Stairs:", Location = new Point(lx, gy + 3), Size = new Size(labelW, 18) });
            txtStairs = new TextBox { Text = TypeCodeStairs, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtStairs);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Roofs:", Location = new Point(lx, gy + 3), Size = new Size(labelW, 18) });
            txtRoofs = new TextBox { Text = TypeCodeRoofs, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtRoofs);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Structural Framing:", Location = new Point(lx, gy + 3), Size = new Size(labelW, 18) });
            txtFraming = new TextBox { Text = TypeCodeFraming, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtFraming);

            Controls.Add(grpTypes);
            y += 150;

            // ── Keep Types ──
            chkKeepTypes = new CheckBox
            {
                Text = "Keep existing Hanger Types and Comments — only adjust Rod Lengths",
                Location = new Point(margin + 5, y),
                Size = new Size(GroupW - 10, 22),
                Checked = KeepHangerTypes
            };
            chkKeepTypes.CheckedChanged += (s, e) =>
            {
                bool disabled = chkKeepTypes.Checked;
                txtFloors.Enabled = !disabled;
                txtStairs.Enabled = !disabled;
                txtRoofs.Enabled = !disabled;
                txtFraming.Enabled = !disabled;
            };
            Controls.Add(chkKeepTypes);
            y += 26;

            // ── Include non-structural geometry ──
            chkIncludeGeneric = new CheckBox
            {
                Text = "Also detect non-structural geometry (IFC, generic models, masses)",
                Location = new Point(margin + 5, y),
                Size = new Size(GroupW - 10, 22),
                Checked = IncludeGenericGeometry
            };
            Controls.Add(chkIncludeGeneric);
            y += 26;

            // ── Include imported CAD ──
            chkIncludeCAD = new CheckBox
            {
                Text = "Also detect linked CAD geometry (DWG, DGN, SAT — STEP/STP via AutoCAD)",
                Location = new Point(margin + 5, y),
                Size = new Size(GroupW - 10, 22),
                Checked = IncludeImportedCAD
            };
            Controls.Add(chkIncludeCAD);
            y += 30;

            // ── Buttons (right-aligned with 10px gap) ──
            // Form width 540, margin 15 → Cancel right edge at 525.
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(450, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);

            var btnOK = new Button
            {
                Text = "Sync Rod Lengths",
                DialogResult = DialogResult.OK,
                Location = new Point(330, y),
                Size = new Size(110, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            TypeCodeFloors = txtFloors.Text.Trim();
            TypeCodeStairs = txtStairs.Text.Trim();
            TypeCodeRoofs = txtRoofs.Text.Trim();
            TypeCodeFraming = txtFraming.Text.Trim();
            KeepHangerTypes = chkKeepTypes.Checked;
            IncludeGenericGeometry = chkIncludeGeneric.Checked;
            IncludeImportedCAD = chkIncludeCAD.Checked;

            // Remember for next time.
            DialogMemory.Set(MemKey, "Floors", TypeCodeFloors);
            DialogMemory.Set(MemKey, "Stairs", TypeCodeStairs);
            DialogMemory.Set(MemKey, "Roofs", TypeCodeRoofs);
            DialogMemory.Set(MemKey, "Framing", TypeCodeFraming);
            DialogMemory.SetBool(MemKey, "KeepTypes", KeepHangerTypes);
            DialogMemory.SetBool(MemKey, "IncludeGeneric", IncludeGenericGeometry);
            DialogMemory.SetBool(MemKey, "IncludeCAD", IncludeImportedCAD);
            DialogMemory.Flush();
        }
    }
}

