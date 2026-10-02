using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// Windows 95 title bar: navy gradient caption, system menu box and the classic
    /// minimise/maximise/close buttons. Drawn entirely by hand so it never inherits
    /// the Windows 10/11 acrylic style.
    /// </summary>
    public class Win95TitleBar : Control
    {
        private bool _pressedClose;
        private bool _pressedMin;
        private bool _pressedMax;
        private bool _closeHover;
        private bool _minHover;
        private bool _maxHover;

        // Snapshots of the pressed state taken at mouse-down, read at mouse-up. Without
        // these the caption buttons could never act on a single click.
        private bool _wasClosePressed;
        private bool _wasMinPressed;
        private bool _wasMaxPressed;

        public Win95TitleBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Win95Style.CreateBoldFont();
            Height = 20;
            BackColor = Win95Style.TitleActive1;
        }

        public event EventHandler CloseClicked;
        public event EventHandler MinimizeClicked;
        public event EventHandler MaximizeClicked;
        public event EventHandler SystemMenuRequested;

        public bool MaximizeBox = true;

        private const int ButtonWidth = 18;
        private const int ButtonHeight = 16;

        private Rectangle MinimizeButtonRect
        {
            get { return new Rectangle(Width - (ButtonWidth * 3) - 6, 3, ButtonWidth, ButtonHeight); }
        }

        private Rectangle MaximizeButtonRect
        {
            get { return new Rectangle(Width - (ButtonWidth * 2) - 6, 3, ButtonWidth, ButtonHeight); }
        }

        private Rectangle CloseButtonRect
        {
            get { return new Rectangle(Width - ButtonWidth - 4, 3, ButtonWidth, ButtonHeight); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            // The Win95 active caption used a left-to-right gradient.
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(Width, 1), Height),
                Win95Style.TitleActive1, Win95Style.TitleActive2, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, 0, 0, Width, Height);
            }

            // System menu icon box.
            Rectangle iconBox = new Rectangle(3, 3, 14, 14);
            g.FillRectangle(new SolidBrush(Win95Style.Face), iconBox.X, iconBox.Y, iconBox.Width, iconBox.Height);
            Win95Style.DrawRaisedBorder(g, iconBox, 1);
            DrawAppIconGlyph(g, iconBox.X + 2, iconBox.Y + 2);

            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(21, 0, Width - 140, Height),
                Win95Style.TitleText,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            DrawMinMax(g);
            DrawClose(g);
        }

        /// <summary>
        /// Draws a tiny pixel-art globe with a query tracer line, used as the system
        /// menu icon. Deliberately hand-drawn so the caption never inherits the modern
        /// Windows 11 title bar treatment.
        /// </summary>
        private static void DrawAppIconGlyph(Graphics g, int x, int y)
        {
            Color sea = Color.FromArgb(0x00, 0x80, 0xFF);
            Color seaDark = Color.FromArgb(0x00, 0x40, 0xA8);
            Color land = Color.FromArgb(0x00, 0xC0, 0x00);
            Color grid = Color.White;

            // 10x10 globe body.
            Win95Style.FillRect(g, sea, x, y + 1, 10, 8);
            Win95Style.DrawEdgePairPublic(g, new Rectangle(x, y + 1, 10, 8),
                                          Color.FromArgb(0xC0, 0xE8, 0xFF), seaDark);

            // Two land masses so the sphere reads as a globe rather than a blue box.
            Win95Style.FillRect(g, land, x + 1, y + 2, 3, 2);
            Win95Style.FillRect(g, land, x + 5, y + 5, 3, 3);

            // Meridians.
            Win95Style.DrawLine(g, grid, x + 3, y + 1, x + 3, y + 8);
            Win95Style.DrawLine(g, grid, x + 6, y + 1, x + 6, y + 8);
            Win95Style.DrawLine(g, grid, x, y + 4, x + 9, y + 4);
            Win95Style.DrawLine(g, grid, x, y + 6, x + 9, y + 6);
        }

        private void DrawMinMax(Graphics g)
        {
            Rectangle min = MinimizeButtonRect;
            Rectangle max = MaximizeButtonRect;

            DrawCaptionButton(g, min, _pressedMin, false);
            if (MaximizeBox)
                DrawCaptionButton(g, max, _pressedMax, false);

            // Minimise glyph: a small bar along the bottom edge.
            int minOffset = _pressedMin ? 1 : 0;
            Win95Style.FillRect(g, Win95Style.WindowText, min.X + 5 + minOffset, min.Bottom - 5 + minOffset, 8, 2);

            if (!MaximizeBox)
                return;

            // Maximise glyph: thick top bar plus an outlined box.
            int o = _pressedMax ? 1 : 0;
            int x = max.X + 4 + o;
            int y = max.Y + 4 + o;

            Win95Style.FillRect(g, Win95Style.WindowText, x, y, 10, 2);
            Win95Style.DrawLine(g, Win95Style.WindowText, x, y + 2, x, y + 7);
            Win95Style.DrawLine(g, Win95Style.WindowText, x + 9, y + 2, x + 9, y + 7);
            Win95Style.DrawLine(g, Win95Style.WindowText, x, y + 7, x + 9, y + 7);
        }

        private void DrawClose(Graphics g)
        {
            Rectangle close = CloseButtonRect;
            DrawCaptionButton(g, close, _pressedClose, true);

            // Two crossed strokes, drawn thick like the original.
            int ox = close.X + 6 + (_pressedClose ? 1 : 0);
            int oy = close.Y + 4 + (_pressedClose ? 1 : 0);
            using (Pen p = new Pen(Win95Style.WindowText, 2))
            {
                g.DrawLine(p, ox, oy, ox + 7, oy + 7);
                g.DrawLine(p, ox + 7, oy, ox, oy + 7);
            }
        }

        private static void DrawCaptionButton(Graphics g, Rectangle r, bool pressed, bool isClose)
        {
            if (pressed)
            {
                Win95Style.FillRect(g, Win95Style.TitleActive1, r.X, r.Y, r.Width, r.Height);
                Win95Style.DrawPressedBorder(g, r);
            }
            else
            {
                Win95Style.FillRect(g, Win95Style.Face, r.X, r.Y, r.Width, r.Height);
                Win95Style.DrawRaisedBorder(g, r, 2);
            }
        }

        private void HandleCaptionClick(MouseEventArgs e)
        {
            // NOTE: the pressed flags are captured by the caller *before* they are
            // cleared. Testing them after clearing them (the previous behaviour) meant
            // the caption buttons could never fire on a single click.
            if (CloseButtonRect.Contains(e.Location))
            {
                if (_wasClosePressed && Enabled)
                    Raise(CloseClicked);
                return;
            }

            if (MinimizeButtonRect.Contains(e.Location))
            {
                if (_wasMinPressed && Enabled)
                    Raise(MinimizeClicked);
                return;
            }

            if (MaximizeBox && MaximizeButtonRect.Contains(e.Location))
            {
                if (_wasMaxPressed && Enabled)
                    Raise(MaximizeClicked);
                return;
            }

            // Clicking the icon box opens the system menu.
            if (e.Location.X < 20)
                Raise(SystemMenuRequested);
        }

        private static void Raise(EventHandler handler)
        {
            if (handler != null)
                handler(null, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _pressedClose = CloseButtonRect.Contains(e.Location);
            _pressedMin = MinimizeButtonRect.Contains(e.Location);
            _pressedMax = MaximizeBox && MaximizeButtonRect.Contains(e.Location);

            // Remember the press so the matching button can act on mouse-up.
            _wasClosePressed = _pressedClose;
            _wasMinPressed = _pressedMin;
            _wasMaxPressed = _pressedMax;

            Invalidate();

            // Press on the caption body (not a button, not the icon box) starts a
            // window drag. The system menu box keeps its own click behaviour.
            if (!_pressedClose && !_pressedMin && !_pressedMax && e.Location.X >= 20)
                StartDrag();
        }

        /// <summary>Hands the drag to the owning form (native caption move loop).</summary>
        private void StartDrag()
        {
            Win95Form form = FindForm() as Win95Form;
            if (form != null)
                form.BeginCaptionDrag();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;

            _pressedClose = false;
            _pressedMin = false;
            _pressedMax = false;
            Invalidate();

            HandleCaptionClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool newClose = CloseButtonRect.Contains(e.Location);
            bool newMin = MinimizeButtonRect.Contains(e.Location);
            bool newMax = MaximizeBox && MaximizeButtonRect.Contains(e.Location);

            if (newClose != _closeHover || newMin != _minHover || newMax != _maxHover)
            {
                _closeHover = newClose;
                _minHover = newMin;
                _maxHover = newMax;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _closeHover = false;
            _minHover = false;
            _maxHover = false;
            Invalidate();
        }

        /// <summary>
        /// Double-clicking the caption toggles maximise, which is standard Windows
        /// behaviour. It is additive only: single clicks on the caption buttons keep
        /// working because those are handled in OnMouseUp.
        /// </summary>
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            // Never let a double-click on a caption button toggle maximise as well;
            // the single-click action has already run.
            if (CloseButtonRect.Contains(e.Location) ||
                MinimizeButtonRect.Contains(e.Location) ||
                (MaximizeBox && MaximizeButtonRect.Contains(e.Location)))
            {
                return;
            }

            if (e.Location.X < 20)
                return;

            Raise(MaximizeClicked);
        }
    }
