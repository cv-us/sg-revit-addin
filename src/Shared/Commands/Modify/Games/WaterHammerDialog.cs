using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SgRevitAddin.Utils;

namespace SgRevitAddin.Commands.Modify.Games
{
    /// <summary>
    /// Water Hammer — a one-button rhythm game. Pressure pulses race down a pipe toward
    /// the valve; press Space (or click) the instant a pulse hits the valve's hit-ring.
    /// Nail the timing for combos; let one slam the closed valve and you get a "water
    /// hammer" — the line shudders and you take a strike. Three strikes ends the run.
    /// Behind a Modify-tab placeholder button.
    /// </summary>
    public class WaterHammerDialog : DpiAwareForm
    {
        private static readonly Color Blue    = Color.FromArgb(0x08, 0x59, 0x90);
        private static readonly Color BlueDk  = Color.FromArgb(0x05, 0x3D, 0x63);
        private static readonly Color Water    = Color.FromArgb(0x2A, 0x74, 0xAD);
        private static readonly Color BoardBg  = Color.FromArgb(0xF4, 0xF8, 0xFB);
        private static readonly Color PipeGrey = Color.FromArgb(0xC7, 0xD2, 0xDB);
        private static readonly Color Danger   = Color.FromArgb(0xC0, 0x28, 0x28);
        private static readonly Color Good      = Color.FromArgb(0x2E, 0x9E, 0x4F);
        private static readonly Color SubText   = Color.FromArgb(0x33, 0x3A, 0x40);

        private enum State { Ready, Running, Over }

        private const int MaxStrikes = 3;
        private const float TargetFrac = 0.80f;   // valve position along the pipe (0..1)
        private const float HitWindow = 0.055f;   // fraction of pipe length

        private readonly GameCanvas _board;
        private readonly Label _score, _hint;
        private readonly Timer _timer;
        private readonly List<Pulse> _pulses = new List<Pulse>();
        private readonly Random _rng = new Random();

        private float _spawnCd, _elapsed, _shake, _ringFlash;
        private Color _flashCol = Good;
        private int _hits, _combo, _bestCombo, _strikes, _best;
        private State _state = State.Ready;

        private readonly Font _overlayTitle = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
        private readonly Font _overlaySub   = new Font("Segoe UI", 9.5f);

        private sealed class Pulse { public float X; public bool Dead; public float Speed; }

        public WaterHammerDialog()
        {
            Text = "SG — Water Hammer";
            AllowResize = false;
            RememberSize = false;

            const int M = 12, ScoreH = 22, HintH = 18, BoardW = 560, BoardH = 150;

            _score = new Label
            {
                Location = new Point(M, 6),
                Size = new Size(BoardW, ScoreH),
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = Blue
            };
            Controls.Add(_score);

            _board = new GameCanvas
            {
                Location = new Point(M, 6 + ScoreH + 4),
                Size = new Size(BoardW, BoardH),
                BackColor = BoardBg
            };
            _board.Paint += Board_Paint;
            _board.MouseDown += (s, e) => Hit();
            Controls.Add(_board);

            _hint = new Label
            {
                Location = new Point(M, _board.Bottom + 4),
                Size = new Size(BoardW, HintH),
                Font = new Font("Segoe UI", 8.25f),
                ForeColor = SystemColors.GrayText,
                Text = "Hit Space when a pulse reaches the valve  •  Space/Enter to start"
            };
            Controls.Add(_hint);

            ClientSize = new Size(M + BoardW + M, _hint.Bottom + M);
            _best = DialogMemory.GetInt(nameof(WaterHammerDialog), "Best", 0);

            _timer = new Timer { Interval = 16 };
            _timer.Tick += Tick;
            Reset();
        }

        private void Reset()
        {
            _pulses.Clear();
            _hits = 0; _combo = 0; _bestCombo = 0; _strikes = 0;
            _elapsed = 0; _spawnCd = 0.9f; _shake = 0; _ringFlash = 0;
            _state = State.Ready;
            UpdateScore(); _board.Invalidate();
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
            _elapsed += dt;
            if (_shake > 0f) _shake -= dt;
            if (_ringFlash > 0f) _ringFlash -= dt;

            // spawn pulses on a tempo that gently quickens
            _spawnCd -= dt;
            if (_spawnCd <= 0f)
            {
                float speed = 0.42f + Math.Min(0.5f, _elapsed * 0.010f);   // frac / sec
                _pulses.Add(new Pulse { X = 0f, Speed = speed });
                float gap = Math.Max(0.5f, 0.95f - _elapsed * 0.012f);
                _spawnCd = gap * (0.85f + (float)_rng.NextDouble() * 0.35f);
            }

            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                Pulse p = _pulses[i];
                if (p.Dead) { _pulses.RemoveAt(i); continue; }
                p.X += p.Speed * dt;
                if (p.X > TargetFrac + HitWindow)   // slammed the valve unhit
                {
                    _pulses.RemoveAt(i);
                    Strike();
                    if (_state != State.Running) return;
                }
            }

