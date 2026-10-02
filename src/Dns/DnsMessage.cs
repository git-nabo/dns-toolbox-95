using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Globalization;

namespace DnsToolbox95.Dns
{
    /// <summary>
    /// Minimal RFC 1035 message codec: builds queries and decodes answers.
    ///
    /// A self-contained implementation is used rather than System.Net.Dns because the
    /// toolbox has to talk to a specific resolver and read a specific record type,
    /// plus TTL and the header flags. System.Net.Dns can do none of that and always
    /// goes through the operating system resolver.
    /// </summary>
    internal static class DnsMessage
    {
        private const int MaxNameLength = 255;
        private const int MaxPointerHops = 32;

        // ---------------------------------------------------------------- query

        /// <summary>
        /// Builds a standard recursive query. When <paramref name="payloadSize"/> is
        /// above 512 an EDNS0 OPT record is added, which is what allows long SPF,
        /// DKIM and DMARC TXT records to arrive in one UDP response.
        /// </summary>
        public static byte[] BuildQuery(string host, DnsRecordType type, ushort id,
                                        bool recursionDesired, ushort payloadSize)
        {
            string qname = NormalizeName(host);

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                // DNS is big-endian on the wire, but BinaryWriter is little-endian, so
                // every multi-byte field is written explicitly through WriteU16.
                WriteU16(w, id);

                int flags = 0;
                if (recursionDesired) flags |= 0x0100;
                WriteU16(w, flags);

                WriteU16(w, 1);                                   // QDCOUNT
                // A query never carries answers. ANCOUNT must be 0; setting it to 1
                // here made strict resolvers answer NOTIMP.
                WriteU16(w, 0);                                   // ANCOUNT
                WriteU16(w, 0);                                   // NSCOUNT
                WriteU16(w, payloadSize > 512 ? 1 : 0);          // ARCOUNT (OPT)

                WriteName(w, qname);
                WriteU16(w, (int)type);
                WriteU16(w, 1);                                   // QCLASS = IN

                if (payloadSize > 512)
                {
                    // OPT pseudo-record: root name, type 41, and the advertised UDP
                    // payload size carried in the "class" field.
                    w.Write((byte)0);
                    WriteU16(w, 41);
                    WriteU16(w, payloadSize);
                    WriteU32(w, 0);                               // rcode, version, flags
                    WriteU16(w, 0);                               // RDLENGTH
                }

                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Writes a 16 bit value in network (big-endian) order.</summary>
        private static void WriteU16(BinaryWriter w, int value)
        {
            w.Write((byte)((value >> 8) & 0xFF));
            w.Write((byte)(value & 0xFF));
        }

        /// <summary>Writes a 32 bit value in network (big-endian) order.</summary>
        private static void WriteU32(BinaryWriter w, uint value)
        {
            w.Write((byte)((value >> 24) & 0xFF));
            w.Write((byte)((value >> 16) & 0xFF));
            w.Write((byte)((value >> 8) & 0xFF));
            w.Write((byte)(value & 0xFF));
        }

        /// <summary>
        /// Rewrites a query with the DNSSEC "DO" bit set, used by the DNSSEC tool.
        /// </summary>
        public static byte[] BuildDnssecQuery(string host, DnsRecordType type, ushort id)
        {
            string qname = NormalizeName(host);

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                WriteU16(w, id);
                WriteU16(w, 0x0180);             // RD + DO (DNSSEC OK)
                WriteU16(w, 1);                  // QDCOUNT
                WriteU16(w, 0);                  // ANCOUNT
                WriteU16(w, 0);                  // NSCOUNT
                WriteU16(w, 1);                  // ARCOUNT (one OPT record)

                WriteName(w, qname);
                WriteU16(w, (int)type);
                WriteU16(w, 1);                  // QCLASS = IN

                w.Write((byte)0);                // root name
                WriteU16(w, 41);                 // OPT
                WriteU16(w, 4096);               // advertised payload size
                WriteU32(w, 0x00008000);         // DO bit in the OPT flags
                WriteU16(w, 0);                  // RDLENGTH

                w.Flush();
                return ms.ToArray();
            }
        }

        private static void WriteName(BinaryWriter w, string name)
        {
            if (name == "." || name.Length == 0)
            {
                w.Write((byte)0);
                return;
            }

            string[] labels = name.Split('.');
            for (int i = 0; i < labels.Length; i++)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(labels[i]);
                if (bytes.Length > 63)
                    throw new FormatException("A DNS label cannot exceed 63 characters.");

                w.Write((byte)bytes.Length);
                w.Write(bytes);
            }

            w.Write((byte)0);
        }

