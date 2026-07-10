using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify.Games
{
    /// <summary>
    /// Deluge Defense — tower defense, fire-protection style. Fire creeps along the
    /// corridor toward the riser; click empty cells to drop sprinkler heads (they cost
    /// coins and auto-spray fire in range). Kill fire for coins; a fire that reaches the
    /// riser costs a life. Press Space to release each wave. Survive as long as you can.
    /// Behind a Modify-tab placeholder button.
    /// </summary>
    public class DelugeDefenseDialog : DpiAwareForm
    {
        private const int Cols = 9, Rows = 7;
        private const int HeadCost = 50, StartCoins = 150, StartLives = 12, KillReward = 8, WaveBonus = 25;
        private const float Range = 1.7f, FireRate = 2.2f, Damage = 7f;

        private static readonly Color Blue     = Color.FromArgb(0x08, 0x59, 0x90);
        private static readonly Color BlueDk   = Color.FromArgb(0x05, 0x3D, 0x63);
        private static readonly Color BoardBg  = Color.FromArgb(0xF4, 0xF8, 0xFB);
        private static readonly Color CellBase = Color.FromArgb(0xEA, 0xF0, 0xF4);
        private static readonly Color CellEdge = Color.FromArgb(0xDD, 0xE5, 0xEB);
        private static readonly Color PathCol  = Color.FromArgb(0xE7, 0xD6, 0xC4);   // corridor
        private static readonly Color PathEdge = Color.FromArgb(0xD4, 0xBE, 0xA6);
        private static readonly Color FireA    = Color.FromArgb(0xF2, 0x6A, 0x1E);
        private static readonly Color FireB    = Color.FromArgb(0xC0, 0x28, 0x28);
        private static readonly Color Spray    = Color.FromArgb(0x2A, 0x74, 0xAD);
        private static readonly Color SubText  = Color.FromArgb(0x33, 0x3A, 0x40);

        private enum State { Building, Running, Over }

        private readonly GameCanvas _board;
        private readonly Label _score, _hint;
        private readonly Timer _timer;
        private readonly Random _rng = new Random();

        private readonly List<Point> _path = new List<Point>();
        private readonly HashSet<int> _pathSet = new HashSet<int>();
        private readonly List<Fire> _fires = new List<Fire>();
        private readonly List<Head> _heads = new List<Head>();

        private int _coins, _lives, _wave, _best, _pending;
        private float _spawnCd;
        private State _state = State.Building;

        private readonly Font _overlayTitle = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
        private readonly Font _overlaySub   = new Font("Segoe UI", 9.5f);

        private sealed class Fire { public float Dist; public float Hp, MaxHp, Speed; }
        private sealed class Head { public Point Cell; public float Cd; public PointF SprayTo; public float SprayT; }

        public DelugeDefenseDialog()
        {
            Text = "SG — Deluge Defense";
            AllowResize = false;
            RememberSize = false;

            const int M = 12, ScoreH = 22, HintH = 18, Cell = 54;
            int boardW = Cols * Cell, boardH = Rows * Cell;

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
                Text = "Click empty cells to place heads  •  Space to release a wave  •  R to restart"
            };
            Controls.Add(_hint);

            ClientSize = new Size(M + boardW + M, _hint.Bottom + M);
            _best = DialogMemory.GetInt(nameof(DelugeDefenseDialog), "Best", 0);

            BuildPath();

            _timer = new Timer { Interval = 30 };
            _timer.Tick += Tick;
            Reset();
        }

        private void BuildPath()
        {
            _path.Clear(); _pathSet.Clear();
            int[] bands = { 1, 3, 5 };
            for (int bi = 0; bi < bands.Length; bi++)
            {
                int row = bands[bi];
                bool ltr = bi % 2 == 0;
                int cStart = ltr ? 0 : Cols - 1, cEnd = ltr ? Cols - 1 : 0, step = ltr ? 1 : -1;
                for (int c = cStart; ; c += step) { AddPath(c, row); if (c == cEnd) break; }
                if (bi < bands.Length - 1) AddPath(cEnd, row + 1);   // vertical connector to the next band
            }
        }

        private void AddPath(int c, int r)
        {
            _path.Add(new Point(c, r));
            _pathSet.Add(r * Cols + c);
        }

        private void Reset()
        {
            _timer.Stop();
            _fires.Clear(); _heads.Clear();
            _coins = StartCoins; _lives = StartLives; _wave = 0; _pending = 0; _spawnCd = 0;
            _state = State.Building;
            UpdateScore(); _board.Invalidate();
        }

        private void StartWave()
        {
            if (_state != State.Building) return;
            _wave++;
            _pending = 4 + _wave * 2;
            _spawnCd = 0.4f;
            _state = State.Running;
            _timer.Start();
            UpdateScore(); _board.Invalidate();
        }

        private float WaveHp() => 14f + _wave * 7f;
        private float WaveSpeed() => 1.05f + _wave * 0.05f;   // cells / sec

        private void Tick(object sender, EventArgs e)
        {
            if (_state != State.Running) return;
            float dt = _timer.Interval / 1000f;

            if (_pending > 0)
            {
                _spawnCd -= dt;
                if (_spawnCd <= 0f)
                {
                    _fires.Add(new Fire { Dist = 0f, Hp = WaveHp(), MaxHp = WaveHp(), Speed = WaveSpeed() });
                    _pending--;
                    _spawnCd = 0.85f;
                }
            }

            int pathLen = _path.Count - 1;
            for (int i = _fires.Count - 1; i >= 0; i--)
            {
                Fire f = _fires[i];
                f.Dist += f.Speed * dt;
                if (f.Dist >= pathLen)
                {
                    _fires.RemoveAt(i);
                    _lives--;
                    if (_lives <= 0) { EndGame(); return; }
                }
            }

            foreach (var h in _heads)
            {
                if (h.SprayT > 0f) h.SprayT -= dt;
                h.Cd -= dt;
                if (h.Cd > 0f) continue;

                Fire target = null; float bestD = Range + 1f;
                PointF hc = new PointF(h.Cell.X + 0.5f, h.Cell.Y + 0.5f);
                foreach (var f in _fires)
                {
                    PointF fp = PathPoint(f.Dist);
                    float d = Dist(hc, fp);
                    if (d <= Range && d < bestD) { bestD = d; target = f; }
                }
                if (target != null)
                {
                    target.Hp -= Damage;
                    h.Cd = 1f / FireRate;
                    h.SprayTo = PathPoint(target.Dist);
                    h.SprayT = 0.12f;
                    if (target.Hp <= 0f)
                    {
                        _fires.Remove(target);
                        _coins += KillReward;
                    }
                }
            }

            if (_pending == 0 && _fires.Count == 0)
            {
                _coins += WaveBonus;
                if (_wave > _best) { _best = _wave; DialogMemory.SetInt(nameof(DelugeDefenseDialog), "Best", _best); DialogMemory.Flush(); }
                _state = State.Building;
                _timer.Stop();
            }

            UpdateScore();
            _board.Invalidate();
        }

        private void EndGame()
        {
            _state = State.Over;
            _timer.Stop();
            if (_wave > _best) { _best = _wave; DialogMemory.SetInt(nameof(DelugeDefenseDialog), "Best", _best); DialogMemory.Flush(); }
            UpdateScore(); _board.Invalidate();
        }

        private PointF PathPoint(float dist)
        {
            if (dist <= 0f) return Center(_path[0]);
            int seg = (int)Math.Floor(dist);
            if (seg >= _path.Count - 1) return Center(_path[_path.Count - 1]);
            float f = dist - seg;
            PointF a = Center(_path[seg]), b = Center(_path[seg + 1]);
            return new PointF(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
        }

        private static PointF Center(Point p) => new PointF(p.X + 0.5f, p.Y + 0.5f);
        private static float Dist(PointF a, PointF b)
        { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        private void Board_MouseDown(object sender, MouseEventArgs e)
        {
            if (_state == State.Over) return;
            Layout(out int cell, out int ox, out int oy);
            int c = (e.X - ox) / cell, r = (e.Y - oy) / cell;
            if (c < 0 || c >= Cols || r < 0 || r >= Rows) return;
            if (_pathSet.Contains(r * Cols + c)) return;                 // not on the corridor
            foreach (var h in _heads) if (h.Cell.X == c && h.Cell.Y == r) return;   // occupied
            if (_coins < HeadCost) return;
            _coins -= HeadCost;
            _heads.Add(new Head { Cell = new Point(c, r) });
            UpdateScore(); _board.Invalidate();
        }

        private void UpdateScore() =>
            _score.Text = $"Coins: {_coins}     Lives: {_lives}     Wave: {_wave}     Best: {_best}" +
                          (_state == State.Building && _wave >= 0 ? "     ▶ Space" : "");

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter)
            {
                if (_state == State.Over) Reset(); else StartWave();
                return true;
            }
            if (keyData == Keys.R) { Reset(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Layout(out int cell, out int ox, out int oy)
        {
            int w = _board.ClientSize.Width, h = _board.ClientSize.Height;
            cell = Math.Max(12, Math.Min(w / Cols, h / Rows));
            ox = (w - cell * Cols) / 2;
            oy = (h - cell * Rows) / 2;
        }

        private void Board_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Layout(out int cell, out int ox, out int oy);

            // cells
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    var rect = new Rectangle(ox + c * cell, oy + r * cell, cell, cell);
                    bool path = _pathSet.Contains(r * Cols + c);
                    using (var b = new SolidBrush(path ? PathCol : CellBase)) g.FillRectangle(b, rect);
                    using (var p = new Pen(path ? PathEdge : CellEdge, 1f))
                        g.DrawRectangle(p, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }

            // entry (fire) + exit (riser) markers
            MarkCell(g, _path[0], cell, ox, oy, FireB, "IN");
            MarkCell(g, _path[_path.Count - 1], cell, ox, oy, Blue, "RISER");

            // heads (+ faint range) and their sprays
            foreach (var h in _heads)
            {
                float hx = ox + (h.Cell.X + 0.5f) * cell, hy = oy + (h.Cell.Y + 0.5f) * cell;
                using (var b = new SolidBrush(Color.FromArgb(22, Spray)))
                    g.FillEllipse(b, hx - Range * cell, hy - Range * cell, Range * 2 * cell, Range * 2 * cell);
                if (h.SprayT > 0f)
                    using (var p = new Pen(Color.FromArgb(150, Spray), 2f))
                        g.DrawLine(p, hx, hy, ox + h.SprayTo.X * cell, oy + h.SprayTo.Y * cell);
                float hd = cell * 0.5f;
                using (var b = new SolidBrush(Blue)) g.FillEllipse(b, hx - hd / 2, hy - hd / 2, hd, hd);
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, hx - hd * 0.16f, hy - hd * 0.16f, hd * 0.32f, hd * 0.32f);
            }

            // fires
            foreach (var f in _fires)
            {
                PointF fp = PathPoint(f.Dist);
                float fx = ox + fp.X * cell, fy = oy + fp.Y * cell;
                float frac = Math.Max(0.35f, f.Hp / f.MaxHp);
                float fd = cell * 0.62f * frac + cell * 0.18f;
                using (var b = new LinearGradientBrush(new RectangleF(fx - fd / 2, fy - fd / 2, fd, fd), FireA, FireB, 90f))
                    g.FillEllipse(b, fx - fd / 2, fy - fd / 2, fd, fd);
                // hp bar
                float bw = cell * 0.6f, bh = 4f, bx = fx - bw / 2, by = fy - fd / 2 - 7;
                using (var b = new SolidBrush(Color.FromArgb(70, Color.Black))) g.FillRectangle(b, bx, by, bw, bh);
                using (var b = new SolidBrush(Color.FromArgb(0x2E, 0x9E, 0x4F))) g.FillRectangle(b, bx, by, bw * (f.Hp / f.MaxHp), bh);
            }

            if (_state != State.Running) DrawOverlay(g, _board.ClientSize.Width, _board.ClientSize.Height);
        }

        private void MarkCell(Graphics g, Point p, int cell, int ox, int oy, Color col, string label)
        {
            var rect = new Rectangle(ox + p.X * cell, oy + p.Y * cell, cell, cell);
            using (var b = new SolidBrush(Color.FromArgb(40, col))) g.FillRectangle(b, rect);
            using (var pen = new Pen(col, 2f)) g.DrawRectangle(pen, rect.X + 1, rect.Y + 1, rect.Width - 3, rect.Height - 3);
            using (var f = new Font("Segoe UI", Math.Max(6.5f, cell / 9f), FontStyle.Bold))
                TextRenderer.DrawText(g, label, f, rect, col, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
        }

        private void DrawOverlay(Graphics g, int w, int h)
        {
            string title, sub;
            if (_state == State.Over)
            {
                title = "Riser breached!";
                sub = $"You held {_wave} wave{(_wave == 1 ? "" : "s")}  •  Space / R to play again";
            }
            else
            {
                title = _wave == 0 ? "Deluge Defense" : $"Wave {_wave} cleared";
                sub = $"Place heads (◦ {HeadCost})  •  Space to release wave {_wave + 1}";
            }
            using (var b = new SolidBrush(Color.FromArgb(205, Color.White)))
                g.FillRectangle(b, 0, h / 2 - 34, w, 68);
            TextRenderer.DrawText(g, title, _overlayTitle, new Rectangle(0, h / 2 - 30, w, 28), Blue, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, sub, _overlaySub, new Rectangle(0, h / 2 + 2, w, 20), SubText, TextFormatFlags.HorizontalCenter);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer?.Dispose(); _overlayTitle?.Dispose(); _overlaySub?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
