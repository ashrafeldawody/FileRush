using System.Net;
using FileRush.Core.Models;

namespace FileRush.Core.Engine;

public sealed class RangeSourceFactory : IRangeSourceFactory
{
    private readonly Func<Uri, NetworkCredential?>? _credentialResolver;

    public RangeSourceFactory(Func<Uri, NetworkCredential?>? credentialResolver = null)
    {
        _credentialResolver = credentialResolver;
    }

    public IRangeSource Create(DownloadItem item, DownloadEngineOptions options)
    {
        var uri = new Uri(item.Url);
        var credential = ResolveCredential(item, uri);
        if (uri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase))
        {
            return new FtpRangeSource(item, options, credential);
        }
        return new HttpRangeSource(CreateHttpClient(options, credential), item, options);
    }

    public NetworkCredential? ResolveCredential(DownloadItem item, Uri uri)
    {
        if (!string.IsNullOrEmpty(item.Username))
        {
            return new NetworkCredential(item.Username, item.Password ?? string.Empty);
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            return new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }
        return _credentialResolver?.Invoke(uri);
    }

    public static HttpClient CreateHttpClient(DownloadEngineOptions options, NetworkCredential? credential)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = options.Timeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 64,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            PreAuthenticate = credential is not null
        };
        if (credential is not null)
        {
            handler.Credentials = credential;
        }
        if (options.Proxy is not null)
        {
            handler.UseProxy = true;
            handler.Proxy = options.Proxy;
        }
        else
        {
            handler.UseProxy = options.UseSystemProxy;
        }
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
    }
}
