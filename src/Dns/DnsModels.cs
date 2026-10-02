using System;
using System.Collections.Generic;
using System.Text;

namespace DnsToolbox95.Dns
{
    /// <summary>
    /// One resource record from a DNS answer, already decoded into a human readable
    /// form. <see cref="Row"/> supplies the per-type extra fields used by the results
    /// table, so the table itself can stay generic.
    /// </summary>
    public sealed class DnsRecordInfo
    {
        public DnsRecordType Type;
        public string Name = string.Empty;
        public uint Ttl;

        /// <summary>Primary value: an IP, a hostname, a TXT blob, ...</summary>
        public string Value = string.Empty;

        /// <summary>MX preference / SRV priority / SOA serial, depending on type.</summary>
        public int Priority = -1;

        /// <summary>SRV weight.</summary>
        public int Weight = -1;

        /// <summary>SRV port.</summary>
        public int Port = -1;

        /// <summary>SOA fields, used only for SOA records.</summary>
        public string SoaPrimaryNs;
        public string SoaResponsible;
        public uint SoaSerial;
        public uint SoaRefresh;
        public uint SoaRetry;
        public uint SoaExpire;
        public uint SoaMinimum;

        /// <summary>DS / DNSKEY / RRSIG extras used by the DNSSEC tool.</summary>
        public string Extra = string.Empty;

        public string TypeName
        {
            get { return DnsTypes.Name(Type); }
        }

        /// <summary>
        /// Builds the row for the results table. The leading cell is always the record
        /// type, then the type-specific fields, and TTL is always last.
        /// </summary>
        public string[] Row()
        {
            List<string> cells = new List<string>();
            cells.Add(TypeName);
            cells.Add(Name);

            switch (Type)
            {
                case DnsRecordType.MX:
                    cells.Add(Priority >= 0 ? Priority.ToString() : string.Empty);
                    cells.Add(Value);
                    break;

                case DnsRecordType.SRV:
                    cells.Add(Priority >= 0 ? Priority.ToString() : string.Empty);
                    cells.Add(Weight >= 0 ? Weight.ToString() : string.Empty);
                    cells.Add(Port >= 0 ? Port.ToString() : string.Empty);
                    cells.Add(Value);
                    break;

                case DnsRecordType.SOA:
                    cells.Add(SoaPrimaryNs ?? string.Empty);
                    cells.Add(SoaResponsible ?? string.Empty);
                    cells.Add(SoaSerial.ToString());
                    cells.Add(SoaRefresh.ToString());
                    cells.Add(SoaRetry.ToString());
                    cells.Add(SoaExpire.ToString());
                    cells.Add(SoaMinimum.ToString());
                    break;

                case DnsRecordType.DS:
                case DnsRecordType.DNSKEY:
                case DnsRecordType.RRSIG:
                    cells.Add(Value);
                    cells.Add(Extra);
                    break;

                default:
                    cells.Add(Value);
                    break;
            }

            cells.Add(Ttl.ToString());
            return cells.ToArray();
        }

        /// <summary>Single-line rendering used by Copy Results and the report writer.</summary>
        public string ToLine()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(TypeName.PadRight(6));
            sb.Append(' ');
            sb.Append(Name.PadRight(32));
            sb.Append(' ');

            if (Type == DnsRecordType.MX)
            {
                sb.Append(("pref " + Priority).PadRight(10));
            }
            else if (Type == DnsRecordType.SRV)
            {
                sb.Append(("pri " + Priority + " w " + Weight + " port " + Port).PadRight(28));
            }

            sb.Append(Value);

            if (Type == DnsRecordType.SOA)
            {
                sb.Append("  serial " + SoaSerial);
            }

            sb.Append("   TTL " + Ttl);
            return sb.ToString();
        }
    }

    /// <summary>The full outcome of one DNS query, including transport metadata.</summary>
    public sealed class DnsQueryResult
    {
        public string Query = string.Empty;
        public string Resolver = string.Empty;
        public DnsRecordType RequestedType = DnsRecordType.A;

        public DnsStatus Status = DnsStatus.NOERROR;
        public bool Authoritative;
        public bool Truncated;
        public bool RecursionAvailable;
        public bool AuthenticatedData;
        public int ResponseMs;

        /// <summary>Set when the transport failed outright (timeout, refused, ...).</summary>
        public bool Failed;

        /// <summary>Human readable failure reason, empty when the query succeeded.</summary>
        public string Error = string.Empty;

        public readonly List<DnsRecordInfo> Answers = new List<DnsRecordInfo>();
        public readonly List<DnsRecordInfo> Authority = new List<DnsRecordInfo>();
        public readonly List<DnsRecordInfo> Additional = new List<DnsRecordInfo>();

        public bool Succeeded
        {
            get { return !Failed; }
        }

        public string StatusText
        {
            get { return Failed ? "FAILED" : Status.ToString(); }
        }

        /// <summary>Answers plus anything returned alongside them.</summary>
        public List<DnsRecordInfo> AllRecords()
        {
            List<DnsRecordInfo> all = new List<DnsRecordInfo>();
            all.AddRange(Answers);
            all.AddRange(Additional);
            return all;
        }
    }

    /// <summary>One line of a resolver comparison or propagation report.</summary>
    public sealed class ResolverComparisonRow
    {
        public string Resolver = string.Empty;
        public string Status = string.Empty;
        public string Response = string.Empty;
        public string Ttl = string.Empty;
        public int ResponseMs;
        public bool Failed;
        public string Error = string.Empty;
    }
}
