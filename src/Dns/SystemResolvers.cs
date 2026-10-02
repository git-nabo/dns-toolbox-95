using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DnsToolbox95.Dns
{
    /// <summary>
    /// Finds the DNS servers Windows is configured to use, so the resolver list can
    /// offer them and the system default really is the system default.
    /// </summary>
    public static class SystemResolvers
    {
        /// <summary>Well known public resolvers offered as presets.</summary>
        public static readonly string[] Presets =
        {
            "1.1.1.1",       // Cloudflare
            "8.8.8.8",       // Google
            "9.9.9.9"        // Quad9
        };

        public static readonly string[] PresetNames =
        {
            "Cloudflare - 1.1.1.1",
            "Google - 8.8.8.8",
            "Quad9 - 9.9.9.9"
        };

        /// <summary>
        /// Every usable configured resolver, IPv4 first, without duplicates and
        /// without loopback or link-local placeholders.
        /// </summary>
        public static List<IPAddress> List()
        {
            List<IPAddress> found = new List<IPAddress>();

            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();

                for (int i = 0; i < adapters.Length; i++)
                {
                    NetworkInterface adapter = adapters[i];

                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    if (properties == null) continue;

                    IPAddressCollection addresses = properties.DnsAddresses;
                    if (addresses == null) continue;

                    foreach (IPAddress address in addresses)
                    {
                        if (IsUsable(address) && !found.Contains(address))
                            found.Add(address);
                    }
                }
            }
            catch
            {
                // A machine without readable adapter data still gets presets.
            }

            found.Sort(ComparePreferV4);
            return found;
        }

        /// <summary>Resolvers formatted for the dropdown, e.g. "1.1.1.1 (Ethernet)".</summary>
        public static List<string> Describe()
        {
            List<string> text = new List<string>();

            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();

                for (int i = 0; i < adapters.Length; i++)
                {
                    NetworkInterface adapter = adapters[i];
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;

                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    if (properties == null || properties.DnsAddresses == null) continue;

                    foreach (IPAddress address in properties.DnsAddresses)
                    {
                        if (!IsUsable(address)) continue;

                        string entry = address + "  (" + adapter.Name + ")";
                        if (!text.Contains(entry)) text.Add(entry);
                    }
                }
            }
            catch
            {
                // Ignored: the presets are always offered regardless.
            }

            return text;
        }

        private static bool IsUsable(IPAddress address)
        {
            if (address == null) return false;
            if (IPAddress.IsLoopback(address)) return false;

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal) return false;
                if (address.IsIPv6SiteLocal) return false;
            }

            return true;
        }

        private static int ComparePreferV4(IPAddress a, IPAddress b)
        {
            bool v4a = a.AddressFamily == AddressFamily.InterNetwork;
            bool v4b = b.AddressFamily == AddressFamily.InterNetwork;

            if (v4a && !v4b) return -1;
            if (!v4a && v4b) return 1;
            return 0;
        }
    }
}
