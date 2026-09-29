using System.Text;
using System.Text.Json;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public class BrowserIntegrationTests
{
    [Fact]
    public void NativeMessageCodecRoundTripsMessages()
    {
        var stream = new MemoryStream();
        NativeMessageCodec.Write(stream, "{\"type\":\"ping\"}");
        NativeMessageCodec.Write(stream, "{\"type\":\"download\",\"url\":\"https://example.com/a.zip\"}");
        stream.Position = 0;
        Assert.Equal("{\"type\":\"ping\"}", NativeMessageCodec.Read(stream));
        Assert.Equal("{\"type\":\"download\",\"url\":\"https://example.com/a.zip\"}", NativeMessageCodec.Read(stream));
        Assert.Null(NativeMessageCodec.Read(stream));
    }

    [Fact]
    public void NativeMessageCodecUsesLittleEndianLengthPrefix()
    {
        var stream = new MemoryStream();
        NativeMessageCodec.Write(stream, "{}");
        var bytes = stream.ToArray();
        Assert.Equal(new byte[] { 2, 0, 0, 0 }, bytes[..4]);
        Assert.Equal("{}", Encoding.UTF8.GetString(bytes[4..]));
    }

    [Fact]
    public void NativeMessageCodecRejectsOversizedMessages()
    {
        var stream = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3 });
        Assert.Throws<InvalidDataException>(() => NativeMessageCodec.Read(stream));
    }

    [Fact]
    public void ParsesDownloadMessageCaseInsensitively()
    {
        var message = BrowserMessage.Parse("{\"Type\":\"Download\",\"URL\":\"https://example.com/file.zip\",\"referrer\":\"https://example.com/\",\"cookie\":\"a=1; b=2\",\"userAgent\":\"UA\",\"fileName\":\"na:me.zip\",\"size\":\"1234\"}");
        Assert.NotNull(message);
        Assert.Equal("download", message!.Type);
        Assert.Equal("https://example.com/file.zip", message.Url);
        Assert.Equal(1234, message.Size);
        var request = message.ToRequest();
        Assert.Equal("https://example.com/file.zip", request.Url);
        Assert.Equal("https://example.com/", request.Referrer);
        Assert.Equal("UA", request.UserAgent);
        Assert.Equal("a=1; b=2", request.Headers["Cookie"]);
        Assert.Equal("na_me.zip", request.FileName);
    }

    [Fact]
    public void ParseRejectsNonJsonAndMissingType()
    {
        Assert.Null(BrowserMessage.Parse("https://example.com/file.zip"));
        Assert.Null(BrowserMessage.Parse("{\"url\":\"https://example.com/file.zip\"}"));
        Assert.Null(BrowserMessage.Parse("{not json"));
        Assert.Null(BrowserMessage.Parse(""));
    }

    [Fact]
    public void AllUrlsMergesBatchAndSingleWithoutDuplicates()
    {
        var message = BrowserMessage.Parse("{\"type\":\"batch\",\"url\":\"https://example.com/a.zip\",\"urls\":[\"https://example.com/b.zip\",\"https://example.com/a.zip\",\"javascript:void(0)\",\"not a url\"]}");
        var urls = message!.AllUrls();
        Assert.Equal(new[] { "https://example.com/b.zip", "https://example.com/a.zip" }, urls);
    }

    [Fact]
    public void RequestWithoutCookieHasNoCookieHeader()
    {
        var message = BrowserMessage.Parse("{\"type\":\"download\",\"url\":\"https://example.com/a.zip\",\"cookie\":\"\"}");
        var request = message!.ToRequest();
        Assert.False(request.Headers.ContainsKey("Cookie"));
        Assert.Null(request.FileName);
    }

    [Theory]
    [InlineData(true, "chrome-extension://gpmhgpppbgfloobhjejdhaoajaielbpd/")]
    [InlineData(true, "chrome-extension://gpmhgpppbgfloobhjejdhaoajaielbpd/", "--parent-window=1234")]
    [InlineData(true, "--native-host")]
    [InlineData(true, "C:\\path\\com.filerush.host.firefox.json", "filerush@filerush.local")]
    [InlineData(false, "https://example.com/file.zip")]
    [InlineData(false, "--minimized")]
    [InlineData(false)]
    public void DetectsNativeHostInvocation(bool expected, params string[] args)
    {
        Assert.Equal(expected, NativeHostInvocation.IsNativeHost(args));
    }

    [Fact]
    public void ExtensionIdMatchesPublicKey()
    {
        Assert.Equal(BrowserIntegrationInfo.ChromeExtensionId, BrowserIntegrationInfo.ChromeExtensionIdFromKey(BrowserIntegrationInfo.ChromeExtensionKey));
        Assert.Matches("^[a-p]{32}$", BrowserIntegrationInfo.ChromeExtensionId);
    }

    [Fact]
    public void HostManifestsReferenceExtensionAndHostPath()
    {
        var chrome = JsonDocument.Parse(BrowserIntegrationInfo.BuildChromeManifest(@"C:\Apps\FileRush.exe")).RootElement;
        Assert.Equal(BrowserIntegrationInfo.HostName, chrome.GetProperty("name").GetString());
        Assert.Equal(@"C:\Apps\FileRush.exe", chrome.GetProperty("path").GetString());
        Assert.Equal("stdio", chrome.GetProperty("type").GetString());
        Assert.Equal($"chrome-extension://{BrowserIntegrationInfo.ChromeExtensionId}/", chrome.GetProperty("allowed_origins")[0].GetString());

        var firefox = JsonDocument.Parse(BrowserIntegrationInfo.BuildFirefoxManifest(@"C:\Apps\FileRush.exe")).RootElement;
        Assert.Equal(BrowserIntegrationInfo.FirefoxExtensionId, firefox.GetProperty("allowed_extensions")[0].GetString());
        Assert.False(firefox.TryGetProperty("allowed_origins", out _));
    }

    [Fact]
    public void ShippedExtensionManifestUsesTheRegisteredKeyAndId()
    {
        var folder = FindExtensionFolder();
        Assert.NotNull(folder);
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder!, "manifest.json"))).RootElement;
        Assert.Equal(3, manifest.GetProperty("manifest_version").GetInt32());
        Assert.Equal(BrowserIntegrationInfo.ChromeExtensionKey, manifest.GetProperty("key").GetString());
        Assert.Contains("nativeMessaging", manifest.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("background.js", manifest.GetProperty("background").GetProperty("service_worker").GetString());
        var firefox = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder!, "manifest.firefox.json"))).RootElement;
        Assert.Equal(BrowserIntegrationInfo.FirefoxExtensionId, firefox.GetProperty("browser_specific_settings").GetProperty("gecko").GetProperty("id").GetString());
        foreach (var file in new[] { "background.js", "content.js", "popup.html", "popup.js", "options.html", "options.js", "icons/icon128.png" })
        {
            Assert.True(File.Exists(Path.Combine(folder!, file)), file);
        }
        Assert.Contains("\"" + BrowserIntegrationInfo.HostName + "\"", File.ReadAllText(Path.Combine(folder!, "background.js")));
    }

    private static string? FindExtensionFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "browser-extension");
            if (File.Exists(Path.Combine(candidate, "manifest.json"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
