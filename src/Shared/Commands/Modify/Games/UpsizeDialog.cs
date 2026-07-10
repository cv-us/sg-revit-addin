using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify.Games
{
    /// <summary>
    /// Upsize — 2048 reskinned as pipe sizes. Slide the 4x4 grid with the arrow keys
    /// (or WASD); two equal pipes merge into the next size up the schedule ladder
    /// (1, 1¼, 1½, 2, 2½, 3, 4, 6, 8, 10, 12). Reach an 8" main to win; keep going for
    /// bragging rights. Behind a Modify-tab placeholder button.
    /// </summary>
    public class UpsizeDialog : DpiAwareForm
    {
        private const int N = 4;
        private static readonly string[] Sizes =
            { "1\"", "1¼\"", "1½\"", "2\"", "2½\"", "3\"", "4\"", "6\"", "8\"", "10\"", "12\"" };
        private const int WinIndex = 8;   // 8"

        private static readonly Color Blue    = Color.FromArgb(0x08, 0x59, 0x90);
        private static readonly Color BlueDk  = Color.FromArgb(0x05, 0x3D, 0x63);
        private static readonly Color BoardBg = Color.FromArgb(0xF3, 0xF6, 0xF9);
        private static readonly Color Empty   = Color.FromArgb(0xDF, 0xE6, 0xEC);
        private static readonly Color SubText = Color.FromArgb(0x33, 0x3A, 0x40);

        private readonly GameCanvas _board;
        private readonly Label _score;
        private readonly Label _hint;

        private readonly int[,] _g = new int[N, N];   // 0 = empty, else (size index + 1)
        private readonly Random _rng = new Random();
        private int _points, _best;
        private bool _over, _won;

        private readonly Font _overlayTitle = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
        private readonly Font _overlaySub   = new Font("Segoe UI", 9.5f);

        public UpsizeDialog()
        {
            Text = "SG — Upsize";
            AllowResize = false;
            RememberSize = false;

            const int M = 12, ScoreH = 22, HintH = 18, Cell = 84, Gap = 8;
            int boardW = N * Cell + (N - 1) * Gap;

            _score = new Label
            {
                Location = new Point(M, 6),
                Size = new Size(boardW, ScoreH),
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = Blue
            };
            Controls.Add(_score);

            _board = new GameCanvas
            {
                Location = new Point(M, 6 + ScoreH + 4),
                Size = new Size(boardW, boardW),
                BackColor = BoardBg
            };
            _board.Paint += Board_Paint;
            Controls.Add(_board);

            _hint = new Label
            {
                Location = new Point(M, _board.Bottom + 4),
                Size = new Size(boardW, HintH),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = SystemColors.GrayText,
                Text = "Arrow keys / WASD to slide & merge  •  R to restart"
            };
            Controls.Add(_hint);

            ClientSize = new Size(M + boardW + M, _hint.Bottom + M);
            _best = DialogMemory.GetInt(nameof(UpsizeDialog), "Best", 0);
            Reset();
        }

        private void Reset()
        {
            Array.Clear(_g, 0, _g.Length);
            _points = 0; _over = false; _won = false;
            SpawnTile(); SpawnTile();
            UpdateScore(); _board.Invalidate();
        }

        private void SpawnTile()
        {
            var empties = new List<Point>();
            for (int x = 0; x < N; x++)
                for (int y = 0; y < N; y++)
                    if (_g[x, y] == 0) empties.Add(new Point(x, y));
            if (empties.Count == 0) return;
            var p = empties[_rng.Next(empties.Count)];
            _g[p.X, p.Y] = _rng.NextDouble() < 0.85 ? 1 : 2;   // mostly 1", sometimes 1¼"
        }

        /// <summary>Slide + merge one direction. 0=left,1=right,2=up,3=down. True if the board changed.</summary>
        private bool Move(int dir)
        {
            bool moved = false;
            bool horizontal = dir == 0 || dir == 1;
            bool toStart = dir == 0 || dir == 2;    // travel toward index 0

            for (int line = 0; line < N; line++)
            {
                var vals = new List<int>();
                for (int i = 0; i < N; i++)
                {
                    int idx = toStart ? i : N - 1 - i;
                    int v = horizontal ? _g[idx, line] : _g[line, idx];
                    if (v != 0) vals.Add(v);
                }

                var merged = new List<int>();
                for (int i = 0; i < vals.Count; i++)
                {
                    if (i + 1 < vals.Count && vals[i] == vals[i + 1] && vals[i] < Sizes.Length)
                    {
                        int up = vals[i] + 1;
                        merged.Add(up);
                        _points += up;
                        if (up - 1 >= WinIndex) _won = true;
                        i++;                        // consume the merged pair
                    }
                    else merged.Add(vals[i]);
                }

                for (int i = 0; i < N; i++)
                {
                    int idx = toStart ? i : N - 1 - i;
                    int nv = i < merged.Count ? merged[i] : 0;
                    int cur = horizontal ? _g[idx, line] : _g[line, idx];
                    if (cur != nv) moved = true;
                    if (horizontal) _g[idx, line] = nv; else _g[line, idx] = nv;
                }
            }
            return moved;
        }

        private bool AnyMoves()
        {
            for (int x = 0; x < N; x++)
                for (int y = 0; y < N; y++)
                {
                    if (_g[x, y] == 0) return true;
                    if (x + 1 < N && _g[x, y] == _g[x + 1, y]) return true;
                    if (y + 1 < N && _g[x, y] == _g[x, y + 1]) return true;
                }
            return false;
        }

        private void DoMove(int dir)
        {
            if (_over) return;
            if (!Move(dir)) return;
            SpawnTile();
            if (_points > _best)
            {
                _best = _points;
                DialogMemory.SetInt(nameof(UpsizeDialog), "Best", _best);
                DialogMemory.Flush();
            }
            if (!AnyMoves()) _over = true;
            UpdateScore();
            _board.Invalidate();
        }

        private void UpdateScore() =>
            _score.Text = $"Score: {_points}     Best: {_best}" + (_won ? "     ✓ 8\" main!" : "");

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left: case Keys.A: DoMove(0); return true;
                case Keys.Right: case Keys.D: DoMove(1); return true;
                case Keys.Up: case Keys.W: DoMove(2); return true;
                case Keys.Down: case Keys.S: DoMove(3); return true;
                case Keys.R: Reset(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Layout(out int cell, out int gap, out int ox, out int oy)
        {
            int w = _board.ClientSize.Width, h = _board.ClientSize.Height;
            gap = Math.Max(4, Math.Min(w, h) / 45);
            cell = Math.Max(10, (Math.Min(w, h) - (N - 1) * gap) / N);
            int grid = cell * N + gap * (N - 1);
            ox = (w - grid) / 2; oy = (h - grid) / 2;
        }

        private void Board_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Layout(out int cell, out int gap, out int ox, out int oy);
            int radius = Math.Max(5, cell / 9);

            for (int x = 0; x < N; x++)
                for (int y = 0; y < N; y++)
                {
                    var rect = new Rectangle(ox + x * (cell + gap), oy + y * (cell + gap), cell, cell);
                    int v = _g[x, y];
                    using (var path = Round(rect, radius))
                    {
                        Color fill = v == 0 ? Empty : TileColor(v - 1);
                        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                        if (v != 0)
                        {
                            string s = Sizes[Math.Min(v - 1, Sizes.Length - 1)];
                            Color txt = v - 1 >= 3 ? Color.White : BlueDk;
                            using (var f = new Font("Segoe UI Semibold", Math.Max(9f, cell / 4.2f), FontStyle.Bold))
                                TextRenderer.DrawText(g, s, f, rect, txt,
                                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        }
                    }
                }

            if (_over || _won) DrawOverlay(g, _board.ClientSize.Width, _board.ClientSize.Height);
        }

        /// <summary>Light grey → SG blue as the size climbs the ladder.</summary>
        private static Color TileColor(int idx)
        {
            float t = Math.Min(1f, idx / 8f);
            int r = (int)(0xCF + (0x08 - 0xCF) * t);
            int gg = (int)(0xE0 + (0x59 - 0xE0) * t);
            int b = (int)(0xEC + (0x90 - 0xEC) * t);
            return Color.FromArgb(r, gg, b);
        }

        private void DrawOverlay(Graphics g, int w, int h)
        {
            string title = _won ? "8\" Main!" : "No moves";
            string sub = _won
                ? $"Score {_points}  •  keep going, or R to restart"
                : $"Score {_points}  •  R to restart";
            using (var b = new SolidBrush(Color.FromArgb(205, Color.White)))
                g.FillRectangle(b, 0, h / 2 - 34, w, 68);
            TextRenderer.DrawText(g, title, _overlayTitle, new Rectangle(0, h / 2 - 30, w, 28), Blue, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, sub, _overlaySub, new Rectangle(0, h / 2 + 2, w, 20), SubText, TextFormatFlags.HorizontalCenter);
        }

        private static GraphicsPath Round(Rectangle r, int radius)
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _overlayTitle?.Dispose(); _overlaySub?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
