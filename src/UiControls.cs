using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VnptSteamBypass
{
    internal class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color BorderColor { get; set; }
        public Color AccentColor { get; set; }

        public RoundedPanel()
        {
            CornerRadius = 12;
            BorderColor = Color.Transparent;
            AccentColor = Color.Empty;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = CreatePath(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreatePath(bounds, CornerRadius))
            {
                using (SolidBrush background = new SolidBrush(BackColor))
                {
                    e.Graphics.FillPath(background, path);
                }

                if (AccentColor != Color.Empty && Height > 50)
                {
                    Rectangle accent = new Rectangle(0, 18, 4, Height - 36);
                    using (GraphicsPath accentPath = CreatePath(accent, 2))
                    using (SolidBrush accentBrush = new SolidBrush(AccentColor))
                    {
                        e.Graphics.FillPath(accentBrush, accentPath);
                    }
                }

                if (BorderColor != Color.Transparent && BorderColor != Color.Empty)
                {
                    using (Pen border = new Pen(BorderColor))
                    {
                        e.Graphics.DrawPath(border, path);
                    }
                }
            }
        }

        internal static GraphicsPath CreatePath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return path;
            }

            int diameter = Math.Min(Math.Min(radius * 2, bounds.Width), bounds.Height);
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }

            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class HeaderPanel : Panel
    {
        public HeaderPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(
                bounds,
                Color.FromArgb(11, 18, 32),
                Color.FromArgb(19, 31, 55),
                0f))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            using (Pen line = new Pen(Color.FromArgb(30, 41, 59)))
            {
                e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public int CornerRadius { get; set; }
        public Color HoverBackColor { get; set; }
        public Color PressedBackColor { get; set; }
        public Color BorderColor { get; set; }
        public Color DisabledBackColor { get; set; }
        public Color DisabledForeColor { get; set; }

        public RoundedButton()
        {
            CornerRadius = 8;
            HoverBackColor = Color.Empty;
            PressedBackColor = Color.Empty;
            BorderColor = Color.Empty;
            DisabledBackColor = Color.FromArgb(30, 41, 59);
            DisabledForeColor = Color.FromArgb(100, 116, 139);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            TabStop = true;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = RoundedPanel.CreatePath(new Rectangle(0, 0, Width, Height), CornerRadius))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = BackColor;
            if (!Enabled)
            {
                fill = DisabledBackColor;
            }
            else if (_pressed && PressedBackColor != Color.Empty)
            {
                fill = PressedBackColor;
            }
            else if (_hovered && HoverBackColor != Color.Empty)
            {
                fill = HoverBackColor;
            }

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedPanel.CreatePath(bounds, CornerRadius))
            {
                using (SolidBrush background = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(background, path);
                }

                if (BorderColor != Color.Empty && BorderColor != Color.Transparent)
                {
                    using (Pen border = new Pen(BorderColor))
                    {
                        e.Graphics.DrawPath(border, path);
                    }
                }
            }

            Color textColor = Enabled ? ForeColor : DisabledForeColor;
            TextFormatFlags flags =
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix;

            Rectangle textBounds = new Rectangle(6, 0, Width - 12, Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor, flags);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = new Rectangle(4, 4, Width - 9, Height - 9);
                ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, textColor, fill);
            }
        }
    }
}
