using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SgSetup.Ui
{
    /// <summary>
    /// A flat button with a generous corner radius and a soft drop shadow, owner-drawn
    /// and anti-aliased. It blends with the parent's background (no true transparency),
    /// so the shadow reads as a floating pill on a solid footer.
    /// </summary>
    public class PillButton : Button
    {
        public Color PillColor { get; set; } = Color.White;
        public Color PillHover { get; set; } = Color.FromArgb(0xEC, 0xF1, 0xF5);
        public Color PillBorder { get; set; } = Color.FromArgb(0xC4, 0xCB, 0xD2);
        public bool ShowBorder { get; set; } = true;
        public int Radius { get; set; } = 10;

        private bool _hover;

        public PillButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);

            int off = Math.Max(2, (int)Math.Round(Radius * 0.4));
            var pill = new Rectangle(1, 1, Width - 3, Height - 2 - off);
            if (pill.Width <= 0 || pill.Height <= 0) return;

            // soft drop shadow below the pill
            for (int i = 0; i < off; i++)
            {
                var sr = new Rectangle(pill.X, pill.Y + (off - i), pill.Width, pill.Height);
                using (var path = Rounded(sr, Radius))
                using (var b = new SolidBrush(Color.FromArgb(5 + i * 3, 0, 0, 0)))
                    g.FillPath(b, path);
            }

            using (var path = Rounded(pill, Radius))
            {
                using (var b = new SolidBrush(_hover ? PillHover : PillColor)) g.FillPath(b, path);
                if (ShowBorder) using (var pen = new Pen(PillBorder)) g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, pill, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
