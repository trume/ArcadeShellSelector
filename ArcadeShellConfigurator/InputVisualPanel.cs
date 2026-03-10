using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ArcadeShellConfigurator
{
    /// <summary>
    /// Visual panel that draws a joystick stick indicator and button grid.
    /// Used in the DirectInput and XInput test areas.
    /// </summary>
    internal sealed class InputVisualPanel : Panel
    {
        // Stick position: normalized -1.0 to 1.0
        private float _stickX;
        private float _stickY;

        // POV hat: -1 = centered, otherwise degrees (0-359)
        private int _povDegrees = -1;

        // Button states (up to 32)
        private bool[] _buttons = Array.Empty<bool>();

        // XInput right-stick position: normalized -1.0 to 1.0
        private float _rightStickX;
        private float _rightStickY;

        // XInput triggers: 0-255
        private int _leftTrigger;
        private int _rightTrigger;

        // Display mode
        private bool _isXInput;
        private string[] _xinputButtonNames = Array.Empty<string>();

        /// <summary>Force XInput layout even when idle (before the first UpdateXInput call).</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool XInputMode
        {
            get => _isXInput;
            set { _isXInput = value; Invalidate(); }
        }

        public InputVisualPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        /// <summary>Update DirectInput state and repaint.</summary>
        public void UpdateDInput(float stickX, float stickY, int povDegrees, bool[] buttons)
        {
            _stickX = Math.Clamp(stickX, -1f, 1f);
            _stickY = Math.Clamp(stickY, -1f, 1f);
            _povDegrees = povDegrees;
            _buttons = buttons ?? Array.Empty<bool>();
            _isXInput = false;
            Invalidate();
        }

        /// <summary>Update XInput state and repaint.</summary>
        public void UpdateXInput(float stickX, float stickY, float rightStickX, float rightStickY,
                                 int leftTrigger, int rightTrigger, string[] pressedButtons)
        {
            _stickX       = Math.Clamp(stickX,       -1f, 1f);
            _stickY       = Math.Clamp(stickY,       -1f, 1f);
            _rightStickX  = Math.Clamp(rightStickX,  -1f, 1f);
            _rightStickY  = Math.Clamp(rightStickY,  -1f, 1f);
            _leftTrigger  = leftTrigger;
            _rightTrigger = rightTrigger;
            _xinputButtonNames = pressedButtons ?? Array.Empty<string>();
            _isXInput = true;
            Invalidate();
        }

        /// <summary>Reset to idle state (preserves XInputMode).</summary>
        public void Reset()
        {
            _stickX = 0; _stickY = 0;
            _rightStickX = 0; _rightStickY = 0;
            _povDegrees = -1;
            _buttons = Array.Empty<bool>();
            _xinputButtonNames = Array.Empty<string>();
            _leftTrigger = 0; _rightTrigger = 0;
            // _isXInput intentionally NOT reset — preserves XInputMode
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_isXInput)
                PaintXInput(g);
            else
                PaintDInput(g);
        }

        private void PaintDInput(Graphics g)
        {
            int pad = 6;

            // --- Left side: Stick + POV ---
            int maxStick = 80;
            int stickAreaSize = Math.Min(maxStick, Math.Min(Height - pad * 2 - 50, (Width / 3) - pad));
            if (stickAreaSize < 40) stickAreaSize = 40;
            int stickCx = pad + stickAreaSize / 2;
            int stickCy = pad + stickAreaSize / 2;

            // Stick base circle
            using var baseBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
            using var basePen = new Pen(Color.FromArgb(100, 100, 100), 1.5f);
            g.FillEllipse(baseBrush, pad, pad, stickAreaSize, stickAreaSize);
            g.DrawEllipse(basePen, pad, pad, stickAreaSize, stickAreaSize);

            // Crosshair
            using var crossPen = new Pen(Color.FromArgb(60, 60, 60), 1f);
            g.DrawLine(crossPen, stickCx, pad + 4, stickCx, pad + stickAreaSize - 4);
            g.DrawLine(crossPen, pad + 4, stickCy, pad + stickAreaSize - 4, stickCy);

            // Stick dot position
            float dotRadius = stickAreaSize * 0.12f;
            float range = (stickAreaSize / 2f) - dotRadius - 3;
            float dotX = stickCx + _stickX * range;
            float dotY = stickCy + _stickY * range;

            // Glow when off-center
            bool active = Math.Abs(_stickX) > 0.1f || Math.Abs(_stickY) > 0.1f;
            var dotColor = active ? Color.FromArgb(0, 200, 80) : Color.FromArgb(160, 160, 160);
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, dotX - dotRadius, dotY - dotRadius, dotRadius * 2, dotRadius * 2);

            // "STICK" label
            using var smallFont = new Font("Segoe UI", 7f);
            using var dimBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
            var labelSize = g.MeasureString("STICK", smallFont);
            g.DrawString("STICK", smallFont, dimBrush, stickCx - labelSize.Width / 2, pad + stickAreaSize + 2);

            // --- POV hat indicator (below or beside stick) ---
            int povSize = (int)(stickAreaSize * 0.45f);
            int povCx = stickCx;
            int povTop = pad + stickAreaSize + 18;
            int povCy = povTop + povSize / 2;

            using var povBaseBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
            using var povBorderPen = new Pen(Color.FromArgb(90, 90, 90), 1f);

            // Draw POV as a small diamond/cross
            var povRect = new Rectangle(povCx - povSize / 2, povTop, povSize, povSize);
            g.FillRectangle(povBaseBrush, povRect);
            g.DrawRectangle(povBorderPen, povRect);

            // Draw 4 direction triangles
            DrawPovArrow(g, povCx, povCy, povSize, 0, _povDegrees >= 315 || (_povDegrees >= 0 && _povDegrees < 45));     // Up
            DrawPovArrow(g, povCx, povCy, povSize, 90, _povDegrees >= 45 && _povDegrees < 135);   // Right
            DrawPovArrow(g, povCx, povCy, povSize, 180, _povDegrees >= 135 && _povDegrees < 225); // Down
            DrawPovArrow(g, povCx, povCy, povSize, 270, _povDegrees >= 225 && _povDegrees < 315); // Left

            var povLabelSize = g.MeasureString("POV", smallFont);
            g.DrawString("POV", smallFont, dimBrush, povCx - povLabelSize.Width / 2, povTop + povSize + 2);

            // --- Right side: Button grid ---
            int btnAreaLeft = pad + stickAreaSize + 20;
            int btnAreaWidth = Width - btnAreaLeft - pad;
            if (btnAreaWidth < 20) return;

            int btnCount = _buttons.Length;
            if (btnCount == 0)
            {
                g.DrawString("No button data", smallFont, dimBrush, btnAreaLeft, pad + 4);
                return;
            }

            // Arrange buttons in a grid
            int cols = Math.Max(1, btnAreaWidth / 28);
            int rows = (int)Math.Ceiling((double)btnCount / cols);
            int btnSize = Math.Min(24, Math.Min((btnAreaWidth - (cols - 1) * 3) / cols, (Height - pad * 2 - 16) / rows - 3));
            if (btnSize < 12) btnSize = 12;
            int gap = 3;

            using var btnFont = new Font("Segoe UI", btnSize > 16 ? 7f : 6f);
            using var offBrush = new SolidBrush(Color.FromArgb(55, 55, 55));
            using var onBrush = new SolidBrush(Color.FromArgb(0, 180, 255));
            using var btnBorder = new Pen(Color.FromArgb(90, 90, 90), 1f);
            using var textBrush = new SolidBrush(Color.White);
            using var textOffBrush = new SolidBrush(Color.FromArgb(130, 130, 130));

            var btnLabelSize = g.MeasureString("BUTTONS", smallFont);
            g.DrawString("BUTTONS", smallFont, dimBrush, btnAreaLeft, pad - 1);

            int startY = pad + 14;
            for (int i = 0; i < btnCount && i < 32; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int bx = btnAreaLeft + col * (btnSize + gap);
                int by = startY + row * (btnSize + gap);
                if (by + btnSize > Height - pad) break;

                bool on = _buttons[i];
                var rect = new Rectangle(bx, by, btnSize, btnSize);
                g.FillRectangle(on ? onBrush : offBrush, rect);
                g.DrawRectangle(on ? Pens.White : btnBorder, rect);

                string num = (i + 1).ToString();
                var ns = g.MeasureString(num, btnFont);
                g.DrawString(num, btnFont, on ? textBrush : textOffBrush,
                    bx + (btnSize - ns.Width) / 2, by + (btnSize - ns.Height) / 2);
            }
        }

        private static void DrawPovArrow(Graphics g, int cx, int cy, int areaSize, int angleDeg, bool active)
        {
            float r = areaSize * 0.35f;
            float arrowSize = areaSize * 0.18f;
            double rad = angleDeg * Math.PI / 180.0 - Math.PI / 2; // 0 = up
            float tipX = cx + (float)(Math.Cos(rad) * r);
            float tipY = cy + (float)(Math.Sin(rad) * r);

            var color = active ? Color.FromArgb(255, 200, 0) : Color.FromArgb(80, 80, 80);
            using var brush = new SolidBrush(color);

            // Small triangle pointing outward
            double perpRad = rad + Math.PI / 2;
            float baseOff = arrowSize * 0.5f;
            float backOff = arrowSize * 0.8f;
            var pts = new PointF[]
            {
                new(tipX, tipY),
                new(tipX - (float)(Math.Cos(rad) * backOff) + (float)(Math.Cos(perpRad) * baseOff),
                    tipY - (float)(Math.Sin(rad) * backOff) + (float)(Math.Sin(perpRad) * baseOff)),
                new(tipX - (float)(Math.Cos(rad) * backOff) - (float)(Math.Cos(perpRad) * baseOff),
                    tipY - (float)(Math.Sin(rad) * backOff) - (float)(Math.Sin(perpRad) * baseOff)),
            };
            g.FillPolygon(brush, pts);
        }

        private void PaintXInput(Graphics g)
        {
            int pad = 6;

            using var baseBrush = new SolidBrush(Color.FromArgb(60, 60, 60));
            using var basePen   = new Pen(Color.FromArgb(100, 100, 100), 1.5f);
            using var crossPen  = new Pen(Color.FromArgb(60, 60, 60), 1f);
            using var smallFont = new Font("Segoe UI", 7f);
            using var dimBrush  = new SolidBrush(Color.FromArgb(120, 120, 120));

            // ── Stick size: fit two sticks + two trigger bars across the top ──
            // Reserve at least 140px on the right for trigger bars; split the rest between sticks.
            int trigAreaW    = Math.Max(100, (Width - pad * 2) / 3);
            int sticksTotalW = Width - pad * 2 - trigAreaW - pad;
            int stickAreaSize = Math.Min(80, Math.Max(36, sticksTotalW / 2 - 8));
            int topH          = stickAreaSize + 18;   // circle + "X STICK" label

            void DrawStick(int cx, int cy, int size, float nx, float ny, string label)
            {
                int left = cx - size / 2;
                int top  = cy - size / 2;
                g.FillEllipse(baseBrush, left, top, size, size);
                g.DrawEllipse(basePen,   left, top, size, size);
                g.DrawLine(crossPen, cx, top + 4,    cx,   top + size - 4);
                g.DrawLine(crossPen, left + 4, cy, left + size - 4, cy);

                float dotR  = size * 0.12f;
                float range = (size / 2f) - dotR - 3;
                float dx    = cx + nx * range;
                float dy    = cy - ny * range;  // XInput Y is inverted
                bool  active = Math.Abs(nx) > 0.08f || Math.Abs(ny) > 0.08f;
                using var dotBrush = new SolidBrush(active ? Color.FromArgb(0, 200, 80) : Color.FromArgb(160, 160, 160));
                g.FillEllipse(dotBrush, dx - dotR, dy - dotR, dotR * 2, dotR * 2);

                var lsz = g.MeasureString(label, smallFont);
                g.DrawString(label, smallFont, dimBrush, cx - lsz.Width / 2, top + size + 2);
            }

            // Left stick
            int lsCx = pad + stickAreaSize / 2;
            int lsCy = pad + stickAreaSize / 2;
            DrawStick(lsCx, lsCy, stickAreaSize, _stickX, _stickY, "L STICK");

            // Right stick
            int rsCx = pad + stickAreaSize + 10 + stickAreaSize / 2;
            int rsCy = pad + stickAreaSize / 2;
            DrawStick(rsCx, rsCy, stickAreaSize, _rightStickX, _rightStickY, "R STICK");

            // Trigger bars — occupy the right portion of the top row
            int trigX      = pad + stickAreaSize * 2 + 18;
            int trigW      = Width - trigX - pad;
            int barH       = 18;
            int ltY        = pad + (stickAreaSize / 2 - barH - 4);
            int rtY        = pad + (stickAreaSize / 2 + 4);
            DrawTriggerBar(g, "LT", trigX, ltY, trigW, barH, _leftTrigger  / 255f);
            DrawTriggerBar(g, "RT", trigX, rtY, trigW, barH, _rightTrigger / 255f);

            // ── Button tags — full width below the analog section ──
            int btnY = pad + topH + 6;

            using var tagFont      = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var tagBrush     = new SolidBrush(Color.FromArgb(0, 180, 255));
            using var tagBgBrush   = new SolidBrush(Color.FromArgb(50, 70, 90));
            using var tagTextBrush = new SolidBrush(Color.White);
            using var sepPen       = new Pen(Color.FromArgb(70, 70, 70), 1f);

            g.DrawLine(sepPen, pad, btnY - 2, Width - pad, btnY - 2);
            var bHdr = g.MeasureString("BUTTONS", smallFont);
            g.DrawString("BUTTONS", smallFont, dimBrush, pad, btnY);

            if (_xinputButtonNames.Length == 0)
            {
                using var idleFont = new Font("Segoe UI", 8.5f, FontStyle.Italic);
                g.DrawString("—", idleFont, dimBrush, pad + bHdr.Width + 6, btnY);
                return;
            }

            int tx = pad;
            int ty = btnY + (int)bHdr.Height + 2;
            int tagPadH = 6, tagPadV = 3, tagGap = 5;
            foreach (var name in _xinputButtonNames)
            {
                var sz  = g.MeasureString(name, tagFont);
                int tw  = (int)sz.Width  + tagPadH * 2;
                int th  = (int)sz.Height + tagPadV * 2;
                if (tx + tw > Width - pad) { tx = pad; ty += th + tagGap; }
                if (ty + th > Height - pad) break;

                var tagRect = new Rectangle(tx, ty, tw, th);
                using var tagPath = RoundRect(tagRect, 4);
                g.FillPath(tagBgBrush, tagPath);
                g.DrawPath(new Pen(tagBrush, 1f), tagPath);
                g.DrawString(name, tagFont, tagTextBrush, tx + tagPadH, ty + tagPadV);
                tx += tw + tagGap;
            }
        }

        private void DrawTriggerBar(Graphics g, string label, int x, int y, int width, int height, float value)
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(50, 50, 50));
            using var borderPen = new Pen(Color.FromArgb(90, 90, 90));
            g.FillRectangle(bgBrush, x, y, width, height);
            g.DrawRectangle(borderPen, x, y, width, height);

            int fillW = (int)(width * Math.Clamp(value, 0f, 1f));
            if (fillW > 0)
            {
                var fillColor = value > 0.5f ? Color.FromArgb(255, 120, 0) : Color.FromArgb(0, 160, 220);
                using var fillBrush = new SolidBrush(fillColor);
                g.FillRectangle(fillBrush, x + 1, y + 1, fillW - 1, height - 1);
            }

            using var font = new Font("Segoe UI", 7f);
            using var brush = new SolidBrush(Color.FromArgb(180, 180, 180));
            g.DrawString(label, font, brush, x + 2, y + 1);
        }

        private static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
