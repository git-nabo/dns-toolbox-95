using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DnsToolbox95.Dns;
using DnsToolbox95.Diagnostics;

namespace DnsToolbox95.Ui
{
    /// <summary>Shared layout helpers for the small tool dialogs.</summary>
    internal static class ToolLayout
    {
        public static TextBox MakeBox(int x, int y, int width, string text)
        {
            TextBox box = new TextBox();
            box.SetBounds(x, y, width, 21);
            box.Font = Win95Style.CreateFontSafe();
            box.Text = text ?? string.Empty;
            return box;
        }

        public static void AddField(Control parent, string caption, int y, Control field)
        {
            ClassicLabel label = new ClassicLabel();
            label.Text = caption;
            label.SetBounds(8, y + 3, 76, 15);
            parent.Controls.Add(label);
            parent.Controls.Add(field);
        }

        /// <summary>A read-only, monospaced output area inside a sunken panel.</summary>
        public static TextBox MakeOutput(Control group)
        {
            SunkenPanel host = new SunkenPanel();
            host.SetBounds(8, 20, group.Width - 16, group.Height - 28);
            host.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right |
                          AnchorStyles.Bottom;
            group.Controls.Add(host);

            TextBox output = new TextBox();
            output.Multiline = true;
            output.ReadOnly = true;
            output.ScrollBars = ScrollBars.Both;
            output.WordWrap = false;
            output.BackColor = Win95Style.FieldBack;
            output.Font = new Font("Consolas", 8.5f);
            output.Dock = DockStyle.Fill;
            host.Controls.Add(output);
            return output;
        }

        public static void ScrollToTop(TextBox box, string text)
        {
            box.Text = text;
            box.SelectionStart = 0;
            box.SelectionLength = 0;
        }
    }

    /// <summary>DKIM check: a domain, a selector and the published public key.</summary>
    public sealed class DkimForm : ToolDialogForm
    {
        private readonly DnsQueryService _dns = new DnsQueryService();
        private readonly string _resolver;

        private TextBox _domainBox;
        private TextBox _selectorBox;
        private TextBox _output;
        private ClassicButton _checkButton;
        private CancellationTokenSource _running;
        public DkimForm(IWin32Window owner, string domain, string resolver)
            : base("DKIM Check - DNS Toolbox 95", owner, 700, 470)
        {
            _resolver = resolver;
            ResolverText = resolver;
            MinimumSize = new Size(520, 320);
            BuildLayout(domain);
        }

        private void BuildLayout(string domain)
        {
            // Two inputs on the top row, with the action buttons on the right.
            Top.Controls.Add(Caption("Domain:", 0, 0, 60));
            _domainBox = Field(64, 0, 300, domain);
            _domainBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Top.Controls.Add(_domainBox);

            Top.Controls.Add(Caption("Selector:", 380, 0, 60));
            _selectorBox = Field(444, 0, 130, "selector1");
            Top.Controls.Add(_selectorBox);

            _checkButton = TopButton("&Check", ButtonWidth + 6, 80, delegate { Run(); });
            _checkButton.IsDefaultButton = true;
            Top.Controls.Add(_checkButton);

            _output = AddResultArea("Key");

            AddBottomButtons("Copy &All", delegate { CopyAll(); }, delegate { Close(); });
        }


        private void CopyAll()
        {
            if (_output == null || string.IsNullOrEmpty(_output.Text)) return;

            try
            {
                Clipboard.SetText(_output.Text);
                SetStatus("Copied to the clipboard.");
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowError(this, Text, "The text could not be copied.",
                                            ex.Message);
            }
        }

        private void Run()
        {
            if (_running != null) return;

            string domain = (_domainBox.Text ?? string.Empty).Trim();
            string selector = (_selectorBox.Text ?? string.Empty).Trim();

            if (domain.Length == 0)
            {
                Win95MessageBox.ShowWarning(this, Text, "Enter a domain name first.",
                                            "For example: example.com");
                return;
            }

            _running = new CancellationTokenSource();
            _checkButton.Enabled = false;
            UseWaitCursor = true;
            _output.Clear();

            RunAsync(domain, selector, _running.Token);
        }

