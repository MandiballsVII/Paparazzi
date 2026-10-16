using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

public static class NetworkAddressUtility
{
    const string PrefsLastJoinAddress = "CoopPrototype.LastJoinAddress";

    public static string LoadLastJoinAddress(string fallback = "127.0.0.1")
    {
        return UnityEngine.PlayerPrefs.GetString(PrefsLastJoinAddress, fallback);
    }

    public static void SaveLastJoinAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        UnityEngine.PlayerPrefs.SetString(PrefsLastJoinAddress, address.Trim());
        UnityEngine.PlayerPrefs.Save();
    }

    public static List<string> GetLocalIPv4Addresses()
    {
        var results = new List<string>();

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                NetworkInterfaceType type = nic.NetworkInterfaceType;
                if (type != NetworkInterfaceType.Ethernet &&
                    type != NetworkInterfaceType.Wireless80211 &&
                    type != NetworkInterfaceType.GigabitEthernet)
                    continue;

                foreach (UnicastIPAddressInformation uni in nic.GetIPProperties().UnicastAddresses)
                {
                    if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    string ip = uni.Address.ToString();
                    if (ip.StartsWith("127."))
                        continue;

                    if (!results.Contains(ip))
                        results.Add(ip);
                }
            }
        }
        catch
        {
            // Fallback below.
        }

        if (results.Count == 0)
        {
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    string ip = address.ToString();
                    if (ip.StartsWith("127."))
                        continue;

                    if (!results.Contains(ip))
                        results.Add(ip);
                }
            }
            catch
            {
                // Ignore.
            }
        }

        return results;
    }

    public static string FormatLocalAddressesForDisplay()
    {
        List<string> addresses = GetLocalIPv4Addresses();
        if (addresses.Count == 0)
            return "No se detectó IP LAN. Usa ipconfig en el host.";

        var sb = new StringBuilder();
        sb.Append("IP(s) de este PC para LAN: ");
        for (int i = 0; i < addresses.Count; i++)
        {
            if (i > 0)
                sb.Append(" | ");
            sb.Append(addresses[i]);
        }

        sb.Append("  ·  Puerto ").Append(GameSessionConfig.DefaultPort);
        return sb.ToString();
    }
}
