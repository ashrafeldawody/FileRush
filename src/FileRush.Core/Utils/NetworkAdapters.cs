using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FileRush.Core.Engine;

namespace FileRush.Core.Utils;

public static class NetworkAdapters
{
    public static IReadOnlyList<NetworkRoute> GetInternetRoutes()
    {
        var routes = new List<NetworkRoute>();
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return routes;
        }
        foreach (var adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var properties = adapter.GetIPProperties();
            if (!properties.GatewayAddresses.Any(g => IsUsableGateway(g.Address))) continue;
            var addresses = properties.UnicastAddresses
                .Select(u => u.Address)
                .Where(IsUsableLocalAddress)
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToList();
            if (addresses.Count == 0) continue;
            routes.Add(new NetworkRoute(adapter.Id, adapter.Name, addresses));
        }
        return routes;
    }

    public static IReadOnlyList<NetworkRoute> Select(IReadOnlyList<NetworkRoute> available, IReadOnlyCollection<string> selectedIds)
    {
        if (selectedIds.Count == 0) return available;
        return available.Where(r => selectedIds.Contains(r.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private static bool IsUsableGateway(IPAddress address)
    {
        return !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);
    }

    private static bool IsUsableLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return !(bytes[0] == 169 && bytes[1] == 254);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast;
        }
        return false;
    }
}
