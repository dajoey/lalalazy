using System;

namespace LazyRetainerLive;

// Minimal stand-ins so HttpServer.cs compiles without Dalamud. Only the members
// HttpServer reads are present.
internal sealed class RetainerLiveService
{
    public CharInfo? Current => null;
}

internal static class Plugin
{
    internal sealed class LogStub
    {
        public void Information(string message) { }
        public void Warning(Exception ex, string message) { }
    }

    public static readonly LogStub Log = new();
}
