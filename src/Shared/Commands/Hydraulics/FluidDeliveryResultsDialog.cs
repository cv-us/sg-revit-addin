using Autodesk.Revit.UI;
using System;
using System.Drawing;
using System.Windows.Forms;
using TextBox = System.Windows.Forms.TextBox;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace SgRevitAddin.Commands.Hydraulics
{
    /// <summary>
    /// Read-only review of a fluid-delivery result: a headline pass/fail banner,
    /// the full monospace breakdown, and a "Save PDF…" button.
    /// </summary>
    public class FluidDeliveryResultsDialog : DpiAwareForm
    {
        public FluidDeliveryResultsDialog(string headline, bool pass, string body, Action savePdf)
        {
            AllowResize = true;
            RememberSize = true;
            Text = "Fluid Delivery — Result";
            Font = new Font("Segoe UI", 9f);
            const int W = 640;
            ClientSize = new Size(W, 560);

            var banner = new Label
            {
                Text = headline,
                Left = 14,
                Top = 12,
                Width = W - 28,
                Height = 30,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = pass ? Color.FromArgb(0x1B, 0x7A, 0x30) : Color.FromArgb(0xB1, 0x1A, 0x1A),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(banner);

            var box = new TextBox
            {
                Left = 14,
                Top = 48,
                Width = W - 28,
                Height = 456,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                Text = body,
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            box.Select(0, 0);
            Controls.Add(box);

            var btnPdf = new Button { Text = "Save PDF…", Left = W - 28 - 210, Top = 516, Width = 110, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnPdf.Click += (s, e) => { try { savePdf?.Invoke(); } catch (Exception ex) { TaskDialog.Show("Fluid Delivery", "PDF export failed:\n" + ex.Message); } };
            var btnClose = new Button { Text = "Close", Left = W - 28 - 90, Top = 516, Width = 90, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnClose.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(btnPdf);
            Controls.Add(btnClose);
            AcceptButton = btnClose;
        }
    }
}
