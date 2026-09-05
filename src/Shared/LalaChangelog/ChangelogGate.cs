// Shared source (NOT a shared DLL) - compiled into every lalalazy plugin.
//
// Wiring (see src/LazyFoodBuff/LazyFoodBuff/Plugin.cs for the reference):
//   _changelog = new ChangelogGate(new ChangelogGate.Options {
//       PluginAssembly = typeof(Plugin).Assembly, DisplayName = "LazyFoodBuff",
//       ChangelogPath = "src/LazyFoodBuff/CHANGELOG.md",
//       Framework = Framework, ClientState = ClientState, Condition = Condition, Log = Log,
//       Windows = _windows,
//       SeenStore = new DelegateSeenStore(() => Config.LastSeenChangelogVersion, v => { Config.LastSeenChangelogVersion = v; SaveConfig(); }),
//   });
//   ... `/<cmd> changelog` -> _changelog.ShowNow();   Dispose -> _changelog.Dispose();
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace Lalalazy.Changelog;

/// <summary>
/// Decides whether the "What's new" popup should be shown after an update, defers it until the
/// player is logged in and out of combat, and persists the last-seen version on dismiss.
/// Rules (settled, t_add3c479):
///   * LastSeen null/empty (fresh install or first build carrying the feature): record current, do NOT show.
///   * current &gt; LastSeen: show once, entries in (LastSeen, current].
///   * Never opens while InCombat / BoundByDuty / BetweenAreas / cutscene.
/// </summary>
public sealed class ChangelogGate : IDisposable
{
    public sealed class Options
    {
        public required Assembly PluginAssembly { get; init; }
        public required string DisplayName { get; init; }
        /// <summary>Repo-relative path, e.g. "src/LazyFoodBuff/CHANGELOG.md" - used for the GitHub link.</summary>
        public required string ChangelogPath { get; init; }
        public required IFramework Framework { get; init; }
        public required IClientState ClientState { get; init; }
        public required ICondition Condition { get; init; }
        public required IPluginLog Log { get; init; }
        public required WindowSystem Windows { get; init; }
        public required IChangelogSeenStore SeenStore { get; init; }
        /// <summary>Embedded resource logical name; every csproj embeds CHANGELOG.md under this name.</summary>
        public string ResourceName { get; init; } = "CHANGELOG.md";
    }

    private const string RepoBlobBase = "https://github.com/dajoey/lalalazy/blob/main/";

    private readonly Options _o;
    private readonly ChangelogWindow _window;
    private readonly List<ChangelogEntry> _all;
    private readonly Version _current;
    private Version? _previous;
    private bool _pending;
    private bool _disposed;

    public Version CurrentVersion => _current;
    public IReadOnlyList<ChangelogEntry> AllEntries => _all;
    public bool IsPending => _pending;

    public ChangelogGate(Options o)
    {
        _o = o;
        _current = ChangelogParser.NormalizeVersion(o.PluginAssembly.GetName().Version ?? new Version(0, 0, 0, 0));
        _all = ChangelogParser.Parse(ReadEmbedded(o.PluginAssembly, o.ResourceName, o.Log));

        _window = new ChangelogWindow(o.DisplayName, RepoBlobBase + o.ChangelogPath.Replace('\\', '/'), OnDismissed);
        o.Windows.AddWindow(_window);

        var seenText = o.SeenStore.Get();
        if (string.IsNullOrWhiteSpace(seenText))
        {
            // First run with the feature (or a fresh install): record, don't nag.
            o.SeenStore.Set(_current.ToString());
            o.Log.Information("[LalaChangelog] first run - recorded v{Version} as seen, popup not shown", _current);
            return;
        }

        var seen = ChangelogParser.NormalizeVersion(seenText);
        if (_current > seen)
        {
            _previous = seen;
            _pending = true;
            o.Log.Information("[LalaChangelog] update detected v{Prev} -> v{Cur}; popup queued until logged in and out of combat", seen, _current);
            o.Framework.Update += OnFrameworkUpdate;
        }
        else if (_current < seen)
        {
            // Downgrade (sideload / rollback): don't loop, just resync.
            o.SeenStore.Set(_current.ToString());
            o.Log.Information("[LalaChangelog] running v{Cur} is older than last seen v{Prev}; resynced", _current, seen);
        }
    }

    /// <summary>Reopen on demand (`/&lt;cmd&gt; changelog`). Shows every embedded entry, newest first.</summary>
    public void ShowNow()
    {
        var entries = ChangelogParser.Between(_all, null, new Version(int.MaxValue, 0, 0, 0));
        _window.Show(entries, _current, null);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_disposed || !_pending) return;
        try
        {
            if (!_o.ClientState.IsLoggedIn) return;
            if (_o.Condition[ConditionFlag.InCombat] || _o.Condition[ConditionFlag.BoundByDuty] ||
                _o.Condition[ConditionFlag.BetweenAreas] || _o.Condition[ConditionFlag.WatchingCutscene] ||
                _o.Condition[ConditionFlag.OccupiedInCutSceneEvent])
                return;

            _pending = false;
            _o.Framework.Update -= OnFrameworkUpdate;

            var entries = ChangelogParser.Between(_all, _previous, _current);
            if (entries.Count == 0)
            {
                // Nothing to say for this range (e.g. version bump with the entry missing) - fall back to newest.
                entries = ChangelogParser.Between(_all, null, _current);
                if (entries.Count > 1) entries = entries.GetRange(0, 1);
            }
            _o.Log.Information("[LalaChangelog] changelog shown for v{Version} ({Count} version block(s), from v{Prev})", _current, entries.Count, _previous?.ToString() ?? "none");
            _window.Show(entries, _current, _previous);
        }
        catch (Exception ex)
        {
            _pending = false;
            _o.Framework.Update -= OnFrameworkUpdate;
            _o.Log.Error(ex, "[LalaChangelog] failed to open the What's new window");
        }
    }

    private void OnDismissed()
    {
        try
        {
            var seen = _o.SeenStore.Get();
            var seenV = string.IsNullOrWhiteSpace(seen) ? null : ChangelogParser.NormalizeVersion(seen);
            if (seenV is null || seenV < _current)
            {
                _o.SeenStore.Set(_current.ToString());
                _o.Log.Information("[LalaChangelog] v{Version} marked as seen", _current);
            }
        }
        catch (Exception ex)
        {
            _o.Log.Error(ex, "[LalaChangelog] failed to persist last-seen version");
        }
    }

    private static string ReadEmbedded(Assembly asm, string name, IPluginLog log)
    {
        try
        {
            using var s = asm.GetManifestResourceStream(name);
            if (s is null)
            {
                log.Warning("[LalaChangelog] embedded resource '{Name}' not found in {Asm}", name, asm.GetName().Name ?? "?");
                return string.Empty;
            }
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch (Exception ex)
        {
            log.Error(ex, "[LalaChangelog] failed to read embedded CHANGELOG");
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _o.Framework.Update -= OnFrameworkUpdate;
        _o.Windows.RemoveWindow(_window);
    }
}
