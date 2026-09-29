using FileRush.Core.Models;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class ClipboardUrlDetectorTests
{
    private static AppSettings Settings() => AppSettings.CreateDefault();

    [Fact]
    public void MonitoredExtension_IsCaptured()
    {
        Assert.True(ClipboardUrlDetector.ShouldCapture("https://example.com/files/setup.ZIP", Settings(), out var uri));
        Assert.Equal("example.com", uri.Host);
    }

    [Fact]
    public void UnmonitoredExtension_IsIgnored()
    {
        Assert.False(ClipboardUrlDetector.ShouldCapture("https://example.com/page.html", Settings(), out _));
    }

    [Fact]
    public void NonUrl_IsIgnored()
    {
        Assert.False(ClipboardUrlDetector.ShouldCapture("hello world", Settings(), out _));
        Assert.False(ClipboardUrlDetector.ShouldCapture(null, Settings(), out _));
        Assert.False(ClipboardUrlDetector.ShouldCapture("https://a.com/x.zip\nhttps://b.com/y.zip", Settings(), out _));
    }

    [Fact]
    public void MonitoringDisabled_IgnoresEverything()
    {
        var settings = Settings();
        settings.MonitorClipboard = false;
        Assert.False(ClipboardUrlDetector.ShouldCapture("https://example.com/a.zip", settings, out _));
    }

    [Fact]
    public void HostException_IsHonored()
    {
        var settings = Settings();
        settings.UrlExceptions.Add("example.com");
        Assert.False(ClipboardUrlDetector.ShouldCapture("https://example.com/a.zip", settings, out _));
        Assert.False(ClipboardUrlDetector.ShouldCapture("https://cdn.example.com/a.zip", settings, out _));
        Assert.True(ClipboardUrlDetector.ShouldCapture("https://other.com/a.zip", settings, out _));
    }

    [Fact]
    public void WildcardException_IsHonored()
    {
        var settings = Settings();
        settings.UrlExceptions.Add("*cdn.example.com*");
        Assert.False(ClipboardUrlDetector.ShouldCapture("https://cdn.example.com/a.zip", settings, out _));
        Assert.True(ClipboardUrlDetector.ShouldCapture("https://www.example.com/a.zip", settings, out _));
    }

    [Fact]
    public void CustomExtensionList_IsUsed()
    {
        var settings = Settings();
        settings.MonitoredExtensions = "abc, def";
        Assert.True(ClipboardUrlDetector.ShouldCapture("http://x.com/f.abc", settings, out _));
        Assert.False(ClipboardUrlDetector.ShouldCapture("http://x.com/f.zip", settings, out _));
    }

    [Fact]
    public void WildcardMatch_Works()
    {
        Assert.True(ClipboardUrlDetector.WildcardMatch("http://a.b.com/x", "http://*.b.com/*"));
        Assert.False(ClipboardUrlDetector.WildcardMatch("http://a.c.com/x", "http://*.b.com/*"));
    }
}
