// Shared source (NOT a shared DLL) - compiled into every lalalazy plugin.
using System;
using System.IO;
// using System.Text.Json;  -- deliberately NOT imported: PvPSolver declares a global
// `using Newtonsoft.Json`, and importing this namespace makes `JsonSerializer` ambiguous
// (CS0104) in that fork. The two call sites below are fully qualified instead.
using Dalamud.Plugin;

namespace Lalalazy.Changelog;

/// <summary>Where "the last version whose changelog the player has seen" is persisted.</summary>
public interface IChangelogSeenStore
{
    string? Get();
    void Set(string version);
}

/// <summary>
/// Backed by the plugin's own Configuration (a string property + the plugin's save routine).
/// Use for original plugins where we own Configuration.cs.
/// </summary>
public sealed class DelegateSeenStore : IChangelogSeenStore
{
    private readonly Func<string?> _get;
    private readonly Action<string> _set;
    public DelegateSeenStore(Func<string?> get, Action<string> set) { _get = get; _set = set; }
    public string? Get() => _get();
    public void Set(string version) => _set(version);
}

/// <summary>
/// Tiny sidecar json (`&lt;ConfigDirectory&gt;/lalachangelog.json`) for forks whose Configuration
/// class is upstream-owned - keeps nightly upstream merges conflict-free.
/// </summary>
public sealed class SidecarSeenStore : IChangelogSeenStore
{
    private sealed class State { public string? LastSeenChangelogVersion { get; set; } }

    private readonly string _path;
    private readonly Action<Exception, string>? _onError;

    public SidecarSeenStore(IDalamudPluginInterface pi, Action<Exception, string>? onError = null)
    {
        _path = Path.Combine(pi.ConfigDirectory.FullName, "lalachangelog.json");
        _onError = onError;
    }

    public string? Get()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            // Fully qualified on purpose: forks (PvPSolver) declare a global `using Newtonsoft.Json`,
            // which makes the bare name ambiguous (CS0104). Do not shorten this.
            var s = System.Text.Json.JsonSerializer.Deserialize<State>(File.ReadAllText(_path));
            return s?.LastSeenChangelogVersion;
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex, "[LalaChangelog] sidecar read failed");
            return null;
        }
    }

    public void Set(string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(new State { LastSeenChangelogVersion = version }));
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex, "[LalaChangelog] sidecar write failed");
        }
    }
}
