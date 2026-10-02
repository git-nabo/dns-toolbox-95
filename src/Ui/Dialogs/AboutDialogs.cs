using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Ui
{
    /// <summary>Help &gt; About DNS Toolbox 95.</summary>
    public sealed class AboutForm : Win95Form
    {
        public AboutForm(IWin32Window owner)
        {
            BuildChrome("About DNS Toolbox 95");
            CloseOnEscape = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = owner is Form
                ? FormStartPosition.CenterParent
                : FormStartPosition.CenterScreen;

            // The window is sized from its measured content, so there is never a large
            // empty area below the text.
            const int bodyLeft = 76;      // leaves room for the icon column
            const int bodyWidth = 250;
            const int iconSize = 32;
            const int margin = 12;
            const int buttonHeight = 23;
            const int buttonGap = 10;

            // The window draws its own caption, and that strip is part of the
            // window height even though it is not part of the content area. It has
            // to be allowed for or the last line of text and the OK button are
            // pushed past the bottom edge and get clipped.
            const int titleHeight = 20;   // matches Win95Form.BuildChrome

            string headline = "DNS Toolbox 95" + Environment.NewLine + "Version 1.0";
            string body =
                "DNS diagnostic and mail troubleshooting" + Environment.NewLine +
                "utility." + Environment.NewLine + Environment.NewLine +
                "Developer:" + Environment.NewLine +
                "Khaled Nabo" + Environment.NewLine + Environment.NewLine +
                "Company:" + Environment.NewLine +
                "geissler-IT" + Environment.NewLine + Environment.NewLine +
                "Built for modern Windows" + Environment.NewLine +
                "with a classic interface." + Environment.NewLine + Environment.NewLine +
                "Part of the Network Tools 95 collection.";

            int headlineHeight;
            int bodyHeight;
            using (Graphics g = CreateGraphics())
            {
                // A little slack on each block: MeasureString can under-report the
                // last line, and the label clips rather than growing.
                headlineHeight = (int)Math.Ceiling(g.MeasureString(
                    headline, Win95Style.BoldFont, bodyWidth,
                    System.Drawing.StringFormat.GenericTypographic).Height) + 6;

                bodyHeight = (int)Math.Ceiling(g.MeasureString(
                    body, Win95Style.CreateFontSafe(), bodyWidth,
                    System.Drawing.StringFormat.GenericTypographic).Height) + 6;
            }

            int textBlock = headlineHeight + 8 + bodyHeight;
            int contentHeight = Math.Max(iconSize, textBlock);

            // title bar + top margin + text + gap + button row + bottom margin
            int buttonTop = margin + contentHeight + buttonGap;
            int contentArea = buttonTop + buttonHeight + margin;

            Width = bodyLeft + bodyWidth + (margin * 2) + 8;
            Height = titleHeight + contentArea;

            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Win95Style.Face;
            root.Padding = new Padding(margin);
            ClientSurface.Controls.Add(root);

            // The same icon the application uses everywhere else, not a second logo.
            PictureBox icon = new PictureBox();
            icon.SizeMode = PictureBoxSizeMode.Zoom;
            icon.Image = LoadApplicationIcon();
            icon.SetBounds(margin, margin, iconSize, iconSize);
            root.Controls.Add(icon);

            Label title = new Label();
            title.AutoSize = false;
            title.BorderStyle = BorderStyle.None;
            title.BackColor = Win95Style.Face;
            title.ForeColor = Win95Style.WindowText;
            title.Font = Win95Style.BoldFont;
            title.Text = headline;
            title.SetBounds(bodyLeft, margin, bodyWidth, headlineHeight);
            root.Controls.Add(title);

            Label text = new Label();
            text.AutoSize = false;
            text.BorderStyle = BorderStyle.None;
            text.BackColor = Win95Style.Face;
            text.ForeColor = Win95Style.WindowText;
            text.Font = Win95Style.CreateFontSafe();
            text.Text = body;
            text.SetBounds(bodyLeft, margin + headlineHeight + 8, bodyWidth, bodyHeight);
            root.Controls.Add(text);

            ClassicButton ok = new ClassicButton();
            ok.Text = "&OK";
            ok.SetBounds(root.Width - 90 - margin, buttonTop, 90, buttonHeight);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.IsDefaultButton = true;
            ok.IsCancelButton = true;
            ok.Click += delegate { Close(); };
            root.Controls.Add(ok);
        }

        /// <summary>
        /// The application's own icon, so the About box shows exactly what Explorer and
        /// the taskbar show.
        ///
        /// The icon is taken from the window itself, which Win95Form has already filled
        /// with the native 32x32 image. Icon.ExtractAssociatedIcon is deliberately not
        /// used: every frame in assets\app.ico is PNG-compressed, and the shell
        /// extractor returns null or a corrupt bitmap for those, which is what left a
        /// grey placeholder here instead of the real logo.
        /// </summary>
        private Image LoadApplicationIcon()
        {
            try
            {
                Icon native = NativeAppIcon;
                if (native != null) return new Icon(native, new Size(32, 32)).ToBitmap();
            }
            catch
            {
                // A missing icon must never stop the About box opening.
            }

            return null;
        }
    }

    /// <summary>Asks for a resolver address to use for the rest of the session.</summary>
    public sealed class CustomResolverForm : Win95Form
    {
        private readonly TextBox _box;

        /// <summary>The address that was accepted, kept after the window closes.</summary>
        private string _accepted = string.Empty;

        public CustomResolverForm(IWin32Window owner, string current)
        {
            BuildChrome("Custom DNS Server");
            CloseOnEscape = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = owner is Form
                ? FormStartPosition.CenterParent
                : FormStartPosition.CenterScreen;

            Width = 430;
            Height = 210;

            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Win95Style.Face;
            root.Padding = new Padding(10, 8, 10, 10);
            ClientSurface.Controls.Add(root);

            ClassicLabel label = new ClassicLabel();
            label.Text = "DNS server address:";
            label.SetBounds(10, 11, 160, 15);
            root.Controls.Add(label);

            _box = new TextBox();
            _box.SetBounds(10, 30, root.Width - 22, 21);
            _box.Font = Win95Style.CreateFontSafe();
            _box.Text = current ?? string.Empty;
            _box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(_box);

            ClassicLabel hint = new ClassicLabel();
            hint.Text = "IPv4 or IPv6, for example 192.168.1.10 or 2001:db8::1.";
            hint.SetBounds(10, 56, root.Width - 22, 15);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            root.Controls.Add(hint);

            ClassicButton ok = new ClassicButton();
            ok.Text = "&OK";
            ok.SetBounds(root.Width - 200, 90, 90, 23);
            ok.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ok.IsDefaultButton = true;
            ok.Click += delegate { Accept(); };
            root.Controls.Add(ok);

            ClassicButton cancel = new ClassicButton();
            cancel.Text = "&Cancel";
            cancel.SetBounds(root.Width - 100, 90, 90, 23);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.IsCancelButton = true;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            root.Controls.Add(cancel);
        }

        public string Value
        {
            get { return _accepted; }
        }

        /// <summary>
        /// Takes the address out of the field and keeps it.
        ///
        /// The value has to be captured before the dialog closes: closing disposes
        /// the window and its child controls, and TextBox.Text reads back as empty
        /// once the handle is gone. Reading the box after ShowDialog returned
        /// therefore always produced an empty string, which is why the chosen
        /// resolver was never actually used.
        /// </summary>
        private void Accept()
        {
            string text = (_box.Text ?? string.Empty).Trim();

            IPAddress address;
            if (!IPAddress.TryParse(text, out address))
            {
                Win95MessageBox.ShowWarning(this, Text,
                    "'" + text + "' is not a valid IP address.",
                    "Enter an IPv4 address such as 1.1.1.1, or an IPv6 address such as 2001:db8::1.");
                _box.Focus();
                _box.SelectAll();
                return;
            }

            _accepted = text;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
