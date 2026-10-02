using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>One mail exchanger and the addresses it resolves to.</summary>
    public sealed class MailExchanger
    {
        public int Priority;
        public string Hostname = string.Empty;
        public readonly List<string> IPv4 = new List<string>();
        public readonly List<string> IPv6 = new List<string>();
        public readonly List<string> Ptr = new List<string>();

        public bool Resolves
        {
            get { return IPv4.Count > 0 || IPv6.Count > 0; }
        }
    }

    /// <summary>
    /// The combined mail check: MX records, address resolution for every exchanger,
    /// reverse DNS, SPF and DMARC. It reports what it actually looked up and does not
    /// pretend to have tested DKIM, which cannot be verified without a message.
    /// </summary>
    public sealed class MailDnsAnalyzer
    {
        private readonly DnsQueryService _dns;
        private readonly string _resolver;
        private readonly int _timeoutMs;

        public MailDnsAnalyzer(DnsQueryService dns, string resolver, int timeoutMs)
        {
            _dns = dns;
            _resolver = resolver;
            _timeoutMs = timeoutMs;
        }

        public async Task<DiagnosticReport> AnalyzeAsync(string domain, CancellationToken cancel)
        {
            DiagnosticReport report = new DiagnosticReport();
            report.Title = "Mail DNS check for " + domain;

            string name = DnsMessage.NormalizeName(domain);

            List<MailExchanger> exchangers = await CheckMxAsync(name, report, cancel)
                                                    .ConfigureAwait(false);

            if (exchangers.Count > 0)
                await CheckExchangersAsync(exchangers, report, cancel).ConfigureAwait(false);

            await CheckNsAsync(name, report, cancel).ConfigureAwait(false);

            report.Add(Severity.Info,
                "DKIM is not tested here. A DKIM key is published per selector, so use " +
                "Tools > DKIM Check with the selector from the message headers.");

            DiagnosticReport spf = await new SpfAnalyzer(_dns, _resolver, _timeoutMs)
                                        .AnalyzeAsync(name, cancel).ConfigureAwait(false);
            Merge(report, "SPF", spf);

            DiagnosticReport dmarc = await new DmarcAnalyzer(_dns, _resolver, _timeoutMs)
                                          .AnalyzeAsync(name, cancel).ConfigureAwait(false);
            Merge(report, "DMARC", dmarc);

            return report;
        }

        /// <summary>Appends a sub-report's findings and details under a heading.</summary>
        private static void Merge(DiagnosticReport into, string heading, DiagnosticReport sub)
        {
            into.Details.Add("");
            into.Details.Add("--- " + heading + " ---");
            foreach (Finding f in sub.Findings)
                into.Findings.Add(f);
            foreach (string d in sub.Details)
                into.Details.Add(d);
        }

        private async Task<List<MailExchanger>> CheckMxAsync(string domain,
                                                              DiagnosticReport report,
                                                              CancellationToken cancel)
        {
            List<MailExchanger> exchangers = new List<MailExchanger>();

            DnsQueryResult mx = await _dns.QueryEitherAsync(_resolver, domain,
                                                             DnsRecordType.MX, _timeoutMs, cancel)
                                         .ConfigureAwait(false);

            if (mx.Failed)
            {
                report.Add(Severity.Fail, "The MX lookup failed: " + mx.Error);
                return exchangers;
            }

            if (mx.Answers.Count == 0)
            {
                if (mx.Status == DnsStatus.NXDOMAIN)
                    report.Add(Severity.Fail, "The domain does not exist (NXDOMAIN).");
                else
                    report.Add(Severity.Fail,
                        "No MX record was found, so mail for this domain has nowhere to go.");

                return exchangers;
            }

            // RFC 7505 "null MX": a single record with an empty exchange.
            bool nullMx = false;
            foreach (DnsRecordInfo r in mx.Answers)
                if (string.IsNullOrEmpty(r.Value) || r.Value == ".") nullMx = true;

            if (nullMx)
            {
                report.Add(Severity.Ok,
                    "A null MX record is published, which correctly states that this domain " +
                    "accepts no mail.");
                return exchangers;
            }

            report.Add(Severity.Ok, "MX record found (" + mx.Answers.Count + ").");

            List<int> priorities = new List<int>();
            foreach (DnsRecordInfo r in mx.Answers)
            {
                if (string.IsNullOrEmpty(r.Value) || r.Value == ".")
                {
                    report.Add(Severity.Warn,
                        "An MX record has an empty exchange, which is not valid.");
                    continue;
                }

                priorities.Add(r.Priority);
                exchangers.Add(new MailExchanger { Priority = r.Priority, Hostname = r.Value });
            }

            for (int i = 0; i < priorities.Count; i++)
            {
                for (int j = i + 1; j < priorities.Count; j++)
                {
                    if (priorities[i] == priorities[j])
                        report.Add(Severity.Warn,
                            "Two MX records share priority " + priorities[i] +
                            ". Some receivers treat this as an error.");
                }
            }

            exchangers.Sort(delegate(MailExchanger a, MailExchanger b)
            {
                return a.Priority.CompareTo(b.Priority);
            });

            return exchangers;
        }

        /// <summary>Resolves every mail exchanger and checks its reverse DNS.</summary>
        private async Task CheckExchangersAsync(List<MailExchanger> exchangers,
                                                DiagnosticReport report,
                                                CancellationToken cancel)
        {
            foreach (MailExchanger mx in exchangers)
            {
                if (cancel.IsCancellationRequested) return;

                report.Details.Add("");
                report.Details.Add("MX " + mx.Priority + "  " + mx.Hostname);

                DnsQueryResult a = await _dns.QueryEitherAsync(_resolver, mx.Hostname,
                                                               DnsRecordType.A, _timeoutMs, cancel)
                                           .ConfigureAwait(false);
                if (!a.Failed)
                    foreach (DnsRecordInfo r in a.Answers) mx.IPv4.Add(r.Value);

                DnsQueryResult aaaa = await _dns.QueryEitherAsync(_resolver, mx.Hostname,
                                                                 DnsRecordType.AAAA, _timeoutMs, cancel)
                                               .ConfigureAwait(false);
                if (!aaaa.Failed)
                    foreach (DnsRecordInfo r in aaaa.Answers) mx.IPv6.Add(r.Value);

                if (mx.Resolves)
                {
                    report.Add(Severity.Ok, "MX host " + mx.Hostname + " resolves.");
                    if (mx.IPv4.Count > 0)
                        report.Details.Add("  IPv4: " + string.Join(", ", mx.IPv4.ToArray()));
                    if (mx.IPv6.Count > 0)
                        report.Details.Add("  IPv6: " + string.Join(", ", mx.IPv6.ToArray()));
                }
                else
                {
                    report.Add(Severity.Fail,
                        "MX host " + mx.Hostname +
                        " does not resolve to any A or AAAA record. Mail to this exchanger " +
                        "cannot be delivered.");
                }

                // Reverse DNS for each address of the exchanger.
                List<string> addresses = new List<string>();
                addresses.AddRange(mx.IPv4);
                addresses.AddRange(mx.IPv6);

                foreach (string address in addresses)
                {
                    if (cancel.IsCancellationRequested) return;

                    DnsQueryResult ptr = await _dns.QueryEitherAsync(
                        _resolver, DnsQueryService.BuildQueryName(address, DnsRecordType.PTR),
                        DnsRecordType.PTR, _timeoutMs, cancel).ConfigureAwait(false);

                    if (!ptr.Failed && ptr.Answers.Count > 0)
                    {
                        foreach (DnsRecordInfo r in ptr.Answers)
                        {
                            if (!string.IsNullOrEmpty(r.Value)) mx.Ptr.Add(r.Value);
                        }
                    }
                }

                if (mx.Ptr.Count == 0)
                {
                    report.Add(Severity.Warn,
                        "No PTR record for " + mx.Hostname + ". Most receiving servers require " +
                        "reverse DNS for the sending address.");
                }
                else
                {
                    report.Add(Severity.Ok,
                        "Reverse DNS for " + mx.Hostname + ": " + string.Join(", ", mx.Ptr.ToArray()));
                }
            }
        }

        private async Task CheckNsAsync(string domain, DiagnosticReport report,
                                        CancellationToken cancel)
        {
            DnsQueryResult ns = await _dns.QueryEitherAsync(_resolver, domain,
                                                            DnsRecordType.NS, _timeoutMs, cancel)
                                        .ConfigureAwait(false);

            if (ns.Failed)
            {
                report.Add(Severity.Warn, "The NS lookup failed: " + ns.Error);
                return;
            }

            if (ns.Answers.Count == 0)
            {
                report.Add(Severity.Warn, "No NS records were returned for this domain.");
                return;
            }

            report.Add(Severity.Ok, "NS record found (" + ns.Answers.Count + "):");
            foreach (DnsRecordInfo r in ns.Answers)
                report.Details.Add("  " + r.Value);
        }
    }
}
