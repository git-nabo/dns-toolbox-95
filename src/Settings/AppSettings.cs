using System;
using System.IO;

namespace DnsToolbox95.Settings
{
    /// <summary>
    /// Optional diagnostic logging. It is OFF by default and writes nothing at all
    /// until the user turns it on, so a normal run never creates a log file.
    /// </summary>
    public static class Log
    {
        private static bool _enabled;
        private static string _path;

        /// <summary>Whether diagnostic logging is on. Off until the user enables it.</summary>
        public static bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    string folder = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DnsToolbox95");
                    _path = System.IO.Path.Combine(folder, "dnstoolbox95.log");
                }
                return _path;
            }
        }



        /// <summary>
        /// Turns logging off and removes the file, so switching it off really does
        /// leave nothing behind.
        /// </summary>
        public static void Disable()
        {
            _enabled = false;

            try
            {
                if (File.Exists(Path)) File.Delete(Path);
            }
            catch
            {
                // A locked log file is not worth reporting to the user.
            }
        }

        /// <summary>Appends one line. Does nothing while logging is disabled.</summary>
        public static void Write(string category, string query, string result, string detail)
        {
            if (!_enabled) return;

            try
            {
                string directory = System.IO.Path.GetDirectoryName(Path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                              "  " + category.PadRight(6) +
                              "  " + Null(query) +
                              "  " + Null(result) +
                              "  " + Null(detail);

                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never interrupt a DNS query.
            }
        }

        private static string Null(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }
    }

    /// <summary>
    /// The few preferences worth remembering between runs. Nothing is stored unless
    /// the user changes it, and the query history is deliberately kept in memory only.
    /// </summary>
    public static class AppSettings
    {
        public static int TimeoutMs = 5000;
        public static string LastDomain = string.Empty;
        public static string LastType = "A";

        private static string File
        {
            get
            {
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DnsToolbox95");
                return System.IO.Path.Combine(folder, "settings.txt");
            }
        }

        /// <summary>Reads the settings file if it exists. Never creates one.</summary>
        public static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return;

                foreach (string line in System.IO.File.ReadAllLines(File))
                {
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;

                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();

                    if (key == "TimeoutMs")
                    {
                        int ms;
                        if (int.TryParse(value, out ms) && ms >= 500 && ms <= 60000) TimeoutMs = ms;
                    }
                    else if (key == "LastDomain") LastDomain = value;
                    else if (key == "LastType") LastType = value;
                }
            }
            catch
            {
                // Corrupt or unreadable settings must not stop the app starting.
            }
        }

        /// <summary>Writes the settings file. Called only when the app closes cleanly.</summary>
        public static void Save()
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(File);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                System.IO.File.WriteAllLines(File, new[]
                {
                    "TimeoutMs=" + TimeoutMs,
                    "LastDomain=" + LastDomain,
                    "LastType=" + LastType
                });
            }
            catch
            {
                // Preferences are a convenience, never a requirement.
            }
        }
    }
}
