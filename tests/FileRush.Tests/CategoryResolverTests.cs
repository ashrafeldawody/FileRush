using FileRush.Core.Models;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class CategoryResolverTests
{
    private static readonly List<Category> Defaults = Category.CreateDefaults("C:\\Downloads");

    [Theory]
    [InlineData("movie.mkv", "Video")]
    [InlineData("song.MP3", "Music")]
    [InlineData("setup.exe", "Programs")]
    [InlineData("paper.pdf", "Documents")]
    [InlineData("archive.7z", "Compressed")]
    [InlineData("unknown.xyz", "General")]
    [InlineData("noextension", "General")]
    public void Resolve_ByExtension(string fileName, string expected)
    {
        Assert.Equal(expected, CategoryResolver.Resolve(Defaults, fileName).Name);
    }

    [Fact]
    public void Resolve_FromUrlIgnoresQuery()
    {
        Assert.Equal("Compressed", CategoryResolver.Resolve(Defaults, "http://x.com/a/b.zip?token=abc.mp3").Name);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.NotNull(CategoryResolver.Find(Defaults, "video"));
        Assert.Null(CategoryResolver.Find(Defaults, "nope"));
    }

    [Fact]
    public void Defaults_HaveDirectoriesUnderRoot()
    {
        Assert.All(Defaults.Where(c => c.Name != "General"), c => Assert.StartsWith("C:\\Downloads", c.SaveDirectory));
        Assert.Equal("C:\\Downloads", Defaults.First(c => c.Name == "General").SaveDirectory);
    }

    [Fact]
    public void Resolve_EmptyList_ReturnsGeneral()
    {
        Assert.Equal("General", CategoryResolver.Resolve(Array.Empty<Category>(), "a.zip").Name);
    }
}