/// <summary>
    /// Windows 95 status bar: a sunken strip divided into resizable panes, each with
    /// a crisp etched separator. Supports the classic "gripper" on either edge.
    /// </summary>
    public class Win95StatusBarControl : Control
    {
        private string[] _panes;
        private bool _showGripper = true;

        public Win95StatusBarControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Win95Style.CreateFont();
            Height = 20;
            BackColor = Win95Style.Face;
        }

        /// <summary>Text for each pane, left to right.</summary>
        public string[] Panes
        {
            get { return _panes ?? new string[0]; }
            set { _panes = value; Invalidate(); }
        }

        /// <summary>Relative widths (0-1) matching the pane array.</summary>
        public float[] PaneWeights = new float[] { 0.72f, 0.28f };

        public bool ShowGripper
        {
            get { return _showGripper; }
            set { _showGripper = value; Invalidate(); }
        }

        public void SetPanes(string primary, string secondary)
        {
            _panes = new string[] { primary ?? string.Empty, secondary ?? string.Empty };
            PaneWeights = new float[] { 0.72f, 0.28f };
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            // The status bar sits in a 1px sunken well.
            Win95Style.DrawSunkenBorder(g, new Rectangle(0, 0, Width, Height), 1);

            string[] panes = Panes;
            if (panes.Length == 0)
                return;

            int left = 3;
            int usable = Width - 6;

            for (int i = 0; i < panes.Length; i++)
            {
                float weight = (i < PaneWeights.Length) ? PaneWeights[i] : (1f / panes.Length);
                int w = (i == panes.Length - 1)
                    ? (Width - left - 3)
                    : (int)(usable * weight);

                if (w <= 0)
                    break;

                Rectangle pane = new Rectangle(left, 2, w, Height - 4);
                TextRenderer.DrawText(g, panes[i], Font, pane,
                    Win95Style.WindowText, TextFormatFlags.NoPadding |
                                           TextFormatFlags.VerticalCenter |
                                           TextFormatFlags.EndEllipsis |
                                           TextFormatFlags.NoPrefix);

                left += w;

                // Etched groove between panes.
                if (i < panes.Length - 1)
                {
                    Win95Style.DrawLine(g, Win95Style.FaceDark, left, 2, left, Height - 3);
                    Win95Style.DrawLine(g, Win95Style.FaceLight, left + 1, 2, left + 1, Height - 3);
                    left += 2;
                }
            }

            if (_showGripper)
            {
                // Classic size grip: two sets of raised diagonal lines.
                int gx = Width - 15;
                int gy = Height - 13;

                for (int setIndex = 0; setIndex < 2; setIndex++)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int x = gx + (i * 3) + (setIndex * 2);
                        int y = gy + (i * 3);

                        Win95Style.DrawLine(g, Win95Style.FaceLight, x, y + 3, x + 3, y);
                        Win95Style.DrawLine(g, Win95Style.FaceDark, x, y + 4, x + 4, y);
                    }
                }
            }
        }
    }
}
