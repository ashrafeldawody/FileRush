using System.Diagnostics;
using System.Security.Cryptography;
using FileRush.Core.Engine;

namespace FileRush.Tests;

public static class TestUtil
{
    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileRushTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch
        {
        }
    }

    public static byte[] RandomBytes(int count, int seed = 1234)
    {
        var bytes = new byte[count];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string? what = null)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > timeout) throw new TimeoutException("Timed out waiting for " + (what ?? "condition"));
            await Task.Delay(20);
        }
    }

    public static Task WaitUntilAsync(Func<bool> condition, string? what = null) => WaitUntilAsync(condition, TimeSpan.FromSeconds(20), what);

    public static DownloadEngineOptions Options(string? tempDirectory, int connections = 4)
    {
        return new DownloadEngineOptions
        {
            MaxConnections = connections,
            Timeout = TimeSpan.FromSeconds(10),
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(50),
            MinSegmentSize = 16 * 1024,
            BufferSize = 32 * 1024,
            TempDirectory = tempDirectory,
            ProgressInterval = TimeSpan.FromMilliseconds(50),
            StateSaveInterval = TimeSpan.FromMilliseconds(200)
        };
    }
}
