using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>The tags parsed out of a DMARC record.</summary>
    public sealed class DmarcRecord
    {
        public string Version = string.Empty;
        public string Policy = string.Empty;
        public string SubdomainPolicy = string.Empty;
        public int Percentage = -1;
        public string Rua = string.Empty;
        public string Ruf = string.Empty;
        public string Adkim = string.Empty;
        public string Aspf = string.Empty;
        public string Raw = string.Empty;
    }

    /// <summary>Reads and checks the DMARC policy published at _dmarc.&lt;domain&gt;.</summary>
    public sealed class DmarcAnalyzer
    {
        private readonly DnsQueryService _dns;
        private readonly string _resolver;
        private readonly int _timeoutMs;

        public DmarcAnalyzer(DnsQueryService dns, string resolver, int timeoutMs)
        {
            _dns = dns;
            _resolver = resolver;
            _timeoutMs = timeoutMs;
        }

        public async Task<DiagnosticReport> AnalyzeAsync(string domain, CancellationToken cancel)
        {
            DiagnosticReport report = new DiagnosticReport();
            report.Title = "DMARC check for " + domain;

            string query = "_dmarc." + DnsMessage.NormalizeName(domain);

            DnsQueryResult txt = await _dns.QueryEitherAsync(_resolver, query,
                                                              DnsRecordType.TXT, _timeoutMs, cancel)
                                          .ConfigureAwait(false);

            if (txt.Failed)
            {
                report.Add(Severity.Fail, "The TXT lookup failed: " + txt.Error);
                return report;
            }

            List<string> records = new List<string>();
            foreach (DnsRecordInfo r in txt.Answers)
            {
                string value = (r.Value ?? string.Empty).Trim();
                if (value.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase))
                    records.Add(value);
            }

            if (records.Count == 0)
            {
                if (txt.Status == DnsStatus.NXDOMAIN)
                    report.Add(Severity.Fail, "The domain does not exist, so no DMARC record can exist.");
                else
                {
                    report.Add(Severity.Fail, "No DMARC record was found at " + query + ".");
                    report.Add(Severity.Warn,
                        "Without DMARC, receivers cannot tell which senders are authorised " +
                        "and spoofed mail may be accepted.");
                }
                return report;
            }

            if (records.Count > 1)
                report.Add(Severity.Fail,
                    "Found " + records.Count + " DMARC records. Only one is permitted; " +
                    "receivers must treat this as a permanent error.");
            else
                report.Add(Severity.Ok, "Exactly one DMARC record is published.");

            DmarcRecord record = Parse(records[0]);

            report.Details.Add("DMARC record:");
            report.Details.Add("  " + record.Raw);
            report.Details.Add("");
            report.Details.Add("Policy:            " + Describe(record.Policy));
            report.Details.Add("Subdomain Policy:  " +
                               (string.IsNullOrEmpty(record.SubdomainPolicy)
                                    ? "(inherits the main policy)"
                                    : Describe(record.SubdomainPolicy)));
            report.Details.Add("Percentage:        " +
                               (record.Percentage >= 0 ? record.Percentage + "%" : "(not set, defaults to 100)"));
            report.Details.Add("RUA:               " + Or(record.Rua, "(none)"));
            report.Details.Add("RUF:               " + Or(record.Ruf, "(none)"));
            report.Details.Add("DKIM alignment:    " + Or(record.Adkim, "(default: relaxed)"));
            report.Details.Add("SPF alignment:     " + Or(record.Aspf, "(default: relaxed)"));

            Evaluate(record, report);
            return report;
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        /// <summary>
        /// Describes a policy without calling a working setup broken. "p=none" is the
        /// normal first step of a rollout, not a fault.
        /// </summary>
        public static string Describe(string policy)
        {
            if (string.IsNullOrEmpty(policy)) return "(none)";

            switch (policy.ToLowerInvariant())
            {
                case "none":
                    return "none - monitoring only. Reports are collected but no mail is asked " +
                           "to be rejected on DMARC grounds. This is the normal starting point, " +
                           "not a fault.";
                case "quarantine":
                    return "quarantine - failing mail is sent to spam instead of the inbox.";
                case "reject":
                    return "reject - failing mail is refused outright.";
                default:
                    return policy + " (not a valid policy value)";
            }
        }

        /// <summary>Splits a DMARC record into its tags.</summary>
        public static DmarcRecord Parse(string raw)
        {
            DmarcRecord record = new DmarcRecord();
            record.Raw = raw;

            string[] parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string tag = part.Trim();
                int equals = tag.IndexOf('=');
                if (equals <= 0) continue;

                string name = tag.Substring(0, equals).Trim().ToLowerInvariant();
                string value = tag.Substring(equals + 1).Trim();

                switch (name)
                {
                    case "v": record.Version = value; break;
                    case "p": record.Policy = value; break;
                    case "sp": record.SubdomainPolicy = value; break;
                    case "pct":
                        int pct;
                        if (int.TryParse(value, out pct)) record.Percentage = pct;
                        break;
                    case "rua": record.Rua = value; break;
                    case "ruf": record.Ruf = value; break;
                    case "adkim": record.Adkim = value; break;
                    case "aspf": record.Aspf = value; break;
                }
            }

            return record;
        }

        /// <summary>Applies the DMARC validation rules.</summary>
        private static void Evaluate(DmarcRecord record, DiagnosticReport report)
        {
            if (!string.Equals(record.Version, "DMARC1", StringComparison.OrdinalIgnoreCase))
            {
                report.Add(Severity.Fail,
                    "The record's version tag is '" + record.Version +
                    "'. Only v=DMARC1 is valid.");
            }

            if (string.IsNullOrEmpty(record.Policy))
            {
                report.Add(Severity.Fail,
                    "The record has no p= tag, so it is not a valid policy. Receivers treat " +
                    "this as no record at all.");
            }
            else if (string.IsNullOrEmpty(Describe(record.Policy)) ||
                     !IsValidPolicy(record.Policy))
            {
                report.Add(Severity.Fail,
                    "Policy '" + record.Policy + "' is not valid. Use none, quarantine or reject.");
            }
            else if (string.Equals(record.Policy, "none", StringComparison.OrdinalIgnoreCase))
            {
                report.Add(Severity.Info,
                    "Policy is p=none. This is monitoring only: reports are collected but " +
                    "nothing is rejected. It is a normal starting point, not an error.");
            }
            else
            {
                report.Add(Severity.Ok, "Policy is p=" + record.Policy + ".");
            }

            if (!string.IsNullOrEmpty(record.SubdomainPolicy) && !IsValidPolicy(record.SubdomainPolicy))
                report.Add(Severity.Warn,
                    "Subdomain policy '" + record.SubdomainPolicy + "' is not a valid value.");

            if (record.Percentage >= 0)
            {
                if (record.Percentage < 0 || record.Percentage > 100)
                    report.Add(Severity.Fail, "pct=" + record.Percentage + " is outside 0-100.");
                else if (record.Percentage < 100)
                    report.Add(Severity.Warn,
                        "pct=" + record.Percentage + " applies the policy to only " +
                        record.Percentage + "% of mail. A staged rollout is fine, but remember " +
                        "to raise it to 100 later.");
                else
                    report.Add(Severity.Ok, "The policy applies to 100% of mail.");
            }

            if (string.IsNullOrEmpty(record.Rua))
                report.Add(Severity.Warn,
                    "No rua= tag. You will not receive aggregate reports, so you cannot see " +
                    "who is failing alignment.");
            else
                report.Add(Severity.Ok, "Aggregate reports are sent to " + record.Rua + ".");

            if (string.IsNullOrEmpty(record.Ruf))
                report.Add(Severity.Info,
                    "No ruf= tag, so forensic (per-message) failure reports are not collected.");
        }

        private static bool IsValidPolicy(string policy)
        {
            string p = (policy ?? string.Empty).ToLowerInvariant();
            return p == "none" || p == "quarantine" || p == "reject";
        }
    }
}
