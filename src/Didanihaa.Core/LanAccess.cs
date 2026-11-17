using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Didanihaa.Core;

public static class LanAccess
{
    public static bool IsLocalClient(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254);
        return address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc;
    }

    public static bool IsValidHost(string host, int? port, int expectedPort, IPAddress localAddress)
    {
        if (port != expectedPort) return false;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host.Trim('[', ']'), out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (localAddress.IsIPv4MappedToIPv6) localAddress = localAddress.MapToIPv4();
        return address.Equals(localAddress);
    }

    public static string[] ViewerUrls(int port) => NetworkInterface.GetAllNetworkInterfaces()
        .Where(network => network.OperationalStatus == OperationalStatus.Up && network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .OrderByDescending(network => network.GetIPProperties().GatewayAddresses.Count > 0)
        .SelectMany(network => network.GetIPProperties().UnicastAddresses)
        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && IsLocalClient(address.Address))
        .Select(address => $"http://{address.Address}:{port}/tv/").Distinct().ToArray();
}
