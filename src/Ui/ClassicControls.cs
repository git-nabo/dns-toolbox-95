using System;
using System.Drawing;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// Owner-drawn group box with the classic etched frame and legend. The legend is
    /// painted over the frame with a gap, exactly like the Windows 95 original.
    /// </summary>
    public class ClassicGroupBox : Panel
    {
        public ClassicGroupBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.Face;
            ForeColor = Win95Style.WindowText;
            Height = 100;

            // Panel honours Padding for docked children, which is what keeps a
            // Dock=Fill result box clear of the caption drawn at the top edge.
            Padding = new Padding(6, 20, 6, 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            // Leave room above for the legend notch.
            Rectangle frame = new Rectangle(1, 8, Width - 2, Height - 9);
            Win95Style.DrawEtchedFrame(g, frame);

            if (!string.IsNullOrEmpty(Text))
            {
                Size textSize = TextRenderer.MeasureText(g, Text, Font);
                int textX = 8;
                int textY = 1;

                // Punch a legend-sized gap in the top frame line.
                Win95Style.FillRect(g, Win95Style.Face, textX - 3, textY, textSize.Width + 6, textSize.Height + 2);

                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(textX, textY, textSize.Width, textSize.Height),
                    ForeColor, TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Flat, square label using the classic system font.</summary>
    public class ClassicLabel : Control
    {
        public ClassicLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Win95Style.CreateFont();
            ForeColor = Win95Style.WindowText;
            BackColor = Win95Style.Face;
            Height = 15;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Win95Style.PrepareGraphics(e.Graphics);

            Color c = Enabled ? ForeColor : Win95Style.ButtonTextDisabled;
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(0, 0, Width, Height), c, TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// Owner-drawn Windows 95 sunken panel used to host child controls with the
    /// correct 2px recessed border.
    /// </summary>
    public class SunkenPanel : Panel
    {
        public SunkenPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Win95Style.FieldBack;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Win95Style.PrepareGraphics(e.Graphics);
            Win95Style.FillRect(e.Graphics, Win95Style.FieldBack, 0, 0, Width, Height);
            Win95Style.DrawSunkenBorder(e.Graphics, new Rectangle(0, 0, Width, Height), 2);
        }
    }

    /// <summary>
    /// Owner-drawn Win95 check box: 13x13 sunken square with a chunky check mark
    /// when checked, plus the classic label.
    /// </summary>
    public class ClassicCheckBox : Control
    {
        private bool _checked;

        public ClassicCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            // The base must not generate mouse events: it only raises Click on
            // captioned windows. See ClassicMouse for the details.
            SetStyle(ControlStyles.UserMouse, false);
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.Face;
            Height = 16;
            TabStop = true;
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                Invalidate();

                EventHandler h = CheckedChanged;
                if (h != null)
                    h(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
                Focus();
        }

        /// <summary>
        /// A single left click must toggle the box exactly once.
        ///
        /// The mouse is handled directly (see ClassicMouse) so the base cannot add a
        /// second click of its own. Releasing over the box toggles it; releasing away
        /// cancels, exactly as a classic checkbox does.
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !Enabled)
                return;

            if (ClientRectangle.Contains(e.Location))
                OnClick(EventArgs.Empty);

            // Raises the MouseUp event; see WndProc for why this is explicit.
            base.OnMouseUp(e);
        }

        /// <summary>Handles the mouse itself so the base raises no extra events.</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ClassicMouse.WM_LBUTTONDOWN)
            {
                OnMouseDown(ClassicMouse.Args(ref m));
                return;
            }

            if (m.Msg == ClassicMouse.WM_LBUTTONUP)
            {
                OnMouseUp(ClassicMouse.Args(ref m));
                return;
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// Toggles the box. Click is raised from OnMouseUp only, so one press is one toggle.
        /// </summary>
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) Checked = !_checked;
        }

        /// <summary>Space toggles the box when it has focus.</summary>
        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            // 13x13 sunken checkbox square.
            Rectangle box = new Rectangle(0, 1, 13, 13);
            Win95Style.FillRect(g, Win95Style.FieldBack, box.X, box.Y, box.Width, box.Height);
            Win95Style.DrawSunkenBorder(g, box, 2);

            if (_checked)
            {
                using (Pen p = new Pen(Win95Style.WindowText, 2))
                {
                    g.DrawLine(p, 3, 7, 5, 10);
                    g.DrawLine(p, 5, 10, 10, 3);
                }
            }

            Color c = Enabled ? ForeColor : Win95Style.ButtonTextDisabled;
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(18, 1, Width - 18, Height),
                c, TextFormatFlags.NoPadding);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && Enabled)
            {
                Checked = !_checked;
                e.Handled = true;
            }
        }
    }
}
