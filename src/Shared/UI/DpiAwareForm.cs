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

        /// <summary>
        /// Logical (96-dpi) breathing room added at the right + bottom of every
        /// dialog on top of its designed content size, so controls (especially the
        /// action buttons) never hug the window edge. Applied once at layout.
        /// </summary>
        protected const int BreathingRoom = 14;

        /// <summary>Corner-radius fraction of the header height — used for the header
        /// logo/close inset and the Win10 region fallback. On Windows 11 the actual
        /// rounding is the OS's own (smooth, anti-aliased) radius.</summary>
        protected const double CornerRadiusFraction = 0.21;

        /// <summary>Sentinel for "no remembered window position".</summary>
        private const int UnsetPos = int.MinValue;

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
        private int _cornerRadius;

        public DpiAwareForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;   // custom SG chrome replaces the OS caption
            SetStyle(ControlStyles.ResizeRedraw, true);   // repaint the border while dragging
            // AutoScaleDimensions is set in OnHandleCreated, NOT here.
        }

        /// <summary>
        /// Thin anti-aliased black border traced around the whole window, following the
        /// rounded corners (whether DWM- or region-rounded).
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = ClientSize.Width, h = ClientSize.Height, r = _cornerRadius;
            if (w <= 2 || h <= 2) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = RoundedRectPath(0, 0, w - 1, h - 1, r))
            using (var pen = new Pen(Color.Black, 1f))
                e.Graphics.DrawPath(pen, path);
        }

        /// <summary>Builds a rounded-rectangle path; a plain rectangle when r &lt;= 1.</summary>
        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(int x, int y, int w, int h, int r)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            if (r <= 1 || w <= r * 2 || h <= r * 2)
            {
                path.AddRectangle(new Rectangle(x, y, w, h));
                return path;
            }
            int d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
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
            // Resize gutter: the strip of FORM-owned pixels left around the content
            // panel so the borderless window can hit-test its own edges (a Dock=Fill
            // child eats every edge pixel, which is why resize did nothing). Wide
            // enough to grab comfortably.
            _resizeBorder = Math.Max(6, (int)Math.Round(8 * factor));

            // (2) Measure the dialog's own controls at their design (now scaled)
            //     positions — the furthest right/bottom edge — so the window opens
            //     large enough to show everything even when the ctor ClientSize was
            //     trimmed tighter than the content (AutoScroll would otherwise hide
            //     the overflow behind a scrollbar). Only the dialog's controls exist
            //     on the form at this point (no header/content panel yet).
            Size natural = ClientSize;
            int contentR = natural.Width, contentB = natural.Height;
            foreach (Control c in Controls)
            {
                if (c.Right > contentR) contentR = c.Right;
                if (c.Bottom > contentB) contentB = c.Bottom;
            }

            int pad = (int)Math.Round(BreathingRoom * factor);
            int gutter = _resizeBorder;
            int cw = contentR + pad;
            int ch = contentB + pad;

            // (3) Build the chrome: full-width blue header on top; a content panel
            //     INSET by the resize gutter on left/right/bottom (the header owns the
            //     top), so those edge strips belong to the form for resize hit-tests.
            //     Grow the form, create the content panel ALREADY at its final size,
            //     THEN reparent the controls into it — so their Bottom/Right anchor
            //     baselines are computed against the correct size. (Reparenting into a
            //     temporarily short panel was giving bottom buttons a wrong baseline
            //     and clipping them under the bottom edge.)
            _cornerRadius = (int)Math.Round(hh * CornerRadiusFraction);   // ~25% of the header height
            FormBorderStyle = FormBorderStyle.None;   // override any FixedDialog a derived ctor set
            BuildHeader(hh);
            ClientSize = new Size(cw + 2 * gutter, hh + ch + gutter);

            _content = new Panel
            {
                AutoScroll = true,
                Bounds = new Rectangle(gutter, hh, cw, ch),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_content);

            foreach (var c in Controls.Cast<Control>().Where(c => c != _content && c != _header).ToList())
                c.Parent = _content;      // final-size panel → correct anchor baselines

            // (4) Flex content on resize (widen inputs + bottom-pinned buttons).
            if (AllowResize) ApplyAutoFlex(_content);

            // (5) Floor at the natural size so it can only be ENLARGED — never shrunk
            //     into overlap. Fixed dialogs are locked to that size.
            MinimumSize = Size;
            if (!AllowResize) MaximumSize = Size;

            string key = GetType().Name;

            // (6) Restore remembered size (logical 96-px -> device px), clamped
            //     between the natural size and the current screen's work area.
            if (RememberSize && AllowResize)
            {
                int w = DialogMemory.GetInt(key, "WinW", 0);
                int h = DialogMemory.GetInt(key, "WinH", 0);
                if (w > 0 && h > 0)
                {
                    var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                    Size = new Size(
                        Math.Min(Math.Max((int)Math.Round(w * factor), MinimumSize.Width), wa.Width),
                        Math.Min(Math.Max((int)Math.Round(h * factor), MinimumSize.Height), wa.Height));
                }
            }

            // (7) Position: reopen where the user last left it (if that spot is
            //     still reachable on a connected monitor), else center on the
            //     screen under the cursor. Done AFTER the final size is known.
            ApplyStartupPosition(key);

            // (8) Round the window corners.
            ApplyRoundedCorners();

            _layoutInit = true;
        }

        // ── Rounded corners ──
        // Windows 11: the OS compositor rounds + anti-aliases the corners and draws a
        // matching border (DwmSetWindowAttribute) — smooth, and the drop shadow follows
        // the rounded shape. GPU-composited, so it's cheaper than region clipping and
        // needs no per-resize work. Windows 10: fall back to an aliased clip region.
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private bool _dwmRounded;

        private void ApplyRoundedCorners()
        {
            _dwmRounded = false;
            try
            {
                int pref = DWMWCP_ROUND;
                if (DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0)
                {
                    _dwmRounded = true;
                    Region = null;   // let the DWM shape + drop shadow stay smooth
                }
            }
            catch { _dwmRounded = false; }

            if (!_dwmRounded) ApplyRoundedRegion();
            Invalidate();   // repaint the border for the (now-known) corner style
        }

        /// <summary>Win10 fallback: clip the window to a rounded rectangle (aliased).</summary>
        private void ApplyRoundedRegion()
        {
            int r = _cornerRadius;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (r <= 1 || w <= r * 2 || h <= r * 2) { Region = null; return; }
            using (var path = RoundedRectPath(0, 0, w, h, r))
                Region = new Region(path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_layoutInit && !_dwmRounded) ApplyRoundedRegion();   // DWM handles resize itself
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
                Padding = new Padding(8, 0, 0, 0),
                AutoEllipsis = true   // long titles fade to "…" instead of clipping mid-glyph
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
                // Equal margin on the left, top and bottom of the logo image. The
                // control is sized to the logo's aspect so Zoom fills it exactly
                // (no extra centring gap), keeping those three margins identical.
                int g = Math.Max(3, _cornerRadius);
                int imgH = Math.Max(1, hh - 2 * g);
                int imgW = Math.Max(1, (int)Math.Round(imgH * (double)logo.Width / logo.Height));
                _logo = new PictureBox
                {
                    Dock = DockStyle.Left,
                    Width = imgW + 2 * g,
                    Image = logo,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = HeaderColor,
                    Padding = new Padding(g)
                };
                _logo.MouseDown += Header_MouseDown;
                _header.Controls.Add(_logo);
            }

            _header.Controls.Add(_closeBtn);
            Controls.Add(_header);   // Top docked after Content

            TextChanged += (s, e) => { if (_titleLabel != null) _titleLabel.Text = Text; };
        }


        /// <summary>
        /// Places the dialog at its remembered last position when that position is
        /// still usable (its title band lands on a connected monitor's work area),
        /// otherwise centers it on the screen under the cursor. Window position is
        /// remembered for every dialog (resizable or not); only the size floor
        /// differs. Runs with StartPosition forced to Manual so it wins over any
        /// CenterScreen/CenterParent a derived ctor set.
        /// </summary>
        private void ApplyStartupPosition(string key)
        {
            StartPosition = FormStartPosition.Manual;

            Rectangle wa;
            try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; }
            catch { wa = Screen.PrimaryScreen.WorkingArea; }

            Point loc;
            if (RememberSize)
            {
                int sx = DialogMemory.GetInt(key, "WinX", UnsetPos);
                int sy = DialogMemory.GetInt(key, "WinY", UnsetPos);
                loc = (sx != UnsetPos && sy != UnsetPos &&
                       IsTitleBandReachable(new Rectangle(sx, sy, Width, Height)))
                    ? new Point(sx, sy)
                    : new Point(wa.X + Math.Max(0, (wa.Width - Width) / 2),
                                wa.Y + Math.Max(0, (wa.Height - Height) / 2));
            }
            else
            {
                loc = new Point(wa.X + Math.Max(0, (wa.Width - Width) / 2),
                                wa.Y + Math.Max(0, (wa.Height - Height) / 2));
            }

            // Clamp so the WHOLE window fits on the work area — otherwise a large
            // remembered size + position pushed the bottom buttons / right column
            // off-screen (they were "missing"). Left/top win if the window is
            // bigger than the work area, keeping the header reachable.
            loc.X = Math.Min(loc.X, wa.Right - Width);
            loc.Y = Math.Min(loc.Y, wa.Bottom - Height);
            loc.X = Math.Max(loc.X, wa.Left);
            loc.Y = Math.Max(loc.Y, wa.Top);
            Location = loc;
        }

        /// <summary>
        /// True when a usable chunk of the window's title band (top ~40px, the drag
        /// + close zone) overlaps some monitor's work area — so the user can always
        /// grab and move it. Guards against a saved position on a now-disconnected
        /// monitor.
        /// </summary>
        private static bool IsTitleBandReachable(Rectangle r)
        {
            Rectangle band = new Rectangle(r.X, r.Y, r.Width, Math.Min(40, r.Height));
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle inter = Rectangle.Intersect(s.WorkingArea, band);
                if (inter.Width >= 80 && inter.Height >= 20) return true;
            }
            return false;
        }

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
        /// Recursively set Anchors so resizing genuinely flexes the content:
        ///   • Buttons pin to the bottom (and their nearer horizontal edge).
        ///   • Any input (ComboBox / TextBox / list / grid / tree / numeric) or
        ///     wide container that has NOTHING to its right widens with the window
        ///     (Left|Right) — it stretches into the empty space, so dropdowns and
        ///     text fields actually get wider when the user enlarges the dialog.
        ///   • A control with a right-hand neighbour (the second column of a
        ///     two-column row) is left fixed so the columns never overlap.
        /// Widen-only (MinimumSize floors the shrink) keeps stacked sections from
        /// overlapping on resize.
        ///
        /// Controls whose Anchor was EXPLICITLY set by the dialog (anything other
        /// than the Top|Left default) are left untouched — that's how a dialog
        /// opts its primary list/grid into vertical growth (Top|Left|Right|Bottom),
        /// or a two-column layout into proportional widening, without this pass
        /// clobbering it.
        /// </summary>
        private void ApplyAutoFlex(Control parent)
        {
            int pw = parent.ClientSize.Width;
            if (pw <= 0) return;

            var kids = parent.Controls.Cast<Control>().ToList();
            foreach (Control c in kids)
            {
                bool explicitAnchor = c.Anchor != (AnchorStyles.Top | AnchorStyles.Left);
                if (explicitAnchor)
                {
                    // Deliberate anchor — respect it, but still flex the children
                    // of container controls.
                    if (IsContainer(c)) ApplyAutoFlex(c);
                    continue;
                }

                if (c is Button btn)
                {
                    if (HasLeftInputNeighbour(btn, kids))
                    {
                        // Trailing action button (Browse / Add / … beside a field):
                        // ride the right edge next to its field, not down to the
                        // bottom corner — and the field to its left is freed to widen.
                        btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    }
                    else
                    {
                        bool right = (btn.Left + btn.Width / 2) * 2 >= pw;
                        btn.Anchor = AnchorStyles.Bottom | (right ? AnchorStyles.Right : AnchorStyles.Left);
                    }
                    continue;
                }

                bool roomToRight = !HasRightNeighbour(c, kids);
                bool flexHoriz = false;

                if (IsContainer(c))
                {
                    // Widen a container that spans most of the row, or that simply
                    // has open space to its right (single-column group).
                    flexHoriz = roomToRight && c.Width >= pw * 0.45;
                }
                else if (c is ComboBox || c is TextBox || c is DataGridView ||
                         c is ListBox || c is ListView || c is TreeView || c is NumericUpDown)
                {
                    // Inputs stretch into empty space to their right, but only when
                    // they're already a substantial field (never balloon a tiny
                    // 2-char type-code box across the whole dialog).
                    flexHoriz = roomToRight && c.Width >= pw * 0.33;
                }
                else if (c is Label || c is CheckBox || c is RadioButton)
                {
                    flexHoriz = roomToRight && c.Width >= pw * 0.55;
                }

                if (flexHoriz)
                    c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                if (IsContainer(c)) ApplyAutoFlex(c);
            }
        }

        private static bool IsContainer(Control c) =>
            c is GroupBox || c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel;

        /// <summary>
        /// True when another sibling sits to the right of <paramref name="c"/> in
        /// the same horizontal band — i.e. widening <paramref name="c"/> would run
        /// it into that neighbour. Used to keep two-column rows from overlapping.
        /// A trailing <see cref="Button"/> is NOT counted: it gets a Top|Right
        /// anchor and rides the right edge, so the field to its left can safely
        /// widen into the space between them (constant gap preserved).
        /// </summary>
        private static bool HasRightNeighbour(Control c, System.Collections.Generic.List<Control> siblings)
        {
            foreach (Control s in siblings)
            {
                if (ReferenceEquals(s, c)) continue;
                if (s is Button) continue;
                bool verticalOverlap = s.Top < c.Bottom && s.Bottom > c.Top;
                if (verticalOverlap && s.Left >= c.Right - 4) return true;
            }
            return false;
        }

        /// <summary>
        /// True when an input control (combo / text / numeric / list) ends just to
        /// the left of <paramref name="btn"/> on the same horizontal band — marking
        /// <paramref name="btn"/> as a trailing action button (Browse / Add / …)
        /// rather than a bottom-row action button. Requires proximity so a bottom
        /// OK/Cancel button that merely shares a Y band with a far-left field isn't
        /// misread as trailing.
        /// </summary>
        private static bool HasLeftInputNeighbour(Control btn, System.Collections.Generic.List<Control> siblings)
        {
            foreach (Control s in siblings)
            {
                if (ReferenceEquals(s, btn)) continue;
                bool isInput = s is ComboBox || s is TextBox || s is NumericUpDown ||
                               s is ListBox || s is ListView || s is DataGridView || s is TreeView;
                if (!isInput) continue;
                bool verticalOverlap = s.Top < btn.Bottom && s.Bottom > btn.Top;
                if (verticalOverlap && s.Right <= btn.Left + 4 && s.Right >= btn.Left - 48)
                    return true;
            }
            return false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_layoutInit && RememberSize && WindowState == FormWindowState.Normal)
            {
                try
                {
                    string key = GetType().Name;

                    // Position is remembered for every dialog (physical px — a
                    // screen coordinate, validated for reachability on restore).
                    DialogMemory.SetInt(key, "WinX", Location.X);
                    DialogMemory.SetInt(key, "WinY", Location.Y);

                    // Size only for resizable dialogs (logical px, since size
                    // scales with DPI; fixed dialogs always reopen at design size).
                    if (AllowResize)
                    {
                        float factor = DeviceDpi / 96f;
                        DialogMemory.SetInt(key, "WinW", (int)Math.Round(Width / factor));
                        DialogMemory.SetInt(key, "WinH", (int)Math.Round(Height / factor));
                    }

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

        private DateTime _lastHeaderClick = DateTime.MinValue;

        private void Header_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            // Manual double-click detection: the drag loop below swallows the
            // WinForms DoubleClick event, so time the clicks ourselves.
            // Double-clicking the header snaps the dialog back to its natural
            // (design) size — the escape hatch for an awkward remembered size.
            var now = DateTime.Now;
            if ((now - _lastHeaderClick).TotalMilliseconds <= SystemInformation.DoubleClickTime)
            {
                _lastHeaderClick = DateTime.MinValue;
                if (AllowResize && MinimumSize.Width > 0)
                    Size = MinimumSize;
                return;
            }
            _lastHeaderClick = now;

            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
        }

        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_NCACTIVATE = 0x0086;

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void WndProc(ref Message m)
        {
            // WS_THICKFRAME (added in CreateParams) is what actually lets the
            // borderless window be sized and shows the resize cursor. Zero the
            // non-client calc so that sizing border isn't drawn as a visible OS
            // frame — the client area covers the whole window and our own
            // WM_NCHITTEST gutters below define the resize handles.
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero && AllowResize)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            // WS_THICKFRAME makes DefWindowProc want to draw a caption/frame in the
            // non-client area — which flashed a phantom title bar on focus change and
            // left stale, un-repainted pixels. Suppress all non-client painting:
            //  • WM_NCACTIVATE with lParam = -1 processes activation WITHOUT
            //    repainting the (non-existent) caption.
            //  • WM_NCPAINT is dropped entirely — there is no frame to draw.
            if (AllowResize && m.Msg == WM_NCACTIVATE)
            {
                m.Result = DefWindowProc(m.HWnd, m.Msg, m.WParam, new IntPtr(-1));
                return;
            }
            if (AllowResize && m.Msg == WM_NCPAINT)
            {
                m.Result = IntPtr.Zero;
                return;
            }

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
                const int WS_THICKFRAME = 0x00040000;    // sizing border — without it HTLEFT/etc. can't resize
                const int WS_EX_COMPOSITED = 0x02000000; // paint the whole window to a back buffer in one pass
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_COMPOSITED;   // fixes the splotchy "paints in pieces" first render
                if (AllowResize) cp.Style |= WS_THICKFRAME;
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
