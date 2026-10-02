using System;
using System.Drawing;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// Shared mouse plumbing for the owner-drawn Win95 controls.
    ///
    /// WinForms synthesises a Click from a mouse release only for captioned windows.
    /// This suite uses borderless windows with a hand-drawn caption, where the base
    /// raises no Click at all - and on a captioned window it raises one at a point the
    /// control cannot predict, so relying on it gives either no clicks or two per
    /// press. ControlStyles.UserMouse is therefore switched off, which stops the base
    /// from generating any mouse events, and the two button messages are handled
    /// directly. Exactly one Click per physical press is then guaranteed, in every
    /// window style.
    /// </summary>
    internal static class ClassicMouse
    {
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;

        public static MouseEventArgs Args(ref Message m)
        {
            long l = m.LParam.ToInt64();
            int x = (int)(l & 0xFFFF);
            int y = (int)((l >> 16) & 0xFFFF);
            return new MouseEventArgs(MouseButtons.Left, 1, x, y, 0);
        }
    }
}