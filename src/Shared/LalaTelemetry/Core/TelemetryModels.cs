// Shared source (NOT a shared DLL). Core/ is Dalamud-free.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Lalalazy.Telemetry;

public enum TelemetryLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Where lines and in-game notices go. The Dalamud adapter maps this to IPluginLog + chat.</summary>
public interface ITelemetrySink
{
    /// <summary>Writes one log entry verbatim (may contain '\n' only for an RP| block).</summary>
    void Write(TelemetryLevel level, string text);

    /// <summary>Shows one short player-facing notice (chat). Called at most a handful of times per session.</summary>
    void Notice(string text);
}

/// <summary>Who is writing: stamped on every ER| / RP| line.</summary>
public sealed class TelemetryIdentity
{
    /// <summary>Plugin internal name (the log context), e.g. "GluttonyCombo".</summary>
    public required string Plugin { get; init; }

    /// <summary>Name used in chat notices, e.g. "Gluttony Combo".</summary>
    public required string DisplayName { get; init; }

    public required string Version { get; init; }

    /// <summary>"testing", "production", "dev" or "unknown".</summary>
    public string Channel { get; init; } = "unknown";

    /// <summary>Short git SHA stamped at build time, or "unknown".</summary>
    public string Commit { get; init; } = BuildStamp.Unknown;

    /// <summary>Slash command the notices point at, e.g. "/gluttony".</summary>
    public string Command { get; init; } = string.Empty;
}

/// <summary>Reads the build-time stamp written by src/Shared/LalaTelemetry/LalaTelemetry.Stamp.targets.</summary>
public static class BuildStamp
{
    public const string Unknown = "unknown";

    /// <summary>The <c>[AssemblyMetadata("LalaCommit", ...)]</c> value, or "unknown" when the build had no git.</summary>
    public static string CommitOf(Assembly assembly)
    {
        try
        {
            var value = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "LalaCommit")?.Value;
            return string.IsNullOrWhiteSpace(value) ? Unknown : value.Trim();
        }
        catch
        {
            // Reflection over our own attributes cannot realistically fail; "unknown" is the documented fallback.
            return Unknown;
        }
    }

    public static string VersionOf(Assembly assembly) => assembly.GetName().Version?.ToString() ?? "0.0.0.0";
}

/// <summary>
/// Game state stamped on ER| lines (the numeric subset) and written in full in an RP| report.
/// Numeric ids on purpose: the ffxivdb review normalises digits, so numbers never split an error group.
/// </summary>
public sealed record GameContext
{
    public static GameContext Empty { get; } = new() { Source = "none" };

    public uint TerritoryId { get; init; }
    public uint ClassJobId { get; init; }
    public string JobAbbr { get; init; } = string.Empty;
    public int Level { get; init; }
    public bool InCombat { get; init; }
    public bool BoundByDuty { get; init; }
    public bool LoggedIn { get; init; }

    /// <summary>"live" (read on the framework thread just now), "cached" (last framework-thread read), or "none".</summary>
    public string Source { get; init; } = "live";

    /// <summary>Unix ms of the read the values come from.</summary>
    public long CapturedUnixMs { get; init; }

    // Report-only detail (left empty for ER| lines).
    public float? X { get; init; }
    public float? Y { get; init; }
    public float? Z { get; init; }

    /// <summary>"kind:baseId:entityId:name", or empty for no target. Player names are never written (name "pc").</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Every ConditionFlag currently set, by name.</summary>
    public IReadOnlyList<string> Conditions { get; init; } = Array.Empty<string>();
}

/// <summary>A visible addon captured for a report in lieu of a screenshot.</summary>
public sealed record AddonCapture(string Name, int ValueCount, string Values);

/// <summary>What a report is built from. Plain data so the harness can build one without the game.</summary>
public sealed class ReportInput
{
    public required string Id { get; init; }
    public required long UnixMs { get; init; }
    public required TelemetryIdentity Identity { get; init; }
    public string Text { get; init; } = string.Empty;
    public GameContext Game { get; init; } = GameContext.Empty;
    public string State { get; init; } = string.Empty;
    public string Config { get; init; } = string.Empty;
    public IReadOnlyList<RingBuffer.Entry> Ring { get; init; } = Array.Empty<RingBuffer.Entry>();
    public long RingTotal { get; init; }
    public IReadOnlyList<RingBuffer.Entry> Errors { get; init; } = Array.Empty<RingBuffer.Entry>();
    public long ErrorTotal { get; init; }
    public IReadOnlyList<string> VisibleAddons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AddonCapture> Addons { get; init; } = Array.Empty<AddonCapture>();

    /// <summary>Anything that went wrong while collecting (a section that threw); written, never fatal.</summary>
    public IReadOnlyList<string> CollectionErrors { get; init; } = Array.Empty<string>();
}
