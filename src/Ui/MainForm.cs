using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DnsToolbox95.Dns;
using DnsToolbox95.Settings;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// The DNS Toolbox main window: a query bar, a results table and a details panel,
    /// plus the specialized tools on the Tools menu.
    ///
    /// Queries run on the thread pool with a cancellation token, so the window never
    /// freezes even when a resolver stops answering.
    /// </summary>
    public sealed class MainForm : Win95Form
    {
        private readonly DnsQueryService _dns = new DnsQueryService();

        private TextBox _domainBox;
        private ComboBox _typeBox;
        private ComboBox _resolverBox;
        private ClassicButton _lookupButton;
        private ClassicButton _clearButton;
        private ClassicButton cancelButton;
        private Panel rootPanel;
        private Panel contentPanel;
        private TableLayoutPanel queryTable;

        /// <summary>One input row, including the gap below it.</summary>
        private const int queryRowHeight = 27;

        /// <summary>Number of input rows in the query block.</summary>
        private const int queryRowCount = 3;

        /// <summary>Height of a single-line query field.</summary>
        private const int queryFieldHeight = 21;

        /// <summary>Height of one input row, including its gap.</summary>
        private const int fieldRowHeight = 27;
        private ClassicListView _results;
        private Win95StatusBarControl _status;
        private MenuStrip _menu;
        private ToolStripMenuItem _loggingItem;

        private readonly List<string> _history = new List<string>();

        private CancellationTokenSource _running;
        private string _customResolver;

        /// <summary>The dropdown entry that opens the address dialog.</summary>
        private const string CustomResolverEntry = "Custom DNS Server...";
        private DnsQueryResult _lastResult;

        public MainForm()
        {
            BuildChrome("DNS Toolbox 95");
            AllowMaximize = true;

            // Compact by default: only as tall as the content needs.
            Width = 780;
            Height = 520;
            MinimumSize = new Size(680, 480);

            BuildMenu();
            BuildLayout();
            LoadSettings();

            Shown += delegate
            {
                SetStatus("Ready.", ResolverText());
                _domainBox.Focus();
            };

            FormClosed += delegate
            {
                if (_running != null) _running.Cancel();
                AppSettings.Save();
            };
        }

        private string ResolverText()
        {
            object selected = _resolverBox.SelectedItem;
            return selected == null ? DnsQueryService.SystemDefaultLabel : selected.ToString();
        }

        // ------------------------------------------------------------------ menu

        private void BuildMenu()
        {
            _menu = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add("&Save Report...", null, delegate { SaveReport(); });
            file.DropDownItems.Add("Copy &Results", null, delegate { CopyResults(); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("E&xit", null, delegate { Close(); });

            ToolStripMenuItem tools = new ToolStripMenuItem("&Tools");
            tools.DropDownItems.Add("&SPF Check...", null, delegate { ShowTool(ToolKind.Spf); });
            tools.DropDownItems.Add("&DKIM Check...", null, delegate { ShowTool(ToolKind.Dkim); });
            tools.DropDownItems.Add("&DMARC Check...", null, delegate { ShowTool(ToolKind.Dmarc); });
            tools.DropDownItems.Add("&Mail DNS Check...", null, delegate { ShowTool(ToolKind.Mail); });
            tools.DropDownItems.Add("&Reverse DNS...", null, delegate { ShowTool(ToolKind.Reverse); });
            tools.DropDownItems.Add("&DNSSEC Check...", null, delegate { ShowTool(ToolKind.Dnssec); });
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add("&Compare Resolvers...", null, delegate { ShowTool(ToolKind.Compare); });
            tools.DropDownItems.Add("&Propagation Check...", null, delegate { ShowTool(ToolKind.Propagation); });

            ToolStripMenuItem view = new ToolStripMenuItem("&View");
            view.DropDownItems.Add("&Clear Results", null, delegate { ClearResults(); });
            view.DropDownItems.Add("&Refresh System DNS Servers", null, delegate { RefreshResolvers(); });

            _loggingItem = new ToolStripMenuItem("&Logging");
            _loggingItem.Checked = Log.Enabled;
            _loggingItem.Click += delegate { ToggleLogging(); };
            view.DropDownItems.Add(_loggingItem);

            ToolStripMenuItem help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add("&About DNS Toolbox 95", null, delegate { ShowAbout(); });

            _menu.Items.Add(file);
            _menu.Items.Add(tools);
            _menu.Items.Add(view);
            _menu.Items.Add(help);

            BuildMenuBar(_menu);
        }

        private void ToggleLogging()
        {
            Log.Enabled = !Log.Enabled;
            _loggingItem.Checked = Log.Enabled;

            if (Log.Enabled) Log.Write("LOG", "logging", "enabled", "-");
            else Log.Disable();

            SetStatus(Log.Enabled ? "Diagnostic logging enabled." : "Diagnostic logging disabled.",
                      ResolverText());
        }

        private void RefreshResolvers()
        {
            string current = ResolverText();
            FillResolvers();
            SelectResolver(current);
            SetStatus("System DNS servers refreshed.", ResolverText());
        }

        // ---------------------------------------------------------------- layout

        private void BuildLayout()
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Win95Style.Face;
            root.Padding = new Padding(8, 6, 8, 6);
            ClientSurface.Controls.Add(root);
            rootPanel = root;

            // WinForms docks the highest child index first. The menu bar was added
            // before this panel, so without this the Fill panel was laid out first,
            // took the whole client area from y=0 and painted over the menu. The query
            // rows then started underneath the menu and the top row was clipped.
            // Moving the panel to index 0 makes it dock last, so it receives only the
            // space below the menu bar and above the status bar.
            ClientSurface.Controls.SetChildIndex(root, 0);

            // The query block is a real layout table rather than a set of hand
            // placed bounds. Three rows, three columns:
            //
            //   label | field | button
            //
            // Each control gets its own column, so a caption can never overlap the
            // control it labels, and the three buttons share one column, so they
            // line up vertically no matter how wide their captions are.
            const int labelColumn = 104;
            const int buttonColumn = 92;

            _domainBox = new TextBox();
            _domainBox.Font = Win95Style.CreateFontSafe();
            _domainBox.BorderStyle = BorderStyle.Fixed3D;
            _domainBox.BackColor = Win95Style.FieldBack;

            // Enter runs the lookup, like any normal single-line field.
            _domainBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    StartLookup();
                }
            };

            _typeBox = new ComboBox();
            _typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _typeBox.Font = Win95Style.CreateFontSafe();

            _resolverBox = new ComboBox();
            _resolverBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _resolverBox.Font = Win95Style.CreateFontSafe();

            _lookupButton = MakeButton("&Lookup", delegate { StartLookup(); });
            _lookupButton.IsDefaultButton = true;
            _clearButton = MakeButton("&Clear", delegate { ClearResults(); });
            cancelButton = MakeButton("&Cancel", delegate { CancelQuery(); });

            queryTable = new TableLayoutPanel();
            queryTable.ColumnCount = 3;
            queryTable.RowCount = queryRowCount;

            // Fixed so the table is exactly as tall as its rows. Left at the default
            // height the last row soaks up the surplus, which stretched the Cancel
            // button to twice the height of the other two.
            queryTable.Height = queryRowHeight * queryRowCount;
            queryTable.BackColor = Win95Style.Face;
            queryTable.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;

            queryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelColumn));
            queryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            queryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, buttonColumn));

            for (int i = 0; i < queryRowCount; i++)
                queryTable.RowStyles.Add(new RowStyle(SizeType.Absolute, queryRowHeight));

            queryTable.Controls.Add(MakeQueryLabel("Domain / Host:"), 0, 0);
            queryTable.Controls.Add(_domainBox, 1, 0);
            queryTable.Controls.Add(_lookupButton, 2, 0);

            queryTable.Controls.Add(MakeQueryLabel("Record Type:"), 0, 1);
            queryTable.Controls.Add(_typeBox, 1, 1);
            queryTable.Controls.Add(_clearButton, 2, 1);

            queryTable.Controls.Add(MakeQueryLabel("DNS Server:"), 0, 2);
            queryTable.Controls.Add(_resolverBox, 1, 2);
            queryTable.Controls.Add(cancelButton, 2, 2);

            // Vertical margins differ per control type so a 21px field, a 21px combo
            // and a 23px button all end up centred on the same row baseline.
            _domainBox.Margin = new Padding(0, 2, 8, 4);
            _typeBox.Margin = new Padding(0, 3, 8, 3);
            _resolverBox.Margin = new Padding(0, 3, 8, 3);

            // The record type list only holds short names, so it keeps its own width
            // instead of stretching to the full column. The width cap is honoured by
            // the control; its height is left to the cell.
            _typeBox.MaximumSize = new Size(180, 0);
            _domainBox.MaximumSize = new Size(0, queryFieldHeight);
            _lookupButton.Margin = new Padding(8, 0, 0, 4);
            _clearButton.Margin = new Padding(8, 0, 0, 4);
            cancelButton.Margin = new Padding(8, 0, 0, 4);

            _domainBox.Dock = DockStyle.Fill;
            _typeBox.Dock = DockStyle.Fill;
            _resolverBox.Dock = DockStyle.Fill;
            _lookupButton.Dock = DockStyle.Fill;
            _clearButton.Dock = DockStyle.Fill;
            cancelButton.Dock = DockStyle.Fill;

            foreach (DnsRecordType type in DnsTypes.CommonTypes())
                _typeBox.Items.Add(DnsTypes.Name(type));

            _typeBox.SelectedIndex = 0;
            FillResolvers();

            root.Controls.Add(queryTable);

            // Results and details live in a content panel that fills whatever is left
            // between the input rows and the status bar. Computing the group positions
            // against the client size while the window was still being built is what left
            // an empty gray band above the status bar, so the panel is sized in
            // LayoutContent once the form has its real dimensions.

            // Anchored, not docked: a docked panel would fill the whole area and cover
            // the input rows above it.
            contentPanel = new Panel();
            contentPanel = new Panel();
            contentPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                                AnchorStyles.Right | AnchorStyles.Bottom;
            contentPanel.BackColor = Win95Style.Face;
            root.Controls.Add(contentPanel);

            ClassicGroupBox resultsGroup = new ClassicGroupBox();
            resultsGroup.Text = "Results";
            resultsGroup.Dock = DockStyle.Fill;

            // ClassicGroupBox reserves 20px of top padding for its caption. That is
            // what keeps "Results" readable: the docked table would otherwise start
            // 6px from the top and cover the legend.
            resultsGroup.Padding = new Padding(6, 20, 6, 6);
            contentPanel.Controls.Add(resultsGroup);

            _results = new ClassicListView();
            _results.Dock = DockStyle.Fill;
            // Headers are visible from the start, before any query has run.
            _results.SetColumns(new[] { "Type", "Name", "Value", "TTL" },
                                new[] { 60, 200, 330, 60 });
            resultsGroup.Controls.Add(_results);

            // Pinned to the bottom of the content panel, so it stays put when the
            // window grows and the results table takes the extra height.
            ClassicGroupBox detailsGroup = new ClassicGroupBox();
            detailsGroup.Text = "Query Details";
            detailsGroup.Dock = DockStyle.Bottom;
            detailsGroup.Height = 108;

            // Same caption spacing as the results box, for a consistent look.
            detailsGroup.Padding = new Padding(6, 20, 6, 6);
            contentPanel.Controls.Add(detailsGroup);

            AddDetailRow(detailsGroup, "Resolver:", 22);
            AddDetailRow(detailsGroup, "Response Time:", 42);
            AddDetailRow(detailsGroup, "Status:", 62);
            AddDetailRow(detailsGroup, "Authoritative:", 82);

            _status = new Win95StatusBarControl();
            _status.Dock = DockStyle.Bottom;
            _status.Height = Win95Style.StatusHeight;
            _status.SetPanes("Ready.", string.Empty);
            ClientSurface.Controls.Add(_status);
        }


        /// <summary>
        /// Places the content panel once the form has its real client size, so the
        /// results table fills the window instead of leaving empty space.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LayoutContent();
        }

        /// <summary>Keeps the results area filling the space under the input rows.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutContent();
        }

        /// <summary>
        /// Places the content panel between the input block and the bottom margin.

        private void LayoutContent()
        {
            if (contentPanel == null || rootPanel == null || queryTable == null) return;

            int margin = 10;
            int width = rootPanel.ClientSize.Width - (margin * 2);
            if (width < 200) return;

            // The query block sits one margin below the menu bar.
            queryTable.SetBounds(margin, margin, width, queryRowHeight * queryRowCount);

            // The results area starts a further row below the inputs, so the two
            // can never overlap however the window is resized.
            int top = queryTable.Bottom + 8;
            int bottom = rootPanel.ClientSize.Height - margin;
            if (bottom - top < 60) return;

            contentPanel.SetBounds(margin, top, width, bottom - top);
        }

        private void AddDetailRow(Control parent, string caption, int y)
        {
            ClassicLabel label = MakeLabel(caption, 12, y);
            parent.Controls.Add(label);

            TextBox value = new TextBox();
            value.Name = "Detail" + y;
            value.ReadOnly = true;
            value.BorderStyle = BorderStyle.None;
            value.BackColor = Win95Style.Face;
            value.ForeColor = Win95Style.WindowText;
            value.Font = Win95Style.CreateFontSafe();
            value.SetBounds(104, y + 1, parent.Width - 122, 15);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            parent.Controls.Add(value);
        }
        /// <summary>
        /// A standard Win95 button for the query block. The layout table gives it a
        /// cell, so the size comes from the cell rather than from a hard-coded width.
        /// </summary>
        private static ClassicButton MakeButton(string text, EventHandler onClick)
        {
            ClassicButton button = new ClassicButton();
            button.Text = text;
            button.Click += onClick;
            return button;
        }

        /// <summary>
        /// A caption for the query block. AutoSize is off on purpose: an auto-sized
        /// caption grows to fit its text and then reaches under the control in the
        /// next column, which is what painted the grey block over the combo boxes.
        /// The label is clipped to its own cell instead.
        /// </summary>
        private static ClassicLabel MakeQueryLabel(string text)
        {
            ClassicLabel label = new ClassicLabel();
            label.Text = text;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(0, 3, 6, 0);
            return label;
        }

        private static ClassicLabel MakeLabel(string text, int x, int y)
        {
            ClassicLabel label = new ClassicLabel();
            label.Text = text;
            label.SetBounds(x, y, 90, 15);
            return label;
        }

        private void SetDetail(int y, string text)
        {
            Control[] found = ClientSurface.Controls.Find("Detail" + y, true);
            if (found.Length == 0) return;

            TextBox box = found[0] as TextBox;
            if (box != null) box.Text = text ?? string.Empty;
        }

        private void SetStatus(string primary, string secondary)
        {
            if (_status != null) _status.SetPanes(primary ?? string.Empty, secondary ?? string.Empty);
        }

        // -------------------------------------------------------------- resolvers

        private void FillResolvers()
        {
            _resolverBox.BeginUpdate();
            try
            {
                _resolverBox.Items.Clear();
                _resolverBox.Items.Add(DnsQueryService.SystemDefaultLabel);

                List<string> configured = SystemResolvers.Describe();
                for (int i = 0; i < configured.Count; i++)
                    _resolverBox.Items.Add(configured[i]);

                _resolverBox.Items.Add(CustomResolverEntry);
            }
            finally
            {
                _resolverBox.EndUpdate();
            }

            _resolverBox.SelectedIndex = 0;
        }

        private void SelectResolver(string previous)
        {
            int index = _resolverBox.Items.IndexOf(previous);
            _resolverBox.SelectedIndex = index >= 0 ? index : 0;
        }

        /// <summary>
        /// Maps the chosen entry to a resolver address.
        ///
        /// "Custom DNS Server..." opens the address dialog, pre-filled with whatever
        /// was used earlier this session, and remembers the answer. It is opened every
        /// time the entry is chosen, so the address can also be corrected later.
        ///
        /// The system entry stays a special label so the engine knows to use the
        /// machine's own resolvers; the listed adapters are reduced to the bare
        /// address by dropping the "  (Adapter)" suffix.
        /// </summary>
        private string ResolveChosenServer()
        {
            string chosen = ResolverText();

            if (chosen == CustomResolverEntry)
            {
                CustomResolverForm picker = new CustomResolverForm(this, _customResolver);
                if (picker.ShowDialog(this) != DialogResult.OK) return null;

                _customResolver = picker.Value;
                if (string.IsNullOrEmpty(_customResolver))
                {
                    Win95MessageBox.ShowWarning(this, "Custom DNS Server",
                        "No resolver address was entered.", "Enter an IPv4 or IPv6 address.");
                    return null;
                }

                return _customResolver;
            }

            // "1.2.3.4  (Ethernet)" -> "1.2.3.4"
            if (chosen != DnsQueryService.SystemDefaultLabel)
            {
                int bracket = chosen.IndexOf("  (");
                if (bracket > 0) return chosen.Substring(0, bracket).Trim();
                return chosen;
            }

            return DnsQueryService.SystemDefaultLabel;
        }

        // ----------------------------------------------------------------- query

        private void StartLookup()
        {
            if (_running != null)
            {
                Win95MessageBox.ShowInfo(this, "DNS Toolbox 95",
                    "A query is already running.", "Wait for it to finish or press Cancel.");
                return;
            }

            string domain = (_domainBox.Text ?? string.Empty).Trim();
            if (domain.Length == 0)
            {
                Win95MessageBox.ShowWarning(this, "DNS Toolbox 95",
                    "Enter a domain, host name or IP address first.",
                    "For a PTR lookup, type an IP address.");
                _domainBox.Focus();
                return;
            }

            DnsRecordType type = SelectedType();
            string resolver = ResolveChosenServer();
            if (resolver == null) return;

            // PTR needs the reverse name; everything else is a normal name.
            string queryName;
            try
            {
                queryName = DnsQueryService.BuildQueryName(domain, type);
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowError(this, "DNS Toolbox 95",
                    "The name could not be used: " + ex.Message, null);
                return;
            }

            _running = new CancellationTokenSource();
            SetBusy(true);
            SetStatus("Querying " + queryName + " ...", resolver);
            ClearResults();
            Remember(domain, type);

            string query = queryName;
            CancellationToken token = _running.Token;

            RunQueryAsync(query, type, resolver, token);
        }

        private DnsRecordType SelectedType()
        {
            object selected = _typeBox.SelectedItem;
            DnsRecordType type;
            if (selected != null && DnsTypes.TryParse(selected.ToString(), out type)) return type;
            return DnsRecordType.A;
        }

        /// <summary>
        /// Runs an action on the UI thread. The engine completes on a pool thread, so
        /// the result is posted back explicitly instead of relying on a capture of the
        /// synchronisation context, which is not always present.
        /// </summary>
        private void PostToUi(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch
            {
                // The window was closed while the query was running.
            }
        }

        private async void RunQueryAsync(string query, DnsRecordType type,
                                         string resolver, CancellationToken token)
        {
            try
            {
                DnsQueryResult result = await _dns.QueryEitherAsync(resolver, query, type,
                                                                    DnsQueryService.DefaultTimeoutMs,
                                                                    token).ConfigureAwait(false);

                PostToUi(delegate
                {
                    if (IsDisposed) return;

                    _lastResult = result;
                    ShowResult(result);

                    if (result.Failed)
                        SetStatus("Query failed: " + result.Error, resolver);
                    else
                        SetStatus("Completed: " + result.Answers.Count + " record(s).", resolver);
                });
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) SetStatus("Query cancelled.", resolver);
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                Log.Write("QUERY", query, ex.GetType().Name, ex.Message);
                SetStatus("The query could not be completed.", resolver);

                Win95MessageBox.ShowError(this, "DNS Toolbox 95",
                    "The DNS query could not be completed.", ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                _running = null;
                PostToUi(delegate { if (!IsDisposed) SetBusy(false); });
            }
        }

        /// <summary>
        /// Fills the table. The columns depend on the record type, so MX shows
        /// priority, SRV shows priority/weight/port and SOA shows all seven SOA
        /// fields. The table is never filled with raw command output.
        /// </summary>
        private void ShowResult(DnsQueryResult result)
        {
            ConfigureColumns(result.RequestedType, result.Answers);

            List<string[]> rows = new List<string[]>();
            foreach (DnsRecordInfo record in result.Answers)
                rows.Add(record.Row());

            _results.AddRows(rows);

            // A CNAME chain is useful context, so it is listed under the answers.
            foreach (DnsRecordInfo record in result.Additional)
            {
                if (record.Type == DnsRecordType.CNAME)
                    rows.Add(record.Row());
            }
            if (rows.Count > result.Answers.Count)
                _results.AddRows(rows.GetRange(result.Answers.Count,
                                               rows.Count - result.Answers.Count));

            SetDetail(22, result.Resolver);
            SetDetail(44, result.ResponseMs + " ms");
            SetDetail(66, result.StatusText + (result.Failed ? " - " + result.Error : string.Empty));
            SetDetail(88, result.Authoritative ? "Yes" : "No");

            Log.Write("QUERY", result.Query + " " + DnsTypes.Name(result.RequestedType),
                      result.StatusText, result.Answers.Count + " answer(s) " + result.Error);
        }

        private void ConfigureColumns(DnsRecordType type, List<DnsRecordInfo> answers)
        {
            // If the resolver only returned CNAMEs, show the columns for those.
            DnsRecordType effective = type;
            if (type == DnsRecordType.A && answers.Count == 0)
            {
                foreach (DnsRecordInfo r in answers)
                    if (r.Type == DnsRecordType.CNAME) effective = DnsRecordType.CNAME;
            }

            switch (effective)
            {
                case DnsRecordType.MX:
                    _results.SetColumns(new[] { "Type", "Name", "Priority", "Mail Server", "TTL" },
                                        new[] { 55, 220, 60, 260, 60 });
                    break;

                case DnsRecordType.SRV:
                    _results.SetColumns(new[] { "Type", "Name", "Pri", "Weight", "Port", "Target", "TTL" },
                                        new[] { 50, 150, 45, 55, 50, 250, 55 });
                    break;

                case DnsRecordType.SOA:
                    _results.SetColumns(
                        new[] { "Type", "Name", "Primary NS", "Responsible", "Serial",
                                "Refresh", "Retry", "Expire", "Minimum", "TTL" },
                        new[] { 45, 120, 150, 130, 95, 60, 55, 60, 60, 50 });
                    break;

                case DnsRecordType.DS:
                case DnsRecordType.DNSKEY:
                case DnsRecordType.RRSIG:
                    _results.SetColumns(new[] { "Type", "Name", "Value", "Extra", "TTL" },
                                        new[] { 60, 200, 240, 220, 60 });
                    break;

                default:
                    _results.SetColumns(new[] { "Type", "Name", "Value", "TTL" },
                                        new[] { 60, 250, 330, 60 });
                    break;
            }
        }

        private void SetBusy(bool busy)
        {
            _lookupButton.Enabled = !busy;
            _domainBox.Enabled = !busy;
            _typeBox.Enabled = !busy;
            _resolverBox.Enabled = !busy;
            UseWaitCursor = busy;
        }

        private void CancelQuery()
        {
            if (_running == null)
            {
                SetStatus("Nothing to cancel.", ResolverText());
                return;
            }

            _running.Cancel();
            SetStatus("Cancelling ...", ResolverText());
        }

        private void ClearResults()
        {
            _results.ClearRows();
            _results.SetColumns(new[] { "Type", "Name", "Value", "TTL" },
                                new[] { 60, 250, 330, 60 });

            SetDetail(22, string.Empty);
            SetDetail(44, string.Empty);
            SetDetail(66, string.Empty);
            SetDetail(88, string.Empty);
            _lastResult = null;
        }

        private void Remember(string domain, DnsRecordType type)
        {
            string entry = domain + "  [" + DnsTypes.Name(type) + "]";

            _history.Remove(entry);
            _history.Insert(0, entry);
            while (_history.Count > 20) _history.RemoveAt(_history.Count - 1);
        }

        // ------------------------------------------------------- copy and report

        /// <summary>Plain, readable text for a support ticket.</summary>
        private string BuildReportText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DNS Toolbox 95");
            sb.AppendLine("=============");
            sb.AppendLine();

            if (_lastResult == null)
            {
                sb.AppendLine("No query has been run yet.");
                return sb.ToString();
            }

            sb.AppendLine("Query:          " + _lastResult.Query);
            sb.AppendLine("Type:           " + DnsTypes.Name(_lastResult.RequestedType));
            sb.AppendLine("Resolver:       " + _lastResult.Resolver);
            sb.AppendLine("Status:         " + _lastResult.StatusText);
            sb.AppendLine("Response time:  " + _lastResult.ResponseMs + " ms");
            sb.AppendLine("Authoritative:  " + (_lastResult.Authoritative ? "Yes" : "No"));
            sb.AppendLine();

            if (_lastResult.Answers.Count == 0)
            {
                sb.AppendLine("Results:");
                sb.AppendLine("  (none)");
            }
            else
            {
                sb.AppendLine("Results:");
                foreach (DnsRecordInfo record in _lastResult.Answers)
                    sb.AppendLine("  " + record.ToLine());
            }

            if (!string.IsNullOrEmpty(_lastResult.Error))
            {
                sb.AppendLine();
                sb.AppendLine("Note:");
                sb.AppendLine("  " + _lastResult.Error);
            }

            return sb.ToString();
        }

        private void CopyResults()
        {
            if (_lastResult == null)
            {
                Win95MessageBox.ShowInfo(this, "DNS Toolbox 95",
                    "There is nothing to copy yet.", "Run a lookup first.");
                return;
            }

            try
            {
                Clipboard.SetText(BuildReportText());
                SetStatus("Results copied to the clipboard.", ResolverText());
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowError(this, "DNS Toolbox 95",
                    "The results could not be copied.", ex.Message);
            }
        }

        private void SaveReport()
        {
            if (_lastResult == null)
            {
                Win95MessageBox.ShowInfo(this, "DNS Toolbox 95",
                    "There is nothing to save yet.", "Run a lookup first.");
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save Report";
                dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = "dns-report-" +
                                  _lastResult.Query.Replace('.', '-') + ".txt";

                // Files are only written when the user explicitly chooses where.
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.WriteAllText(dialog.FileName, BuildReportText(),
                                      new UTF8Encoding(false));
                    SetStatus("Report saved to " + dialog.FileName, ResolverText());
                }
                catch (Exception ex)
                {
                    Win95MessageBox.ShowError(this, "DNS Toolbox 95",
                        "The report could not be saved.", ex.Message);
                }
            }
        }

        // ----------------------------------------------------------------- tools

        private enum ToolKind
        {
            Spf, Dkim, Dmarc, Mail, Reverse, Dnssec, Compare, Propagation
        }

        private void ShowTool(ToolKind kind)
        {
            string domain = (_domainBox.Text ?? string.Empty).Trim();
            string resolver = ResolveChosenServer();
            if (resolver == null) return;

            Win95Form form;

            switch (kind)
            {
                case ToolKind.Spf:
                    form = new DiagnosticForm("SPF Check", this, domain, resolver, "spf");
                    break;
                case ToolKind.Dmarc:
                    form = new DiagnosticForm("DMARC Check", this, domain, resolver, "dmarc");
                    break;
                case ToolKind.Mail:
                    form = new DiagnosticForm("Mail DNS Check", this, domain, resolver, "mail");
                    break;
                case ToolKind.Dnssec:
                    form = new DiagnosticForm("DNSSEC Check", this, domain, resolver, "dnssec");
                    break;
                case ToolKind.Dkim:
                    form = new DkimForm(this, domain, resolver);
                    break;
                case ToolKind.Reverse:
                    form = new ReverseDnsForm(this, domain, resolver);
                    break;
                case ToolKind.Compare:
                    form = new CompareResolversForm(this, domain, SelectedType());
                    break;
                default:
                    form = new PropagationForm(this, domain, SelectedType());
                    break;
            }

            form.ShowDialog(this);
        }

        
        // ------------------------------------------------------------- settings

        private void LoadSettings()
        {
            if (!string.IsNullOrEmpty(AppSettings.LastDomain)) _domainBox.Text = AppSettings.LastDomain;

            DnsRecordType type;
            if (DnsTypes.TryParse(AppSettings.LastType, out type))
            {
                string name = DnsTypes.Name(type);
                int index = _typeBox.Items.IndexOf(name);
                if (index >= 0) _typeBox.SelectedIndex = index;
            }
        }

        private void ShowAbout()
        {
            using (AboutForm about = new AboutForm(this))
                about.ShowDialog(this);
        }
        }
}
