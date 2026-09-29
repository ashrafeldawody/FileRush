namespace FileRush.Core.Models;

public enum ProxyMode
{
    None,
    System,
    Manual
}

public enum ProxyKind
{
    Http,
    Socks4,
    Socks5
}

public sealed class ProxySettings
{
    public ProxyMode Mode { get; set; } = ProxyMode.System;
    public ProxyKind Kind { get; set; } = ProxyKind.Http;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Bypass { get; set; } = string.Empty;
    public bool UseFtpPassive { get; set; } = true;
}