        /// <summary>
        /// Normalises user input into a fully qualified name: lower case, no trailing
        /// dot, IDN converted to punycode, and the root name for an empty input.
        /// </summary>
        public static string NormalizeName(string host)
        {
            if (host == null) return ".";
            string name = host.Trim();
            if (name.Length == 0) return ".";

            while (name.EndsWith(".", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 1);

            if (name.Length == 0) return ".";
            if (name.Length > MaxNameLength)
                throw new FormatException("The name is longer than the DNS limit of 255 characters.");

            // Internationalised domains arrive as Unicode; DNS itself is ASCII only.
            if (name.IndexOfAny(new[] { '\u00e4', '\u00f6', '\u00fc', '\u00df' }) >= 0 ||
                AnyNonAscii(name))
            {
                try { return new IdnMapping().GetAscii(name).ToLowerInvariant(); }
                catch { /* fall through and use the text as typed */ }
            }

            return name.ToLowerInvariant();
        }

        private static bool AnyNonAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] > 127) return true;
            return false;
        }

        // -------------------------------------------------------------- response

        /// <summary>Decoded header plus the three record sections.</summary>
        public sealed class Response
        {
            public ushort Id;
            public DnsStatus Status;
            public bool Authoritative;
            public bool Truncated;
            public bool RecursionAvailable;
            public bool RecursionDesired;
            public bool AuthenticatedData;
            public int ExtendedRcode;

            public readonly List<DnsRecordInfo> Answers = new List<DnsRecordInfo>();
            public readonly List<DnsRecordInfo> Authority = new List<DnsRecordInfo>();
            public readonly List<DnsRecordInfo> Additional = new List<DnsRecordInfo>();
        }

        /// <summary>
        /// Parses a DNS response. Unknown record types are skipped rather than failing
        /// the whole answer, so a resolver that adds extra records cannot break a query.
        /// </summary>
        public static Response Decode(byte[] data)
        {
            Response response = new Response();

            if (data == null || data.Length < 12)
                throw new FormatException("The DNS response was too short to be valid.");

            int pos = 0;
            response.Id = (ushort)ReadUInt16(data, ref pos);
            int flags = ReadUInt16(data, ref pos);

            response.RecursionDesired = (flags & 0x0100) != 0;
            response.Authoritative = (flags & 0x0400) != 0;
            response.Truncated = (flags & 0x0200) != 0;
            response.RecursionAvailable = (flags & 0x0080) != 0;
            response.AuthenticatedData = (flags & 0x0020) != 0;

            int rcode = flags & 0x000F;
            response.Status = (DnsStatus)rcode;

            int qd = ReadUInt16(data, ref pos);
            int an = ReadUInt16(data, ref pos);
            int ns = ReadUInt16(data, ref pos);
            int ar = ReadUInt16(data, ref pos);

            for (int i = 0; i < qd; i++)
            {
                ReadName(data, ref pos);
                pos += 4; // QTYPE + QCLASS
            }

            ReadSection(data, ref pos, an, response.Answers, response);
            ReadSection(data, ref pos, ns, response.Authority, response);
            ReadSection(data, ref pos, ar, response.Additional, response);

            return response;
        }

        private static void ReadSection(byte[] data, ref int pos, int count,
                                        List<DnsRecordInfo> into, Response response)
        {
            for (int i = 0; i < count; i++)
            {
                if (pos >= data.Length) return;

                int start = pos;
                string name = ReadName(data, ref pos);

                if (pos + 10 > data.Length) return;

                int typeCode = ReadUInt16(data, ref pos);
                ReadUInt16(data, ref pos);            // CLASS
                uint ttl = ReadUInt32(data, ref pos);
                int rdLength = ReadUInt16(data, ref pos);

                int rdStart = pos;
                int rdEnd = rdStart + rdLength;
                if (rdEnd > data.Length) rdEnd = data.Length;

                // OPT pseudo-record (type 41) carries the extended RCODE and flags.
                if (typeCode == 41)
                {
                    int optPos = rdStart;
                    if (optPos + 4 <= rdEnd)
                    {
                        uint optField = ReadUInt32(data, ref optPos);
                        response.ExtendedRcode = (int)((optField >> 24) & 0xFF);
                        if ((optField & 0x8000) != 0) response.AuthenticatedData = true;
                    }
                    pos = rdEnd;
                    continue;
                }

                DnsRecordInfo record = new DnsRecordInfo();
                record.Name = name;
                record.Ttl = ttl;
                record.Type = (DnsRecordType)typeCode;

                int rdataPos = rdStart;
                ParseRdata(data, ref rdataPos, rdEnd, record);

                into.Add(record);
                pos = rdEnd;
                if (start < 0) return;   // defensive: never loop on a bad pointer
            }
        }

        /// <summary>
        /// Reads only the 12 byte header. Used to decide whether a UDP answer has to be
        /// retried over TCP: a truncated reply has no usable records, so decoding it in
        /// full would throw on the cut-off name instead of reporting the TC flag.
        /// </summary>
        public static bool IsTruncated(byte[] data)
        {
            if (data == null || data.Length < 12) return true;
            int flags = (data[2] << 8) | data[3];
            return (flags & 0x0200) != 0;
        }

        /// <summary>The 16 bit transaction ID at the start of the message.</summary>
        public static ushort PeekId(byte[] data)
        {
            if (data == null || data.Length < 2) return 0;
            return (ushort)((data[0] << 8) | data[1]);
        }

        /// <summary>Decodes the type-specific payload of one record.</summary>
        private static void ParseRdata(byte[] data, ref int pos, int end, DnsRecordInfo record)
        {
            switch (record.Type)
            {
                case DnsRecordType.A:
                    if (pos + 4 <= end) record.Value = new IPAddress(ReadBytes(data, ref pos, 4)).ToString();
                    break;

                case DnsRecordType.AAAA:
                    if (pos + 16 <= end) record.Value = new IPAddress(ReadBytes(data, ref pos, 16)).ToString();
                    break;

                case DnsRecordType.NS:
                case DnsRecordType.CNAME:
                case DnsRecordType.PTR:
                    if (pos < end) record.Value = ReadName(data, ref pos);
                    break;

                case DnsRecordType.MX:
                    if (pos + 2 <= end)
                    {
                        record.Priority = ReadUInt16(data, ref pos);
                        if (pos < end) record.Value = ReadName(data, ref pos);
                    }
                    break;

                case DnsRecordType.SRV:
                    if (pos + 6 <= end)
                    {
                        record.Priority = ReadUInt16(data, ref pos);
                        record.Weight = ReadUInt16(data, ref pos);
                        record.Port = ReadUInt16(data, ref pos);
                        if (pos < end) record.Value = ReadName(data, ref pos);
                    }
                    break;

                case DnsRecordType.TXT:
                    if (pos < end) record.Value = ReadTxtStrings(data, ref pos, end);
                    break;

                case DnsRecordType.SOA:
                    ParseSoa(data, ref pos, end, record);
                    break;

                case DnsRecordType.CAA:
                    ParseCaa(data, ref pos, end, record);
                    break;

                case DnsRecordType.DNSKEY:
                    ParseDnskey(data, ref pos, end, record);
                    break;

                case DnsRecordType.DS:
                    ParseDs(data, ref pos, end, record);
                    break;

                case DnsRecordType.RRSIG:
                    ParseRrsig(data, ref pos, end, record);
                    break;

                default:
                    record.Value = "(" + (end - pos) + " bytes)";
                    break;
            }
        }

        /// <summary>
        /// Joins the character-strings of a TXT record. A long SPF, DKIM or DMARC
        /// record is split into several strings by the author, and treating those as
        /// separate records is a classic source of wrong verdicts.
        /// </summary>
        private static string ReadTxtStrings(byte[] data, ref int pos, int end)
        {
            StringBuilder sb = new StringBuilder();

            while (pos < end)
            {
                int len = data[pos++];
                if (len == 0) continue;
                if (pos + len > end) len = end - pos;

                sb.Append(Encoding.UTF8.GetString(data, pos, len));
                pos += len;
            }

            return sb.ToString();
        }

        private static void ParseSoa(byte[] data, ref int pos, int end, DnsRecordInfo record)
        {
            if (pos >= end) return;
            record.SoaPrimaryNs = ReadName(data, ref pos);
            if (pos >= end) return;

            record.SoaResponsible = ReadName(data, ref pos);
            if (pos + 20 > end) return;

            record.SoaSerial = ReadUInt32(data, ref pos);
            record.SoaRefresh = ReadUInt32(data, ref pos);
            record.SoaRetry = ReadUInt32(data, ref pos);
            record.SoaExpire = ReadUInt32(data, ref pos);
            record.SoaMinimum = ReadUInt32(data, ref pos);

            record.Value = record.SoaPrimaryNs;
            record.Extra = record.SoaResponsible;
        }

        private static void ParseCaa(byte[] data, ref int pos, int end, DnsRecordInfo record)
        {
            if (pos + 2 > end) return;

            byte flags = data[pos++];
            int tagLen = data[pos++];
            if (pos + tagLen > end) tagLen = end - pos;

            string tag = Encoding.ASCII.GetString(data, pos, tagLen);
            pos += tagLen;

            string value = (pos < end) ? Encoding.UTF8.GetString(data, pos, end - pos) : string.Empty;

            record.Value = tag + " \"" + value + "\"";
            record.Extra = "flags " + flags + ", critical=" + ((flags & 0x80) != 0 ? "yes" : "no");
        }

        private static void ParseDnskey(byte[] data, ref int pos, int end, DnsRecordInfo record)
        {
            if (pos + 4 > end) return;

            int flags = ReadUInt16(data, ref pos);
            pos++;   // protocol, always 3
            byte algorithm = data[pos++];

            record.Value = "flags " + flags + ", algorithm " + algorithm;
            record.Extra = "key " + (end - pos) + " bytes";
        }

        private static void ParseDs(byte[] data, ref int pos, int end, DnsRecordInfo record)
        {
            if (pos + 4 > end) return;

            int keyTag = ReadUInt16(data, ref pos);
            byte algorithm = data[pos++];
            byte digestType = data[pos++];

            record.Value = "key tag " + keyTag + ", algorithm " + algorithm + ", digest " + digestType;
            record.Extra = (end - pos) + " bytes";
        }

        private static void ParseRrsig(byte[] data, ref int pos, int end, DnsRecordInfo record)
        {
            if (pos + 18 > end) return;

            int typeCovered = ReadUInt16(data, ref pos);
            byte algorithm = data[pos++];
            pos++;   // label count
            pos++;   // original TTL
            uint expiration = ReadUInt32(data, ref pos);
            uint inception = ReadUInt32(data, ref pos);
            ReadUInt16(data, ref pos);   // key tag

            record.Value = "covers " + DnsTypes.Name((DnsRecordType)typeCovered) + ", algorithm " + algorithm;
            record.Extra = "expires " + FormatDate(expiration) + ", signed " + FormatDate(inception);
        }

        private static string FormatDate(uint secondsSinceEpoch)
        {
            try
            {
                DateTime d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                             .AddSeconds(secondsSinceEpoch);
                return d.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "?";
            }
        }

        // ------------------------------------------------------------- primitives

        private static int ReadUInt16(byte[] data, ref int pos)
        {
            if (pos + 2 > data.Length) throw new FormatException("Unexpected end of DNS message.");
            int value = (data[pos] << 8) | data[pos + 1];
            pos += 2;
            return value;
        }

        private static uint ReadUInt32(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) throw new FormatException("Unexpected end of DNS message.");
            uint value = ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) |
                         ((uint)data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            return value;
        }

        private static byte[] ReadBytes(byte[] data, ref int pos, int count)
        {
            if (count < 0 || pos + count > data.Length)
                count = Math.Max(0, data.Length - pos);

            byte[] result = new byte[count];
            Buffer.BlockCopy(data, pos, result, 0, count);
            pos += count;
            return result;
        }

        /// <summary>
        /// Reads a possibly compressed domain name and leaves <paramref name="pos"/>
        /// just past the name as it appears in the byte stream.
        ///
        /// Compression pointers (top two bits set) are followed, with a hop counter to
        /// stop a corrupt or hostile pointer loop. A pointer may only appear as the
        /// last element of a name, so once one is followed the stream position is
        /// settled at pointer+2 and the labels it refers to are read without moving
        /// the stream cursor any further.
        /// </summary>
        private static string ReadName(byte[] data, ref int pos)
        {
            StringBuilder sb = new StringBuilder();
            int streamPos = pos;          // where the name starts in the stream
            int cursor = pos;             // where the labels are read from
            int hops = 0;
            int nameLength = 0;
            bool streamSettled = false;   // true once pos no longer tracks the labels

            while (true)
            {
                if (cursor >= data.Length)
                    throw new FormatException("Domain name ran past the end of the message.");

                byte length = data[cursor];

                // Compression pointer: two high bits set.
                if ((length & 0xC0) == 0xC0)
                {
                    if (cursor + 1 >= data.Length)
                        throw new FormatException("Truncated compression pointer.");

                    int target = ((length & 0x3F) << 8) | data[cursor + 1];

                    if (++hops > MaxPointerHops)
                        throw new FormatException("Too many compression pointers in the name.");

                    // The pointer ends this name in the byte stream. If we have not
                    // already consumed a label, the stream position is the pointer.
                    if (!streamSettled)
                    {
                        streamPos = cursor + 2;
                        streamSettled = true;
                    }

                    cursor = target;
                    continue;
                }

                if (length == 0)
                {
                    // Terminating zero byte. It always ends the name in the stream,
                    // whether or not a pointer was involved.
                    if (!streamSettled) streamPos = cursor + 1;
                    break;
                }

                if ((length & 0xC0) != 0)
                    throw new FormatException("Unsupported label type in the domain name.");

                if (cursor + 1 + length > data.Length)
                    throw new FormatException("Truncated label in the domain name.");

                nameLength += length + 1;
                if (nameLength > MaxNameLength)
                    throw new FormatException("Domain name is longer than 255 characters.");

                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(data, cursor + 1, length));

                cursor += 1 + length;

                // The first label was read straight from the stream, so the stream
                // position now follows the labels until a zero or a pointer ends it.
                if (!streamSettled) streamPos = cursor;
            }

            pos = streamPos;
            return sb.ToString();
        }
    }
}
