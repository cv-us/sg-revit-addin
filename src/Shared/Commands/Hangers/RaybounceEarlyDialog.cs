using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the Raybounce Early command — the original, simple
    /// raybounce: native ReferenceIntersector straight up to structural
    /// categories (floors, stairs, roofs, framing), including linked
    /// models. No CAD/IFC mesh handling. Kept as the stable fallback while
    /// "Raybounce Dev" is refined.
    ///
    /// Collects hanger type codes per structural category and a keep-types
    /// option. Type codes and the keep-types option are remembered between
    /// runs via <see cref="DialogMemory"/> (the diagnostic option is
    /// deliberately NOT remembered — it should never be sticky).
    /// </summary>
    public class RaybounceEarlyDialog : DpiAwareForm
    {
        private const string MemKey = "RaybounceEarly";

        // ── Results ──
        public string TypeCodeFloors { get; private set; } = "05";
        public string TypeCodeStairs { get; private set; } = "02";
        public string TypeCodeRoofs { get; private set; } = "03";
        public string TypeCodeFraming { get; private set; } = "02";
        public bool KeepHangerTypes { get; private set; } = false;
        public bool Diagnostic { get; private set; } = false;

        // ── Controls ──
        private TextBox txtFloors;
        private TextBox txtStairs;
        private TextBox txtRoofs;
        private TextBox txtFraming;
        private CheckBox chkKeepTypes;
        private CheckBox chkDiagnostic;

        private readonly int _hangerCount;

        public RaybounceEarlyDialog(int hangerCount,
            string defaultFloors = "05", string defaultStairs = "02",
            string defaultRoofs = "03", string defaultFraming = "02",
            bool defaultKeepTypes = false)
        {
            _hangerCount = hangerCount;
            // Last-used values win over the passed-in defaults.
            TypeCodeFloors = DialogMemory.Get(MemKey, "Floors", defaultFloors);
            TypeCodeStairs = DialogMemory.Get(MemKey, "Stairs", defaultStairs);
            TypeCodeRoofs = DialogMemory.Get(MemKey, "Roofs", defaultRoofs);
            TypeCodeFraming = DialogMemory.Get(MemKey, "Framing", defaultFraming);
            KeepHangerTypes = DialogMemory.GetBool(MemKey, "KeepTypes", defaultKeepTypes);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Raybounce Early — Hangers to Structural";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 390);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(450, 70)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Original raybounce: shoots a ray straight up from each hanger to the\n" +
                       "structural element above (floors, stairs, roofs, framing — including\n" +
                       "linked models). Sets Rod Length to the vertical distance to the hit.",
                Location = new Point(10, 18),
                Size = new Size(430, 45)
            });
            Controls.Add(grpInfo);
            y += 80;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(450, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_hangerCount} pipe hanger{(_hangerCount != 1 ? "s" : "")} selected.",
                Location = new Point(10, 20),
                Size = new Size(430, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 60;

            // ── Hanger Type Codes ──
            var grpTypes = new GroupBox
            {
                Text = "Hanger Assembly Type Codes (Hydratec)",
                Location = new Point(margin, y),
                Size = new Size(450, 124)
            };

            int lx = 10, tx = 150, tw = 60, rowH = 24;
            int gy = 20;
            grpTypes.Controls.Add(new Label { Text = "Floors:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtFloors = new TextBox { Text = TypeCodeFloors, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtFloors);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Stairs:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtStairs = new TextBox { Text = TypeCodeStairs, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtStairs);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Roofs:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtRoofs = new TextBox { Text = TypeCodeRoofs, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtRoofs);

            gy += rowH;
            grpTypes.Controls.Add(new Label { Text = "Structural Framing:", Location = new Point(lx, gy + 3), Size = new Size(130, 18) });
            txtFraming = new TextBox { Text = TypeCodeFraming, Location = new Point(tx, gy), Size = new Size(tw, 22) };
            grpTypes.Controls.Add(txtFraming);

            Controls.Add(grpTypes);
            y += 134;

            // ── Keep Types ──
            chkKeepTypes = new CheckBox
            {
                Text = "Keep existing Hanger Types and Comments — only adjust Rod Lengths",
                Location = new Point(margin + 5, y),
                Size = new Size(440, 20),
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
            // Apply the enabled state for the restored value (Checked was set
            // before the handler above was wired, so it didn't fire).
            if (chkKeepTypes.Checked)
            {
                txtFloors.Enabled = false;
                txtStairs.Enabled = false;
                txtRoofs.Enabled = false;
                txtFraming.Enabled = false;
            }
            y += 26;

            // ── Diagnostic ──
            chkDiagnostic = new CheckBox
            {
                Text = "Diagnostic: report each hanger's hit (don't change rods)",
                Location = new Point(margin + 5, y),
                Size = new Size(440, 20),
                Checked = Diagnostic
            };
            Controls.Add(chkDiagnostic);
            y += 30;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Sync Rod Lengths",
                DialogResult = DialogResult.OK,
                Location = new Point(260, y),
                Size = new Size(120, 30)
            };
            btnOK.Click += BtnOK_Click;
            AcceptButton = btnOK;
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(390, y),
                Size = new Size(75, 30)
            };
            CancelButton = btnCancel;
            Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            TypeCodeFloors = txtFloors.Text.Trim();
            TypeCodeStairs = txtStairs.Text.Trim();
            TypeCodeRoofs = txtRoofs.Text.Trim();
            TypeCodeFraming = txtFraming.Text.Trim();
            KeepHangerTypes = chkKeepTypes.Checked;
            Diagnostic = chkDiagnostic.Checked;

            // Remember for next time (Diagnostic intentionally not remembered).
            DialogMemory.Set(MemKey, "Floors", TypeCodeFloors);
            DialogMemory.Set(MemKey, "Stairs", TypeCodeStairs);
            DialogMemory.Set(MemKey, "Roofs", TypeCodeRoofs);
            DialogMemory.Set(MemKey, "Framing", TypeCodeFraming);
            DialogMemory.SetBool(MemKey, "KeepTypes", KeepHangerTypes);
            DialogMemory.Flush();
        }
    }
}
