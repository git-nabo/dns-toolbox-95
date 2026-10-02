using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DnsToolbox95.Dns
{
    /// <summary>Raised when a query could not be completed, with a readable reason.</summary>
    public class DnsQueryException : Exception
    {
        public DnsQueryException(string message) : base(message) { }
    }

    /// <summary>
    /// The DNS query engine: sends a real DNS message to a chosen resolver over UDP
    /// (falling back to TCP when the answer is truncated) and decodes the reply.
    ///
    /// Every call has a timeout, so a dead resolver can never freeze the UI, and every
    /// failure is reported as a readable message rather than an exception dump.
    /// </summary>
    public sealed class DnsQueryService
    {
        /// <summary>Label used for the operating system's own resolver.</summary>
        public const string SystemDefaultLabel = "System Default";

        public const int DefaultTimeoutMs = 5000;

        private static int _queryId = new Random().Next(1, 30000);

        /// <summary>Queries one resolver and returns a fully populated result.</summary>
        public Task<DnsQueryResult> QueryAsync(string server, string host,
                                                DnsRecordType type, int timeoutMs)
        {
            return QueryAsync(server, host, type, timeoutMs, false, CancellationToken.None);
        }

        /// <summary>
        /// Queries one resolver. <paramref name="dnssec"/> sets the DO bit so DNSSEC
        /// records are returned as well.
        /// </summary>
        public async Task<DnsQueryResult> QueryAsync(string server, string host,
                                                      DnsRecordType type, int timeoutMs,
                                                      bool dnssec,
                                                      CancellationToken cancel)
        {
            if (timeoutMs < 200) timeoutMs = 200;

            string name = DnsMessage.NormalizeName(host);
            DnsQueryResult result = new DnsQueryResult();
            result.Query = name;
            result.RequestedType = type;
            result.Resolver = server;

            IPAddress address = ResolveServer(server);
            if (address == null)
            {
                result.Failed = true;
                result.Error = "'" + server + "' is not a valid IP address for a DNS server.";
                return result;
            }

            result.Resolver = address.ToString();

            ushort id = NextId();
            byte[] query = dnssec
                ? DnsMessage.BuildDnssecQuery(name, type, id)
                : DnsMessage.BuildQuery(name, type, id, true, 4096);

            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                byte[] responseData = await ExchangeAsync(address, query, timeoutMs, cancel)
                                          .ConfigureAwait(false);

                watch.Stop();
                result.ResponseMs = (int)watch.ElapsedMilliseconds;

                // The ID is read from the header so it can be validated even if the
                // body later turns out to be unreadable.
                ushort replyId = DnsMessage.PeekId(responseData);
                if (replyId != 0 && replyId != id)
                {
                    result.Failed = true;
                    result.Error = "The resolver returned a reply with an unexpected transaction ID.";
                    return result;
                }

                DnsMessage.Response decoded = DnsMessage.Decode(responseData);

                result.Status = decoded.Status;
                result.Authoritative = decoded.Authoritative;
                result.Truncated = decoded.Truncated;
                result.RecursionAvailable = decoded.RecursionAvailable;
                result.AuthenticatedData = decoded.AuthenticatedData;

                result.Answers.AddRange(decoded.Answers);
                result.Authority.AddRange(decoded.Authority);
                result.Additional.AddRange(decoded.Additional);

                if (decoded.Status == DnsStatus.SERVFAIL)
                    result.Error = "The resolver reported SERVFAIL for this query.";
                else if (decoded.Status == DnsStatus.REFUSED)
                    result.Error = "The resolver refused to answer this query.";
                else if (decoded.Status == DnsStatus.FORMERR)
                    result.Error = "The resolver reported a malformed query (FORMERR).";
                else if (decoded.Status == DnsStatus.NOTIMP)
                    result.Error = "The resolver does not implement this query (NOTIMP).";
                else if (decoded.Status == DnsStatus.NXDOMAIN)
                    result.Error = "The name does not exist (NXDOMAIN).";

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Failed = true;
                result.Error = "The query was cancelled.";
                return result;
            }
            catch (SocketException ex)
            {
                watch.Stop();
                result.ResponseMs = (int)watch.ElapsedMilliseconds;
                result.Failed = true;
                result.Error = DescribeSocketError(ex);
                return result;
            }
            catch (Exception ex)
            {
                watch.Stop();
                result.ResponseMs = (int)watch.ElapsedMilliseconds;
                result.Failed = true;
                result.Error = FriendlyMessage(ex);
                return result;
            }
        }

        /// <summary>
        /// Queries the operating system's own resolver. When the machine has several
        /// configured servers they are tried in turn, so one unreachable server does
        /// not make every lookup look broken.
        /// </summary>
        public async Task<DnsQueryResult> QuerySystemAsync(string host, DnsRecordType type,
                                                            int timeoutMs,
                                                            CancellationToken cancel)
        {
            List<IPAddress> servers = SystemResolvers.List();
            if (servers.Count == 0)
            {
                DnsQueryResult empty = new DnsQueryResult();
                empty.Query = host;
                empty.Resolver = SystemDefaultLabel;
                empty.RequestedType = type;
                empty.Failed = true;
                empty.Error = "No DNS server is configured on this computer.";
                return empty;
            }

            DnsQueryResult last = null;

            for (int i = 0; i < servers.Count; i++)
            {
                DnsQueryResult attempt = await QueryAsync(servers[i].ToString(), host, type,
                                                           timeoutMs, false, cancel)
                                           .ConfigureAwait(false);
                last = attempt;
                if (!attempt.Failed) return attempt;
            }

            if (last != null) last.Resolver = SystemDefaultLabel + " (" + servers[0] + ")";
            return last;
        }

        /// <summary>Queries the system resolver, or a specific one when named.</summary>
        public Task<DnsQueryResult> QueryEitherAsync(string server, string host,
                                                      DnsRecordType type, int timeoutMs,
                                                      CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(server) || server == SystemDefaultLabel)
                return QuerySystemAsync(host, type, timeoutMs, cancel);

            return QueryAsync(server, host, type, timeoutMs, false, cancel);
        }

        // ------------------------------------------------------------- transport

        /// <summary>
        /// Sends the query over UDP and, if the answer came back truncated, repeats it
        /// over TCP. TCP is required for large TXT answers such as long SPF records.
        /// </summary>
        private async Task<byte[]> ExchangeAsync(IPAddress server, byte[] query,
                                                 int timeoutMs, CancellationToken cancel)
        {
            byte[] udp = await UdpExchangeAsync(server, query, timeoutMs, cancel)
                              .ConfigureAwait(false);

            // Read the header only: a truncated answer has no complete records, so a
            // full decode would throw instead of telling us to retry over TCP.
            if (DnsMessage.IsTruncated(udp))
            {
                byte[] tcp = await TcpExchangeAsync(server, query, timeoutMs)
                                  .ConfigureAwait(false);
                if (tcp != null) return tcp;
            }

            return udp;
        }

        private async Task<byte[]> UdpExchangeAsync(IPAddress server, byte[] query,
                                                    int timeoutMs, CancellationToken cancel)
        {
            using (UdpClient client = CreateUdpClient(server))
            {
                Task<UdpReceiveResult> receive = client.ReceiveAsync();

                await client.SendAsync(query, query.Length, new IPEndPoint(server, 53))
                           .ConfigureAwait(false);

                UdpReceiveResult packet = await WithTimeout(receive, timeoutMs, cancel,
                    "Request timed out after " + (timeoutMs / 1000.0).ToString("0.#") +
                    " seconds. The server " + server + " did not answer.").ConfigureAwait(false);

                cancel.ThrowIfCancellationRequested();
                return packet.Buffer;
            }
        }

        /// <summary>
        /// Awaits a task with a time limit and an optional cancellation token.
        ///
        /// The timeout is implemented with a token source rather than by disposing a
        /// pending delay task: disposing a Task that has not finished throws, which
        /// would turn a plain timeout into a confusing error. On expiry the original
        /// task is abandoned rather than cancelled, because a UDP receive has no
        /// portable cancel.
        /// </summary>
        private static async Task<T> WithTimeout<T>(Task<T> task, int timeoutMs,
                                                  CancellationToken cancel, string timeoutMessage)
        {
            using (CancellationTokenSource timer = new CancellationTokenSource())
            using (CancellationTokenSource linked =
                       CancellationTokenSource.CreateLinkedTokenSource(cancel, timer.Token))
            {
                Task delay = Task.Delay(timeoutMs, linked.Token);
                Task finished = await Task.WhenAny(task, delay).ConfigureAwait(false);

                if (finished == task)
                {
                    // Stop the timer so it does not fire later and get observed.
                    timer.Cancel();
                    return await task.ConfigureAwait(false);
                }

                if (cancel.IsCancellationRequested)
                    throw new OperationCanceledException();

                throw new DnsQueryException(timeoutMessage);
            }
        }

        private async Task<byte[]> TcpExchangeAsync(IPAddress server, byte[] query,
                                                    int timeoutMs)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    Task connect = client.ConnectAsync(server, 53);
                    Task finished = await Task.WhenAny(connect, Task.Delay(timeoutMs))
                                                .ConfigureAwait(false);
                    if (finished != connect) return null;

                    await connect.ConfigureAwait(false);

                    using (NetworkStream stream = client.GetStream())
                    {
                        // DNS over TCP prefixes the message with its 16-bit length.
                        byte[] framed = new byte[query.Length + 2];
                        framed[0] = (byte)(query.Length >> 8);
                        framed[1] = (byte)(query.Length & 0xFF);
                        Buffer.BlockCopy(query, 0, framed, 2, query.Length);

                        await stream.WriteAsync(framed, 0, framed.Length).ConfigureAwait(false);

                        byte[] header = await ReadExactlyAsync(stream, 2, timeoutMs)
                                             .ConfigureAwait(false);
                        if (header == null) return null;

                        int length = (header[0] << 8) | header[1];
                        if (length <= 0 || length > 65535) return null;

                        return await ReadExactlyAsync(stream, length, timeoutMs)
                                    .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // A failed TCP retry simply means we keep the truncated UDP answer.
                return null;
            }
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count,
                                                          int timeoutMs)
        {
            byte[] buffer = new byte[count];
            int read = 0;

            while (read < count)
            {
                Task<int> pending = stream.ReadAsync(buffer, read, count - read);
                Task finished = await Task.WhenAny(pending, Task.Delay(timeoutMs))
                                          .ConfigureAwait(false);
                if (finished != pending) return null;

                int n = await pending.ConfigureAwait(false);
                if (n <= 0) return null;
                read += n;
            }

            return buffer;
        }

        /// <summary>
        /// Creates a bound UDP client.
        ///
        /// ReceiveAsync throws unless the socket is bound first, and a dual-stack
        /// IPv6 socket will not bind for an IPv4 destination on some systems, so the
        /// address family is chosen to match the resolver and the socket is bound to
        /// an ephemeral port explicitly.
        /// </summary>
        private static UdpClient CreateUdpClient(IPAddress server)
        {
            AddressFamily family = (server != null &&
                                    server.AddressFamily == AddressFamily.InterNetworkV6)
                ? AddressFamily.InterNetworkV6
                : AddressFamily.InterNetwork;

            UdpClient client = new UdpClient(family);

            IPAddress any = (family == AddressFamily.InterNetworkV6)
                ? IPAddress.IPv6Any
                : IPAddress.Any;

            client.Client.Bind(new IPEndPoint(any, 0));
            return client;
        }

        // --------------------------------------------------------------- helpers

        private static ushort NextId()
        {
            int id = Interlocked.Increment(ref _queryId) & 0xFFFF;
            return (ushort)(id == 0 ? 1 : id);
        }

        /// <summary>
        /// Resolves a resolver entry to an IP address. The system label and any
        /// non-address text fall back to the machine's own resolver.
        /// </summary>
        private static IPAddress ResolveServer(string server)
        {
            if (string.IsNullOrEmpty(server)) return null;

            string text = server.Trim();
            if (text.StartsWith("udp:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(4);
            }

            // A bracketed IPv6 literal, e.g. [2001:db8::1].
            if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
                text = text.Substring(1, text.Length - 2);

            IPAddress address;
            if (IPAddress.TryParse(text, out address)) return address;

            List<IPAddress> configured = SystemResolvers.List();
            return configured.Count > 0 ? configured[0] : null;
        }

        /// <summary>Turns a socket error into the wording an admin would expect.</summary>
        private static string DescribeSocketError(SocketException ex)
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.TimedOut:
                    return "The request timed out. The server did not answer in time.";
                case SocketError.ConnectionRefused:
                    return "The server refused the connection. DNS queries use UDP port 53, " +
                           "which may be blocked.";
                case SocketError.HostUnreachable:
                    return "The server is unreachable from this network.";
                case SocketError.NetworkUnreachable:
                    return "No route to the DNS server. Check the network connection.";
                case SocketError.MessageSize:
                    return "The DNS response was too large for a UDP reply and could not be " +
                           "retried over TCP.";
                default:
                    return "Network error: " + ex.SocketErrorCode + ".";
            }
        }

        private static string FriendlyMessage(Exception ex)
        {
            if (ex is DnsQueryException) return ex.Message;
            if (ex is FormatException) return "The DNS response could not be understood: " + ex.Message;
            if (ex is OperationCanceledException) return "The query was cancelled.";

            // Never leak a stack trace; the type plus message is enough to diagnose.
            return ex.GetType().Name + ": " + ex.Message;
        }

        /// <summary>
        /// Builds the reverse lookup name for an IP address, for PTR queries:
        /// 203.0.113.10 becomes 10.113.0.203.in-addr.arpa, and IPv6 addresses use
        /// the nibble-reversed ip6.arpa form.
        /// </summary>
        public static string ReverseName(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                return b[3] + "." + b[2] + "." + b[1] + "." + b[0] + ".in-addr.arpa";
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] b = address.GetAddressBytes();
                StringBuilder sb = new StringBuilder();
                for (int i = b.Length - 1; i >= 0; i--)
                {
                    int hi = b[i] >> 4;
                    int lo = b[i] & 0x0F;
                    sb.Append(hi.ToString("x")).Append('.');
                    sb.Append(lo.ToString("x")).Append('.');
                }
                sb.Append("ip6.arpa");
                return sb.ToString();
            }

            throw new ArgumentException("Unsupported address family.");
        }

        /// <summary>
        /// Turns whatever the user typed into a query. IP addresses become the
        /// matching reverse name when a PTR lookup is requested; anything else is
        /// treated as a domain name.
        /// </summary>
        public static string BuildQueryName(string input, DnsRecordType type)
        {
            IPAddress address;
            if (type == DnsRecordType.PTR && IPAddress.TryParse((input ?? string.Empty).Trim(), out address))
                return ReverseName(address);

            return DnsMessage.NormalizeName(input);
        }
    }
}
