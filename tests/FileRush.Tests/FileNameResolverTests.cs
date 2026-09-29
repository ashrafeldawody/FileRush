using FileRush.Core.Utils;
using Xunit;

namespace FileRush.Tests;

public sealed class FileNameResolverTests
{
    [Fact]
    public void Resolve_UsesLastUrlSegment()
    {
        Assert.Equal("file.zip", FileNameResolver.Resolve("http://x.com/a/b/file.zip?dl=1", null));
    }

    [Fact]
    public void Resolve_UnescapesUrlSegment()
    {
        Assert.Equal("my file.pdf", FileNameResolver.Resolve("http://x.com/docs/my%20file.pdf", null));
    }

    [Fact]
    public void Resolve_FallsBackToIndexHtml()
    {
        Assert.Equal("index.html", FileNameResolver.Resolve("http://x.com/", null));
    }

    [Fact]
    public void Resolve_AddsExtensionFromContentType()
    {
        Assert.Equal("download.pdf", FileNameResolver.Resolve("http://x.com/download?id=5", null, "application/pdf; charset=binary"));
    }

    [Fact]
    public void Resolve_PrefersQuotedContentDisposition()
    {
        Assert.Equal("my file.zip", FileNameResolver.Resolve("http://x.com/x", "attachment; filename=\"my file.zip\""));
    }

    [Fact]
    public void Resolve_PlainContentDisposition()
    {
        Assert.Equal("plain.zip", FileNameResolver.Resolve("http://x.com/x", "attachment; filename=plain.zip"));
    }

    [Fact]
    public void Resolve_FileNameStarIsDecoded()
    {
        Assert.Equal("naïve file.txt", FileNameResolver.Resolve("http://x.com/x", "attachment; filename*=UTF-8''na%C3%AFve%20file.txt"));
    }

    [Fact]
    public void FromContentDisposition_ReturnsNullWhenMissing()
    {
        Assert.Null(FileNameResolver.FromContentDisposition(null));
        Assert.Null(FileNameResolver.FromContentDisposition("inline"));
    }

    [Fact]
    public void Sanitize_ReplacesInvalidCharacters()
    {
        var result = FileNameResolver.Sanitize("a<b>:c|d?e*f");
        Assert.DoesNotContain('<', result);
        Assert.DoesNotContain(':', result);
        Assert.DoesNotContain('*', result);
        Assert.StartsWith("a_b_", result);
    }

    [Fact]
    public void Sanitize_TrimsTrailingDotsAndHandlesEmpty()
    {
        Assert.Equal("name", FileNameResolver.Sanitize("name..."));
        Assert.Equal("download", FileNameResolver.Sanitize("   "));
    }

    [Fact]
    public void Sanitize_TruncatesVeryLongNames()
    {
        var name = new string('a', 300) + ".zip";
        var result = FileNameResolver.Sanitize(name);
        Assert.True(result.Length <= 200);
        Assert.EndsWith(".zip", result);
    }

    [Fact]
    public void MakeUnique_AppendsCounter()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("C:\\d", "file.zip"),
            Path.Combine("C:\\d", "file_2.zip")
        };
        Assert.Equal("file_3.zip", FileNameResolver.MakeUnique("C:\\d", "file.zip", taken.Contains));
        Assert.Equal("other.zip", FileNameResolver.MakeUnique("C:\\d", "other.zip", taken.Contains));
    }

    [Fact]
    public void ExtensionForContentType_KnownTypes()
    {
        Assert.Equal(".pdf", FileNameResolver.ExtensionForContentType("application/pdf"));
        Assert.Equal(".mp4", FileNameResolver.ExtensionForContentType("video/mp4; codecs=avc1"));
        Assert.Null(FileNameResolver.ExtensionForContentType("application/octet-stream"));
        Assert.Null(FileNameResolver.ExtensionForContentType("application/x-unknown"));
    }

    [Fact]
    public void FromUrl_ReturnsEmptyForInvalid()
    {
        Assert.Equal(string.Empty, FileNameResolver.FromUrl("not a url"));
    }
}
