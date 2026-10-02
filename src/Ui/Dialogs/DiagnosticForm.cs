using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DnsToolbox95.Dns;
using DnsToolbox95.Diagnostics;
using DnsToolbox95.Settings;

namespace DnsToolbox95.Ui
{
    /// <summary>
    /// The window used by SPF, DMARC, Mail DNS and DNSSEC. They all run one analyzer
    /// and show the same [OK]/[WARN]/[FAIL]/[INFO] findings, so one form serves them
    /// all and the presentation stays identical across the suite.
    ///
    /// The run happens on the thread pool with a Cancel button, so a slow resolver
    /// never blocks the window.
    /// </summary>
    public sealed class DiagnosticForm : ToolDialogForm
    {
        private readonly DnsQueryService _dns = new DnsQueryService();
        private readonly string _kind;
        private readonly string _resolver;

        private TextBox _domainBox;
        private TextBox _output;
        private ClassicButton _runButton;
        private ClassicButton _cancelButton;

        private CancellationTokenSource _running;
        private string _lastText = string.Empty;

        public DiagnosticForm(string caption, IWin32Window owner, string domain,
                               string resolver, string kind)
            // Sized to the content: wide enough for a long SPF record to read, with
            // no large empty area below the results.
            : base(caption + " - DNS Toolbox 95", owner, 760, 520)
        {
            _kind = kind;
            _resolver = resolver;
            ResolverText = resolver;

            MinimumSize = new Size(560, 360);
            BuildLayout(domain);

            Shown += delegate { Run(); };
        }

        private void BuildLayout(string domain)
        {
            // Top row: caption, input and the two action buttons on the right.
            Top.Controls.Add(Caption("Domain:", 0, 0, 60));

            _domainBox = Field(64, 0, 380, domain);
            _domainBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Top.Controls.Add(_domainBox);

            _runButton = TopButton("&Check", ButtonWidth + 6, 80, delegate { Run(); });
            _runButton.IsDefaultButton = true;
            Top.Controls.Add(_runButton);

            _cancelButton = TopButton("&Cancel", 0, 90, delegate { Cancel(); });
            _cancelButton.Enabled = false;
            Top.Controls.Add(_cancelButton);

            // Middle: the result area fills everything that is left.
            _output = AddResultArea("Results");

            AddBottomButtons("Copy &All", delegate { CopyAll(); }, delegate { Close(); });
        }




        private void Cancel()
        {
            if (_running != null) _running.Cancel();
        }

        private void CopyAll()
        {
            if (string.IsNullOrEmpty(_lastText)) return;

            try
            {
                Clipboard.SetText(_lastText);
                SetStatus("Copied to the clipboard.");
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowError(this, Text, "The text could not be copied.", ex.Message);
            }
        }

        private void Run()
        {
            if (_running != null) return;

            string domain = (_domainBox.Text ?? string.Empty).Trim();
            if (domain.Length == 0)
            {
                Win95MessageBox.ShowWarning(this, Text,
                    "Enter a domain name first.", "For example: example.com");
                _domainBox.Focus();
                return;
            }

            _running = new CancellationTokenSource();
            SetBusy(true);
            SetStatus("Working ...");
            _output.Clear();

            CancellationToken token = _running.Token;
            string kind = _kind;
            string resolver = _resolver;

            RunAsync(domain, kind, resolver, token);
        }

        private async void RunAsync(string domain, string kind, string resolver,
                                    CancellationToken token)
        {
            try
            {
                DiagnosticReport report = await ExecuteAsync(domain, kind, resolver, token);

                if (IsDisposed) return;

                _lastText = report.ToText();
                _output.Text = _lastText;
                _output.SelectionStart = 0;
                _output.SelectionLength = 0;

                SetStatus(Summary(report));
                Log.Write("DIAG", domain + " " + kind, Summary(report), null);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) SetStatus("Cancelled.");
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                _output.Text = ex.GetType().Name + ": " + ex.Message;
                SetStatus("The check could not be completed.");
            }
            finally
            {
                _running = null;
                if (!IsDisposed) SetBusy(false);
            }
        }

        private Task<DiagnosticReport> ExecuteAsync(string domain, string kind, string resolver,
                                                    CancellationToken token)
        {
            switch (kind)
            {
                case "spf":
                    return new SpfAnalyzer(_dns, resolver, DnsQueryService.DefaultTimeoutMs)
                        .AnalyzeAsync(domain, token);

                case "dmarc":
                    return new DmarcAnalyzer(_dns, resolver, DnsQueryService.DefaultTimeoutMs)
                        .AnalyzeAsync(domain, token);

                case "mail":
                    return new MailDnsAnalyzer(_dns, resolver, DnsQueryService.DefaultTimeoutMs)
                        .AnalyzeAsync(domain, token);

                default:
                    return new DnssecAnalyzer(_dns, resolver, DnsQueryService.DefaultTimeoutMs)
                        .AnalyzeAsync(domain, token);
            }
        }

        private static string Summary(DiagnosticReport report)
        {
            int fail = report.Count(Severity.Fail);
            int warn = report.Count(Severity.Warn);

            if (fail > 0) return "Completed with " + fail + " problem(s) and " + warn + " warning(s).";
            if (warn > 0) return "Completed with " + warn + " warning(s).";
            return "Completed: no problems found.";
        }

        private void SetBusy(bool busy)
        {
            _runButton.Enabled = !busy;
            _cancelButton.Enabled = busy;
            _domainBox.Enabled = !busy;
            UseWaitCursor = busy;
        }
    }
}
