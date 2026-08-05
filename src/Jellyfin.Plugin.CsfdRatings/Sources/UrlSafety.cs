// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net;
using System.Net.Sockets;

namespace Jellyfin.Plugin.CsfdRatings.Sources;

/// <summary>
/// Heuristics for "is this host somewhere only I can reach".
/// Used purely to warn the user, never to block a request.
/// </summary>
public static class UrlSafety
{
    public static bool IsPrivateHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        host = host.Trim().Trim('[', ']');

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IsPrivateAddress(ip);
        }

        // A bare label with no dot is a Docker/Compose service name or a LAN hostname,
        // e.g. "csfd-api". Anything with a dot is treated as potentially routable.
        if (!host.Contains('.', StringComparison.Ordinal))
        {
            return true;
        }

        return host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPrivateAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7 unique local, fe80::/10 link local.
            var v6 = ip.GetAddressBytes();
            return (v6[0] & 0xFE) == 0xFC || (v6[0] == 0xFE && (v6[1] & 0xC0) == 0x80);
        }

        var bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            127 => true,
            169 when bytes[1] == 254 => true,                   // link local
            172 when bytes[1] >= 16 && bytes[1] <= 31 => true,  // 172.16.0.0/12
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }
}
