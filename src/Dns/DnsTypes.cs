using System;
using System.Collections.Generic;

namespace DnsToolbox95.Dns
{
    /// <summary>DNS record types the toolbox can query (IANA type codes).</summary>
    public enum DnsRecordType
    {
        A = 1,
        NS = 2,
        CNAME = 5,
        SOA = 6,
        PTR = 12,
        MX = 15,
        TXT = 16,
        AAAA = 28,
        SRV = 33,
        DS = 43,
        RRSIG = 46,
        NSEC = 47,
        DNSKEY = 48,
        CAA = 257,
        ANY = 255
    }

    /// <summary>Response codes returned in the DNS header.</summary>
    public enum DnsStatus
    {
        NOERROR = 0,
        FORMERR = 1,
        SERVFAIL = 2,
        NXDOMAIN = 3,
        NOTIMP = 4,
        REFUSED = 5
    }

    public static class DnsTypes
    {
        private static readonly Dictionary<DnsRecordType, string> Names =
            new Dictionary<DnsRecordType, string>
            {
                { DnsRecordType.A, "A" },
                { DnsRecordType.AAAA, "AAAA" },
                { DnsRecordType.CNAME, "CNAME" },
                { DnsRecordType.MX, "MX" },
                { DnsRecordType.TXT, "TXT" },
                { DnsRecordType.NS, "NS" },
                { DnsRecordType.SOA, "SOA" },
                { DnsRecordType.PTR, "PTR" },
                { DnsRecordType.SRV, "SRV" },
                { DnsRecordType.CAA, "CAA" },
                { DnsRecordType.DS, "DS" },
                { DnsRecordType.RRSIG, "RRSIG" },
                { DnsRecordType.NSEC, "NSEC" },
                { DnsRecordType.DNSKEY, "DNSKEY" },
                { DnsRecordType.ANY, "ANY" }
            };

        /// <summary>Short display name, e.g. "AAAA".</summary>
        public static string Name(DnsRecordType type)
        {
            string n;
            if (Names.TryGetValue(type, out n)) return n;
            return "TYPE" + ((int)type);
        }

        /// <summary>Parses a display name such as "mx" into a record type.</summary>
        public static bool TryParse(string text, out DnsRecordType type)
        {
            type = DnsRecordType.A;
            if (string.IsNullOrEmpty(text)) return false;

            foreach (KeyValuePair<DnsRecordType, string> kvp in Names)
            {
                if (string.Equals(kvp.Value, text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    type = kvp.Key;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The record types offered in the main window's type list.</summary>
        public static DnsRecordType[] CommonTypes()
        {
            return new[]
            {
                DnsRecordType.A, DnsRecordType.AAAA, DnsRecordType.CNAME, DnsRecordType.MX,
                DnsRecordType.TXT, DnsRecordType.NS, DnsRecordType.SOA, DnsRecordType.PTR,
                DnsRecordType.SRV, DnsRecordType.CAA
            };
        }
    }
}
