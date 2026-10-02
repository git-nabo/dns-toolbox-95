using System;
using System.Drawing;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// The common frame for every diagnostic window.
    ///
    /// Each tool used to place its controls at hard-coded coordinates, which is why
    /// several ended up with a clipped Cancel button and buttons sitting in the middle
    /// of the window. This base lays every tool out the same way, using docking so
    /// the content follows the window when it is resized:
    ///
    ///   top     - the input fields and the action buttons
    ///   middle  - the result area, which fills whatever space is left
    ///   bottom  - Copy / Close, anchored to the bottom right
    ///   footer  - the status bar, docked to the bottom edge
    /// </summary>
    public abstract class ToolDialogForm : Win95Form
    {
        /// <summary>Outer margin inside the client area, in pixels.</summary>
        protected new const int Margin = 10;

        /// <summary>Height of a single-line input row.</summary>
        protected const int RowHeight = 21;

        /// <summary>Height of a caption above a control.</summary>
        protected const int LabelHeight = 15;

        /// <summary>Standard button size used throughout the suite.</summary>
        protected const int ButtonWidth = 90;
        protected const int ButtonHeight = 23;

        /// <summary>Height of the strip that holds the bottom buttons.</summary>
        private const int BottomBarHeight = 35;

        private Panel _root;
        private Panel _top;
        private Panel _bottom;
        private TextBox _output;
        private Win95StatusBarControl _status;

        protected ToolDialogForm(string caption, IWin32Window owner, int width, int height)
        {
            BuildChrome(caption);
            CloseOnEscape = true;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = (owner is Form)
                ? FormStartPosition.CenterParent
                : FormStartPosition.CenterScreen;

            Width = width;
            Height = height;

            BuildFrame();
        }

        /// <summary>Builds the docked frame, before any content is added.</summary>
        private void BuildFrame()
        {
            _root = new Panel();
            _root.Dock = DockStyle.Fill;
            _root.BackColor = Win95Style.Face;
            _root.Padding = new Padding(Margin);
            ClientSurface.Controls.Add(_root);

            // Docking resolves in reverse add order, so the fixed strips are added
            // first and the filler last, which is what makes the middle expand.
            _bottom = new Panel();
            _bottom.Dock = DockStyle.Bottom;
            _bottom.Height = BottomBarHeight;
            _bottom.BackColor = Win95Style.Face;
            _root.Controls.Add(_bottom);

            _top = new Panel();
            _top.Dock = DockStyle.Top;
            _top.Height = RowHeight + 4;
            _top.BackColor = Win95Style.Face;
            _root.Controls.Add(_top);

            _status = new Win95StatusBarControl();
            _status.Dock = DockStyle.Bottom;
            _status.Height = Win95Style.StatusHeight;
            _status.SetPanes("Ready.", string.Empty);
            ClientSurface.Controls.Add(_status);
        }

        /// <summary>The padded content area, for tools that add their own panels.</summary>
        protected Panel Root
        {
            get { return _root; }
        }

        /// <summary>Grows the top strip so a tool can fit a second line of input.</summary>
        protected void GrowTop(int extra)
        {
            _top.Height += extra;
        }

        /// <summary>The strip that holds the input fields.</summary>
        protected new Panel Top
        {
            get { return _top; }
        }

        /// <summary>Sets both status bar panes.</summary>
        protected void SetStatus(string text)
        {
            if (_status != null) _status.SetPanes(text ?? string.Empty, ResolverText);
        }

        /// <summary>Right-hand text shown in the second status bar pane.</summary>
        protected string ResolverText
        {
            get; set;
        }

        /// <summary>
        /// Adds the middle result area and returns the read-only text box inside it.
        /// The text wraps, so a long SPF or DMARC record does not force a horizontal
        /// scrollbar that has nothing useful to show.
        /// </summary>
        protected TextBox AddResultArea(string title)
        {
            ClassicGroupBox group = AddResultGroup(title);

            SunkenPanel host = new SunkenPanel();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(3);
            group.Controls.Add(host);

            _output = new TextBox();
            _output.Multiline = true;
            _output.ReadOnly = true;
            _output.WordWrap = true;
            _output.ScrollBars = ScrollBars.Vertical;
            _output.BackColor = Win95Style.FieldBack;
            _output.ForeColor = Win95Style.FieldText;
            _output.Font = new Font("Consolas", 8.5f);
            _output.Dock = DockStyle.Fill;
            host.Controls.Add(_output);

            return _output;
        }

        /// <summary>
        /// Adds a group box that fills the middle of the window, for a tool that
        /// shows a table rather than text.
        /// </summary>
        protected ClassicGroupBox AddResultGroup(string title)
        {
            ClassicGroupBox group = new ClassicGroupBox();
            group.Text = title;
            group.Dock = DockStyle.Fill;

            // The extra top padding leaves room for the group caption. Without it the
            // docked child panel covers the caption and the box looks unlabelled.
            group.Padding = new Padding(6, 20, 6, 6);
            _root.Controls.Add(group);

            // WinForms docks the highest child index first, so a Fill control added
            // last would claim the whole area and squeeze the top and bottom strips
            // to nothing. Moving it to index 0 makes it dock last, which leaves it
            // only the space the input row and the button bar did not claim.
            _root.Controls.SetChildIndex(group, 0);

            return group;
        }

        /// <summary>Places the Copy and Close buttons at the bottom right.</summary>
        protected void AddBottomButtons(string copyCaption, EventHandler onCopy,
                                        EventHandler onClose)
        {
            ClassicButton copy = new ClassicButton();
            copy.Text = copyCaption;
            copy.SetBounds(_bottom.Width - (ButtonWidth * 2) - Margin, 6,
                           ButtonWidth, ButtonHeight);
            copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            copy.Click += onCopy;
            _bottom.Controls.Add(copy);

            ClassicButton close = new ClassicButton();
            close.Text = "&Close";
            close.SetBounds(_bottom.Width - ButtonWidth - Margin, 6,
                            ButtonWidth, ButtonHeight);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.IsCancelButton = true;
            close.IsDefaultButton = true;
            close.Click += onClose;
            _bottom.Controls.Add(close);
        }

        /// <summary>The shared result text box, or null for a table based tool.</summary>
        protected TextBox Output
        {
            get { return _output; }
        }

        /// <summary>Standard read-only input field for the top row.</summary>
        protected static TextBox Field(int x, int y, int width, string text)
        {
            TextBox box = new TextBox();
            box.SetBounds(x, y, width, RowHeight);
            box.Font = Win95Style.CreateFontSafe();
            box.Text = text ?? string.Empty;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            return box;
        }

        /// <summary>A caption aligned with a field on the same row.</summary>
        protected static ClassicLabel Caption(string text, int x, int y, int width)
        {
            ClassicLabel label = new ClassicLabel();
            label.Text = text;
            label.SetBounds(x, y + 3, width, LabelHeight);
            return label;
        }

        /// <summary>
        /// A button on the top row. <paramref name="fromRight"/> is the distance from
        /// the right edge of the top strip, so the button stays attached to the right
        /// when the window is resized.
        /// </summary>
        protected ClassicButton TopButton(string text, int fromRight, int width,
                                          EventHandler onClick)
        {
            ClassicButton button = new ClassicButton();
            button.Text = text;
            button.SetBounds(_top.Width - fromRight - width, 0, width, ButtonHeight);
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.Click += onClick;
            return button;
        }
    }
}
