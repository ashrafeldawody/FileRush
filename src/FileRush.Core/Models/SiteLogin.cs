namespace FileRush.Core.Models;

public sealed class SiteLogin
{
    public string Host { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public bool Matches(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(Host)) return false;
        var host = Host.Trim();
        if (host.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(host, UriKind.Absolute, out var u)) host = u.Host;
        return uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }
}
