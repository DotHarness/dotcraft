using System.Net;
using System.Net.Sockets;

namespace DotCraft.Tools.Web;

internal static class PublicIpAddress
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPublicV4(address.GetAddressBytes());

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (address.IsIPv4MappedToIPv6)
            return IsPublicV4(address.MapToIPv4().GetAddressBytes());

        var b = address.GetAddressBytes();

        if (IsNat64WellKnown(b))
            return IsPublicV4(b[12..16]);

        if ((b[0] & 0xE0) != 0x20)
            return false;

        if (b[0] == 0x20 && b[1] == 0x01 && b[2] < 0x02)
            return false;

        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
            return false;

        if (b[0] == 0x20 && b[1] == 0x02)
            return IsPublicV4(b[2..6]);

        return true;
    }

    private static bool IsNat64WellKnown(byte[] b) =>
        b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B
        && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0;

    private static bool IsPublicV4(byte[] b)
    {
        var a0 = b[0];
        var a1 = b[1];
        var a2 = b[2];

        return !(a0 == 0
                 || a0 == 10
                 || a0 == 127
                 || a0 >= 224
                 || (a0 == 100 && (a1 & 0xC0) == 64)
                 || (a0 == 169 && a1 == 254)
                 || (a0 == 172 && (a1 & 0xF0) == 16)
                 || (a0 == 192 && a1 == 168)
                 || (a0 == 192 && a1 == 0 && (a2 == 0 || a2 == 2))
                 || (a0 == 192 && a1 == 88 && a2 == 99)
                 || (a0 == 198 && a1 == 51 && a2 == 100)
                 || (a0 == 203 && a1 == 0 && a2 == 113));
    }
}