            UpdateScore();
            _board.Invalidate();
        }

        private void Hit()
        {
            if (_state != State.Running) return;

            Pulse best = null; float bestErr = HitWindow + 1f;
            foreach (var p in _pulses)
            {
                if (p.Dead) continue;
                float err = Math.Abs(p.X - TargetFrac);
                if (err <= HitWindow && err < bestErr) { bestErr = err; best = p; }
            }

            if (best != null)
            {
                best.Dead = true;
                _hits++;
                _combo++;
                if (_combo > _bestCombo) _bestCombo = _combo;
                if (_hits > _best) { _best = _hits; DialogMemory.SetInt(nameof(WaterHammerDialog), "Best", _best); DialogMemory.Flush(); }
                _flashCol = bestErr < HitWindow * 0.4f ? Good : Water;   // perfect vs good
                _ringFlash = 0.18f;
            }
            else
            {
                _combo = 0;   // mistimed press — just breaks the combo, no strike
            }
            UpdateScore();
            _board.Invalidate();
        }

        private void Strike()
        {
            _strikes++;
            _combo = 0;
            _shake = 0.28f;
            _flashCol = Danger;
            _ringFlash = 0.22f;
            if (_strikes >= MaxStrikes) EndRun();
        }

        private void EndRun()
        {
            _state = State.Over;
            _timer.Stop();
            if (_hits > _best) { _best = _hits; DialogMemory.SetInt(nameof(WaterHammerDialog), "Best", _best); DialogMemory.Flush(); }
            UpdateScore();
            _board.Invalidate();
        }

        private void UpdateScore() =>
            _score.Text = $"Hits: {_hits}     Combo: {_combo} (best {_bestCombo})     " +
                          $"Strikes: {_strikes}/{MaxStrikes}     Best: {_best}";

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter)
            {
                if (_state == State.Running) Hit(); else Start();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ── Rendering ──
        private void Board_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _board.ClientSize.Width, h = _board.ClientSize.Height;

            if (_shake > 0f)
            {
                float s = _shake / 0.28f * 5f;
                g.TranslateTransform((float)(_rng.NextDouble() * 2 - 1) * s, (float)(_rng.NextDouble() * 2 - 1) * s);
            }

            int pad = Math.Max(20, w / 16);
            int cy = h / 2;
            int pipeH = Math.Max(18, h / 5);
            int x0 = pad, x1 = w - pad;
            float Px(float frac) => x0 + (x1 - x0) * frac;

            // pipe
            var pipe = new Rectangle(x0, cy - pipeH / 2, x1 - x0, pipeH);
            using (var path = Round(pipe, pipeH / 2))
            {
                using (var b = new LinearGradientBrush(pipe, Color.White, PipeGrey, 90f)) g.FillPath(b, path);
                using (var p = new Pen(PipeGrey, 1.5f)) g.DrawPath(p, path);
            }

            // valve / hit ring at the target
            float vx = Px(TargetFrac);
            int rr = (int)(pipeH * 1.15f);
            var ring = new RectangleF(vx - rr / 2f, cy - rr / 2f, rr, rr);
            using (var p = new Pen(Blue, 3f)) g.DrawEllipse(p, ring);
            if (_ringFlash > 0f)
            {
                int a = (int)(180 * Math.Min(1f, _ringFlash / 0.2f));
                using (var b = new SolidBrush(Color.FromArgb(a, _flashCol))) g.FillEllipse(b, ring);
            }
            // valve stem
            using (var p = new Pen(BlueDk, 3f))
                g.DrawLine(p, vx, cy - rr / 2f - 8, vx, cy - rr / 2f);

            // pulses (droplets travelling right)
            foreach (var p in _pulses)
            {
                if (p.Dead) continue;
                float x = Px(p.X);
                float d = pipeH * 0.72f;
                bool near = Math.Abs(p.X - TargetFrac) <= HitWindow;
                using (var b = new SolidBrush(near ? Blue : Water))
                    g.FillEllipse(b, x - d / 2f, cy - d / 2f, d, d);
                using (var b = new SolidBrush(Color.FromArgb(90, Color.White)))
                    g.FillEllipse(b, x - d * 0.18f, cy - d * 0.26f, d * 0.28f, d * 0.28f);
            }

            g.ResetTransform();
            if (_state != State.Running) DrawOverlay(g, w, h);
        }

        private void DrawOverlay(Graphics g, int w, int h)
        {
            string title, sub;
            if (_state == State.Over)
            {
                title = "Water Hammer!";
                sub = $"{_hits} hits  •  best combo {_bestCombo}  •  Space/Enter to play again";
            }
            else
            {
                title = "Water Hammer";
                sub = "Tap Space as each pulse reaches the valve  •  Space/Enter to start";
            }
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
