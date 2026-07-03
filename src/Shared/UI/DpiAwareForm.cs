using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin
{
    /// <summary>
    /// Base for all hand-coded WinForms dialogs. Gives every dialog:
    ///   • correct scaling on &gt;100% display scaling (PMv2 + re-fired auto-scale),
    ///   • a custom SG-blue title bar with a replaceable square logo, the dialog
    ///     title, and a close button (borderless — the OS caption is replaced),
    ///   • resizable + remembered size, with a MinimumSize floor at the natural
    ///     (design) size so dialogs can only be enlarged — never shrunk into the
    ///     button/panel overlap that a smaller-than-designed size would cause.
    ///
    /// Derived dialogs are written exactly as before — add controls to the form at
    /// absolute positions and set ClientSize to the CONTENT size (no header). At
    /// handle-creation the base builds the header, reparents that content into a
    /// fill panel below it, and grows the form by the header height so nothing is
    /// clipped. Set <see cref="Text"/> for the title.
    ///
    /// The logo is an embedded "logo.png" (square) — replace that file to rebrand.
    /// </summary>
    public class DpiAwareForm : Form
    {
        private bool _chromeInit;
        private bool _layoutInit;

        /// <summary>Set false in a derived ctor to keep the dialog fixed-size.</summary>
        protected bool AllowResize { get; set; } = true;
        /// <summary>Set false in a derived ctor to skip remembering the size.</summary>
        protected bool RememberSize { get; set; } = true;

        /// <summary>Logical (96-dpi) height of the blue title band.</summary>
        protected const int HeaderHeight = 38;

        private static readonly Color HeaderColor = Color.FromArgb(0x08, 0x59, 0x90);  // SG blue #085990
        private static readonly Color HeaderHover = Color.FromArgb(0x2A, 0x74, 0xAD);
        private static Image _logoImage;
        private static bool _logoTried;

        private Panel _header;
        private Panel _content;
        private Label _titleLabel;
        private Button _closeBtn;
        private PictureBox _logo;
        private int _resizeBorder = 6;

        public DpiAwareForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;   // custom SG chrome replaces the OS caption
            // AutoScaleDimensions is set in OnHandleCreated, NOT here.
        }

        protected override void CreateHandle()
        {
            using (DpiContext.PerMonitorV2())
                base.CreateHandle();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_chromeInit) return;      // guard against handle recreation
            _chromeInit = true;

            float factor = DeviceDpi / 96f;

            // (1) DPI scale FIRST — re-fire the swallowed pass at the real DPI.
            if (DeviceDpi != 96)
            {
                AutoScaleDimensions = SizeF.Empty;
                AutoScaleDimensions = new SizeF(96f, 96f);
                PerformAutoScale();
            }

            int hh = (int)Math.Round(HeaderHeight * factor);
            _resizeBorder = Math.Max(4, (int)Math.Round(6 * factor));

            // (2) The derived dialog's content is currently direct children of the
            //     form at their design (now scaled) positions. Capture that content
            //     size, build the header, and reparent the content into a fill panel
            //     below the header.
            Size natural = ClientSize;

            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(_content);
            BuildHeader(hh);

            foreach (var c in Controls.Cast<Control>().Where(c => c != _content && c != _header).ToList())
                c.Parent = _content;      // preserves bounds + anchors, now relative to _content

            // (3) Grow the form by the header so the content panel keeps its size.
            FormBorderStyle = FormBorderStyle.None;   // override any FixedDialog a derived ctor set
            ClientSize = new Size(natural.Width, natural.Height + hh);

            // (4) Flex content on resize (widen-only + bottom-pinned buttons).
            if (AllowResize) ApplyAutoFlex(_content);

            // (5) Floor at the natural size so it can only be ENLARGED — never shrunk
            //     into overlap. Fixed dialogs are locked to that size.
            MinimumSize = Size;
            if (!AllowResize) MaximumSize = Size;

            // (6) Restore remembered size (logical 96-px -> device px), clamped.
            if (RememberSize && AllowResize)
            {
                string key = GetType().Name;
                int w = DialogMemory.GetInt(key, "WinW", 0);
                int h = DialogMemory.GetInt(key, "WinH", 0);
                if (w > 0 && h > 0)
                    Size = new Size(
                        Math.Max((int)Math.Round(w * factor), MinimumSize.Width),
                        Math.Max((int)Math.Round(h * factor), MinimumSize.Height));
            }
            _layoutInit = true;
        }

        private void BuildHeader(int hh)
        {
            _header = new Panel { Dock = DockStyle.Top, Height = hh, BackColor = HeaderColor };
            _header.MouseDown += Header_MouseDown;

            _titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = Text,
                ForeColor = Color.White,
                BackColor = HeaderColor,
                Font = new Font("Segoe UI", (float)(hh * 0.42), FontStyle.Bold, GraphicsUnit.Pixel),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            _titleLabel.MouseDown += Header_MouseDown;

            _closeBtn = new Button
            {
                Dock = DockStyle.Right,
                Width = hh,
                Text = "✕",
                ForeColor = Color.White,
                BackColor = HeaderColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", (float)(hh * 0.34), FontStyle.Regular, GraphicsUnit.Pixel),
                TabStop = false
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.FlatAppearance.MouseOverBackColor = HeaderHover;
            _closeBtn.Click += (s, e) =>
            {
                if (CancelButton is Button cb) cb.PerformClick();   // run any Cancel-side logic
                else { DialogResult = DialogResult.Cancel; Close(); }
            };

            // Fill added first (resolves last); logo (Left) + close (Right) claim edges.
            _header.Controls.Add(_titleLabel);

            Image logo = LoadLogo();
            if (logo != null)
            {
                _logo = new PictureBox
                {
                    Dock = DockStyle.Left,
                    Width = hh,
                    Image = logo,
                    SizeMode = PictureBoxImageMode(),
                    BackColor = HeaderColor,
                    Padding = new Padding((int)(hh * 0.12))
                };
                _logo.MouseDown += Header_MouseDown;
                _header.Controls.Add(_logo);
            }

            _header.Controls.Add(_closeBtn);
            Controls.Add(_header);   // Top docked after Content

            TextChanged += (s, e) => { if (_titleLabel != null) _titleLabel.Text = Text; };
        }

        private static PictureBoxSizeMode PictureBoxImageMode() => PictureBoxSizeMode.Zoom;

        private static Image LoadLogo()
        {
            if (_logoTried) return _logoImage;
            _logoTried = true;
            try
            {
                var asm = typeof(DpiAwareForm).Assembly;
                string name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(".Icons.logo.png", StringComparison.OrdinalIgnoreCase)
                                      || n.EndsWith(".logo.png", StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using (var s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (var tmp = Image.FromStream(s))
                        _logoImage = new Bitmap(tmp);   // copy so the stream can close
                }
            }
            catch { _logoImage = null; }
            return _logoImage;
        }

        /// <summary>
        /// Recursively set Anchors so resizing flexes the content: wide controls
        /// (≥55% of parent width) widen with the window, and buttons pin to the
        /// bottom (and nearer horizontal edge). Widen-only keeps stacked sections
        /// from overlapping on resize.
        /// </summary>
        private void ApplyAutoFlex(Control parent)
        {
            int pw = parent.ClientSize.Width;
            if (pw <= 0) return;
            foreach (Control c in parent.Controls)
            {
                if (c is Button btn)
                {
                    bool right = (btn.Left + btn.Width / 2) * 2 >= pw;
                    btn.Anchor = AnchorStyles.Bottom | (right ? AnchorStyles.Right : AnchorStyles.Left);
                    continue;
                }

                bool wide = c.Width >= pw * 0.55;
                if (wide && (c is GroupBox || c is Panel || c is ComboBox || c is TextBox ||
                             c is DataGridView || c is ListBox || c is ListView || c is TreeView ||
                             c is Label || c is CheckBox || c is RadioButton ||
                             c is FlowLayoutPanel || c is TableLayoutPanel))
                {
                    c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                }

                if (c is GroupBox || c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel)
                    ApplyAutoFlex(c);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_layoutInit && RememberSize && AllowResize && WindowState == FormWindowState.Normal)
            {
                try
                {
                    float factor = DeviceDpi / 96f;
                    string key = GetType().Name;
                    DialogMemory.SetInt(key, "WinW", (int)Math.Round(Width / factor));
                    DialogMemory.SetInt(key, "WinH", (int)Math.Round(Height / factor));
                    DialogMemory.Flush();
                }
                catch { }
            }
            base.OnFormClosing(e);
        }

        // ── Drag the header to move the window ──
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private const int WM_NCHITTEST = 0x84;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
            HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void Header_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && AllowResize)
            {
                base.WndProc(ref m);
                Point pos = PointToClient(new Point(m.LParam.ToInt32()));
                int b = _resizeBorder;
                int hh = _header != null ? _header.Height : 0;
                bool left = pos.X <= b, right = pos.X >= ClientSize.Width - b;
                bool bottom = pos.Y >= ClientSize.Height - b;
                // No TOP-edge resize — the blue header owns the top (drag zone), and
                // side resize only BELOW the header so header clicks aren't eaten.
                bool belowHeader = pos.Y >= hh;
                if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                else if (left && belowHeader) m.Result = (IntPtr)HTLEFT;
                else if (right && belowHeader) m.Result = (IntPtr)HTRIGHT;
                return;
            }
            base.WndProc(ref m);
        }

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

        // Wrap both ShowDialog overloads so the whole modal show runs PMv2.
        public new DialogResult ShowDialog()
        {
            using (DpiContext.PerMonitorV2())
                return base.ShowDialog();
        }

        public new DialogResult ShowDialog(IWin32Window owner)
        {
            using (DpiContext.PerMonitorV2())
                return base.ShowDialog(owner);
        }
    }

    /// <summary>
    /// Per-thread DPI awareness override. Sets the calling thread to
    /// PER_MONITOR_AWARE_V2 for the lifetime of the scope and restores the
    /// previous context on Dispose. Safe no-op on OSes older than Windows 10 1703
    /// or if the user32 entry points are missing, so it can never crash a command.
    /// </summary>
    internal sealed class DpiContext : IDisposable
    {
        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        private readonly IntPtr _previous;
        private readonly bool _restore;

        private DpiContext()
        {
            _restore = false;
            _previous = IntPtr.Zero;
            try
            {
                if (!IsValidDpiAwarenessContext(PerMonitorAwareV2))
                    return;

                IntPtr prev = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
                if (prev != IntPtr.Zero)
                {
                    _previous = prev;
                    _restore = true;
                }
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        public static DpiContext PerMonitorV2() => new DpiContext();

        public void Dispose()
        {
            if (!_restore) return;
            try { SetThreadDpiAwarenessContext(_previous); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsValidDpiAwarenessContext(IntPtr dpiContext);
    }
}
