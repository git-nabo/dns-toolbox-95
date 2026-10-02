using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>Reads the public key published for one DKIM selector.</summary>
    public sealed class DkimAnalyzer
    {
        private readonly DnsQueryService _dns;
        private readonly string _resolver;
        private readonly int _timeoutMs;

        public DkimAnalyzer(DnsQueryService dns, string resolver, int timeoutMs)
        {
            _dns = dns;
            _resolver = resolver;
            _timeoutMs = timeoutMs;
        }

        public async Task<DiagnosticReport> AnalyzeAsync(string domain, string selector,
                                                          CancellationToken cancel)
        {
            DiagnosticReport report = new DiagnosticReport();
            report.Title = "DKIM check for " + selector + "._domainkey." + domain;

            if (string.IsNullOrEmpty(selector) || selector.Trim().Length == 0)
            {
                report.Add(Severity.Warn,
                    "No selector was given. DKIM keys are published per selector, so a check " +
                    "without one cannot succeed.");
                return report;
            }

            string query = selector.Trim() + "._domainkey." + DnsMessage.NormalizeName(domain);

            DnsQueryResult txt = await _dns.QueryEitherAsync(_resolver, query,
                                                              DnsRecordType.TXT, _timeoutMs, cancel)
                                          .ConfigureAwait(false);

            if (txt.Failed)
            {
                report.Add(Severity.Fail, "The TXT lookup failed: " + txt.Error);
                return report;
            }

            if (txt.Status == DnsStatus.NXDOMAIN)
            {
                report.Add(Severity.Fail,
                    "The name " + query + " does not exist, so this selector has no key.");
                return report;
            }

            List<string> keys = new List<string>();
            foreach (DnsRecordInfo r in txt.Answers)
            {
                string value = (r.Value ?? string.Empty).Trim();
                // Long keys are split into several character-strings; the decoder
                // rejoins them, so each key arrives here as one value.
                if (value.StartsWith("v=DKIM1", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("p=", StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(value);
                }
            }

            if (keys.Count == 0)
            {
                report.Add(Severity.Fail,
                    "No DKIM key was found at " + query + ".");
                report.Add(Severity.Info,
                    "Keys are published per selector. Check the selector used in the message " +
                    "headers (the s= tag) and try again.");
                return report;
            }

            if (keys.Count > 1)
            {
                report.Add(Severity.Warn,
                    "Found " + keys.Count + " key records for this selector. Receivers normally " +
                    "treat more than one as an error.");
            }

            foreach (string key in keys)
            {
                report.Details.Add("DKIM record:");
                report.Details.Add("  " + Truncate(key, 400));
                report.Details.Add("");

                DkimTags tags = Parse(key);
                report.Details.Add("Version:      " + Or(tags.Version, "(not set)"));
                report.Details.Add("Key type:     " + Or(tags.KeyType, "rsa (default)"));
                report.Details.Add("Flags:        " + Or(tags.Flags, "(none)"));
                report.Details.Add("Service:      " + Or(tags.Service, "email (default)"));
                report.Details.Add("Public key:   " + DescribeKey(tags.Key));
            }

            Evaluate(keys[0], report);
            return report;
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static string Truncate(string value, int max)
        {
            if (value.Length <= max) return value;
            return value.Substring(0, max) + "...";
        }

        /// <summary>Describes the key material without pretending to have parsed RSA.</summary>
        private static string DescribeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "MISSING - this is a revoked key: mail signed with it fails validation";

            int length = key.Length;
            if (length < 20) return "present but very short (" + length + " characters)";
            return "present (" + length + " characters)";
        }

        /// <summary>The tags of a DKIM key record.</summary>
        public sealed class DkimTags
        {
            public string Version = string.Empty;
            public string KeyType = string.Empty;
            public string Flags = string.Empty;
            public string Service = string.Empty;
            public string Notes = string.Empty;
            public string Key = string.Empty;
        }

        /// <summary>Splits a DKIM record into its tags, tolerating whitespace in the key.</summary>
        public static DkimTags Parse(string raw)
        {
            DkimTags tags = new DkimTags();

            string[] parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder key = new StringBuilder();

            foreach (string part in parts)
            {
                string tag = part.Trim();
                int equals = tag.IndexOf('=');
                if (equals <= 0) continue;

                string name = tag.Substring(0, equals).Trim().ToLowerInvariant();
                string value = tag.Substring(equals + 1).Trim();

                switch (name)
                {
                    case "v": tags.Version = value; break;
                    case "k": tags.KeyType = value; break;
                    case "t": tags.Flags = value; break;
                    case "s": tags.Service = value; break;
                    case "n": tags.Notes = value; break;
                    case "p": tags.Key = value; break;
                    default:
                        // Unknown tags are preserved in the key buffer so nothing is lost.
                        key.Append(value);
                        break;
                }
            }

            return tags;
        }

        private static void Evaluate(string raw, DiagnosticReport report)
        {
            DkimTags tags = Parse(raw);

            if (string.IsNullOrEmpty(tags.Version))
                report.Add(Severity.Warn,
                    "The record has no v= tag. RFC 6376 requires v=DKIM1.");
            else if (!string.Equals(tags.Version, "DKIM1", StringComparison.OrdinalIgnoreCase))
                report.Add(Severity.Fail, "Version '" + tags.Version + "' is not valid. Use v=DKIM1.");
            else
                report.Add(Severity.Ok, "Version tag is v=DKIM1.");

            if (string.IsNullOrEmpty(tags.Key))
            {
                report.Add(Severity.Fail,
                    "The record has an empty p= tag. This is a revoked key: any mail signed " +
                    "with this selector will fail DKIM validation.");
            }
            else
            {
                report.Add(Severity.Ok, "A public key is published (" + tags.Key.Length +
                                       " characters).");
            }

            if (!string.IsNullOrEmpty(tags.Flags) &&
                tags.Flags.IndexOf("s", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                report.Add(Severity.Warn,
                    "The 's' flag is set, so this key may only be used for a test message and " +
                    "must not sign normal mail.");
            }

            if (!string.IsNullOrEmpty(tags.Service) &&
                !string.Equals(tags.Service, "email", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tags.Service, "*", StringComparison.Ordinal))
            {
                report.Add(Severity.Warn,
                    "Service type '" + tags.Service + "' does not cover email, so receivers " +
                    "will not use this key for mail.");
            }

            if (string.IsNullOrEmpty(tags.KeyType))
                report.Add(Severity.Info, "No k= tag, so the key type is rsa (the default).");
        }
    }
}
