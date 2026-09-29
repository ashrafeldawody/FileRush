using System.IO;
using System.IO.Pipes;

namespace FileRush.App.Infrastructure;

public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "FileRush.SingleInstance.Mutex";
    private const string PipeName = "FileRush.SingleInstance.Pipe";
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }
        return createdNew;
    }

    public static bool IsRunning()
    {
        try
        {
            using var existing = Mutex.OpenExisting(MutexName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartServer(Action<string> onMessage)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(message)) onMessage(message.Trim());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    public static bool SendToRunning(string message, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client);
            writer.Write(message);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
        }
        _mutex?.Dispose();
    }
}
