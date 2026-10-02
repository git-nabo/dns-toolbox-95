using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>A single tab: a caption and its content control.</summary>
    public sealed class TablessPage
    {
        public string Caption;
        public Control Content;

        public TablessPage(string caption, Control content)
        {
            Caption = caption;
            Content = content;
        }
    }

    /// <summary>
    /// Owner-drawn Windows 95 tab strip. The active tab is raised and joined to the
    /// page, inactive tabs are recessed - exactly like the original tab control.
    /// </summary>
    public sealed class TablessTabControl : Control
    {
        private readonly List<TablessPage> _pages = new List<TablessPage>();
        private readonly List<Rectangle> _tabRects = new List<Rectangle>();
        private int _activeIndex;

        private const int TabHeight = 20;
        private const int MinTabWidth = 80;

        public TablessTabControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Win95Style.CreateFont();
            BackColor = Win95Style.Face;
        }

        public IList<TablessPage> Pages
        {
            get { return _pages; }
        }

        /// <summary>
        /// Adds a page and parents its content to this control. The content must be a
        /// real child control, otherwise it can never be painted.
        /// </summary>
        public void AddPage(TablessPage page)
        {
            if (page == null)
                return;

            _pages.Add(page);

            if (page.Content != null && !Controls.Contains(page.Content))
                Controls.Add(page.Content);

            if (page.Content != null)
                page.Content.Visible = false;

            RefreshTabs();
        }

        /// <summary>Shows the first page. Call once all pages have been added.</summary>
        public void EnsureFirstPageVisible()
        {
            if (_pages.Count == 0)
                return;

            _activeIndex = 0;
            ShowActivePage();
            RefreshTabs();
        }

        public int ActiveIndex
        {
            get { return _activeIndex; }
            set
            {
                if (value < 0 || value >= _pages.Count || value == _activeIndex)
                    return;

                _activeIndex = value;
                ShowActivePage();
                Invalidate();
            }
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control != null && _pages.Count > 0)
                e.Control.Visible = false;
        }

        private void ShowActivePage()
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                Control c = _pages[i].Content;
                if (c == null)
                    continue;

                c.Visible = (i == _activeIndex);
            }

            // The page content is docked inside the well below the tab strip, so it
            // only becomes visible once it is actually a child of this control.
            if (_activeIndex >= 0 && _activeIndex < _pages.Count)
            {
                Control active = _pages[_activeIndex].Content;
                if (active != null && active.Dock == DockStyle.Fill)
                {
                    Rectangle page = new Rectangle(1, TabHeight + 2, Width - 2, Height - TabHeight - 3);
                    active.SetBounds(page.X, page.Y, page.Width, page.Height);
                }
            }
        }

        public void Activate(int index)
        {
            ActiveIndex = index;
            ShowActivePage();
        }

        /// <summary>Recomputes tab rectangles; call after changing captions.</summary>
        public void RefreshTabs()
        {
            _tabRects.Clear();
            int x = 2;

            using (Graphics g = CreateGraphics())
            {
                foreach (TablessPage p in _pages)
                {
                    int w = TextRenderer.MeasureText(g, p.Caption, Font).Width + 22;
                    if (w < MinTabWidth)
                        w = MinTabWidth;

                    _tabRects.Add(new Rectangle(x, 2, w, TabHeight));
                    x += w - 3;
                }
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Win95Style.PrepareGraphics(g);

            Win95Style.FillRect(g, Win95Style.Face, 0, 0, Width, Height);

            if (_tabRects.Count != _pages.Count)
                RefreshTabs();

            // Page well under the tabs.
            Rectangle page = new Rectangle(1, TabHeight + 2, Width - 2, Height - TabHeight - 3);
            Win95Style.FillRect(g, Win95Style.Face, page.X, page.Y, page.Width, page.Height);
            Win95Style.DrawSunkenBorder(g, page, 2);

            for (int i = 0; i < _pages.Count; i++)
            {
                Rectangle r = _tabRects[i];
                bool active = (i == _activeIndex);

                // Inactive tabs sit slightly lower and are fully closed.
                Rectangle tab = new Rectangle(r.X, active ? r.Y : r.Y + 2, r.Width, r.Height);

                Win95Style.FillRect(g, Win95Style.Face, tab.X, tab.Y, tab.Width, tab.Height);

                if (active)
                    Win95Style.DrawRaisedBorder(g, tab, 2);
                else
                    Win95Style.DrawSunkenBorder(g, tab, 1);

                TextRenderer.DrawText(g, _pages[i].Caption, Font,
                    new Rectangle(tab.X + 4, tab.Y + 2, tab.Width - 8, tab.Height - 2),
                    Win95Style.WindowText,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            // Bridge the active tab into the page so they look joined.
            if (_activeIndex >= 0 && _activeIndex < _tabRects.Count)
            {
                Rectangle activeTab = _tabRects[_activeIndex];
                Win95Style.FillRect(g, Win95Style.Face,
                    activeTab.X + 2, activeTab.Y + TabHeight - 2,
                    activeTab.Width - 4, 4);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            for (int i = 0; i < _tabRects.Count; i++)
            {
                Rectangle r = _tabRects[i];
                if (e.Y >= r.Y - 2 && e.Y <= r.Bottom + 2 &&
                    e.X >= r.X && e.X <= r.Right)
                {
                    Activate(i);
                    return;
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RefreshTabs();
        }
    }
}