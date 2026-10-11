using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace FileRush.Core.Engine;

public static class RouteConnector
{
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> For(NetworkRoute route)
    {
        return (context, ct) => ConnectAsync(route, context.DnsEndPoint, ct);
    }

    public static async ValueTask<Stream> ConnectAsync(NetworkRoute route, DnsEndPoint endPoint, CancellationToken ct)
    {
        var remotes = IPAddress.TryParse(endPoint.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(endPoint.Host, ct).ConfigureAwait(false);
        foreach (var remote in remotes)
        {
            var local = route.Addresses.FirstOrDefault(a => a.AddressFamily == remote.AddressFamily);
            if (local is null) continue;
            var stream = await TryConnectBoundAsync(local, new IPEndPoint(remote, endPoint.Port), ct).ConfigureAwait(false);
            if (stream is not null) return stream;
        }
        return await ConnectUnboundAsync(endPoint, ct).ConfigureAwait(false);
    }

    private static async Task<Stream?> TryConnectBoundAsync(IPAddress local, IPEndPoint remote, CancellationToken ct)
    {
        var socket = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.Bind(new IPEndPoint(local, 0));
            await socket.ConnectAsync(remote, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<Stream> ConnectUnboundAsync(DnsEndPoint endPoint, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
