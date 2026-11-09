using System.Net;
using System.Net.Sockets;

namespace ButikkPilot.Api.Hosting;

public static class ClientKey
{
    /// <summary>
    /// Identifies a client for rate limiting. IPv6 clients are bucketed by /64 because a single subscriber
    /// typically owns a whole /64 and could otherwise rotate addresses to dodge the limit.
    /// Behind a proxy this only works if forwarded headers are processed (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true).
    /// </summary>
    public static string From(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes).ToString() + "/64";
        }

        return ip.ToString();
    }
}
