using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify.Games
{
    /// <summary>
    /// Sprinkler Snake — the classic reskinned as pipe routing. Steer a growing
    /// branch line (arrow keys / WASD) to connect sprinkler heads; every head you
    /// reach adds a pipe segment. Run into a wall or your own pipe and the run ends.
    /// A coffee-break easter egg behind the Modify-tab "Sprinkler Snake" button.
    ///
    /// Fixed-size playfield (AllowResize off). The board draws in the panel's Paint
    /// handler, deriving the cell size from the panel's live size so it scales with
    /// display DPI. Keys are captured via ProcessCmdKey so arrows reach the game
    /// regardless of focus.
    /// </summary>
    public class SnakeGameDialog : DpiAwareForm
    {
        private const int Cols = 26, Rows = 18;

        private static readonly Color Blue     = Color.FromArgb(0x08, 0x59, 0x90);
        private static readonly Color BlueDark = Color.FromArgb(0x05, 0x3D, 0x63);
        private static readonly Color BoardBg  = Color.FromArgb(0xF6, 0xF9, 0xFB);
        private static readonly Color GridLine = Color.FromArgb(0xE6, 0xEC, 0xF1);
        private static readonly Color HeadRed  = Color.FromArgb(0xC0, 0x28, 0x28);
        private static readonly Color HeadRing = Color.FromArgb(0x8A, 0x1C, 0x1C);
        private static readonly Color SubText  = Color.FromArgb(0x33, 0x3A, 0x40);

        private enum State { Ready, Running, Paused, Dead, Won }

        private readonly GameCanvas _board;
        private readonly Label _score;
        private readonly Label _hint;
        private readonly Timer _timer;

        private readonly LinkedList<Point> _snake = new LinkedList<Point>();  // First = head
        private Point _dir, _pendingDir;
        private Point _food;
        private int _points, _best;
        private State _state = State.Ready;
        private readonly Random _rng = new Random();

        private readonly Font _overlayTitle = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
        private readonly Font _overlaySub   = new Font("Segoe UI", 9.5f);

        public SnakeGameDialog()
        {
            Text = "SG — Sprinkler Snake";
            AllowResize = false;
            RememberSize = false;

            const int M = 12, ScoreH = 22, HintH = 18, Cell = 16;
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
            Controls.Add(_board);

            _hint = new Label
            {
                Location = new Point(M, _board.Bottom + 4),
                Size = new Size(boardW, HintH),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = SystemColors.GrayText,
                Text = "Arrow keys / WASD to steer  •  Space to pause"
            };
            Controls.Add(_hint);

            ClientSize = new Size(M + boardW + M, _hint.Bottom + M);

            _best = DialogMemory.GetInt(nameof(SnakeGameDialog), "Best", 0);

            _timer = new Timer { Interval = 110 };
            _timer.Tick += Tick;

            Reset();
        }

        // ── Game state ──
        private void Reset()
        {
            _snake.Clear();
            int cx = Cols / 2, cy = Rows / 2;
            for (int i = 0; i < 4; i++) _snake.AddLast(new Point(cx - i, cy));  // head at (cx,cy)
            _dir = _pendingDir = new Point(1, 0);
            _points = 0;
            _state = State.Ready;
            PlaceFood();
            UpdateScore();
            _board.Invalidate();
        }

        private bool PlaceFood()
        {
            var taken = new HashSet<Point>(_snake);
            if (taken.Count >= Cols * Rows) return false;   // board full — nowhere to place
            Point p;
            do { p = new Point(_rng.Next(Cols), _rng.Next(Rows)); } while (taken.Contains(p));
            _food = p;
            return true;
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_state != State.Running) return;

            _dir = _pendingDir;
            Point head = _snake.First.Value;
            Point next = new Point(head.X + _dir.X, head.Y + _dir.Y);

            if (next.X < 0 || next.Y < 0 || next.X >= Cols || next.Y >= Rows) { Die(); return; }

            bool grow = next == _food;
            Point tail = _snake.Last.Value;
            foreach (Point seg in _snake)
            {
                if (seg != next) continue;
                if (!grow && seg == tail) break;   // tail vacates this cell as we move
                Die(); return;
            }

            _snake.AddFirst(next);
            if (grow)
            {
                _points++;
                if (_points > _best) _best = _points;
                if (!PlaceFood()) { UpdateScore(); Win(); return; }   // filled the whole board
            }
            else
            {
                _snake.RemoveLast();
            }
            UpdateScore();
            _board.Invalidate();
        }

        private void Die() => EndRun(State.Dead);
        private void Win() => EndRun(State.Won);

        private void EndRun(State end)
        {
            _state = end;
            _timer.Stop();
            if (_points > _best) _best = _points;
            DialogMemory.SetInt(nameof(SnakeGameDialog), "Best", _best);
            DialogMemory.Flush();
            _board.Invalidate();
        }

        private void TrySetDir(int dx, int dy)
        {
            if (_state != State.Running) return;
            if (_pendingDir != _dir) return;                 // one turn per tick (prevents reverse-into-self)
            if (dx == -_dir.X && dy == -_dir.Y) return;      // no 180° reversal
            _pendingDir = new Point(dx, dy);
        }

        private void OnEnter()
        {
            if (_state == State.Running) return;
            if (_state == State.Dead || _state == State.Won) Reset();
            _state = State.Running;
            _timer.Start();
            _board.Invalidate();
        }

        private void TogglePause()
        {
            if (_state == State.Running) { _state = State.Paused; _timer.Stop(); }
            else if (_state == State.Paused) { _state = State.Running; _timer.Start(); }
            _board.Invalidate();
        }

        // Space: start/restart from the idle or game-over screens (like Enter),
        // pause/resume while playing.
        private void OnSpace()
        {
            if (_state == State.Running || _state == State.Paused) TogglePause();
            else OnEnter();
        }

        private void UpdateScore() => _score.Text = $"Heads: {_points}     Best: {_best}";

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left:  case Keys.A: TrySetDir(-1, 0); return true;
                case Keys.Right: case Keys.D: TrySetDir(1, 0);  return true;
                case Keys.Up:    case Keys.W: TrySetDir(0, -1); return true;
                case Keys.Down:  case Keys.S: TrySetDir(0, 1);  return true;
                case Keys.Enter: OnEnter();  return true;
                case Keys.Space: OnSpace();  return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Rendering ──
        private void Board_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _board.ClientSize.Width, h = _board.ClientSize.Height;
            int cell = Math.Max(4, Math.Min(w / Cols, h / Rows));
            int gw = cell * Cols, gh = cell * Rows;
            int ox = (w - gw) / 2, oy = (h - gh) / 2;

            using (var pen = new Pen(GridLine))
            {
                for (int c = 0; c <= Cols; c++) g.DrawLine(pen, ox + c * cell, oy, ox + c * cell, oy + gh);
                for (int r = 0; r <= Rows; r++) g.DrawLine(pen, ox, oy + r * cell, ox + gw, oy + r * cell);
            }

            // Sprinkler head (food)
            var fr = new Rectangle(ox + _food.X * cell + 2, oy + _food.Y * cell + 2, cell - 4, cell - 4);
            using (var b = new SolidBrush(HeadRed)) g.FillEllipse(b, fr);
            using (var pen = new Pen(HeadRing, Math.Max(1f, cell / 10f))) g.DrawEllipse(pen, fr);

            // Pipe run (snake)
            int idx = 0;
            foreach (Point seg in _snake)
            {
                var rect = new Rectangle(ox + seg.X * cell + 1, oy + seg.Y * cell + 1, cell - 2, cell - 2);
                using (var b = new SolidBrush(idx == 0 ? BlueDark : Blue))
                using (var path = Round(rect, Math.Max(2, cell / 4)))
                    g.FillPath(b, path);
                idx++;
            }

            if (_state != State.Running) DrawOverlay(g, w, h);
        }

        private void DrawOverlay(Graphics g, int w, int h)
        {
            string title, sub;
            switch (_state)
            {
                case State.Paused:
                    title = "Paused"; sub = "Space to resume"; break;
                case State.Dead:
                    title = "Crashed!";
                    sub = $"You connected {_points} head{(_points == 1 ? "" : "s")}  •  Space/Enter to retry"; break;
                case State.Won:
                    title = "Perfect run!";
                    sub = "You filled the whole board  •  Space/Enter to play again"; break;
                default:
                    title = "Sprinkler Snake"; sub = "Press Space or Enter to start"; break;
            }

            using (var b = new SolidBrush(Color.FromArgb(190, Color.White)))
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
            if (_points > _best) _best = _points;
            DialogMemory.SetInt(nameof(SnakeGameDialog), "Best", _best);
            base.OnFormClosing(e);   // persists window position + flushes
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
