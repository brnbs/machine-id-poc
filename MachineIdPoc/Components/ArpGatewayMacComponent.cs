using System.Globalization;
using System.Net.NetworkInformation;
using DeviceId;

namespace MachineIdPoc.Components;

/// <summary>
/// Custom DeviceId component that resolves the MAC address of the container's
/// default gateway from the ARP cache at /proc/net/arp.
///
/// In a standard Docker bridge network the default gateway is the docker0 bridge
/// interface on the HOST. Its MAC address is:
///   - identical for every container running on the same host
///   - different on different hosts (each Docker daemon generates its own bridge MAC)
///   - does NOT require any special Docker capabilities or host configuration
///
/// If the ARP entry is absent (cache cold) the component issues a single ICMP ping
/// to populate it before retrying.
/// </summary>
public sealed class ArpGatewayMacComponent : IDeviceIdComponent
{
    private string _value = string.Empty;
    private bool _resolved;

    /// <summary>Name used as the component key when registering with DeviceIdBuilder.</summary>
    public string Name => "ArpGatewayMac";

    /// <inheritdoc />
    public string GetValue()
    {
        if (!_resolved)
        {
            _value = Resolve() ?? string.Empty;
            _resolved = true;
        }
        return _value;
    }

    private static string? Resolve()
    {
        try
        {
            string? gatewayIp = FindDefaultGateway();
            if (gatewayIp is null)
                return null;

            string? mac = LookupArpMac(gatewayIp);
            if (mac is not null)
                return mac;

            // ARP cache is cold — ping once to force an ARP request, then retry
            PingOnce(gatewayIp);
            Thread.Sleep(500);
            return LookupArpMac(gatewayIp);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses /proc/net/route to find the default gateway IP.
    /// The file columns are: Iface, Destination, Gateway, Flags, RefCnt, Use,
    /// Metric, Mask, MTU, Window, IRTT  — all tab-separated, values in hex.
    /// Default route: Destination == "00000000" and Flags has RTF_GATEWAY (0x0002).
    /// </summary>
    private static string? FindDefaultGateway()
    {
        const string routeFile = "/proc/net/route";
        if (!File.Exists(routeFile))
            return null;

        foreach (string line in File.ReadLines(routeFile).Skip(1))
        {
            string[] parts = line.Split('\t');
            if (parts.Length < 4)
                continue;

            string destination = parts[1].Trim();
            string gatewayHex  = parts[2].Trim();
            string flagsHex    = parts[3].Trim();

            if (!int.TryParse(flagsHex, NumberStyles.HexNumber, null, out int flags))
                continue;

            // RTF_GATEWAY = 0x0002; default route has destination 00000000
            if (destination == "00000000" && (flags & 0x0002) != 0)
                return HexToIp(gatewayHex);
        }

        return null;
    }

    /// <summary>
    /// /proc/net/route stores IPs as a 32-bit value in host byte order.
    /// On little-endian x86/x64 systems the bytes are naturally in
    /// network order when read via BitConverter.GetBytes.
    /// Example: "0101A8C0" -> bytes [0xC0, 0xA8, 0x01, 0x01] -> 192.168.1.1
    /// </summary>
    private static string HexToIp(string hex)
    {
        uint value = Convert.ToUInt32(hex, 16);
        byte[] bytes = BitConverter.GetBytes(value); // little-endian layout on x86
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    /// <summary>
    /// Searches /proc/net/arp for the MAC address of the given IP.
    /// Columns: IP address, HW type, Flags, HW address, Mask, Device
    /// Skips incomplete entries (all-zero MAC).
    /// </summary>
    private static string? LookupArpMac(string ip)
    {
        const string arpFile = "/proc/net/arp";
        if (!File.Exists(arpFile))
            return null;

        foreach (string line in File.ReadLines(arpFile).Skip(1))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                continue;

            if (parts[0] == ip)
            {
                string mac = parts[3];
                if (mac != "00:00:00:00:00:00")
                    return mac;
            }
        }

        return null;
    }

    private static void PingOnce(string ip)
    {
        try
        {
            using var ping = new Ping();
            ping.Send(ip, 1000);
        }
        catch
        {
            // Ignore — ping may fail in restricted environments;
            // ARP might still populate via other traffic.
        }
    }
}
