using FileRush.Core.Models;
using FileRush.Core.Services;
using Xunit;

namespace FileRush.Tests;

public sealed class ImportExportTests
{
    private static List<DownloadItem> Items() => new()
    {
        new DownloadItem { Url = "http://a.com/1.zip", FileName = "1.zip", SaveDirectory = "C:\\d", Category = "Compressed", Description = "first", Status = DownloadStatus.Completed, TotalSize = 10 },
        new DownloadItem { Url = "http://b.com/2.mp3", FileName = "2.mp3", SaveDirectory = "C:\\m", Category = "Music", Username = "u", Password = "p", Status = DownloadStatus.Paused, TotalSize = 20 }
    };

    [Fact]
    public void UrlList_OnePerLine()
    {
        var text = ImportExport.ToUrlList(Items());
        var lines = text.Split(Environment.NewLine);
        Assert.Equal(new[] { "http://a.com/1.zip", "http://b.com/2.mp3" }, lines);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var json = ImportExport.ToJson(Items());
        var parsed = ImportExport.FromJson(json);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("http://a.com/1.zip", parsed[0].Url);
        Assert.True(parsed[0].Completed);
        Assert.Equal("first", parsed[0].Description);
        Assert.Equal("u", parsed[1].Username);
        Assert.Equal("p", parsed[1].Password);
        Assert.Equal(20, parsed[1].TotalSize);
        Assert.False(parsed[1].Completed);
    }

    [Fact]
    public void FromJson_Invalid_ReturnsEmpty()
    {
        Assert.Empty(ImportExport.FromJson("{not json"));
    }

    [Fact]
    public void FromUrlList_ExtractsUrlsFromNoise()
    {
        var text = "see http://a.com/x.zip and \"https://b.com/y.pdf\",\r\nftp://c.com/z.iso plain words\nhttp://a.com/x.zip";
        var urls = ImportExport.FromUrlList(text);
        Assert.Equal(3, urls.Count);
        Assert.Contains("http://a.com/x.zip", urls);
        Assert.Contains("https://b.com/y.pdf", urls);
        Assert.Contains("ftp://c.com/z.iso", urls);
    }

    [Fact]
    public void Import_DetectsJsonOrText()
    {
        var fromJson = ImportExport.Import(ImportExport.ToJson(Items()));
        Assert.Equal(2, fromJson.Count);
        Assert.Equal("1.zip", fromJson[0].FileName);
        var fromText = ImportExport.Import("http://a.com/x.zip\nhttp://b.com/y.zip");
        Assert.Equal(2, fromText.Count);
        Assert.Equal("http://b.com/y.zip", fromText[1].Url);
        Assert.Equal(string.Empty, fromText[1].FileName);
    }
}
