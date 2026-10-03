using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Lalalazy.Hub;
using LazyHub.Core;

namespace LazyHub;

/// <summary>One lalalazy plugin as the hub sees it: its Dalamud state and, when it offers them, its controls and values.</summary>
internal sealed class PluginLink
{
    public required CatalogEntry Entry { get; init; }
    public PluginState State { get; set; } = PluginState.NotInstalled;
    public ParsedDescriptor? Descriptor { get; set; }
    public Dictionary<string, ParsedState> Values { get; set; } = new(StringComparer.Ordinal);

    internal long NextDescribeAt;
    internal int Failures;
    internal ICallGateSubscriber<string>? DescribeCall;
    internal ICallGateSubscriber<string>? GetAllCall;
    internal ICallGateSubscriber<string, string, string>? SetCall;
    internal ICallGateSubscriber<string, string>? InvokeCall;

    public bool IsRunning => PluginStatus.CanOpen(State);

    public ParsedControl? Master => Descriptor?.Controls.FirstOrDefault(c => c.Master);

    public bool HasSettings(string excludedGroup)
        => Descriptor != null && Descriptor.Controls.Any(c => !string.Equals(c.Group, excludedGroup, StringComparison.Ordinal));

    public ParsedState? ValueOf(string controlId) => Values.TryGetValue(controlId, out var s) ? s : null;
}

/// <summary>
/// The hub side of the lalalazy hub protocol (src/Shared/LalaHub): asks every running plugin that offers it for its
/// controls (Describe, once per load) and values (GetAll, while the window polls), and sends changes (Set, Invoke).
///
/// Every call is wrapped: a plugin without the provider, still loading, mid-unload, or answering garbage is simply
/// "no controls" and is asked again after a pause. Only called from the framework thread (the window's update and
/// its click handlers), which is what Dalamud IPC expects. Never called while the window is closed.
/// </summary>
internal sealed class HubClient(IDalamudPluginInterface pi, IPluginLog log)
{
    private const long RetryDescribeMs = 10_000;
    private const int MaxFailures = 3;

    public IReadOnlyList<PluginLink> Links { get; } = Catalog.Plugins.Select(e => new PluginLink { Entry = e }).ToList();

    public PluginLink? Find(string internalName)
        => Links.FirstOrDefault(l => string.Equals(l.Entry.InternalName, internalName, StringComparison.Ordinal));

    /// <summary>Updates every plugin's state from Dalamud, then asks the running ones for controls and, when asked to, values.</summary>
    public void Refresh(IReadOnlyList<PluginMonitor.Row> rows, bool pollValues, long nowMs)
    {
        var state = new Dictionary<string, PluginState>(rows.Count, StringComparer.Ordinal);
        foreach (var r in rows) state[r.Entry.InternalName] = r.State;

        foreach (var link in Links)
        {
            link.State = state.GetValueOrDefault(link.Entry.InternalName, PluginState.NotInstalled);

            if (!link.IsRunning)
            {
                Forget(link);
                continue;
            }

            if (link.Descriptor == null)
            {
                if (nowMs >= link.NextDescribeAt) TryDescribe(link, nowMs);
                continue;
            }

            if (pollValues) PollValues(link, nowMs);
        }
    }

    public SetOutcome Set(PluginLink link, string controlId, object value)
    {
        try
        {
            link.SetCall ??= pi.GetIpcSubscriber<string, string, string>(HubProtocol.EndpointName(link.Entry.InternalName, "Set"));
            var outcome = DescriptorParser.ParseResult(link.SetCall.InvokeFunc(controlId, DescriptorParser.ValueToJson(value)));
            PollValues(link, Environment.TickCount64);
            return outcome;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Set {Plugin}.{Control} failed", link.Entry.InternalName, controlId);
            return SetOutcome.Refuse("Could not reach " + link.Entry.DisplayName + ".");
        }
    }

    public SetOutcome Invoke(PluginLink link, string controlId)
    {
        try
        {
            link.InvokeCall ??= pi.GetIpcSubscriber<string, string>(HubProtocol.EndpointName(link.Entry.InternalName, "Invoke"));
            var outcome = DescriptorParser.ParseResult(link.InvokeCall.InvokeFunc(controlId));
            PollValues(link, Environment.TickCount64);
            return outcome;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Invoke {Plugin}.{Control} failed", link.Entry.InternalName, controlId);
            return SetOutcome.Refuse("Could not reach " + link.Entry.DisplayName + ".");
        }
    }

    private void TryDescribe(PluginLink link, long nowMs)
    {
        try
        {
            link.DescribeCall ??= pi.GetIpcSubscriber<string>(HubProtocol.EndpointName(link.Entry.InternalName, "Describe"));
            var parsed = DescriptorParser.ParseDescriptor(link.DescribeCall.InvokeFunc());
            if (parsed == null || parsed.Controls.Count == 0)
            {
                link.NextDescribeAt = nowMs + RetryDescribeMs;
                return;
            }

            link.Descriptor = parsed;
            link.Failures = 0;
            PollValues(link, nowMs);
        }
        catch (Exception)
        {
            // Not offering the hub protocol (yet): the normal case for a plugin without an adapter.
            link.NextDescribeAt = nowMs + RetryDescribeMs;
        }
    }

    private void PollValues(PluginLink link, long nowMs)
    {
        try
        {
            link.GetAllCall ??= pi.GetIpcSubscriber<string>(HubProtocol.EndpointName(link.Entry.InternalName, "GetAll"));
            var values = DescriptorParser.ParseState(link.GetAllCall.InvokeFunc());
            if (values == null) throw new InvalidOperationException("unreadable state");
            link.Values = values;
            link.Failures = 0;
        }
        catch (Exception ex)
        {
            if (++link.Failures >= MaxFailures)
            {
                log.Debug(ex, "{Plugin} stopped answering the hub; dropping its controls", link.Entry.InternalName);
                Forget(link);
                link.NextDescribeAt = nowMs + 2_000;
            }
        }
    }

    private static void Forget(PluginLink link)
    {
        link.Descriptor = null;
        link.Values = new Dictionary<string, ParsedState>(StringComparer.Ordinal);
        link.Failures = 0;
        link.DescribeCall = null;
        link.GetAllCall = null;
        link.SetCall = null;
        link.InvokeCall = null;
    }
}
