using System;
using System.Drawing;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// A fully owner-drawn Windows 95 push button: square corners, 2px raised border,
    /// classic focus rectangle and disabled dithered text. No themed rendering is used,
    /// so it looks identical on Windows 10 and Windows 11.
    /// </summary>
    public class ClassicButton : Control
    {
        private bool _pressed;
        private bool _isDefault;
        private bool _isCancel;

        public ClassicButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            // The base must not generate mouse events: it only raises Click on
            // captioned windows. See ClassicMouse for the full explanation.
            SetStyle(ControlStyles.UserMouse, false);
            TabStop = true;
            Height = 23;
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.Face;
        }

        public bool IsDefaultButton
        {
            get { return _isDefault; }
            set { _isDefault = value; Invalidate(); }
        }

        public bool IsCancelButton
        {
            get { return _isCancel; }
            set { _isCancel = value; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled)
                return;

            _pressed = true;

            // Capture the mouse so the button still receives the mouse-up even if the
            // pointer leaves the control slightly while clicking. Focus() is taken
            // afterwards so the first click is never consumed just to gain focus.
            try { Capture = true; } catch { /* capture is best-effort */ }
            Focus();
            Invalidate();
        }

        /// <summary>
        /// One physical click must raise exactly one logical Click.
        ///
        /// The mouse is handled directly (see ClassicMouse) so the base cannot add a
        /// click of its own. Pressed here and released here runs the command;
        /// releasing away from the button cancels it, as a classic button does.
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool wasPressed = _pressed;
            _pressed = false;
            try { Capture = false; } catch { /* capture is best-effort */ }
            Invalidate();

            if (e.Button != MouseButtons.Left)
                return;

            if (wasPressed && Enabled && ClientRectangle.Contains(e.Location))
                OnClick(EventArgs.Empty);

            // Raises the MouseUp event. The base cannot synthesise a Click here
            // because the message never reaches its handler (see WndProc).
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

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);

            // Losing capture cancels the click (classic buttons cancel when dragged off).
            if (!Capture && _pressed)
            {
                _pressed = false;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            Rectangle outer = new Rectangle(1, 1, Width - 2, Height - 2);

            if (_pressed)
                Win95Style.DrawPressedBorder(g, outer);
            else
                Win95Style.DrawRaisedBorder(g, outer, 2);

            // The default button gets an extra dotted outline just outside the border.
            if (_isDefault && Enabled)
            {
                Win95Style.DrawFocusRect(g, new Rectangle(0, 0, Width - 1, Height - 1));
            }

            // Pressed buttons shift their label 1px down-right, like the original.
            int offsetX = _pressed ? 1 : 0;
            int offsetY = _pressed ? 1 : 0;

            Size textSize = TextRenderer.MeasureText(g, Text, Font);
            int tx = (Width - textSize.Width) / 2 + offsetX;
            int ty = (Height - textSize.Height) / 2 + offsetY;

            if (Enabled)
            {
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(tx, ty, textSize.Width, textSize.Height),
                    Win95Style.ButtonText, TextFormatFlags.NoPadding);
            }
            else
            {
                // Classic disabled look: white shadow 1px down-right, grey face text.
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(tx + 1, ty + 1, textSize.Width, textSize.Height),
                    Win95Style.FaceLight, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(tx, ty, textSize.Width, textSize.Height),
                    Win95Style.ButtonTextDisabled, TextFormatFlags.NoPadding);
            }

            if (Focused && Enabled && !_pressed)
                Win95Style.DrawFocusRect(g, new Rectangle(3, 3, Width - 6, Height - 6));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                _pressed = true;
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && _isDefault)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            else
            {
                base.OnKeyDown(e);
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                if (_pressed)
                {
                    _pressed = false;
                    Invalidate();
                    OnClick(EventArgs.Empty);
                }
                e.Handled = true;
            }
            else
            {
                base.OnKeyUp(e);
            }
        }
    }
}
