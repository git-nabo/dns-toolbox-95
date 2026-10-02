using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// Windows 95 (Plus!) palette, metrics and 3D border painters.
    /// Colours are the exact values from the original 16-colour VGA palette.
    /// </summary>
    public static class Win95Style
    {
        // Base colours
        public static readonly Color Face = Color.FromArgb(0xC0, 0xC0, 0xC0);
        public static readonly Color FaceLight = Color.FromArgb(0xDF, 0xDF, 0xDF);
        public static readonly Color FaceDark = Color.FromArgb(0x80, 0x80, 0x80);
        public static readonly Color FaceShadow = Color.FromArgb(0x00, 0x00, 0x00);
        public static readonly Color ButtonText = Color.FromArgb(0x00, 0x00, 0x00);
        public static readonly Color ButtonTextDisabled = Color.FromArgb(0x80, 0x80, 0x80);
        public static readonly Color WindowText = Color.FromArgb(0x00, 0x00, 0x00);
        public static readonly Color WindowFrame = Color.FromArgb(0x00, 0x00, 0x00);

        // Title bar (active = gradient navy to mid-blue)
        public static readonly Color TitleActive1 = Color.FromArgb(0x00, 0x00, 0x80);
        public static readonly Color TitleActive2 = Color.FromArgb(0x10, 0x84, 0xD0);
        public static readonly Color TitleInactive1 = Color.FromArgb(0x80, 0x80, 0x80);
        public static readonly Color TitleInactive2 = Color.FromArgb(0xB5, 0xB5, 0xB5);
        public static readonly Color TitleText = Color.White;

        // Fields / lists
        public static readonly Color FieldBack = Color.White;
        public static readonly Color FieldText = Color.Black;
        public static readonly Color SelectionBack = Color.FromArgb(0x00, 0x00, 0x80);
        public static readonly Color SelectionText = Color.White;
        public static readonly Color SelectionInactiveBack = Color.FromArgb(0xC0, 0xC0, 0xC0);

        // Metrics (in unscaled pixels at 96 DPI)
        public const int BorderWidth = 1;
        public const int ButtonHeight = 23;
        public const int CaptionHeight = 18;
        public const int MenuHeight = 19;
        public const int StatusHeight = 20;

        /// <summary>Classic UI font, falling back to Tahoma when MS Sans Serif is absent.</summary>
        public static Font CreateFont()
        {
            try
            {
                return new Font("MS Sans Serif", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                try
                {
                    return new Font("Tahoma", 8f, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch
                {
                    return new Font("Microsoft Sans Serif", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
                }
            }
        }

        public static Font CreateBoldFont()
        {
            try
            {
                return new Font("MS Sans Serif", 8.25f, FontStyle.Bold, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Tahoma", 8f, FontStyle.Bold, GraphicsUnit.Point);
            }
        }

        /// <summary>Single-pixel square-point brush helper (no smoothing, no aliasing).</summary>
        public static void FillRect(Graphics g, Color c, int x, int y, int w, int h)
        {
            using (SolidBrush b = new SolidBrush(c))
                g.FillRectangle(b, x, y, w, h);
        }

        public static void DrawLine(Graphics g, Color c, int x1, int y1, int x2, int y2)
        {
            using (Pen p = new Pen(c))
                g.DrawLine(p, x1, y1, x2, y2);
        }
/// <summary>
        /// Raised 3D border: light on top/left, dark on bottom/right.
        /// depth=1 is a thin field border, depth=2 is a full button border.
        /// </summary>
        public static void DrawRaisedBorder(Graphics g, Rectangle r, int depth)
        {
            if (depth < 1) depth = 1;

            DrawEdgePair(g, r, FaceLight, FaceShadow);

            if (depth >= 2)
            {
                Rectangle inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                DrawEdgePair(g, inner, Color.White, FaceDark);
            }
        }

        /// <summary>Sunken 3D border: dark on top/left, light on bottom/right.</summary>
        public static void DrawSunkenBorder(Graphics g, Rectangle r, int depth)
        {
            if (depth < 1) depth = 1;

            DrawEdgePair(g, r, FaceDark, Color.White);

            if (depth >= 2)
            {
                Rectangle inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                DrawEdgePair(g, inner, FaceShadow, FaceLight);
            }
        }

        /// <summary>Pressed state for a button: fully inverted border.</summary>
        public static void DrawPressedBorder(Graphics g, Rectangle r)
        {
            DrawEdgePair(g, r, FaceShadow, Color.White);
            Rectangle inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            DrawEdgePair(g, inner, FaceDark, FaceLight);
        }

        /// <summary>
        /// Draws the classic etched group-box frame: a sunken line followed by a
        /// raised line, with a gap where the legend text sits.
        /// </summary>
        public static void DrawEtchedFrame(Graphics g, Rectangle r)
        {
            DrawEdgePair(g, r, FaceDark, Color.White);

            Rectangle inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            DrawEdgePair(g, inner, Color.White, FaceDark);
        }

        private static void DrawEdgePair(Graphics g, Rectangle r, Color topLeft, Color bottomRight)
        {
            DrawLine(g, topLeft, r.Left, r.Top, r.Right - 1, r.Top);
            DrawLine(g, topLeft, r.Left, r.Top, r.Left, r.Bottom - 1);
            DrawLine(g, bottomRight, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
            DrawLine(g, bottomRight, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
        }

        /// <summary>Public wrapper for drawing a single 2px bevel edge pair.</summary>
        public static void DrawEdgePairPublic(Graphics g, Rectangle r, Color topLeft, Color bottomRight)
        {
            DrawEdgePair(g, r, topLeft, bottomRight);
        }

        /// <summary>Shared bold classic font instance (created once, reused).</summary>
        private static Font _boldFont;

        public static Font BoldFont
        {
            get
            {
                if (_boldFont == null)
                    _boldFont = CreateBoldFont();
                return _boldFont;
            }
        }

        /// <summary>Shared regular classic font instance (created once, reused).</summary>
        private static Font _regularFont;

        public static Font CreateFontSafe()
        {
            if (_regularFont == null)
                _regularFont = CreateFont();
            return _regularFont;
        }

        /// <summary>Draws the classic focus rectangle (1px dotted line).</summary>
        public static void DrawFocusRect(Graphics g, Rectangle r)
        {
            using (Pen p = new Pen(WindowText))
            {
                p.DashStyle = DashStyle.Dot;
                g.DrawLine(p, r.Left, r.Top, r.Right - 1, r.Top);
                g.DrawLine(p, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
                g.DrawLine(p, r.Right - 1, r.Bottom - 1, r.Left, r.Bottom - 1);
                g.DrawLine(p, r.Left, r.Bottom - 1, r.Left, r.Top);
            }
        }

        /// <summary>
        /// Fills a control background with the classic 50% dither pattern used for
        /// disabled text and unfilled areas.
        /// </summary>
        public static void FillDither(Graphics g, Rectangle r, Color fore, Color back)
        {
            using (SolidBrush b = new SolidBrush(back))
                g.FillRectangle(b, r);

            using (SolidBrush b = new SolidBrush(fore))
            {
                for (int y = r.Top; y < r.Bottom; y += 2)
                {
                    for (int x = r.Left; x < r.Right; x += 2)
                        g.FillRectangle(b, x, y, 1, 1);
                }
            }
        }

        /// <summary>Enables crisp, non-antialiased Win95-style drawing.</summary>
        public static void PrepareGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }
    }
}