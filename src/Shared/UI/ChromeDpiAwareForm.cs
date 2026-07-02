using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SgRevitAddin
{
    /// <summary>
    /// A <see cref="DpiAwareForm"/> with a custom SG-blue title bar instead of the
    /// OS caption. Borderless (FormBorderStyle.None); the top strip is an
    /// <see cref="HeaderColor"/> band with the dialog title in white and a close
    /// (✕) button, and the band is draggable to move the window.
    ///
    /// Inherits all of DpiAwareForm's PMv2 + auto-scale behavior unchanged (it does
    /// NOT override CreateHandle / ShowDialog). Fixed-size (AllowResize=false) to
    /// keep the custom chrome simple and robust.
    ///
    /// DERIVED DIALOGS: add content directly to the form, but start the vertical
    /// layout at <see cref="HeaderHeight"/> + margin so it clears the title band,
    /// and size ClientSize.Height to include the header. Everything is authored in
    /// logical 96-dpi px; the base auto-scale pass scales it, and the header band /
    /// fonts are re-stamped to the exact device size in OnHandleCreated.
    /// </summary>
    public class ChromeDpiAwareForm : DpiAwareForm
    {
        /// <summary>Logical (96-dpi) height of the blue title band.</summary>
        protected const int HeaderHeight = 30;

        /// <summary>SG brand blue — the same #085990 the ribbon accent uses
        /// (RibbonStyling.AccentColor is a WPF color, so we restate it here as a
        /// System.Drawing color).</summary>
        protected static readonly Color HeaderColor = Color.FromArgb(0x08, 0x59, 0x90);
        private static readonly Color HeaderHover = Color.FromArgb(0x2A, 0x74, 0xAD);

        private Panel _header;
        private Label _titleLabel;
        private Button _closeBtn;

        protected override bool UseOsBorder => false;

        public ChromeDpiAwareForm()
        {
            AllowResize = false;      // fixed-size custom chrome
            RememberSize = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;

            _header = new Panel { BackColor = HeaderColor, Location = new Point(0, 0) };

            _titleLabel = new Label
            {
                AutoSize = false,
                Text = Text,
                ForeColor = Color.White,
                BackColor = HeaderColor,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _titleLabel.MouseDown += Header_MouseDown;

            _closeBtn = new Button
            {
                Text = "✕",
                ForeColor = Color.White,
                BackColor = HeaderColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                TabStop = false
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.FlatAppearance.MouseOverBackColor = HeaderHover;
            _closeBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _header.MouseDown += Header_MouseDown;
            _header.Controls.Add(_titleLabel);
            _header.Controls.Add(_closeBtn);
            Controls.Add(_header);

            TextChanged += (s, e) => { if (_titleLabel != null) _titleLabel.Text = Text; };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);   // PMv2 auto-scale + (no) autoflex

            // Re-stamp the header band + fonts to the exact device size. Setting
            // absolute values is idempotent whether or not auto-scale already
            // touched them.
            float f = DeviceDpi / 96f;
            int hh = (int)Math.Round(HeaderHeight * f);

            _header.Bounds = new Rectangle(0, 0, ClientSize.Width, hh);
            _closeBtn.Bounds = new Rectangle(_header.Width - hh, 0, hh, hh);
            _titleLabel.Bounds = new Rectangle((int)Math.Round(10 * f), 0,
                Math.Max(0, _header.Width - hh - (int)Math.Round(14 * f)), hh);

            _titleLabel.Font = new Font(_titleLabel.Font.FontFamily, 13f * f, FontStyle.Bold, GraphicsUnit.Pixel);
            _closeBtn.Font = new Font(_closeBtn.Font.FontFamily, 12f * f, FontStyle.Regular, GraphicsUnit.Pixel);
            _titleLabel.Text = Text;
            _header.BringToFront();
        }

        // ── Drag the header to move the window (OS-driven move loop) ──
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void Header_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
        }

        // Subtle drop shadow so the borderless dialog reads as a window.
        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }
    }
}
