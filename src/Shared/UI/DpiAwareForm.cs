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
            // Resize gutter: the strip of FORM-owned pixels left around the content
            // panel so the borderless window can hit-test its own edges (a Dock=Fill
            // child eats every edge pixel, which is why resize did nothing). Wide
            // enough to grab comfortably.
            _resizeBorder = Math.Max(6, (int)Math.Round(8 * factor));

            // (2) The derived dialog's content is currently direct children of the
            //     form at their design (now scaled) positions. Capture that content
            //     size, build the header, and reparent the content into a panel below
            //     the header (temporarily Dock=Fill for the reparent + measurement).
            Size natural = ClientSize;

            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(_content);
            BuildHeader(hh);

            foreach (var c in Controls.Cast<Control>().Where(c => c != _content && c != _header).ToList())
                c.Parent = _content;      // preserves bounds + anchors, now relative to _content

            // (2b) Size to the MEASURED content, not the ctor's ClientSize. After the
            //      DPI autoscale in (1), scaled control positions can exceed a tightly
            //      trimmed ClientSize; AutoScroll would then hide the overflow behind a
            //      scrollbar (the "have to scroll to reach Continue" bug). Measure the
            //      furthest control on each axis so nothing is ever clipped on first
            //      open, at any DPI.
            int contentR = natural.Width, contentB = natural.Height;
            foreach (Control c in _content.Controls)
            {
                if (c.Right > contentR) contentR = c.Right;
                if (c.Bottom > contentB) contentB = c.Bottom;
            }

            // (3) Final layout: full-width blue header on top; the content panel INSET
            //     by the resize gutter on left/right/bottom (the header owns the top),
            //     so those edge strips belong to the form and can receive resize
            //     hit-tests. Breathing pad keeps controls off the panel's own edge.
            int pad = (int)Math.Round(BreathingRoom * factor);
            int gutter = _resizeBorder;
            int cw = contentR + pad;
            int ch = contentB + pad;

            FormBorderStyle = FormBorderStyle.None;   // override any FixedDialog a derived ctor set
            _content.Dock = DockStyle.None;
            _content.Location = new Point(gutter, hh);
            _content.Size = new Size(cw, ch);
            _content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ClientSize = new Size(cw + 2 * gutter, hh + ch + gutter);

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

            if (RememberSize)
            {
                int sx = DialogMemory.GetInt(key, "WinX", UnsetPos);
                int sy = DialogMemory.GetInt(key, "WinY", UnsetPos);
                if (sx != UnsetPos && sy != UnsetPos &&
                    IsTitleBandReachable(new Rectangle(sx, sy, Width, Height)))
                {
                    Location = new Point(sx, sy);
                    return;
                }
            }

            CenterOnCursorScreen();
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

        private void CenterOnCursorScreen()
        {
            Rectangle wa;
            try { wa = Screen.FromPoint(Cursor.Position).WorkingArea; }
            catch { wa = Screen.PrimaryScreen.WorkingArea; }
            Location = new Point(
                wa.X + Math.Max(0, (wa.Width - Width) / 2),
                wa.Y + Math.Max(0, (wa.Height - Height) / 2));
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
