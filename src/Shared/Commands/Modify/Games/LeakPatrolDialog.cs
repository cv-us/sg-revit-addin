using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify.Games
{
    /// <summary>
    /// Leak Patrol — a whack-a-mole coffee break. Leaks spring up on a grid of
    /// ceiling tiles and slowly fill; click a leaking tile to patch it before it
    /// floods. Miss too many and the round ends; otherwise survive the 60-second
    /// clock. Score is tiles patched. Behind the Modify-tab "Leak Patrol" button.
    ///
    /// Fixed-size playfield. The board draws in the panel's Paint handler and derives
    /// its tile size from the panel's live size, so it scales with display DPI. A
    /// single Forms.Timer advances the sim on the UI thread with a fixed timestep.
    /// </summary>
    public class LeakPatrolDialog : DpiAwareForm
    {
        private const int Cols = 5, Rows = 4;
        private const int RoundSeconds = 60;
        private const int MaxFloods = 8;

        private static readonly Color Blue     = Color.FromArgb(0x08, 0x59, 0x90);
        private static readonly Color Water    = Color.FromArgb(0x2A, 0x74, 0xAD);
        private static readonly Color BoardBg  = Color.FromArgb(0xFB, 0xFD, 0xFE);
        private static readonly Color TileBase = Color.FromArgb(0xEC, 0xF1, 0xF5);
        private static readonly Color TileEdge = Color.FromArgb(0xD3, 0xDC, 0xE3);
        private static readonly Color Amber    = Color.FromArgb(0xE0, 0x8A, 0x1C);
        private static readonly Color Danger   = Color.FromArgb(0xC0, 0x28, 0x28);
        private static readonly Color Patched  = Color.FromArgb(0x2E, 0x9E, 0x4F);
        private static readonly Color SubText  = Color.FromArgb(0x33, 0x3A, 0x40);

        private enum State { Ready, Running, Over }

        private readonly GameCanvas _board;
        private readonly Label _score;
        private readonly Label _hint;
        private readonly Timer _timer;

        private readonly float[,] _fill  = new float[Cols, Rows];   // 0 = dry, (0..1] = leaking fraction
        private readonly float[,] _flash = new float[Cols, Rows];   // green patched flash, seconds remaining
        private readonly Random _rng = new Random();

        private int _patched, _floods, _best;
        private float _timeLeft, _spawnCooldown;
        private State _state = State.Ready;

        private readonly Font _overlayTitle = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
        private readonly Font _overlaySub   = new Font("Segoe UI", 9.5f);

        public LeakPatrolDialog()
        {
            Text = "SG — Leak Patrol";
            AllowResize = false;
            RememberSize = false;

            const int M = 12, ScoreH = 22, HintH = 18, Tile = 70, Gap = 8;
            int boardW = Cols * Tile + (Cols - 1) * Gap;
            int boardH = Rows * Tile + (Rows - 1) * Gap;

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
                Size = new Size(boardW, boardH),
                BackColor = BoardBg
            };
            _board.Paint += Board_Paint;
            _board.MouseDown += Board_MouseDown;
            Controls.Add(_board);

            _hint = new Label
            {
                Location = new Point(M, _board.Bottom + 4),
                Size = new Size(boardW, HintH),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = SystemColors.GrayText,
                Text = "Click leaks before they flood  •  Space/Enter to start"
            };
            Controls.Add(_hint);

            ClientSize = new Size(M + boardW + M, _hint.Bottom + M);

            _best = DialogMemory.GetInt(nameof(LeakPatrolDialog), "Best", 0);

            _timer = new Timer { Interval = 40 };
            _timer.Tick += Tick;

            Reset();
        }

        // ── Game state ──
        private void Reset()
        {
            Array.Clear(_fill, 0, _fill.Length);
            Array.Clear(_flash, 0, _flash.Length);
            _patched = 0;
            _floods = 0;
            _timeLeft = RoundSeconds;
            _spawnCooldown = 1.0f;
            _state = State.Ready;
            UpdateScore();
            _board.Invalidate();
        }

        private void Start()
        {
            if (_state == State.Over) Reset();
            if (_state == State.Running) return;
            _state = State.Running;
            _timer.Start();
            _board.Invalidate();
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_state != State.Running) return;
            float dt = _timer.Interval / 1000f;

            _timeLeft -= dt;
            float elapsed = RoundSeconds - _timeLeft;

            _spawnCooldown -= dt;
            if (_spawnCooldown <= 0f)
            {
                SpawnLeak();
                if (elapsed > 30f && _rng.NextDouble() < 0.4) SpawnLeak();   // double up late-game (gentler)
                _spawnCooldown = SpawnInterval(elapsed);
            }

            float leakDur = Math.Max(1.0f, 1.7f - elapsed * 0.018f);   // brisk from the start, ramps gently
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    if (_fill[c, r] > 0f)
                    {
                        _fill[c, r] += dt / leakDur;
                        if (_fill[c, r] >= 1f)
                        {
                            _fill[c, r] = 0f;
                            _floods++;
                            if (_floods >= MaxFloods) { EndRound(); return; }
                        }
                    }
                    if (_flash[c, r] > 0f) _flash[c, r] -= dt;
                }

            if (_timeLeft <= 0f) { _timeLeft = 0f; EndRound(); return; }

            UpdateScore();
            _board.Invalidate();
        }

        private float SpawnInterval(float elapsed)
        {
            float baseGap = Math.Max(0.45f, 0.85f - elapsed * 0.012f);
            return baseGap * (0.7f + (float)_rng.NextDouble() * 0.45f);
        }

        private void SpawnLeak()
        {
            var dry = new List<Point>();
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                    if (_fill[c, r] <= 0f && _flash[c, r] <= 0f) dry.Add(new Point(c, r));
            if (dry.Count == 0) return;
            Point p = dry[_rng.Next(dry.Count)];
            _fill[p.X, p.Y] = 0.01f;
        }

        private void EndRound()
        {
            _state = State.Over;
            _timer.Stop();
            if (_patched > _best) _best = _patched;
            DialogMemory.SetInt(nameof(LeakPatrolDialog), "Best", _best);
            DialogMemory.Flush();
            UpdateScore();
            _board.Invalidate();
        }

        private void Board_MouseDown(object sender, MouseEventArgs e)
        {
            if (_state != State.Running) return;
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    if (!TileRect(c, r).Contains(e.Location)) continue;
                    if (_fill[c, r] > 0f)
                    {
                        _patched++;
                        if (_patched > _best) _best = _patched;
                        _fill[c, r] = 0f;
                        _flash[c, r] = 0.35f;
                        UpdateScore();
                        _board.Invalidate();
                    }
                    return;
                }
        }

        private void UpdateScore() =>
            _score.Text = $"Patched: {_patched}     Missed: {_floods}/{MaxFloods}     " +
                          $"Time: {(int)Math.Ceiling(_timeLeft)}s     Best: {_best}";

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space) { Start(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Layout (derived from the live panel size, so it scales with DPI) ──
        private void Layout(out int tile, out int gap, out int ox, out int oy)
        {
            int w = _board.ClientSize.Width, h = _board.ClientSize.Height;
            gap = Math.Max(3, Math.Min(w, h) / 45);
            int tw = (w - (Cols - 1) * gap) / Cols;
            int th = (h - (Rows - 1) * gap) / Rows;
            tile = Math.Max(8, Math.Min(tw, th));
            int gridW = tile * Cols + gap * (Cols - 1);
            int gridH = tile * Rows + gap * (Rows - 1);
            ox = (w - gridW) / 2;
            oy = (h - gridH) / 2;
        }

        private Rectangle TileRect(int c, int r)
        {
            Layout(out int tile, out int gap, out int ox, out int oy);
            return new Rectangle(ox + c * (tile + gap), oy + r * (tile + gap), tile, tile);
        }

        // ── Rendering ──
        private void Board_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Layout(out int tile, out int gap, out int ox, out int oy);
            int radius = Math.Max(4, tile / 8);

            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    var rect = new Rectangle(ox + c * (tile + gap), oy + r * (tile + gap), tile, tile);
                    DrawTile(g, rect, radius, _fill[c, r], _flash[c, r]);
                }

            if (_state != State.Running)
            {
                int w = _board.ClientSize.Width, h = _board.ClientSize.Height;
                DrawOverlay(g, w, h);
            }
        }

        private void DrawTile(Graphics g, Rectangle rect, int radius, float fill, float flash)
        {
            using (var path = Round(rect, radius))
            {
                using (var b = new SolidBrush(TileBase)) g.FillPath(b, path);

                if (fill > 0f)
                {
                    // Rising water clipped to the rounded tile. Dispose the saved clip
                    // region each call (getter clones) so we don't churn GDI handles.
                    int wh = (int)Math.Round(rect.Height * Math.Min(1f, fill));
                    var waterRect = new Rectangle(rect.X, rect.Bottom - wh, rect.Width, wh);
                    using (var prev = g.Clip)
                    {
                        g.SetClip(path, CombineMode.Replace);
                        using (var b = new SolidBrush(Water)) g.FillRectangle(b, waterRect);
                        g.SetClip(prev, CombineMode.Replace);
                    }

                    DrawDroplet(g, rect, fill);
                }
                else if (flash > 0f)
                {
                    int a = (int)Math.Round(160 * Math.Min(1f, flash / 0.35f));
                    using (var b = new SolidBrush(Color.FromArgb(a, Patched))) g.FillPath(b, path);
                    DrawCheck(g, rect);
                }

                // Border: amber → red as a leak nears flooding.
                Color edge = TileEdge;
                float pen = 1.4f;
                if (fill > 0.66f) { edge = Danger; pen = 2.2f; }
                else if (fill > 0.33f) { edge = Amber; pen = 1.8f; }
                using (var p = new Pen(edge, pen)) g.DrawPath(p, path);
            }
        }

        private void DrawDroplet(Graphics g, Rectangle tile, float fill)
        {
            float d = Math.Max(8, tile.Width / 4f);
            float cx = tile.X + tile.Width / 2f;
            float cy = tile.Y + tile.Height / 3f;   // vertical centre of the round part
            Color drop = fill > 0.66f ? Danger : Blue;

            using (var path = new GraphicsPath())
            {
                // Round bottom (a near-full circle) closed off to a point at the top.
                path.AddArc(cx - d / 2f, cy - d / 2f, d, d, 35, 290);
                path.AddLine(cx + d * 0.34f, cy - d * 0.28f, cx, cy - d);   // up to the tip
                path.AddLine(cx, cy - d, cx - d * 0.34f, cy - d * 0.28f);   // back down
                path.CloseFigure();
                using (var b = new SolidBrush(drop)) g.FillPath(b, path);
            }

            // Little highlight so it reads as water.
            using (var b = new SolidBrush(Color.FromArgb(90, Color.White)))
                g.FillEllipse(b, cx - d * 0.20f, cy - d * 0.02f, d * 0.24f, d * 0.34f);
        }

        private void DrawCheck(Graphics g, Rectangle tile)
        {
            using (var p = new Pen(Color.White, Math.Max(2f, tile.Width / 12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float x = tile.X + tile.Width * 0.30f, y = tile.Y + tile.Height * 0.52f;
                g.DrawLines(p, new[]
                {
                    new PointF(x, y),
                    new PointF(x + tile.Width * 0.12f, y + tile.Height * 0.14f),
                    new PointF(x + tile.Width * 0.40f, y - tile.Height * 0.20f)
                });
            }
        }

        private void DrawOverlay(Graphics g, int w, int h)
        {
            string title, sub;
            if (_state == State.Over)
            {
                title = _floods >= MaxFloods ? "Flooded!" : "Time!";
                sub = $"You patched {_patched} leak{(_patched == 1 ? "" : "s")}  •  Space/Enter to play again";
            }
            else
            {
                title = "Leak Patrol";
                sub = "Click leaks before they flood  •  Space/Enter to start";
            }

            using (var b = new SolidBrush(Color.FromArgb(200, Color.White)))
                g.FillRectangle(b, 0, h / 2 - 34, w, 68);
            TextRenderer.DrawText(g, title, _overlayTitle, new Rectangle(0, h / 2 - 30, w, 28), Blue,
                TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, sub, _overlaySub, new Rectangle(0, h / 2 + 2, w, 20), SubText,
                TextFormatFlags.HorizontalCenter);
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            if (_patched > _best) _best = _patched;
            DialogMemory.SetInt(nameof(LeakPatrolDialog), "Best", _best);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _overlayTitle?.Dispose();
                _overlaySub?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
