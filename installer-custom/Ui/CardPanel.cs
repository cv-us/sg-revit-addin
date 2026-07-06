using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SgSetup.Ui
{
    /// <summary>
    /// A rounded, softly-shadowed container panel — the "card" used for the feature
    /// tiles on the welcome page. Owner-drawn to match the pill buttons. Child controls
    /// should use <see cref="Fill"/> as their BackColor so they sit seamlessly on it.
    /// </summary>
    public class CardPanel : Panel
    {
        public int Radius { get; set; } = 12;
        public Color Fill { get; set; } = Color.FromArgb(0xF6, 0xF9, 0xFB);
        public Color BorderColor { get; set; } = Color.FromArgb(0xDD, 0xE4, 0xEA);

        public CardPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);

            int sh = Math.Max(2, Radius / 3);
            var rect = new Rectangle(1, 1, Width - 3, Height - 2 - sh);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            for (int i = 0; i < sh; i++)
            {
                var sr = new Rectangle(rect.X, rect.Y + (sh - i), rect.Width, rect.Height);
                using (var path = Rounded(sr, Radius))
                using (var b = new SolidBrush(Color.FromArgb(4 + i * 2, 0, 0, 0)))
                    g.FillPath(b, path);
            }

            using (var path = Rounded(rect, Radius))
            {
                using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
                using (var pen = new Pen(BorderColor)) g.DrawPath(pen, path);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
