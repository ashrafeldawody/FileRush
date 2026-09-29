namespace FileRush.Core.Engine;

public sealed class RangeNotSupportedException : Exception
{
    public RangeNotSupportedException() : base("The server does not support resuming (ranged requests).") { }
}

public sealed class RemoteFileChangedException : Exception
{
    public RemoteFileChangedException() : base("The remote file has changed since the download was started.") { }
}

public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class ConnectionYieldException : Exception
{
    public ConnectionYieldException() : base("The connection was released because the server limits simultaneous connections.") { }
}
