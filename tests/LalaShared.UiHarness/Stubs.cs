namespace Lalalazy.Telemetry;

// Game/runtime boundary only. The report popup reads the telemetry identity from the hub and files the report through
// FileReport; everything else the real DalamudTelemetry does (log sink, chat, game state, addon dump) is not compiled.
internal sealed class DalamudTelemetry
{
    /// <summary>What FileReport returns; null models a refused or failed write (the popup then stays open).</summary>
    public string? NextReportId { get; set; } = "7F3A9C21";

    public int FileCount { get; private set; }

    public string LastText { get; private set; } = string.Empty;

    public TelemetryHub Hub { get; }

    public DalamudTelemetry(string displayName)
    {
        Hub = new TelemetryHub(
            new TelemetryIdentity { Plugin = displayName, DisplayName = displayName, Version = "0.5.5.0" },
            new NullSink());
    }

    public string? FileReport(string text)
    {
        FileCount++;
        LastText = text;
        return NextReportId;
    }

    private sealed class NullSink : ITelemetrySink
    {
        public void Write(TelemetryLevel level, string text) { }

        public void Notice(string text) { }
    }
}
