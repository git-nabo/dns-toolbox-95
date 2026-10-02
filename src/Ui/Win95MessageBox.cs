using System;
using System.Drawing;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    public enum Win95Icon
    {
        None,
        Information,
        Warning,
        Error,
        Question
    }

    /// <summary>
    /// A hand-drawn Windows 95 message box: 32x32 pixel-art icon, sunken text area
    /// and classic [ OK ] / [ Yes ] / [ No ] buttons. Used for every error so the
    /// user never sees a modern dialog or a raw stack trace.
    /// </summary>
    public sealed class Win95MessageBox : Win95Form
    {
        private readonly Win95Icon _icon;
        private readonly string _text;
        private readonly string _detail;

        public Win95MessageBox(IWin32Window owner, string caption, string text, string detail,
                                Win95Icon icon, bool yesNo)
        {
            _icon = icon;
            _text = text ?? string.Empty;
            _detail = detail;
            _owner = owner;

            BuildChrome(caption);
            CloseOnEscape = true;

            int bodyWidth = yesNo ? 430 : 400;
            int bodyHeight = MeasureBodyHeight(bodyWidth, yesNo);

            Width = bodyWidth + 16;
            Height = bodyHeight + 20 + 37 + 8;

            if (owner is Form)
                StartPosition = FormStartPosition.CenterParent;
            else
                StartPosition = FormStartPosition.CenterScreen;

            Panel message = new Panel();
            message.Dock = DockStyle.Top;
            message.Height = bodyHeight;
            message.BackColor = Win95Style.Face;
            message.Paint += delegate(object sender, PaintEventArgs e)
            {
                PaintMessage(g: e.Graphics, panel: message);
            };
            ClientSurface.Controls.Add(message);

            Panel buttonBar = new Panel();
            buttonBar.Dock = DockStyle.Bottom;
            buttonBar.Height = 37;
            buttonBar.BackColor = Win95Style.Face;

            if (yesNo)
            {
                AddButton(buttonBar, "&Yes", 90, DialogResult.Yes);
                AddButton(buttonBar, "&No", 90, DialogResult.No);
            }
            else
            {
                AddButton(buttonBar, "OK", 90, DialogResult.OK);
            }

            ClientSurface.Controls.Add(buttonBar);
        }

        private readonly IWin32Window _owner;

        /// <summary>
        /// Measures the height needed for the message and optional detail box so long
        /// text (for example the About text) is never clipped.
        /// </summary>
        private int MeasureBodyHeight(int bodyWidth, bool yesNo)
        {
            const int pad = 16;
            int textLeft = (_icon == Win95Icon.None) ? pad : 60;
            int textWidth = bodyWidth - textLeft - pad;

            int height = 16; // top margin

            using (Graphics g = CreateGraphics())
            {
                SizeF measured = g.MeasureString(_text, Win95Style.CreateFontSafe(),
                    textWidth, System.Drawing.StringFormat.GenericTypographic);
                _measuredTextHeight = (int)Math.Ceiling(measured.Height);
            }

            height += _measuredTextHeight + 8;

            if (!string.IsNullOrEmpty(_detail))
            {
                // The detail sits in a sunken box. Measure it properly so long text
                // (for example the About box) is never clipped.
                int detailHeight;
                using (Graphics g = CreateGraphics())
                {
                    SizeF d = g.MeasureString(_detail, Win95Style.CreateFontSafe(),
                        textWidth, System.Drawing.StringFormat.GenericTypographic);
                    detailHeight = (int)Math.Ceiling(d.Height);
                }

                // Room for the border and a little internal padding.
                detailHeight += 12;

                // Keep it sensible for very long diagnostics text.
                if (detailHeight < 40) detailHeight = 40;
                if (detailHeight > 260) detailHeight = 260;

                _detailBoxHeight = detailHeight;
                height += 8 + detailHeight;
            }
            else
            {
                _detailBoxHeight = 0;
                height += 8;
            }

            return height;
        }

        private int _detailBoxHeight;
        private int _measuredTextHeight;

        private void PaintMessage(Graphics g, Panel panel)
        {
            Win95Style.PrepareGraphics(g);
            Win95Style.FillRect(g, Win95Style.Face, 0, 0, panel.Width, panel.Height);

            if (_icon != Win95Icon.None)
                DrawIcon(g, 16, 14, _icon);

            int textLeft = (_icon == Win95Icon.None) ? 16 : 60;
            int textWidth = panel.Width - textLeft - 16;

            TextRenderer.DrawText(g, _text, Win95Style.CreateFontSafe(),
                new Rectangle(textLeft, 16, textWidth, Math.Max(14, _measuredTextHeight)),
                Win95Style.WindowText,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);

            if (string.IsNullOrEmpty(_detail))
                return;

            int top = 16 + _measuredTextHeight + 8;
            Rectangle box = new Rectangle(textLeft, top, textWidth, _detailBoxHeight);
            Win95Style.FillRect(g, Win95Style.FieldBack, box.X, box.Y, box.Width, box.Height);
            Win95Style.DrawSunkenBorder(g, box, 2);

            TextRenderer.DrawText(g, _detail, Win95Style.CreateFontSafe(),
                new Rectangle(box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8),
                Win95Style.FieldText,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        }

        private void AddButton(Panel bar, string caption, int width, DialogResult result)
        {
            ClassicButton b = new ClassicButton();
            b.Text = caption;
            b.Width = width;
            b.Height = 23;
            b.IsDefaultButton = true;
            b.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            b.Top = 7;
            b.Left = bar.Width - width - 16;

            if (result == DialogResult.No)
                b.Left = bar.Width - width - 16;
            else if (result == DialogResult.Yes)
                b.Left = bar.Width - (width * 2) - 30;

            b.Click += delegate
            {
                DialogResult = result;
                Close();
            };
            bar.Controls.Add(b);
        }
/// <summary>Draws a chunky 32x32 pixel-art icon in the classic style.</summary>
        internal static void DrawIcon(Graphics g, int x, int y, Win95Icon icon)
        {
            Rectangle r = new Rectangle(x, y, 32, 32);

            switch (icon)
            {
                case Win95Icon.Error:
                    FillOctagon(g, r, Color.FromArgb(0xC0, 0x00, 0x00), Color.FromArgb(0x80, 0x00, 0x00));
                    DrawGlyph(g, x, y, "X", Color.White);
                    break;

                case Win95Icon.Warning:
                    FillTriangle(g, r, Color.FromArgb(0xFF, 0xD8, 0x00), Color.FromArgb(0xC0, 0xA0, 0x00));
                    DrawGlyph(g, x, y, "!", Color.Black);
                    break;

                case Win95Icon.Question:
                    FillOctagon(g, r, Color.FromArgb(0x00, 0x00, 0xC0), Color.FromArgb(0x00, 0x00, 0x80));
                    DrawGlyph(g, x, y, "?", Color.White);
                    break;

                case Win95Icon.Information:
                    FillSpeechBubble(g, r);
                    DrawGlyph(g, x, y, "i", Color.FromArgb(0x00, 0x00, 0xC0));
                    break;
            }
        }

        private static void FillOctagon(Graphics g, Rectangle r, Color fill, Color edge)
        {
            Win95Style.FillRect(g, fill, r.X + 3, r.Y, r.Width - 6, r.Height);
            Win95Style.FillRect(g, fill, r.X, r.Y + 3, r.Width, r.Height - 6);

            Win95Style.DrawLine(g, edge, r.X + 4, r.Y, r.Right - 5, r.Y);
            Win95Style.DrawLine(g, edge, r.X + 4, r.Y, r.X, r.Y + 4);
            Win95Style.DrawLine(g, edge, r.Right - 5, r.Y, r.Right - 1, r.Y + 4);
            Win95Style.DrawLine(g, edge, r.Right - 1, r.Y + 4, r.Right - 1, r.Bottom - 5);
            Win95Style.DrawLine(g, edge, r.Right - 1, r.Bottom - 5, r.Right - 5, r.Bottom - 1);
            Win95Style.DrawLine(g, edge, r.Right - 5, r.Bottom - 1, r.X + 4, r.Bottom - 1);
            Win95Style.DrawLine(g, edge, r.X + 4, r.Bottom - 1, r.X, r.Bottom - 5);
            Win95Style.DrawLine(g, edge, r.X, r.Bottom - 5, r.X, r.Y + 4);
        }

        private static void FillTriangle(Graphics g, Rectangle r, Color fill, Color edge)
        {
            int cx = r.X + (r.Width / 2);
            int top = r.Y + 2;
            int bottom = r.Bottom - 6;

            for (int y = top; y < bottom; y++)
            {
                int span = (int)((double)(y - top) / (bottom - top) * ((r.Width / 2) - 2));
                if (span <= 0)
                    continue;

                Win95Style.FillRect(g, fill, cx - span, y, span * 2, 1);
            }

            Win95Style.DrawLine(g, edge, cx - (r.Width / 2 - 2), bottom, cx + (r.Width / 2 - 2), bottom);
        }

        private static void FillSpeechBubble(Graphics g, Rectangle r)
        {
            Win95Style.FillRect(g, Color.White, r.X, r.Y, r.Width, r.Height - 7);
            Win95Style.FillRect(g, Color.White, r.X + 5, r.Bottom - 7, 12, 7);

            Rectangle box = new Rectangle(r.X, r.Y, r.Width, r.Height - 7);
            Win95Style.DrawEdgePairPublic(g, box, Color.FromArgb(0x00, 0x00, 0x80),
                                          Color.FromArgb(0x00, 0x00, 0x80));

            Win95Style.DrawLine(g, Color.FromArgb(0x00, 0x00, 0x80), r.X + 6, r.Bottom - 1,
                                r.X + 9, r.Bottom - 1);
            Win95Style.DrawLine(g, Color.FromArgb(0x00, 0x00, 0x80), r.X + 9, r.Bottom - 1,
                                r.X + 12, r.Bottom - 7);
        }

        private static void DrawGlyph(Graphics g, int x, int y, string glyph, Color color)
        {
            try
            {
                using (Font f = new Font("MS Sans Serif", 19f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush b = new SolidBrush(color))
                {
                    SizeF s = g.MeasureString(glyph, f);
                    g.DrawString(glyph, f, b,
                        x + (32 - s.Width) / 2,
                        y + (30 - s.Height) / 2);
                }
            }
            catch
            {
                // A missing font must never break an error dialog.
            }
        }
/// <summary>Shows a dialog with a single OK button, owned by <paramref name="owner"/>.</summary>
        public static void Show(IWin32Window owner, string caption, string text, string detail, Win95Icon icon)
        {
            using (Win95MessageBox box = new Win95MessageBox(owner, caption, text, detail, icon, false))
                box.ShowDialog(owner);
        }

        public static void ShowInfo(IWin32Window owner, string caption, string text)
        {
            Show(owner, caption, text, null, Win95Icon.Information);
        }

        public static void ShowInfo(IWin32Window owner, string caption, string text, string detail)
        {
            Show(owner, caption, text, detail, Win95Icon.Information);
        }

        public static void ShowWarning(IWin32Window owner, string caption, string text)
        {
            Show(owner, caption, text, null, Win95Icon.Warning);
        }

        public static void ShowWarning(IWin32Window owner, string caption, string text, string detail)
        {
            Show(owner, caption, text, detail, Win95Icon.Warning);
        }

        public static void ShowError(IWin32Window owner, string caption, string text, string detail)
        {
            Show(owner, caption, text, detail, Win95Icon.Error);
        }

        /// <summary>Shows a Yes/No question. Returns true only when the user clicks Yes.</summary>
        public static bool Ask(IWin32Window owner, string caption, string text, string detail)
        {
            using (Win95MessageBox box = new Win95MessageBox(owner, caption, text, detail, Win95Icon.Question, true))
            {
                box.ShowDialog(owner);
                return box.DialogResult == DialogResult.Yes;
            }
        }
    }
}