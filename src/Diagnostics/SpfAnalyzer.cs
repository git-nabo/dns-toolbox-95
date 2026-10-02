using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>One parsed term of an SPF record.</summary>
    public sealed class SpfTerm
    {
        public string Mechanism = string.Empty;   // include, ip4, a, mx, ...
        public string Value = string.Empty;       // the domain, address or CIDR
        public string Qualifier = string.Empty;   // + - ~ ?
        public bool IsDirective;                  // all or redirect=
    }

    /// <summary>
    /// Validates an SPF record: finds it among the TXT records, parses the
    /// mechanisms and counts the DNS lookups it performs.
    ///
    /// The lookup count follows includes, a, mx and exists recursively, with cycle
    /// detection and a depth limit, so a record that includes itself cannot hang the
    /// check. The 10 lookup ceiling is RFC 7208's limit for mail-from evaluation.
    /// </summary>
    public sealed class SpfAnalyzer
    {
        public const int LookupLimit = 10;
        private const int MaxDepth = 5;

        private readonly DnsQueryService _dns;
        private readonly string _resolver;
        private readonly int _timeoutMs;

        public SpfAnalyzer(DnsQueryService dns, string resolver, int timeoutMs)
        {
            _dns = dns;
            _resolver = resolver;
            _timeoutMs = timeoutMs;
        }

        public async Task<DiagnosticReport> AnalyzeAsync(string domain, CancellationToken cancel)
        {
            DiagnosticReport report = new DiagnosticReport();
            report.Title = "SPF check for " + domain;

            List<string> spfRecords = await FindSpfRecordsAsync(domain, report, cancel)
                                          .ConfigureAwait(false);

            if (spfRecords.Count == 0)
            {
                report.Add(Severity.Fail, "No SPF record was found for this domain.");
                report.Add(Severity.Warn,
                    "Receivers cannot verify the sending server, so mail may be rejected or " +
                    "placed in spam.");
                return report;
            }

            if (spfRecords.Count > 1)
            {
                report.Add(Severity.Fail,
                    "Found " + spfRecords.Count + " SPF records. RFC 7208 allows only one; " +
                    "receivers must treat this as a permanent error.");
            }
            else
            {
                report.Add(Severity.Ok, "Exactly one SPF record is published.");
            }

            string record = spfRecords[0];
            report.Details.Add("SPF record:");
            report.Details.Add("  " + record);

            List<SpfTerm> terms = Parse(record, report);

            if (terms.Count == 0)
            {
                report.Add(Severity.Fail, "The SPF record contains no usable mechanisms.");
                return report;
            }

            report.Details.Add("");
            report.Details.Add("Parsed mechanisms:");

            foreach (SpfTerm term in terms)
            {
                report.Details.Add("  " + (term.Qualifier.Length > 0 ? term.Qualifier : "+") +
                                   term.Mechanism + (term.Value.Length > 0 ? ":" + term.Value : string.Empty));
            }

            Evaluate(terms, report);
            await CountLookupsAsync(domain, terms, report, cancel).ConfigureAwait(false);

            return report;
        }

        /// <summary>
        /// Reads the TXT records and keeps only those that start with "v=spf1".
        /// Split TXT strings are already rejoined by the decoder, so a long record
        /// split across several character-strings is seen as one value.
        /// </summary>
        private async Task<List<string>> FindSpfRecordsAsync(string domain,
                                                             DiagnosticReport report,
                                                             CancellationToken cancel)
        {
            List<string> found = new List<string>();

            DnsQueryResult txt = await _dns.QueryEitherAsync(_resolver, domain,
                                                              DnsRecordType.TXT, _timeoutMs, cancel)
                                          .ConfigureAwait(false);

            if (txt.Failed)
            {
                report.Add(Severity.Fail,
                    "The TXT lookup failed: " + txt.Error);
                return found;
            }

            if (txt.Status == DnsStatus.NXDOMAIN)
            {
                report.Add(Severity.Fail, "The domain does not exist (NXDOMAIN).");
                return found;
            }

            foreach (DnsRecordInfo record in txt.Answers)
            {
                string value = (record.Value ?? string.Empty).Trim();

                if (value.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase))
                    found.Add(value);
            }

            return found;
        }

        /// <summary>Splits an SPF record into its terms.</summary>
        public static List<SpfTerm> Parse(string record, DiagnosticReport report)
        {
            List<SpfTerm> terms = new List<SpfTerm>();

            string body = record.Trim();
            int versionEnd = body.IndexOf(' ');
            if (versionEnd > 0) body = body.Substring(versionEnd + 1);

            string[] parts = body.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (part.Length == 0) continue;

                SpfTerm term = new SpfTerm();
                string token = part;

                char first = token[0];
                if (first == '+' || first == '-' || first == '~' || first == '?')
                {
                    term.Qualifier = first.ToString();
                    token = token.Substring(1);
                }

                int colon = token.IndexOf(':');
                int equals = token.IndexOf('=');

                if (equals == 0)                       // redirect=
                {
                    term.IsDirective = true;
                    term.Mechanism = "redirect";
                    term.Value = token.Substring(equals + 1);
                }
                else if (colon > 0)                    // include:domain
                {
                    term.Mechanism = token.Substring(0, colon).ToLowerInvariant();
                    term.Value = token.Substring(colon + 1);
                }
                else if (token.EndsWith("all", StringComparison.OrdinalIgnoreCase) &&
                         token.Length == 3)
                {
                    term.IsDirective = true;
                    term.Mechanism = "all";
                }
                else
                {
                    term.Mechanism = token.ToLowerInvariant();
                }

                terms.Add(term);
            }

            return terms;
        }

        /// <summary>Applies the structural rules that do not need extra DNS lookups.</summary>
        private static void Evaluate(List<SpfTerm> terms, DiagnosticReport report)
        {
            bool hasAll = false;
            bool hasRedirect = false;
            int ipTerms = 0;

            foreach (SpfTerm term in terms)
            {
                if (term.IsDirective && term.Mechanism == "all") hasAll = true;
                if (term.IsDirective && term.Mechanism == "redirect") hasRedirect = true;

                if (term.Mechanism == "ip4" || term.Mechanism == "ip6") ipTerms++;

                if (term.Mechanism.Length == 0)
                    report.Add(Severity.Warn, "A term could not be parsed: " + term.Qualifier);
            }

            if (hasAll)
            {
                foreach (SpfTerm term in terms)
                {
                    if (term.IsDirective && term.Mechanism == "all")
                    {
                        switch (term.Qualifier)
                        {
                            case "-":
                                report.Add(Severity.Ok, "Policy is -all (hard fail). Unauthorised senders are rejected.");
                                break;
                            case "~":
                                report.Add(Severity.Warn, "Policy is ~all (soft fail). Receivers are asked to mark mail as spam rather than reject it.");
                                break;
                            case "?":
                                report.Add(Severity.Warn, "Policy is ?all (neutral). The SPF result tells receivers nothing.");
                                break;
                            default:
                                report.Add(Severity.Warn, "Policy is +all (pass all). This authorises every server and defeats the purpose of SPF.");
                                break;
                        }
                    }
                }
            }
            else
            {
                report.Add(Severity.Fail,
                    "The record has no 'all' mechanism, so it is not a complete policy.");
            }

            if (hasRedirect && hasAll)
            {
                report.Add(Severity.Warn,
                    "Both 'all' and 'redirect=' are present. 'all' is evaluated first, so " +
                    "redirect= is ignored.");
            }

            if (ipTerms == 0 && !hasRedirect)
            {
                report.Add(Severity.Info,
                    "The record has no ip4 or ip6 mechanism, so authorisation relies entirely " +
                    "on DNS lookups.");
            }
        }

        /// <summary>
        /// Counts the DNS lookups the record performs, following includes, a, mx,
        /// exists and redirect. Each of those costs one lookup at the receiver, and
        /// RFC 7208 caps the total at 10.
        /// </summary>
        private async Task CountLookupsAsync(string domain, List<SpfTerm> terms,
                                             DiagnosticReport report, CancellationToken cancel)
        {
            LookupCounter counter = new LookupCounter();
            List<string> visited = new List<string>();
            visited.Add(domain.ToLowerInvariant());

            foreach (SpfTerm term in terms)
            {
                if (cancel.IsCancellationRequested) return;

                switch (term.Mechanism)
                {
                    case "include":
                        await ExpandAsync(term.Value, term.Qualifier, counter, visited,
                                          1, report, cancel).ConfigureAwait(false);
                        break;

                    case "a":
                        counter.Add();
                        await ExpandAAsync(Qualify(domain, term.Value), counter, visited,
                                           1, report, cancel).ConfigureAwait(false);
                        break;

                    case "mx":
                        counter.Add();
                        await ExpandMxAsync(Qualify(domain, term.Value), counter, visited,
                                            1, report, cancel).ConfigureAwait(false);
                        break;

                    case "exists":
                        counter.Add();
                        break;

                    case "redirect":
                        await ExpandAsync(term.Value, term.Qualifier, counter, visited,
                                          1, report, cancel).ConfigureAwait(false);
                        break;
                }
            }

            report.Details.Add("");
            report.Details.Add("DNS lookups: " + counter.Count + " / " + LookupLimit);

            if (counter.Count > LookupLimit)
            {
                report.Add(Severity.Fail,
                    "The record performs " + counter.Count + " DNS lookups, over the limit of " +
                    LookupLimit + ". Receivers return permerror, which can cause mail to be " +
                    "rejected.");
            }
            else if (counter.Count == LookupLimit)
            {
                report.Add(Severity.Warn,
                    "The record performs exactly " + LookupLimit +
                    " lookups. Any future addition will exceed the limit.");
            }
            else
            {
                report.Add(Severity.Ok,
                    "The record performs " + counter.Count + " of " + LookupLimit +
                    " permitted DNS lookups.");
            }
        }

        private static string Qualify(string domain, string value)
        {
            if (string.IsNullOrEmpty(value)) return domain;
            if (value.EndsWith(".", StringComparison.Ordinal)) return value.TrimEnd('.');
            return value;
        }

        /// <summary>Follows an include: or redirect= into the included domain's record.</summary>
        private async Task ExpandAsync(string target, string qualifier, LookupCounter counter,
                                       List<string> visited, int depth, DiagnosticReport report,
                                       CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested) return;

            string name = Qualify(target, string.Empty).ToLowerInvariant();

            if (depth > MaxDepth)
            {
                report.Add(Severity.Warn,
                    "Stopped following includes at depth " + MaxDepth + " (" + name +
                    "). Deeper chains are not evaluated.");
                return;
            }

            if (visited.Contains(name))
            {
                report.Add(Severity.Warn,
                    "Include loop detected at " + name + "; the chain was not followed again.");
                return;
            }

            counter.Add();

            DnsQueryResult txt = await _dns.QueryEitherAsync(_resolver, name,
                                                              DnsRecordType.TXT, _timeoutMs, cancel)
                                          .ConfigureAwait(false);

            if (txt.Failed)
            {
                report.Add(Severity.Warn,
                    "The include " + name + " could not be read: " + txt.Error);
                return;
            }

            string record = null;
            foreach (DnsRecordInfo r in txt.Answers)
            {
                if ((r.Value ?? string.Empty).Trim().StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase))
                {
                    record = r.Value.Trim();
                    break;
                }
            }

            if (record == null)
            {
                report.Add(Severity.Warn,
                    "The include " + name + " has no SPF record. Receivers treat this as " +
                    "permerror for the including domain.");
                return;
            }

            visited.Add(name);
            List<SpfTerm> nested = Parse(record, report);

            foreach (SpfTerm term in nested)
            {
                if (cancel.IsCancellationRequested) return;

                switch (term.Mechanism)
                {
                    case "include":
                    case "redirect":
                        await ExpandAsync(term.Value, term.Qualifier, counter, visited,
                                          depth + 1, report, cancel).ConfigureAwait(false);
                        break;

                    case "a":
                        counter.Add();
                        await ExpandAAsync(Qualify(name, term.Value), counter, visited,
                                           depth + 1, report, cancel).ConfigureAwait(false);
                        break;

                    case "mx":
                        counter.Add();
                        await ExpandMxAsync(Qualify(name, term.Value), counter, visited,
                                            depth + 1, report, cancel).ConfigureAwait(false);
                        break;

                    case "exists":
                        counter.Add();
                        break;
                }
            }
        }

        /// <summary>An "a" term also authorises the addresses of any MX targets.</summary>
        private async Task ExpandAAsync(string name, LookupCounter counter, List<string> visited,
                                        int depth, DiagnosticReport report,
                                        CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested || depth > MaxDepth) return;

            DnsQueryResult mx = await _dns.QueryEitherAsync(_resolver, name, DnsRecordType.MX,
                                                             _timeoutMs, cancel)
                                      .ConfigureAwait(false);

            if (!mx.Failed && mx.Answers.Count > 0)
            {
                foreach (DnsRecordInfo r in mx.Answers)
                    counter.Add();      // one A and one AAAA per mail exchanger
            }
        }

        /// <summary>An "mx" term costs one lookup per mail exchanger.</summary>
        private async Task ExpandMxAsync(string name, LookupCounter counter, List<string> visited,
                                         int depth, DiagnosticReport report,
                                         CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested || depth > MaxDepth) return;

            DnsQueryResult mx = await _dns.QueryEitherAsync(_resolver, name, DnsRecordType.MX,
                                                             _timeoutMs, cancel)
                                      .ConfigureAwait(false);

            if (mx.Failed || mx.Answers.Count == 0) return;

            foreach (DnsRecordInfo r in mx.Answers)
                counter.Add();
        }

        /// <summary>A tiny mutable counter shared across the recursive walk.</summary>
        private sealed class LookupCounter
        {
            private int _count;

            public int Count
            {
                get { return _count; }
            }

            public void Add()
            {
                _count++;
            }
        }
    }
}
