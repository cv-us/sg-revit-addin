using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SgRevitAddin
{
    /// <summary>
    /// A <see cref="DpiAwareForm"/> with a custom SG-blue title bar instead of the
    /// OS caption. Borderless (FormBorderStyle.None); the top strip is a docked
    /// <see cref="HeaderColor"/> band with the dialog title in white and a close
    /// (✕) button, draggable to move the window. Content goes in <see cref="Content"/>
    /// (a docked-fill panel below the band).
    ///
    /// The header is Dock=Top so it always spans the full client width (no gap on
    /// the right). Inherits DpiAwareForm's PMv2 + auto-scale behavior unchanged.
    /// Fixed-size to keep the custom chrome simple.
    /// </summary>
    public class ChromeDpiAwareForm : DpiAwareForm
    {
        /// <summary>Logical (96-dpi) height of the blue title band.</summary>
        protected const int HeaderHeight = 30;

        /// <summary>SG brand blue — the same #085990 the ribbon accent uses.</summary>
        protected static readonly Color HeaderColor = Color.FromArgb(0x08, 0x59, 0x90);
        private static readonly Color HeaderHover = Color.FromArgb(0x2A, 0x74, 0xAD);

        private Panel _header;
        private Label _titleLabel;
        private Button _closeBtn;
        private Panel _content;

        /// <summary>Add dialog content here — it fills the area below the title band.</summary>
        protected Panel Content => _content;

        protected override bool UseOsBorder => false;

        public ChromeDpiAwareForm()
        {
            AllowResize = false;      // fixed-size custom chrome
            RememberSize = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;

            // Content fills below the header. Add it FIRST so the Top-docked header
            // claims the top strip and Content fills the remainder.
            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(_content);

            _header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = HeaderColor };
            _header.MouseDown += Header_MouseDown;

            _titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = Text,
                ForeColor = Color.White,
                BackColor = HeaderColor,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            _titleLabel.MouseDown += Header_MouseDown;

            _closeBtn = new Button
            {
                Dock = DockStyle.Right,
                Width = HeaderHeight,
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

            // Fill added first, Right (close) added last so it claims the right edge.
            _header.Controls.Add(_titleLabel);
            _header.Controls.Add(_closeBtn);
            Controls.Add(_header);   // Top docked after Content

            TextChanged += (s, e) => { if (_titleLabel != null) _titleLabel.Text = Text; };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);   // PMv2 auto-scale

            // Re-stamp band height + fonts to the exact device size (docking handles width).
            float f = DeviceDpi / 96f;
            int hh = (int)Math.Round(HeaderHeight * f);
            _header.Height = hh;
            _closeBtn.Width = hh;
            _titleLabel.Font = new Font(_titleLabel.Font.FontFamily, 13f * f, FontStyle.Bold, GraphicsUnit.Pixel);
            _closeBtn.Font = new Font(_closeBtn.Font.FontFamily, 12f * f, FontStyle.Regular, GraphicsUnit.Pixel);
            _titleLabel.Text = Text;
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
