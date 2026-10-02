using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DnsToolbox95.Dns;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>
    /// Queries the same name and type against several resolvers and compares the
    /// answers. This is a factual comparison of the resolvers that were actually
    /// asked - it does not and cannot claim to cover every resolver on the internet.
    /// </summary>
    public sealed class ResolverComparisonService
    {
        private readonly DnsQueryService _dns;
        private readonly int _timeoutMs;

        public ResolverComparisonService(DnsQueryService dns, int timeoutMs)
        {
            _dns = dns;
            _timeoutMs = timeoutMs;
        }

        /// <summary>The resolvers offered by default, plus any the user added.</summary>
        public static List<string> DefaultResolvers()
        {
            List<string> list = new List<string>();
            list.Add(DnsQueryService.SystemDefaultLabel);
            list.AddRange(SystemResolvers.Presets);
            return list;
        }

        public async Task<List<ResolverComparisonRow>> CompareAsync(
            string domain, DnsRecordType type, List<string> resolvers,
            IProgress<string> progress, CancellationToken cancel)
        {
            List<ResolverComparisonRow> rows = new List<ResolverComparisonRow>();

            if (resolvers == null || resolvers.Count == 0)
                resolvers = DefaultResolvers();

            for (int i = 0; i < resolvers.Count; i++)
            {
                if (cancel.IsCancellationRequested) break;

                string resolver = resolvers[i];

                if (progress != null)
                    progress.Report("Querying " + resolver + " ...");

                ResolverComparisonRow row = new ResolverComparisonRow();
                row.Resolver = resolver;

                try
                {
                    DnsQueryResult result = await _dns.QueryEitherAsync(resolver, domain, type,
                                                                        _timeoutMs, cancel)
                                                   .ConfigureAwait(false);

                    row.ResponseMs = result.ResponseMs;

                    if (result.Failed)
                    {
                        row.Failed = true;
                        row.Status = "ERROR";
                        row.Error = result.Error;
                        row.Response = "-";
                    }
                    else
                    {
                        row.Status = result.StatusText;
                        row.Ttl = MinTtl(result);

                        if (result.Answers.Count == 0)
                        {
                            row.Response = "(no " + DnsTypes.Name(type) + " records)";
                        }
                        else
                        {
                            StringBuilder sb = new StringBuilder();
                            for (int a = 0; a < result.Answers.Count; a++)
                            {
                                if (a > 0) sb.Append(", ");
                                sb.Append(result.Answers[a].Value);
                            }
                            row.Response = sb.ToString();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    row.Failed = true;
                    row.Status = "CANCELLED";
                    row.Response = "-";
                }
                catch (Exception ex)
                {
                    row.Failed = true;
                    row.Status = "ERROR";
                    row.Response = "-";
                    row.Error = ex.Message;
                }

                rows.Add(row);
            }

            return rows;
        }

        private static string MinTtl(DnsQueryResult result)
        {
            if (result.Answers.Count == 0) return "-";

            uint min = uint.MaxValue;
            foreach (DnsRecordInfo r in result.Answers)
                if (r.Ttl < min) min = r.Ttl;

            return min == uint.MaxValue ? "-" : min.ToString();
        }

        /// <summary>
        /// Summarises whether the resolvers agreed. Agreement is compared on the
        /// sorted set of returned values, so a different order is not a difference.
        /// </summary>
        public static string Summarize(List<ResolverComparisonRow> rows)
        {
            if (rows == null || rows.Count == 0) return "No resolvers were queried.";

            int failed = 0;
            List<string> distinct = new List<string>();

            foreach (ResolverComparisonRow row in rows)
            {
                if (row.Failed) { failed++; continue; }
                if (row.Response == null) continue;

                string key = row.Response;
                if (!distinct.Contains(key)) distinct.Add(key);
            }

            int compared = rows.Count - failed;

            if (compared <= 1)
                return "Only " + compared + " resolver answered, so the results cannot be compared.";

            if (distinct.Count <= 1)
            {
                return "All " + compared + " resolvers that answered returned the same result" +
                       (failed > 0 ? " (" + failed + " did not answer)." : ".");
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("Resolver results differ: ").Append(distinct.Count)
              .Append(" different answers across ").Append(compared).Append(" resolvers");

            if (failed > 0)
                sb.Append(", and ").Append(failed).Append(" did not answer");

            sb.Append(".");
            return sb.ToString();
        }
    }
}
