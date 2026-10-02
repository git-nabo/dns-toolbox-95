using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>
    /// Reverse (PTR) lookups for an IP address, with an optional
    /// forward-confirmed reverse DNS check: the PTR name is resolved forward and the
    /// result compared with the original address.
    /// </summary>
    public sealed class ReverseDnsAnalyzer
    {
        private readonly DnsQueryService _dns;
        private readonly string _resolver;
        private readonly int _timeoutMs;

        public ReverseDnsAnalyzer(DnsQueryService dns, string resolver, int timeoutMs)
        {
            _dns = dns;
            _resolver = resolver;
            _timeoutMs = timeoutMs;
        }

        public async Task<DiagnosticReport> AnalyzeAsync(string input, bool forwardConfirm,
                                                          CancellationToken cancel)
        {
            DiagnosticReport report = new DiagnosticReport();

            IPAddress address;
            if (!IPAddress.TryParse((input ?? string.Empty).Trim(), out address))
            {
                report.Title = "Reverse DNS for " + input;
                report.Add(Severity.Fail,
                    "'" + input + "' is not a valid IPv4 or IPv6 address.");
                return report;
            }

            report.Title = "Reverse DNS for " + address;

            string reverseName = DnsQueryService.ReverseName(address);
            report.Details.Add("IP:            " + address);
            report.Details.Add("Reverse name:  " + reverseName);
            report.Details.Add("Resolver:      " +
                               (string.IsNullOrEmpty(_resolver) ? DnsQueryService.SystemDefaultLabel : _resolver));
            report.Details.Add("");

            DnsQueryResult ptr = await _dns.QueryEitherAsync(_resolver, reverseName,
                                                              DnsRecordType.PTR, _timeoutMs, cancel)
                                          .ConfigureAwait(false);

            report.Details.Add("Response time: " + ptr.ResponseMs + " ms");
            report.Details.Add("Status:        " + ptr.StatusText);

            if (ptr.Failed)
            {
                report.Add(Severity.Fail, "The PTR lookup failed: " + ptr.Error);
                return report;
            }

            if (ptr.Answers.Count == 0)
            {
                report.Add(Severity.Warn,
                    "No PTR record exists for this address. Reverse DNS is optional for " +
                    "general hosts but expected for mail servers.");

                if (ptr.Status == DnsStatus.NXDOMAIN)
                    report.Details.Add("The reverse zone returned NXDOMAIN.");

                return report;
            }

            report.Add(Severity.Ok, "A PTR record exists.");

            List<string> names = new List<string>();
            foreach (DnsRecordInfo r in ptr.Answers)
            {
                if (string.IsNullOrEmpty(r.Value)) continue;
                names.Add(r.Value);
                report.Details.Add("PTR hostname:  " + r.Value + "   TTL " + r.Ttl);
            }

            if (names.Count > 1)
                report.Add(Severity.Warn,
                    "The address resolves to " + names.Count + " names. Some mail servers reject " +
                    "addresses with multiple PTR records.");

            if (!forwardConfirm || names.Count == 0)
                return report;

            // Forward-confirmed reverse DNS.
            foreach (string name in names)
            {
                if (cancel.IsCancellationRequested) return report;

                DnsQueryResult forward = await _dns.QueryEitherAsync(_resolver, name,
                                                                    DnsRecordType.A, _timeoutMs, cancel)
                                                .ConfigureAwait(false);

                if (forward.Failed)
                {
                    report.Add(Severity.Warn,
                        "Forward lookup of " + name + " failed: " + forward.Error);
                    continue;
                }

                bool confirmed = false;
                foreach (DnsRecordInfo r in forward.Answers)
                {
                    if (string.Equals(r.Value, address.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        confirmed = true;
                        break;
                    }
                }

                if (confirmed)
                {
                    report.Add(Severity.Ok,
                        "Forward-confirmed reverse DNS: yes (" + name + " resolves back to " +
                        address + ").");
                }
                else
                {
                    report.Add(Severity.Warn,
                        "Forward-confirmed reverse DNS: no. " + name + " does not resolve back to " +
                        address + ".");
                    foreach (DnsRecordInfo r in forward.Answers)
                        report.Details.Add("  " + name + " -> " + r.Value);
                }
            }

            return report;
        }
    }
}
