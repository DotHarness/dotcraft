using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DotCraft.Hub;

internal static class MobileNetwork
{
    private static readonly IPAddress RouteProbe = IPAddress.Parse("203.0.113.1");

    public static bool IsAllowedSource(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        Span<byte> bytes = stackalloc byte[4];
        address.TryWriteBytes(bytes, out _);
        return bytes[0] == 10
               || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16)
               || (bytes[0] == 192 && bytes[1] == 168)
               || (bytes[0] == 169 && bytes[1] == 254)
               || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64);
    }

    public static IReadOnlyList<string> AdvertisedAddresses()
    {
        List<IPAddress> addresses;
        try
        {
            addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up
                               && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Select(item => item.Address)
                .Where(item => item.AddressFamily == AddressFamily.InterNetwork
                               && !IPAddress.IsLoopback(item)
                               && !item.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        if (DefaultRouteAddress() is { } preferred && addresses.Remove(preferred))
            addresses.Insert(0, preferred);
        return [.. addresses.Select(item => item.ToString())];
    }

    // Connecting a UDP socket sends nothing; it only asks the OS which source address the default route uses.
    private static IPAddress? DefaultRouteAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(RouteProbe, 9);
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
