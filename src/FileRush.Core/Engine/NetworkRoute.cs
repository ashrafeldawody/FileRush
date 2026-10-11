using System.Net;

namespace FileRush.Core.Engine;

public sealed record NetworkRoute(string Id, string Name, IReadOnlyList<IPAddress> Addresses);
