using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class BatchUrlExpanderTests
{
    [Fact]
    public void Numbers_WithPadding()
    {
        var urls = BatchUrlExpander.Expand("http://x/f*.jpg", "1", "3", false, 3);
        Assert.Equal(new[] { "http://x/f001.jpg", "http://x/f002.jpg", "http://x/f003.jpg" }, urls);
    }

    [Fact]
    public void Numbers_WithoutPadding()
    {
        var urls = BatchUrlExpander.Expand("http://x/*.jpg", "9", "11", false, 0);
        Assert.Equal(new[] { "http://x/9.jpg", "http://x/10.jpg", "http://x/11.jpg" }, urls);
    }

    [Fact]
    public void Numbers_ReversedRangeIsSwapped()
    {
        var urls = BatchUrlExpander.Expand("http://x/*.jpg", "3", "1", false, 0);
        Assert.Equal(3, urls.Count);
        Assert.Equal("http://x/1.jpg", urls[0]);
    }

    [Fact]
    public void Letters_LowerAndUpper()
    {
        Assert.Equal(new[] { "http://x/a.txt", "http://x/b.txt", "http://x/c.txt" }, BatchUrlExpander.Expand("http://x/*.txt", "a", "c", true, 0));
        Assert.Equal(new[] { "http://x/A.txt", "http://x/B.txt" }, BatchUrlExpander.Expand("http://x/*.txt", "A", "B", true, 0));
    }

    [Fact]
    public void NoWildcard_ReturnsSingle()
    {
        var urls = BatchUrlExpander.Expand("http://x/file.zip", "1", "5", false, 0);
        Assert.Single(urls);
        Assert.Equal("http://x/file.zip", urls[0]);
    }

    [Fact]
    public void Empty_ReturnsNothing()
    {
        Assert.Empty(BatchUrlExpander.Expand("   ", "1", "5", false, 0));
    }

    [Fact]
    public void Brackets_NumericWithPaddingPreserved()
    {
        var urls = BatchUrlExpander.ExpandBrackets("http://x/[01-03].jpg");
        Assert.Equal(new[] { "http://x/01.jpg", "http://x/02.jpg", "http://x/03.jpg" }, urls);
    }

    [Fact]
    public void Brackets_Letters()
    {
        var urls = BatchUrlExpander.ExpandBrackets("http://x/[a-c].jpg");
        Assert.Equal(new[] { "http://x/a.jpg", "http://x/b.jpg", "http://x/c.jpg" }, urls);
    }

    [Fact]
    public void Brackets_Nested()
    {
        var urls = BatchUrlExpander.ExpandBrackets("http://x/[a-b]/[1-2].jpg");
        Assert.Equal(new[] { "http://x/a/1.jpg", "http://x/a/2.jpg", "http://x/b/1.jpg", "http://x/b/2.jpg" }, urls);
    }

    [Fact]
    public void Brackets_None_ReturnsSingle()
    {
        var urls = BatchUrlExpander.ExpandBrackets("http://x/plain.jpg");
        Assert.Single(urls);
    }

    [Fact]
    public void Cap_NotExceeded()
    {
        var urls = BatchUrlExpander.Expand("http://x/*.jpg", "1", "1000000", false, 0);
        Assert.Equal(BatchUrlExpander.MaxUrls, urls.Count);
    }
}
