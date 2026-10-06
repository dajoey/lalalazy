using System;

namespace GluttonyCombo.Data;

/// <summary>STAGE-1 STUB (tests first): every method returns nothing so the new harness cases run red.</summary>
internal static class LeaseTelemetryFormat
{
    public const string Prefix = "LS|";
    public const int MaxLineLength = 200;

    public static string Register(long unixMs, string plugin, string leaseId) => string.Empty;
    public static string State(long unixMs, string plugin, bool on, bool stored) => string.Empty;
    public static string Config(long unixMs, string plugin, string option, int value, int? stored) => string.Empty;
    public static string Release(long unixMs, string plugin, string reason) => string.Empty;

    internal sealed class ChangeGate
    {
        public bool ShouldEmit(string plugin, string key, string value) => false;
        public void Forget(string plugin) { }
    }
}
