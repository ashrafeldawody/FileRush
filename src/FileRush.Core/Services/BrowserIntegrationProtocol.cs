using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileRush.Core.Models;
using FileRush.Core.Utils;

namespace FileRush.Core.Services;

public static class NativeMessageCodec
{
    public const int MaxMessageSize = 4 * 1024 * 1024;

    public static string? Read(Stream input)
    {
        Span<byte> header = stackalloc byte[4];
        if (!ReadExactly(input, header)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxMessageSize) throw new InvalidDataException($"Invalid native message length {length}.");
        var body = new byte[length];
        if (!ReadExactly(input, body)) return null;
        return Encoding.UTF8.GetString(body);
    }

    public static void Write(Stream output, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        output.Write(header);
        output.Write(body);
        output.Flush();
    }

    private static bool ReadExactly(Stream input, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = input.Read(buffer[total..]);
            if (read <= 0) return false;
            total += read;
        }
        return true;
    }
}

public sealed class BrowserMessage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Type { get; set; } = string.Empty;
    public string? Url { get; set; }
    public List<string>? Urls { get; set; }
    public string? Referrer { get; set; }
    public string? Cookie { get; set; }
    public string? UserAgent { get; set; }
    public string? FileName { get; set; }
    public string? Mime { get; set; }
    public long Size { get; set; } = -1;
    public bool Start { get; set; }

    public static BrowserMessage? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var trimmed = json.TrimStart();
        if (!trimmed.StartsWith('{')) return null;
        try
        {
            var message = JsonSerializer.Deserialize<BrowserMessage>(trimmed, Options);
            if (message is null || string.IsNullOrWhiteSpace(message.Type)) return null;
            message.Type = message.Type.Trim().ToLowerInvariant();
            return message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> AllUrls()
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Urls is not null)
        {
            foreach (var url in Urls)
            {
                if (UrlUtils.IsDownloadableUrl(url, out var uri) && seen.Add(uri.AbsoluteUri)) list.Add(uri.AbsoluteUri);
            }
        }
        if (UrlUtils.IsDownloadableUrl(Url, out var single) && seen.Add(single.AbsoluteUri)) list.Add(single.AbsoluteUri);
        return list;
    }

    public NewDownloadRequest ToRequest(string? url = null)
    {
        var request = new NewDownloadRequest
        {
            Url = url ?? Url ?? string.Empty,
            Referrer = string.IsNullOrWhiteSpace(Referrer) ? null : Referrer,
            UserAgent = string.IsNullOrWhiteSpace(UserAgent) ? null : UserAgent,
            FileName = string.IsNullOrWhiteSpace(FileName) ? null : FileNameResolver.Sanitize(FileName)
        };
        if (!string.IsNullOrWhiteSpace(Cookie)) request.Headers["Cookie"] = Cookie;
        return request;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

public static class NativeHostInvocation
{
    public static bool IsNativeHost(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Equals("--native-host", StringComparison.OrdinalIgnoreCase)) return true;
            if (arg.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)) return true;
            if (arg.StartsWith("--parent-window", StringComparison.OrdinalIgnoreCase)) return true;
            if (arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count && args[i + 1].Contains('@')) return true;
        }
        return false;
    }
}

public static class BrowserIntegrationInfo
{
    public const string HostName = "com.filerush.host";
    public const string HostDescription = "File Rush browser integration host";
    public const string ChromeExtensionId = "gpmhgpppbgfloobhjejdhaoajaielbpd";
    public const string ChromeExtensionKey = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAru3UzIncXwucrkwuTzATZ2DJBGUxaOihX35LV1oOlXyPHnJgLiUDD5alJlPZ3nbgGD7p+yyA5UEyF6O9CyXDz882EUGkXwpEiGqnHOc4MUG80XSz11/B0lHhHBgxy80NYiVmJ8zGe6DsZ3SEjX0QKp8mP71Alz8eAKdgAwsqVUkSbXlak+HDaFSG5cs5m1V13I4IirpPTY8L+HGgxIvXAlQkeTCjq7Sz0IyO8xsDqOlFeJnQUDcW6QQoVl8EUOhSWiiPtO892Npv9GrfSepn9RX3g1qxxZXJA2tbw9AntAB0owxwQ87JRTPug2LDUC+5yf3YEzIInOJiPhZWxXz1LQIDAQAB";
    public const string FirefoxExtensionId = "filerush@filerush.local";

    public static string ChromeExtensionIdFromKey(string base64PublicKey)
    {
        var der = Convert.FromBase64String(base64PublicKey);
        var hash = SHA256.HashData(der);
        var sb = new StringBuilder(32);
        foreach (var b in hash.AsSpan(0, 16))
        {
            sb.Append((char)('a' + (b >> 4)));
            sb.Append((char)('a' + (b & 0x0F)));
        }
        return sb.ToString();
    }

    public static string BuildChromeManifest(string hostPath)
    {
        var manifest = new Dictionary<string, object>
        {
            ["name"] = HostName,
            ["description"] = HostDescription,
            ["path"] = hostPath,
            ["type"] = "stdio",
            ["allowed_origins"] = new[] { $"chrome-extension://{ChromeExtensionId}/" }
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildFirefoxManifest(string hostPath)
    {
        var manifest = new Dictionary<string, object>
        {
            ["name"] = HostName,
            ["description"] = HostDescription,
            ["path"] = hostPath,
            ["type"] = "stdio",
            ["allowed_extensions"] = new[] { FirefoxExtensionId }
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    public static readonly IReadOnlyDictionary<string, string> ChromiumRegistryKeys = new Dictionary<string, string>
    {
        ["chrome"] = @"Software\Google\Chrome\NativeMessagingHosts",
        ["edge"] = @"Software\Microsoft\Edge\NativeMessagingHosts",
        ["brave"] = @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts",
        ["chromium"] = @"Software\Chromium\NativeMessagingHosts",
        ["vivaldi"] = @"Software\Vivaldi\NativeMessagingHosts"
    };

    public const string FirefoxRegistryKey = @"Software\Mozilla\NativeMessagingHosts";
}
