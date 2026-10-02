using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// A results table styled as a Windows 95 list view.
    ///
    /// This deliberately wraps the standard ListView in Details mode rather than
    /// owner-drawing it. The common controls handle selection, scrolling, column
    /// resizing and redraw during resize correctly, which is exactly what a custom
    /// painter usually gets wrong. The visual theme is switched off so the header
    /// draws as a classic raised bar, and the columns are sized to fit the control so
    /// a horizontal scrollbar only appears when the content really is wider.
    /// </summary>
    public sealed class ClassicListView : ListView
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName,
                                                 string subIdList);

        private const int ExListViewDoubleBuffered = 0x1000;

        /// <summary>
        /// The column that soaks up the width the fixed columns do not use. Set to the
        /// widest column when the columns are built, so the important data - the mail
        /// server or the record value - grows instead of a blank spacer at the edge.
        /// </summary>
        private int _flexColumn = -1;

        public ClassicListView()
        {
            View = View.Details;
            FullRowSelect = true;
            GridLines = true;
            HideSelection = false;
            MultiSelect = false;
            BorderStyle = BorderStyle.FixedSingle;

            BackColor = Win95Style.FieldBack;
            ForeColor = Win95Style.FieldText;

            // No owner drawing: the native control is what stays stable during a
            // resize, and it keeps selected rows readable without custom painting.
            OwnerDraw = false;

            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        /// <summary>
        /// Strips the visual theme and re-fits the columns once the handle exists.
        ///
        /// A list view measures its horizontal extent when its window is created, so
        /// columns sized before that leave a scrollbar for space no longer in use.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            try
            {
                // An empty sub-app name switches the control back to classic,
                // non-themed rendering, which gives a Win95 sunken header.
                SetWindowTheme(Handle, "", "");
            }
            catch
            {
                // Not fatal: the colours below still apply.
            }

            ResizeColumns();
        }

        /// <summary>
        /// Gives the last column the width the others do not need, so the table fills
        /// the control and no column is left clipped.
        /// </summary>
        private void ResizeColumns()
        {
            if (Columns.Count == 0) return;

            int total = ClientSize.Width - 4;
            if (total <= 0) return;

            int used = 0;
            for (int i = 0; i < Columns.Count - 1; i++)
                used += Columns[i].Width;

            int last = total - used;
            if (last < 20) last = 20;

            Columns[Columns.Count - 1].Width = last;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ResizeColumns();
        }

        /// <summary>
        /// Replaces the columns and re-fits them. The widest column absorbs the space the
        /// fixed-width columns do not use, so a short column such as TTL never
        /// stretches across the table and no scrollbar appears.
        /// </summary>
        public void SetColumns(string[] titles, int[] widths)
        {
            BeginUpdate();
            try
            {
                Columns.Clear();

                for (int i = 0; i < titles.Length; i++)
                {
                    ColumnHeader column = new ColumnHeader();
                    column.Text = titles[i];
                    column.Width = (widths != null && i < widths.Length) ? widths[i] : 100;
                    Columns.Add(column);
                }

                // The widest column takes the leftover width. There is no trailing
                // spacer column, because an empty header cell at the right edge reads
                // as a broken table rather than as padding.
                _flexColumn = 0;
                for (int i = 1; i < Columns.Count; i++)
                    if (Columns[i].Width > Columns[_flexColumn].Width)
                        _flexColumn = i;
            }
            finally
            {
                EndUpdate();
            }

            ResizeColumns();
        }

        /// <summary>Clears every row in one update, so no flicker is visible.</summary>
        public void ClearRows()
        {
            BeginUpdate();
            try
            {
                Items.Clear();
            }
            finally
            {
                EndUpdate();
            }
        }

        /// <summary>Adds one row from an array of cell values.</summary>
        public void AddRow(string[] cells)
        {
            ListViewItem item = new ListViewItem(cells[0]);

            for (int i = 1; i < cells.Length && i < Columns.Count; i++)
                item.SubItems.Add(cells[i]);

            Items.Add(item);
        }

        /// <summary>Adds many rows inside a single update.</summary>
        public void AddRows(List<string[]> rows)
        {
            BeginUpdate();
            try
            {
                for (int i = 0; i < rows.Count; i++)
                    AddRow(rows[i]);
            }
            finally
            {
                EndUpdate();
            }
        }

        /// <summary>True when the columns are wider than the control can show.</summary>
        public bool NeedsHorizontalScroll()
        {
            int used = 0;
            for (int i = 0; i < Columns.Count; i++)
                used += Columns[i].Width;

            return used > ClientSize.Width;
        }
    }
}
