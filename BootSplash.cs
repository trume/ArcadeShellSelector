using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NAudio.Wave;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Full-screen terminal boot animation displayed before the launcher appears.
    /// Feeds real system and config data line-by-line in green monospace text with a
    /// blinking cursor and a subtle CRT scanline overlay.
    ///
    /// Usage:
    ///   using var splash = new BootSplash();
    ///   splash.BuildSequence(cfg);  // fill the line queue from real data
    ///   splash.ShowDialog();        // runs until animation ends (auto-closes)
    /// </summary>
    internal sealed class BootSplash : Form
    {
        // ── Speed ───────────────────────────────────────────────────────────
        /// <summary>
        /// Global typing speed multiplier. 1.0 = normal, 0.5 = twice as fast, 2.0 = half speed.
        /// </summary>
        private const float SpeedScale = 0.13f;

        // ── Palette ─────────────────────────────────────────────────────────
        private static readonly Color ColBg      = Color.Black;
        private static readonly Color ColDefault = Color.FromArgb(0,  210,  70);   // main green
        private static readonly Color ColDim     = Color.FromArgb(0,  110,  35);   // dim green (sep)
        private static readonly Color ColBright  = Color.FromArgb(120, 255, 140);  // highlights / header
        private static readonly Color ColCyan    = Color.FromArgb(0,  200, 220);   // [BOOT]
        private static readonly Color ColYellow  = Color.FromArgb(210, 195,   0);  // [INIT]
        private static readonly Color ColOrange  = Color.FromArgb(255, 140,   0);  // [WARN]

        // ── Line model ──────────────────────────────────────────────────────
        /// <param name="Text">Text content (empty = blank line / pause).</param>
        /// <param name="Color">Foreground colour.</param>
        /// <param name="DelayBefore">Milliseconds to pause before typing starts.</param>
        /// <param name="Speed">Milliseconds per character. 0 = instant reveal.</param>
        private record BootLine(string Text, Color Color, int DelayBefore = 0, int Speed = 0, string Timestamp = "");

        // ── State ────────────────────────────────────────────────────────────
        private readonly List<BootLine> _printed  = new();
        private readonly Queue<BootLine> _queue   = new();
        private BootLine?  _current;
        private int        _typedChars;
        private bool       _cursorOn   = true;
        private bool       _animDone;
        private bool       _allQueued;
        private int        _scrollOffset;

        // ── Pre-animation (blinking cursor phase) ──────────────────────────
        private bool _preAnimDone;
        private System.Windows.Forms.Timer? _preAnimTimer;

        // ── Background sound ─────────────────────────────────────────
        private WaveOutEvent?    _soundOut;
        private AudioFileReader? _soundReader;

        // ── RNG for realistic timing jitter ──────────────────────────────────
        private static readonly Random _rng = new();

        // ── Resources ────────────────────────────────────────────────────────
        private readonly Font   _font;
        private float           _lineH;        // cached line height (set on first paint)
        private DoubleBufferedPanel _canvas = null!;

        // ── Timers ───────────────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer _typeTimer;
        private readonly System.Windows.Forms.Timer _cursorTimer;

        // ─────────────────────────────────────────────────────────────────────
        public BootSplash()
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState     = FormWindowState.Maximized;
            BackColor       = ColBg;
            ShowInTaskbar   = false;
            TopMost         = true;

            _font  = new Font("Courier New", 16f, FontStyle.Regular, GraphicsUnit.Point);
            _lineH = _font.GetHeight() + 4f;

            _canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = ColBg };
            _canvas.Paint += Canvas_Paint;
            Controls.Add(_canvas);

            // Any key: skip animation or close when already done
            KeyPreview = true;
            KeyDown   += (_, _) => HandleSkip();

            // Cursor blink
            _cursorTimer = new System.Windows.Forms.Timer { Interval = 530 };
            _cursorTimer.Tick += (_, _) => { _cursorOn = !_cursorOn; _canvas.Invalidate(); };
            // cursor blink disabled — timer not started; will be started in pre-anim phase

            // Character clock — fired interval varies per line speed
            _typeTimer = new System.Windows.Forms.Timer { Interval = 10 };
            _typeTimer.Tick += (_, _) => TypeTick();

            // Background sound — look in Media\Sounds\ for first audio file and loop it
            try
            {
                var soundsDir = Path.Combine(AppContext.BaseDirectory, "Media", "Sounds");
                if (Directory.Exists(soundsDir))
                {
                    string? soundFile = null;
                    foreach (var ext in new[] { "*.wav", "*.mp3", "*.ogg", "*.flac" })
                    {
                        soundFile = Directory.GetFiles(soundsDir, ext, SearchOption.TopDirectoryOnly).Length > 0
                            ? Directory.GetFiles(soundsDir, ext, SearchOption.TopDirectoryOnly)[0]
                            : null;
                        if (soundFile != null) break;
                    }
                    if (soundFile != null)
                    {
                        _soundReader = new AudioFileReader(soundFile);
                        _soundOut    = new WaveOutEvent();
                        _soundOut.Init(_soundReader);
                        _soundOut.PlaybackStopped += (_, _) =>
                        {
                            if (!IsDisposed && _soundReader != null && _soundOut != null)
                            { _soundReader.Position = 0; _soundOut.Play(); }
                        };
                    }
                }
            }
            catch { /* sound is cosmetic only */ }

        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Fills the animation queue with real system and config data.
        /// Call this before ShowDialog().
        /// </summary>
        public void BuildSequence(AppConfig? cfg)
        {
            string ver    = typeof(BootSplash).Assembly.GetName().Version?.ToString(3) ?? "1.0.2";
            var    screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            string os     = RuntimeInformation.OSDescription;
            string rt     = RuntimeInformation.FrameworkDescription;

            // ── Helpers ─────────────────────────────────────────────────────
            int S(int ms) => Math.Max(0, (int)(ms * SpeedScale));
            int D(int ms) => Math.Max(0, (int)(ms * SpeedScale));

            void Blank(int delay = 0) =>
                Enq("", ColDefault, D(delay), 0);

            void Sep(int delay = 0) =>
                Enq(new string('─', 60), ColDim, D(delay), 0);

            void Init(string t, int delay = 0) =>
                Enq(t, ColYellow, D(delay), S(3));

            void Boot(string t, int delay = 0) =>
                Enq(t, ColCyan, D(delay), S(5));

            void Ok(string t, int delay = 0) =>
                Enq(t, ColDefault, D(delay), S(1));

            void Warn(string t, int delay = 0) =>
                Enq(t, ColOrange, D(delay), S(2));

            // Random pause — simulates hardware taking time between stages
            // min/max are raw ms values (will be scaled by D)
            void Pause(int minMs, int maxMs) =>
                Enq("", ColDefault, D(_rng.Next(minMs, maxMs + 1)), 0);

            // ── SYSTEM ──────────────────────────────────────────────────────
            Pause(300, 700);
            Init("[INIT] System check...", 80);
            Init($"[INIT] OS       : {Trim(os, 46)}", 30);
            Init($"[INIT] Runtime  : {Trim(rt, 46)}", 20);
            Init($"[INIT] Display  : {screen.Width}x{screen.Height}", 20);
            Init($"[INIT] Base dir : {Trim(AppContext.BaseDirectory, 46)}", 20);

            // ── CONFIG ──────────────────────────────────────────────────────
            Pause(200, 600);
            Boot("[BOOT] Loading configuration...", 60);
            Pause(80, 300);
            if (cfg != null)
            {
                Ok("[OK  ] Config loaded successfully");
                Ok($"[OK  ] Title    : {Trim(cfg.Ui.Title ?? "(none)", 46)}");
                Ok($"[OK  ] Frontends: {cfg.Options.Count} option(s) configured");
                Ok($"[OK  ] Music    : {(cfg.Music.Enabled ? "enabled — " + Trim(cfg.Music.MusicRoot ?? "(no path)", 36) : "disabled")}");
                Ok($"[OK  ] Debug log: {(cfg.Activa.Activa ? "enabled" : "disabled")}");
            }
            else
            {
                Warn("[WARN] Config file not found — defaults applied");
            }

            // ── INPUT ───────────────────────────────────────────────────────
            Pause(300, 800);
            Boot("[BOOT] Scanning input devices...", 60);
            Pause(150, 500);
            if (cfg != null)
            {
                string xiSlot = cfg.Input.XInputSlot < 0 ? "auto" : $"slot {cfg.Input.XInputSlot}";
                Ok(cfg.Input.XInputEnabled
                    ? $"[OK  ] XInput   : enabled ({xiSlot})"
                    : "[OK  ] XInput   : disabled");

                string diDev = string.IsNullOrWhiteSpace(cfg.Input.DInputDeviceName)
                    ? "any device"
                    : Trim(cfg.Input.DInputDeviceName, 38);
                Ok(cfg.Input.DInputEnabled
                    ? $"[OK  ] DInput   : enabled — {diDev}"
                    : "[OK  ] DInput   : disabled");

                Ok($"[OK  ] Nav cooldown  : {cfg.Input.NavCooldownMs} ms");
            }

            // ── MEDIA ───────────────────────────────────────────────────────
            Pause(400, 900);
            Boot("[BOOT] Pre-loading media assets...", 60);
            Pause(100, 400);

            // Count music files
            int musicCount = 0;
            if (cfg?.Music.Enabled == true && !string.IsNullOrWhiteSpace(cfg.Music.MusicRoot))
            {
                var dir = Path.IsPathRooted(cfg.Music.MusicRoot)
                    ? cfg.Music.MusicRoot
                    : Path.Combine(AppContext.BaseDirectory, cfg.Music.MusicRoot);
                if (Directory.Exists(dir))
                    foreach (var ext in new[] { "*.mod","*.xm","*.it","*.s3m","*.mp3","*.wav","*.ogg","*.flac" })
                        musicCount += Directory.GetFiles(dir, ext, SearchOption.TopDirectoryOnly).Length;
            }

            if (musicCount > 0)
                Ok($"[OK  ] Music lib : {musicCount} track(s) found");
            else if (cfg?.Music.Enabled == true)
                Warn("[WARN] Music lib : no tracks found in configured path");
            else
                Ok("[OK  ] Music lib : skipped (disabled)");

            bool hasVideo = !string.IsNullOrWhiteSpace(cfg?.Paths.VideoBackground);
            Ok(hasVideo
                ? $"[OK  ] Video bg  : {Trim(cfg!.Paths.VideoBackground, 46)}"
                : "[OK  ] Video bg  : not configured");

            // Count option images
            int imgCount = 0;
            if (cfg != null)
            {
                foreach (var opt in cfg.Options)
                {
                    if (!string.IsNullOrWhiteSpace(opt.Image))
                    {
                        var imgPath = Path.IsPathRooted(opt.Image)
                            ? opt.Image
                            : Path.Combine(AppContext.BaseDirectory, cfg.Paths.ImagesRoot, opt.Image);
                        if (File.Exists(imgPath)) imgCount++;
                    }
                }
                if (cfg.Options.Count > 0)
                    Ok($"[OK  ] Images    : {imgCount}/{cfg.Options.Count} artwork file(s) found");
            }

            // ── RENDERER ────────────────────────────────────────────────────
            Pause(300, 700);
            Boot("[BOOT] Warming up renderer...", 60);
            Pause(100, 350);

            var libVlcDir = Path.Combine(AppContext.BaseDirectory, "libvlc");
            Ok(Directory.Exists(libVlcDir)
                ? "[OK  ] LibVLC    : native libs present"
                : "[WARN] LibVLC    : native lib directory not found");
            Ok("[OK  ] Spectrum  : analyzer ready");
            Ok("[OK  ] Video     : renderer initializing");

            // ── LEDBLINKY ───────────────────────────────────────────────────
            if (cfg?.LedBlinky.Enabled == true)
            {
                Pause(200, 500);
                Boot("[BOOT] Connecting LedBlinky...", 40);
                Pause(80, 200);
                Ok($"[OK  ] LedBlinky : {Trim(cfg.LedBlinky.ExePath, 46)}");
            }

            // ── DONE ────────────────────────────────────────────────────────
            Pause(200, 500);
            Sep(0);
            Ok("  ▶  READY — LAUNCHING FRONTEND...");
            Sep(0);
            Blank(40);

            _allQueued = true;
        }

        // ── Animation engine ────────────────────────────────────────────────

        private void Enq(string text, Color color, int delayBefore, int speed) =>
            _queue.Enqueue(new BootLine(text, color, delayBefore, speed));

        private void TypeTick()
        {
            _typeTimer.Stop();

            // Still typing a line?
            if (_current != null && _typedChars < _current.Text.Length)
            {
                if (_current.Speed == 0)
                    _typedChars = _current.Text.Length; // instant: reveal whole line
                else
                    _typedChars++;
                _canvas.Invalidate();
                _typeTimer.Interval = Math.Max(1, _current.Speed);
                _typeTimer.Start();
                return;
            }

            // Finish current line
            if (_current != null)
            {
                _printed.Add(_current);
                _current     = null;
                _typedChars  = 0;
                RecalcScroll();
                _canvas.Invalidate();
            }

            // Dequeue next
            if (_queue.Count > 0)
            {
                var next = _queue.Dequeue();

                if (next.Text.Length == 0)
                {
                    // Blank line = pause
                    _printed.Add(next);
                    RecalcScroll();
                    _canvas.Invalidate();
                    _typeTimer.Interval = Math.Max(1, next.DelayBefore > 0 ? next.DelayBefore : 1);
                    _typeTimer.Start();
                }
                else
                {
                    _current = next with { Timestamp = DateTime.Now.ToString("HH:mm:ss.fff") };
                    _typeTimer.Interval = Math.Max(1, next.DelayBefore > 0 ? next.DelayBefore : 1);
                    _typeTimer.Start();
                }
                return;
            }

            // Queue empty
            if (_allQueued && !_animDone)
            {
                _animDone = true;
                _canvas.Invalidate();
                var t = new System.Windows.Forms.Timer { Interval = 1400 };
                t.Tick += (_, _) => { t.Stop(); t.Dispose(); if (!IsDisposed) Close(); };
                t.Start();
            }
        }

        private void RecalcScroll()
        {
            if (_lineH <= 0) return;
            int usable     = ClientSize.Height - 60;
            int maxVisible = (int)(usable / _lineH);
            _scrollOffset  = Math.Max(0, _printed.Count - maxVisible + 2);
        }

        private void HandleSkip()
        {
            if (_animDone) { Close(); return; }

            // During pre-anim: cancel cursor phase and jump straight to flushing
            if (!_preAnimDone)
            {
                _preAnimTimer?.Stop(); _preAnimTimer?.Dispose(); _preAnimTimer = null;
                _preAnimDone = true;
                _cursorTimer.Stop();
                _cursorOn = true;
            }

            // Flush all queued lines instantly
            _typeTimer.Stop();
            if (_current != null) { _printed.Add(_current); _current = null; _typedChars = 0; }
            while (_queue.Count > 0) _printed.Add(_queue.Dequeue());
            _animDone = true;
            RecalcScroll();
            _canvas.Invalidate();

            var t = new System.Windows.Forms.Timer { Interval = 700 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); if (!IsDisposed) Close(); };
            t.Start();
        }

        // ── Paint ────────────────────────────────────────────────────────────

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ColBg);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            _lineH = _font.GetHeight(g) + 3f;

            float y = 28f;

            // Center the text block horizontally; timestamp prefix "[HH:mm:ss.fff] " sits left of x
            float blockW = g.MeasureString(new string('─', 60), _font).Width;
            float tsW    = g.MeasureString("[00:00:00.000] ", _font).Width;
            float x      = Math.Max(tsW + 20f, (Width - blockW) / 2f);
            float tsX    = x - tsW;
            int   idx    = 0;

            // Print completed lines (with scroll offset)
            foreach (var line in _printed)
            {
                if (idx++ < _scrollOffset) continue;
                if (line.Text.Length > 0)
                {
                    if (line.Timestamp.Length > 0)
                    {
                        using var tsBr = new SolidBrush(ColDim);
                        g.DrawString($"[{line.Timestamp}] ", _font, tsBr, tsX, y);
                    }
                    using var br = new SolidBrush(line.Color);
                    g.DrawString(line.Text, _font, br, x, y);
                }
                y += _lineH;
                if (y > Height - 40) break;
            }

            // Partially typed current line
            if (_current != null && y <= Height - 40)
            {
                if (_current.Timestamp.Length > 0)
                {
                    using var tsBr = new SolidBrush(ColDim);
                    g.DrawString($"[{_current.Timestamp}] ", _font, tsBr, tsX, y);
                }
                string partial = _current.Text[.._typedChars];
                if (partial.Length > 0)
                {
                    using var br = new SolidBrush(_current.Color);
                    g.DrawString(partial, _font, br, x, y);
                }

                // Block cursor
                if (_cursorOn)
                {
                    float cx = partial.Length > 0
                        ? x + g.MeasureString(partial, _font).Width - 4
                        : x;
                    using var cb = new SolidBrush(ColBright);
                    g.FillRectangle(cb, cx, y + 2f, 10f, _lineH - 4f);
                }
                y += _lineH;
            }
            else if (!_animDone && y <= Height - 40)
            {
                // Idle cursor between lines
                if (_cursorOn)
                {
                    using var cb = new SolidBrush(ColBright);
                    g.FillRectangle(cb, x, y + 2f, 10f, _lineH - 4f);
                }
            }

            // ── CRT effects ──────────────────────────────────────────────────

            // Phosphor green tint over entire surface
            using var phosphorBr = new SolidBrush(Color.FromArgb(12, 0, 60, 10));
            g.FillRectangle(phosphorBr, 0, 0, Width, Height);

            // Scanlines — dark stripe every 2 px for interlace look
            using var scanBr = new SolidBrush(Color.FromArgb(55, 0, 0, 0));
            for (int sy = 0; sy < Height; sy += 2)
                g.FillRectangle(scanBr, 0, sy, Width, 1);

            // Vignette — darken the four edges/corners
            int vw = Width / 4;
            int vh = Height / 4;
            using var vigL = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, vw, Height),
                Color.FromArgb(120, 0, 0, 0), Color.Transparent,
                System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
            g.FillRectangle(vigL, 0, 0, vw, Height);
            using var vigR = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(Width - vw, 0, vw, Height),
                Color.Transparent, Color.FromArgb(120, 0, 0, 0),
                System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
            g.FillRectangle(vigR, Width - vw, 0, vw, Height);
            using var vigT = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, Width, vh),
                Color.FromArgb(100, 0, 0, 0), Color.Transparent,
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            g.FillRectangle(vigT, 0, 0, Width, vh);
            using var vigB = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, Height - vh, Width, vh),
                Color.Transparent, Color.FromArgb(100, 0, 0, 0),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            g.FillRectangle(vigB, 0, Height - vh, Width, vh);

            // Skip hint (bottom-right, very dim)
            using var hintFont = new Font("Courier New", 9f);
            using var hintBr   = new SolidBrush(Color.FromArgb(55, 0, 160, 50));
            const string hint  = "Press any key to skip";
            var hsz = g.MeasureString(hint, hintFont);
            g.DrawString(hint, hintFont, hintBr, Width - hsz.Width - 16, Height - hsz.Height - 10);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _soundOut?.Play();

            // 320 ms settle, then 7-second blinking cursor pre-phase before text starts
            var settle = new System.Windows.Forms.Timer { Interval = 320 };
            settle.Tick += (_, _) =>
            {
                settle.Stop(); settle.Dispose();
                _cursorTimer.Start(); // blink on for pre-anim
                _preAnimTimer = new System.Windows.Forms.Timer { Interval = 11000 };
                _preAnimTimer.Tick += (_, _) =>
                {
                    _preAnimTimer!.Stop(); _preAnimTimer.Dispose(); _preAnimTimer = null;
                    _preAnimDone = true;
                    _cursorTimer.Stop();
                    _cursorOn = true; // keep cursor visible during typing
                    _typeTimer.Start();
                };
                _preAnimTimer.Start();
            };
            settle.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _preAnimTimer?.Stop(); _preAnimTimer?.Dispose();
            _typeTimer.Stop();   _typeTimer.Dispose();
            _cursorTimer.Stop(); _cursorTimer.Dispose();
            _soundOut?.Stop();   _soundOut?.Dispose();
            _soundReader?.Dispose();
            _font.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string Trim(string s, int max) =>
            s.Length <= max ? s : "…" + s[^(max - 1)..];

        // ── Double-buffered canvas ────────────────────────────────────────────
        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint  |
                         ControlStyles.UserPaint, true);
                UpdateStyles();
            }
        }
    }
}
