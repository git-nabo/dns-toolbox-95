using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>
    /// Reports the DNSSEC material published for a domain.
    ///
    /// The DNSSEC OK bit is set on the query so the resolver returns DS, DNSKEY and
    /// RRSIG records. Whether the data was actually cryptographically validated is
    /// stated plainly: only a validating resolver reports the AD flag, and this tool
    /// never claims a chain was verified when the resolver did not say so.
    /// </summary>
    public sealed class DnssecAnalyzer
    {
        private readonly DnsQueryService _dns;
        private readonly string _resolver;
        private readonly int _timeoutMs;

        public DnssecAnalyzer(DnsQueryService dns, string resolver, int timeoutMs)
        {
            _dns = dns;
            _resolver = resolver;
            _timeoutMs = timeoutMs;
        }

        public async Task<DiagnosticReport> AnalyzeAsync(string domain, CancellationToken cancel)
        {
            DiagnosticReport report = new DiagnosticReport();
            report.Title = "DNSSEC check for " + domain;

            string name = DnsMessage.NormalizeName(domain);

            List<DnsRecordInfo> ds = new List<DnsRecordInfo>();
            List<DnsRecordInfo> dnskey = new List<DnsRecordInfo>();
            List<DnsRecordInfo> rrsig = new List<DnsRecordInfo>();
            bool adFlag = false;
            bool anyResponse = false;

            // The parent zone publishes the DS record, so it is queried separately.
            DnsQueryResult dsResult = await QueryDnssecAsync(name, DnsRecordType.DS, cancel)
                                          .ConfigureAwait(false);
            if (!dsResult.Failed) { anyResponse = true; adFlag |= dsResult.AuthenticatedData; }
            ds.AddRange(dsResult.Answers);

            DnsQueryResult keyResult = await QueryDnssecAsync(name, DnsRecordType.DNSKEY, cancel)
                                           .ConfigureAwait(false);
            if (!keyResult.Failed) { anyResponse = true; adFlag |= keyResult.AuthenticatedData; }
            dnskey.AddRange(keyResult.Answers);

            DnsQueryResult sigResult = await QueryDnssecAsync(name, DnsRecordType.RRSIG, cancel)
                                           .ConfigureAwait(false);
            if (!sigResult.Failed) { anyResponse = true; adFlag |= sigResult.AuthenticatedData; }
            rrsig.AddRange(sigResult.Answers);

            if (!anyResponse)
            {
                report.Add(Severity.Fail,
                    "No DNSSEC records could be read from the selected resolver.");
                return report;
            }

            report.Details.Add("Resolver:  " +
                               (string.IsNullOrEmpty(_resolver) ? DnsQueryService.SystemDefaultLabel : _resolver));
            report.Details.Add("");

            if (ds.Count == 0)
            {
                report.Add(Severity.Warn,
                    "No DS record was found. The parent zone publishes no delegation for this " +
                    "domain, so it is not signed with DNSSEC.");
            }
            else
            {
                report.Add(Severity.Ok, "DS record found (" + ds.Count + "):");
                foreach (DnsRecordInfo r in ds)
                    report.Details.Add("  " + r.Name + "  " + r.Value);
            }

            if (dnskey.Count == 0)
            {
                report.Add(Severity.Warn,
                    "No DNSKEY record was found at the domain apex. Unsigned zones do not " +
                    "publish one.");
            }
            else
            {
                report.Add(Severity.Ok, "DNSKEY record found (" + dnskey.Count + "):");
                foreach (DnsRecordInfo r in dnskey)
                    report.Details.Add("  " + r.Value);
            }

            if (rrsig.Count == 0)
                report.Add(Severity.Warn, "No RRSIG record was returned for the domain.");
            else
                report.Add(Severity.Ok, "RRSIG record found (" + rrsig.Count + ").");

            report.Details.Add("");
            if (adFlag)
            {
                report.Add(Severity.Ok,
                    "The resolver set the Authenticated Data flag, so it validated the " +
                    "DNSSEC chain for these answers.");
            }
            else
            {
                report.Add(Severity.Info,
                    "The resolver did not set the Authenticated Data flag. It may be a plain " +
                    "resolver that does not validate signatures, so the chain was NOT verified " +
                    "by this check. Use a validating resolver such as 1.1.1.1, 8.8.8.8 or " +
                    "9.9.9.9 for a genuine validation result.");
            }

            return report;
        }

        private async Task<DnsQueryResult> QueryDnssecAsync(string name, DnsRecordType type,
                                                            CancellationToken cancel)
        {
            // A specific resolver is required: the system resolver may ignore the DO bit.
            if (string.IsNullOrEmpty(_resolver) || _resolver == DnsQueryService.SystemDefaultLabel)
            {
                List<System.Net.IPAddress> servers = SystemResolvers.List();
                if (servers.Count == 0) return new DnsQueryResult();

                return await _dns.QueryAsync(servers[0].ToString(), name, type,
                                             _timeoutMs, true, cancel).ConfigureAwait(false);
            }

            return await _dns.QueryAsync(_resolver, name, type, _timeoutMs, true, cancel)
                             .ConfigureAwait(false);
        }
    }
}
