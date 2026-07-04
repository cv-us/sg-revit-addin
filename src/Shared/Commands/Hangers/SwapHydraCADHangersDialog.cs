using System;
using System.Drawing;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Hangers
{
    /// <summary>
    /// Dialog for the AutoSwap HydraCAD Hangers command.
    ///
    /// Shows info about the swap operation and lets the user choose whether
    /// to delete the original HydraCAD hangers after replacement. The choice
    /// is remembered between runs via <see cref="DialogMemory"/>.
    /// </summary>
    public class SwapHydraCADHangersDialog : DpiAwareForm
    {
        private const string MemKey = "SwapHydraCAD";

        // ── Results ──
        public bool DeleteOriginals { get; private set; } = true;

        // ── Controls ──
        private CheckBox chkDelete;

        private readonly int _hangerCount;

        public SwapHydraCADHangersDialog(int hangerCount)
        {
            _hangerCount = hangerCount;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "AutoSwap HydraCAD Hangers";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 312);

            int margin = 15;
            int y = margin;

            // ── Info ──
            var grpInfo = new GroupBox
            {
                Text = "About",
                Location = new Point(margin, y),
                Size = new Size(450, 120)
            };
            grpInfo.Controls.Add(new Label
            {
                Text = "Replaces HydraCAD Pipe Hangers (Adjustable Ring Hanger) with\n" +
                       "SG \"-Pipe Hanger - Standard\" family instances.\n\n" +
                       "Parameters transferred: Nominal Diameter, Rod Length, Type Code (Hydratec),\n" +
                       "HCAD-System, Elevation from Level, Additional Stocklist Information.",
                Location = new Point(10, 18),
                Size = new Size(430, 96)
            });
            Controls.Add(grpInfo);
            y += 130;

            // ── Summary ──
            var grpSummary = new GroupBox
            {
                Text = "Selection",
                Location = new Point(margin, y),
                Size = new Size(450, 50)
            };
            grpSummary.Controls.Add(new Label
            {
                Text = $"{_hangerCount} HydraCAD hanger{(_hangerCount != 1 ? "s" : "")} found in selection.",
                Location = new Point(10, 20),
                Size = new Size(430, 18),
                Font = new Font(Font, FontStyle.Bold)
            });
            Controls.Add(grpSummary);
            y += 60;

            // ── Options ──
            var grpOptions = new GroupBox
            {
                Text = "Options",
                Location = new Point(margin, y),
                Size = new Size(450, 50)
            };
            chkDelete = new CheckBox
            {
                Text = "Delete original HydraCAD hangers after creating replacements",
                Location = new Point(10, 20),
                Size = new Size(420, 20),
                Checked = DialogMemory.GetBool(MemKey, "DeleteOriginals", true)
            };
            grpOptions.Controls.Add(chkDelete);
            Controls.Add(grpOptions);
            y += 62;

            // ── Buttons ──
            var btnOK = new Button
            {
                Text = "Swap Hangers",
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
            DeleteOriginals = chkDelete.Checked;

            DialogMemory.SetBool(MemKey, "DeleteOriginals", DeleteOriginals);
            DialogMemory.Flush();
        }
    }
}

