using System;
using System.Collections.Generic;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using DnsToolbox95.Dns;
using DnsToolbox95.Diagnostics;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// Shared body for the two resolver-comparison windows. Both run the same
    /// comparison; only the resolver list and the wording of the note differ.
    /// </summary>
    public abstract class ResolverCompareBase : ToolDialogForm
    {
        protected readonly DnsQueryService Dns = new DnsQueryService();
        protected readonly DnsRecordType RecordType;

        private TextBox _domainBox;
        private ClassicListView _grid;
        private TextBox _summaryBox;
        private ClassicButton _runButton;
        private CancellationTokenSource _running;

        protected ResolverCompareBase(string caption, IWin32Window owner,
                                      string domain, DnsRecordType type, string note)
            : base(caption, owner, 720, 520)
        {
            RecordType = type;
            MinimumSize = new Size(560, 400);
            BuildLayout(domain, note);
        }

        /// <summary>The resolvers this dialog queries.</summary>
        protected abstract List<string> ChooseResolvers();

        private void BuildLayout(string domain, string note)
        {
            Top.Controls.Add(Caption("Domain:", 0, 0, 60));

            _domainBox = Field(64, 0, 360, domain);
            _domainBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Top.Controls.Add(_domainBox);

            if (!string.IsNullOrEmpty(note))
            {
                // A second line of top strip holds the explanatory note. 
                GrowTop(RowHeight + 6);
                Top.Controls.Add(Caption(note, 64, RowHeight + 8, 560));
            }

            _runButton = TopButton("&Compare", ButtonWidth + 6, 90, delegate { Run(); });
            _runButton.IsDefaultButton = true;
            Top.Controls.Add(_runButton);

            ClassicGroupBox group = AddResultGroup("Answers (" + DnsTypes.Name(RecordType) + ")");

            _grid = new ClassicListView();
            _grid.Dock = DockStyle.Fill;
            _grid.SetColumns(new[] { "Resolver", "Status", "Response", "TTL", "Time" },
                             new[] { 130, 80, 300, 55, 60 });
            group.Controls.Add(_grid);

            ClassicGroupBox summaryGroup = new ClassicGroupBox();
            summaryGroup.Text = "Summary";
            summaryGroup.Dock = DockStyle.Bottom;
            summaryGroup.Height = 70;
            summaryGroup.Padding = new Padding(6);
            Root.Controls.Add(summaryGroup);

            _summaryBox = new TextBox();
            _summaryBox.Multiline = true;
            _summaryBox.ReadOnly = true;
            _summaryBox.ScrollBars = ScrollBars.Vertical;
            _summaryBox.BackColor = Win95Style.FieldBack;
            _summaryBox.Font = Win95Style.CreateFontSafe();
            _summaryBox.Dock = DockStyle.Fill;
            SunkenPanel summaryHost = new SunkenPanel();
            summaryHost.Dock = DockStyle.Fill;
            summaryHost.Padding = new Padding(3);
            summaryGroup.Controls.Add(summaryHost);
            summaryHost.Controls.Add(_summaryBox);

            AddBottomButtons(null, null, delegate { Close(); });
        }


        private void Run()
        {
            if (_running != null) return;

            string domain = (_domainBox.Text ?? string.Empty).Trim();
            if (domain.Length == 0)
            {
                Win95MessageBox.ShowWarning(this, Text, "Enter a domain name first.",
                                            "For example: example.com");
                return;
            }

            _running = new CancellationTokenSource();
            _runButton.Enabled = false;
            UseWaitCursor = true;
            _grid.ClearRows();
            _summaryBox.Text = "Comparing resolvers ...";

            RunAsync(domain, _running.Token);
        }

        private async void RunAsync(string domain, System.Threading.CancellationToken token)
        {
            try
            {
                ResolverComparisonService service = new ResolverComparisonService(
                    Dns, DnsQueryService.DefaultTimeoutMs);

                Progress<string> progress =
                    new Progress<string>(delegate(string text)
                    {
                        if (!IsDisposed) _summaryBox.Text = text;
                    });

                List<ResolverComparisonRow> rows = await service.CompareAsync(
                    domain, RecordType, ChooseResolvers(), progress, token);

                if (IsDisposed) return;

                List<string[]> cells = new List<string[]>();
                foreach (ResolverComparisonRow row in rows)
                {
                    string response = row.Failed
                        ? (row.Error.Length > 40 ? row.Error.Substring(0, 40) + "..." : row.Error)
                        : row.Response;

                    cells.Add(new[]
                    {
                        row.Resolver, row.Status, response, row.Ttl, row.ResponseMs + " ms"
                    });
                }

                _grid.AddRows(cells);
                _summaryBox.Text = ResolverComparisonService.Summarize(rows);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _summaryBox.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _summaryBox.Text = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                _running = null;
                if (!IsDisposed) { _runButton.Enabled = true; UseWaitCursor = false; }
            }
        }
    }

    /// <summary>Compare the same record across the system resolver and the public ones.</summary>
    public sealed class CompareResolversForm : ResolverCompareBase
    {
        public CompareResolversForm(IWin32Window owner, string domain, DnsRecordType type)
            : base("Compare Resolvers - DNS Toolbox 95", owner, domain, type,
                   "Compares the answers given by each resolver below.")
        {
        }

        protected override List<string> ChooseResolvers()
        {
            return ResolverComparisonService.DefaultResolvers();
        }
    }

    /// <summary>
    /// A sample-based propagation check. It is explicitly not presented as a scan of
    /// every resolver on the internet, only a comparison of the ones queried.
    /// </summary>
    public sealed class PropagationForm : ResolverCompareBase
    {
        public PropagationForm(IWin32Window owner, string domain, DnsRecordType type)
            : base("Propagation Check - DNS Toolbox 95", owner, domain, type,
                   "Compares a sample of public resolvers. This is not a worldwide scan.")
        {
        }

        protected override List<string> ChooseResolvers()
        {
            List<string> list = new List<string>();
            list.AddRange(SystemResolvers.Presets);
            return list;
        }
    }
}
