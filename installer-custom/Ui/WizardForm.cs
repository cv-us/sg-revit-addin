using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SgSetup.Ui
{
    /// <summary>
    /// The installer window — a borderless form carrying the same SG chrome as the
    /// add-in dialogs: a blue title band with the company logo, DWM-rounded corners,
    /// a drop shadow and a thin black border. It hosts a swappable "page" panel in the
    /// middle with Back / Next / Cancel below.
    /// </summary>
    public class WizardForm : Form
    {
        public static readonly Color SgBlue = Color.FromArgb(0x08, 0x59, 0x90);
        public static readonly Color SgBlueHover = Color.FromArgb(0x2A, 0x74, 0xAD);
        private static readonly Color FooterBack = Color.FromArgb(0xF3, 0xF5, 0xF7);

        private Panel _header;
        private Label _title;
        private Button _close;
        private PictureBox _logo;
        private Panel _content;   // hosts the current page
        private Panel _footer;
        private int _cornerRadius;

        public Panel ContentHost => _content;
        public Button BackButton { get; private set; }
        public Button NextButton { get; private set; }
        public Button CancelButton2 { get; private set; }

        public WizardForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Text = "SG Revit Addin Setup";
            SetStyle(ControlStyles.ResizeRedraw, true);
            ClientSize = new Size(640, 470);
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;   // fixed-size wizard
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            float f = DeviceDpi / 96f;
            int hh = (int)Math.Round(52 * f);
            _cornerRadius = (int)Math.Round(10 * f);
            int footerH = (int)Math.Round(58 * f);

            BuildHeader(hh, f);
            BuildFooter(footerH, f);

            _content = new Panel
            {
                Location = new Point(0, hh),
                Size = new Size(ClientSize.Width, ClientSize.Height - hh - footerH),
                BackColor = Color.White
            };
            Controls.Add(_content);
            _content.BringToFront();

            ApplyRoundedCorners();
        }

        private void BuildHeader(int hh, float f)
        {
            _header = new Panel { Dock = DockStyle.Top, Height = hh, BackColor = SgBlue };
            _header.MouseDown += DragMove;

            _title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "SG Revit Addin  •  Setup",
                ForeColor = Color.White,
                BackColor = SgBlue,
                Font = new Font("Segoe UI", (float)(hh * 0.34), FontStyle.Bold, GraphicsUnit.Pixel),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding((int)Math.Round(10 * f), 0, 0, 0)
            };
            _title.MouseDown += DragMove;
            _header.Controls.Add(_title);

            Image logo = LoadLogo();
            if (logo != null)
            {
                int g = _cornerRadius + (int)Math.Round(4 * f);
                int imgH = Math.Max(1, hh - 2 * g);
                int imgW = Math.Max(1, (int)Math.Round(imgH * (double)logo.Width / logo.Height));
                _logo = new PictureBox
                {
                    Dock = DockStyle.Left,
                    Width = imgW + 2 * g,
                    Image = logo,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = SgBlue,
                    Padding = new Padding(g)
                };
                _logo.MouseDown += DragMove;
                _header.Controls.Add(_logo);
            }

            _close = new Button
            {
                Dock = DockStyle.Right,
                Width = hh,
                Text = "✕",
                ForeColor = Color.White,
                BackColor = SgBlue,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", (float)(hh * 0.30), FontStyle.Regular, GraphicsUnit.Pixel),
                TabStop = false
            };
            _close.FlatAppearance.BorderSize = 0;
            _close.FlatAppearance.MouseOverBackColor = SgBlueHover;
            _close.Click += (s, e) => Close();
            _header.Controls.Add(_close);

            Controls.Add(_header);
        }

        private void BuildFooter(int footerH, float f)
        {
            _footer = new Panel { Dock = DockStyle.Bottom, Height = footerH, BackColor = FooterBack };
            int bw = (int)Math.Round(96 * f), bh = (int)Math.Round(30 * f);
            int m = (int)Math.Round(14 * f), gap = (int)Math.Round(8 * f);
            int by = (footerH - bh) / 2;

            CancelButton2 = MakeFooterButton("Cancel", bw, bh);
            CancelButton2.Location = new Point(ClientSize.Width - m - bw, by);
            CancelButton2.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            NextButton = MakeFooterButton("Next", bw, bh);
            NextButton.Location = new Point(CancelButton2.Left - gap - bw, by);
            NextButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AccentButton(NextButton);

            BackButton = MakeFooterButton("Back", bw, bh);
            BackButton.Location = new Point(NextButton.Left - gap - bw, by);
            BackButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            _footer.Controls.Add(BackButton);
            _footer.Controls.Add(NextButton);
            _footer.Controls.Add(CancelButton2);

            // Hairline separator above the footer.
            _footer.Paint += (s, e) =>
            {
                using (var p = new Pen(Color.FromArgb(0xDD, 0xE1, 0xE6)))
                    e.Graphics.DrawLine(p, 0, 0, _footer.Width, 0);
            };
            Controls.Add(_footer);
        }

        private static Button MakeFooterButton(string text, int w, int h)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(0xC4, 0xCB, 0xD2);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xEC, 0xF1, 0xF5);
            return b;
        }

        private static void AccentButton(Button b)
        {
            b.BackColor = SgBlue;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = SgBlue;
            b.FlatAppearance.MouseOverBackColor = SgBlueHover;
            b.Font = new Font(b.Font, FontStyle.Bold);
        }

        /// <summary>Recolour Next as the accent (primary) or a neutral button.</summary>
        public void SetNextAccent(bool accent)
        {
            if (accent) AccentButton(NextButton);
            else
            {
                NextButton.BackColor = Color.White;
                NextButton.ForeColor = SystemColors.ControlText;
                NextButton.FlatAppearance.BorderColor = Color.FromArgb(0xC4, 0xCB, 0xD2);
                NextButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xEC, 0xF1, 0xF5);
                NextButton.Font = new Font(NextButton.Font, FontStyle.Regular);
            }
        }

        // ── Chrome painting ──
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = ClientSize.Width, h = ClientSize.Height, r = _cornerRadius;
            if (w <= 2 || h <= 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(0, 0, w - 1, h - 1, r))
            using (var pen = new Pen(Color.Black, 1f))
                e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath Rounded(int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            if (r <= 1) { path.AddRectangle(new Rectangle(x, y, w, h)); return path; }
            int d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ── DWM rounded corners (Win11) + region fallback ──
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void ApplyRoundedCorners()
        {
            try
            {
                int pref = DWMWCP_ROUND;
                if (DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0)
                {
                    Region = null;
                    return;
                }
            }
            catch { }
            using (var path = Rounded(0, 0, ClientSize.Width, ClientSize.Height, _cornerRadius))
                Region = new Region(path);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                const int WS_EX_COMPOSITED = 0x02000000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_COMPOSITED;
                return cp;
            }
        }

        // ── Drag the header ──
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void DragMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
        }

        private static Image LoadLogo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using (var s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (var tmp = Image.FromStream(s))
                        return new Bitmap(tmp);
                }
            }
            catch { return null; }
        }
    }
}
