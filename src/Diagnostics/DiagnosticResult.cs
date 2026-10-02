using System;
using System.Collections.Generic;
using System.Text;

namespace DnsToolbox95.Diagnostics
{
    /// <summary>Severity of a single finding. Deliberately conservative.</summary>
    public enum Severity
    {
        Info,
        Ok,
        Warn,
        Fail
    }

    /// <summary>One line of output from a diagnostic tool.</summary>
    public sealed class Finding
    {
        public Severity Level = Severity.Info;
        public string Message = string.Empty;

        public static Finding Ok(string message) { return new Finding { Level = Severity.Ok, Message = message }; }
        public static Finding Info(string message) { return new Finding { Level = Severity.Info, Message = message }; }
        public static Finding Warn(string message) { return new Finding { Level = Severity.Warn, Message = message }; }
        public static Finding Fail(string message) { return new Finding { Level = Severity.Fail, Message = message }; }

        public string Tag
        {
            get
            {
                switch (Level)
                {
                    case Severity.Ok: return "[OK]  ";
                    case Severity.Warn: return "[WARN]";
                    case Severity.Fail: return "[FAIL]";
                    default: return "[INFO]";
                }
            }
        }

        public string Line
        {
            get { return Tag + "  " + Message; }
        }
    }

    /// <summary>
    /// The shared result shape for every diagnostic dialog: a title, a list of
    /// findings and any free-form detail text.
    /// </summary>
    public class DiagnosticReport
    {
        public string Title = string.Empty;
        public readonly List<Finding> Findings = new List<Finding>();
        public readonly List<string> Details = new List<string>();

        public void Add(Severity level, string message)
        {
            Findings.Add(new Finding { Level = level, Message = message });
        }

        /// <summary>True when nothing reached [FAIL].</summary>
        public bool HasFailure
        {
            get
            {
                foreach (Finding f in Findings)
                    if (f.Level == Severity.Fail) return true;
                return false;
            }
        }

        public int Count(Severity level)
        {
            int n = 0;
            foreach (Finding f in Findings)
                if (f.Level == level) n++;
            return n;
        }

        /// <summary>The whole report as text, for the results box and the clipboard.</summary>
        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Title);
            sb.AppendLine(new string('-', 60));

            foreach (Finding f in Findings)
                sb.AppendLine(f.Line);

            if (Details.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Details:");
                foreach (string d in Details)
                    sb.AppendLine("  " + d);
            }

            return sb.ToString().TrimEnd();
        }
    }
}