        private async void RunAsync(string domain, string selector, CancellationToken token)
        {
            try
            {
                DiagnosticReport report = await new DkimAnalyzer(_dns, _resolver,
                                                                 DnsQueryService.DefaultTimeoutMs)
                                                 .AnalyzeAsync(domain, selector, token);
                if (IsDisposed) return;

                ToolLayout.ScrollToTop(_output, report.ToText());
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _output.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _output.Text = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                _running = null;
                if (!IsDisposed) { _checkButton.Enabled = true; UseWaitCursor = false; }
            }
        }
    }

    /// <summary>Reverse DNS for an address, with forward-confirmed checking.</summary>
    public sealed class ReverseDnsForm : ToolDialogForm
    {
        private readonly DnsQueryService _dns = new DnsQueryService();
        private readonly string _resolver;

        private TextBox _ipBox;
        private TextBox _output;
        private ClassicCheckBox _fcrdnsBox;
        private ClassicButton _checkButton;
        private CancellationTokenSource _running;

        public ReverseDnsForm(IWin32Window owner, string initial, string resolver)
            : base("Reverse DNS - DNS Toolbox 95", owner, 700, 440)
        {
            _resolver = resolver;
            ResolverText = resolver;
            MinimumSize = new Size(480, 320);
            BuildLayout(initial);
        }

        private void BuildLayout(string initial)
        {
            // IP address and the forward-confirmation option share the top row,
            // with Check and Cancel anchored to the right edge.
            Top.Controls.Add(Caption("IP address:", 0, 0, 76));

            _ipBox = Field(80, 0, 200, initial);
            _ipBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Top.Controls.Add(_ipBox);

            _fcrdnsBox = new ClassicCheckBox();
            _fcrdnsBox.Text = "Forward-confirmed reverse DNS";
            _fcrdnsBox.Checked = true;
            _fcrdnsBox.SetBounds(286, 4, 230, 16);
            _fcrdnsBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Top.Controls.Add(_fcrdnsBox);

            _checkButton = TopButton("&Check", ButtonWidth + 6, 80, delegate { Run(); });
            _checkButton.IsDefaultButton = true;
            Top.Controls.Add(_checkButton);

            ClassicButton cancel = TopButton("Cancel", 0, 90, delegate { Close(); });
            cancel.IsCancelButton = true;
            Top.Controls.Add(cancel);

            _output = AddResultArea("Result");

            AddBottomButtons("Copy &All", delegate { CopyAll(); }, delegate { Close(); });
        }

        private void CopyAll()
        {
            if (_output == null || string.IsNullOrEmpty(_output.Text)) return;

            try
            {
                Clipboard.SetText(_output.Text);
                SetStatus("Copied to the clipboard.");
            }
            catch (Exception ex)
            {
                Win95MessageBox.ShowError(this, Text, "The text could not be copied.",
                                            ex.Message);
            }
        }


        private void Run()
        {
            if (_running != null) return;

            string ip = (_ipBox.Text ?? string.Empty).Trim();
            if (ip.Length == 0)
            {
                Win95MessageBox.ShowWarning(this, Text, "Enter an IP address first.",
                                            "IPv4 and IPv6 are both supported.");
                return;
            }

            _running = new CancellationTokenSource();
            _checkButton.Enabled = false;
            UseWaitCursor = true;
            _output.Clear();

            RunAsync(ip, _fcrdnsBox.Checked, _running.Token);
        }

        private async void RunAsync(string ip, bool fcrdns, CancellationToken token)
        {
            try
            {
                DiagnosticReport report = await new ReverseDnsAnalyzer(_dns, _resolver,
                                                                       DnsQueryService.DefaultTimeoutMs)
                                               .AnalyzeAsync(ip, fcrdns, token);
                if (IsDisposed) return;

                ToolLayout.ScrollToTop(_output, report.ToText());
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _output.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                if (!IsDisposed) _output.Text = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                _running = null;
                if (!IsDisposed) { _checkButton.Enabled = true; UseWaitCursor = false; }
            }
        }
    }
}
