using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FileRush.Core.Services;

namespace FileRush.App.Infrastructure;

public static class NativeHost
{
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(20);

    public static int Run(string[] args)
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        while (true)
        {
            string? json;
            try
            {
                json = NativeMessageCodec.Read(input);
            }
            catch
            {
                break;
            }
            if (json is null) break;
            var response = Handle(json);
            try
            {
                NativeMessageCodec.Write(output, response);
            }
            catch
            {
                break;
            }
        }
        return 0;
    }

    public static string Handle(string json)
    {
        var message = BrowserMessage.Parse(json);
        if (message is null) return Error("Invalid message");
        if (message.Type == "ping")
        {
            return JsonSerializer.Serialize(new { ok = true, running = SingleInstance.IsRunning() });
        }
        if (message.Type is not ("download" or "batch" or "show")) return Error("Unknown message type: " + message.Type);
        if (message.Type != "show" && message.AllUrls().Count == 0) return Error("No downloadable URL in the message");
        if (SingleInstance.SendToRunning(json)) return Ok();
        if (!StartApplication()) return Error("Could not start File Rush");
        var deadline = DateTime.UtcNow + StartupWait;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(400);
            if (SingleInstance.SendToRunning(json)) return Ok();
        }
        return Error("File Rush did not respond after starting");
    }

    private static bool StartApplication()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            Process.Start(new ProcessStartInfo(exe, "--minimized") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Ok() => "{\"ok\":true}";

    private static string Error(string error) => JsonSerializer.Serialize(new { ok = false, error });
}
