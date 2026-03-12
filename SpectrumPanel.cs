using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Owner-drawn panel that renders 6 vertical spectrum bars.
    /// Refreshes at ~60 FPS via an internal timer.
    /// </summary>
    internal sealed class SpectrumPanel : Panel
    {
        private readonly SpectrumAnalyzer _analyzer;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly float[] _levels;

        private static readonly Color BarColor = Color.White;

        public SpectrumPanel(SpectrumAnalyzer analyzer)
        {
            _analyzer = analyzer;
            _levels = new float[analyzer.BandCount];

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);

            BackColor = Color.Transparent;

            _refreshTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 FPS
            _refreshTimer.Tick += (_, __) =>
            {
                _analyzer.GetBands(_levels);
                Invalidate();
            };
        }

        public void StartRefresh() => _refreshTimer.Start();
        public void StopRefresh() => _refreshTimer.Stop();

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            int bands = _analyzer.BandCount;
            int gap = 3;
            int totalGaps = (bands - 1) * gap;
            int barWidth = Math.Max(3, (Width - totalGaps) / bands);

            for (int i = 0; i < bands; i++)
            {
                float level = Math.Clamp(_levels[i], 0f, 1f);
                int barHeight = (int)(level * Height);
                int x = i * (barWidth + gap);
                int y = Height - barHeight;

                using var brush = new SolidBrush(BarColor);
                g.FillRectangle(brush, x, y, barWidth, barHeight);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
