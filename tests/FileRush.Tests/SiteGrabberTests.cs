using System.Text;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class SiteGrabberTests
{
    [Fact]
    public void ExtractLinks_ResolvesAndFilters()
    {
        var html = """
            <html><body>
            <a href="a.jpg">a</a>
            <a href="/root.zip">r</a>
            <a href="https://other.com/x.mp3">o</a>
            <a href="javascript:void(0)">j</a>
            <a href="mailto:a@b.c">m</a>
            <a href="#top">t</a>
            <img src='img.png'>
            <a href=bare.gif>b</a>
            <a href="a.jpg#frag">dup</a>
            <a href="data:image/png;base64,xx">d</a>
            </body></html>
            """;
        var links = SiteGrabber.ExtractLinks(html, new Uri("http://example.com/dir/page.html")).Select(u => u.AbsoluteUri).ToList();
        Assert.Contains("http://example.com/dir/a.jpg", links);
        Assert.Contains("http://example.com/root.zip", links);
        Assert.Contains("https://other.com/x.mp3", links);
        Assert.Contains("http://example.com/dir/img.png", links);
        Assert.Contains("http://example.com/dir/bare.gif", links);
        Assert.DoesNotContain(links, l => l.StartsWith("javascript", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(links, l => l.StartsWith("mailto", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(links, l => l.StartsWith("data:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, links.Count(l => l == "http://example.com/dir/a.jpg"));
        Assert.Equal(5, links.Count);
    }

    [Fact]
    public void ExtractLinks_EmptyHtml()
    {
        Assert.Empty(SiteGrabber.ExtractLinks(string.Empty, new Uri("http://example.com/")));
    }

    [Fact]
    public async Task GrabAsync_CollectsFilesAcrossPages()
    {
        using var server = new TestHttpServer();
        var index = """
            <html><body>
            <a href="a.jpg">a</a>
            <a href="/files/b.zip">b</a>
            <img src="img/c.png">
            <a href="sub.html">sub</a>
            <a href="http://other.invalid/x.zip">external</a>
            <a href="skip.txt">skip</a>
            </body></html>
            """;
        var sub = """
            <html><body>
            <a href="d.pdf">d</a>
            <a href="deeper.html">deeper</a>
            </body></html>
            """;
        var deeper = """
            <html><body><a href="e.zip">e</a></body></html>
            """;
        var start = server.AddFile("/index.html", Encoding.UTF8.GetBytes(index), contentType: "text/html; charset=utf-8");
        server.AddFile("/sub.html", Encoding.UTF8.GetBytes(sub), contentType: "text/html");
        server.AddFile("/deeper.html", Encoding.UTF8.GetBytes(deeper), contentType: "text/html");
        var options = new SiteGrabberOptions
        {
            StartUrl = start,
            MaxDepth = 1,
            SameHostOnly = true,
            Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "jpg", "zip", "png", "pdf" }
        };
        var reports = new List<SiteGrabberProgress>();
        var progress = new Progress<SiteGrabberProgress>(reports.Add);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var files = await new SiteGrabber().GrabAsync(options, progress, cts.Token);

        Assert.Contains(server.UrlFor("a.jpg"), files);
        Assert.Contains(server.UrlFor("files/b.zip"), files);
        Assert.Contains(server.UrlFor("img/c.png"), files);
        Assert.Contains(server.UrlFor("d.pdf"), files);
        Assert.DoesNotContain(server.UrlFor("e.zip"), files);
        Assert.DoesNotContain(files, f => f.Contains("other.invalid"));
        Assert.DoesNotContain(files, f => f.EndsWith("skip.txt"));
        Assert.Equal(4, files.Count);
    }

    [Fact]
    public async Task GrabAsync_InvalidStartUrl_ReturnsEmpty()
    {
        var files = await new SiteGrabber().GrabAsync(new SiteGrabberOptions { StartUrl = "not a url" }, null, CancellationToken.None);
        Assert.Empty(files);
    }

    [Fact]
    public async Task GrabAsync_RespectsMaxPages()
    {
        using var server = new TestHttpServer();
        var pages = new StringBuilder("<html><body>");
        for (int i = 0; i < 10; i++)
        {
            pages.Append($"<a href=\"p{i}.html\">p</a>");
            server.AddFile($"/p{i}.html", Encoding.UTF8.GetBytes($"<html><body><a href=\"f{i}.zip\">f</a></body></html>"), contentType: "text/html");
        }
        pages.Append("</body></html>");
        var start = server.AddFile("/index.html", Encoding.UTF8.GetBytes(pages.ToString()), contentType: "text/html");
        var options = new SiteGrabberOptions { StartUrl = start, MaxDepth = 2, MaxPages = 3, Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zip" } };

        var files = await new SiteGrabber().GrabAsync(options, null, CancellationToken.None);

        Assert.True(files.Count <= 3);
        Assert.True(files.Count >= 1);
    }
}
