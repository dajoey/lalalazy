#nullable enable
using System;
using System.Threading;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Lalalazy.Hub;

/// <summary>
/// Registers a <see cref="HubEndpoint"/> on Dalamud IPC as <c>Lala.&lt;InternalName&gt;.Describe / GetAll / Set / Invoke</c>.
///
/// Adapter pattern (the same three lines in every plugin):
/// <code>
/// _hub = LalaHubProvider.TryCreate(pi, Log, "AutoPotion", Version, ep => ep.Toggle(...));   // LAST statement of the constructor
/// _hub?.Dispose();                                                                          // FIRST line of Dispose
/// </code>
///
/// Fail-safe by design: an invalid declaration is logged and not registered, registration errors are
/// swallowed, and <see cref="Dispose"/> is idempotent and unregisters everything even if a step throws.
/// A provider left registered after its plugin unloads would point into a freed load context.
/// </summary>
public sealed class LalaHubProvider : IDisposable
{
    private readonly IPluginLog _log;
    private ICallGateProvider<string>? _describe;
    private ICallGateProvider<string>? _getAll;
    private ICallGateProvider<string, string, string>? _set;
    private ICallGateProvider<string, string>? _invoke;
    private bool _registered;
    private int _disposed;

    /// <summary>Returns the provider, or null (after logging why) when nothing was registered.</summary>
    public static LalaHubProvider? TryCreate(IDalamudPluginInterface pi, IPluginLog log, string internalName, string version, Action<HubEndpoint> declare)
    {
        try
        {
            var endpoint = new HubEndpoint(internalName, version)
            {
                Log = message => log.Warning("[LalaHub] {Message}", message),
            };
            declare(endpoint);

            var errors = endpoint.Validate();
            if (errors.Count > 0)
            {
                log.Warning("[LalaHub] {Plugin} not registered, invalid declaration: {Errors}", internalName, string.Join("; ", errors));
                return null;
            }

            var provider = new LalaHubProvider(pi, log, endpoint, internalName);
            return provider._registered ? provider : null;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[LalaHub] {Plugin} could not register", internalName);
            return null;
        }
    }

    private LalaHubProvider(IDalamudPluginInterface pi, IPluginLog log, HubEndpoint endpoint, string internalName)
    {
        _log = log;
        try
        {
            _describe = pi.GetIpcProvider<string>(HubProtocol.EndpointName(internalName, "Describe"));
            _describe.RegisterFunc(endpoint.Describe);

            _getAll = pi.GetIpcProvider<string>(HubProtocol.EndpointName(internalName, "GetAll"));
            _getAll.RegisterFunc(endpoint.GetAll);

            _set = pi.GetIpcProvider<string, string, string>(HubProtocol.EndpointName(internalName, "Set"));
            _set.RegisterFunc(endpoint.Set);

            _invoke = pi.GetIpcProvider<string, string>(HubProtocol.EndpointName(internalName, "Invoke"));
            _invoke.RegisterFunc(endpoint.Invoke);

            _registered = true;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[LalaHub] {Plugin} registration failed, undoing it", internalName);
            Unregister();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Unregister();
    }

    private void Unregister()
    {
        try { _describe?.UnregisterFunc(); } catch (Exception ex) { _log.Warning(ex, "[LalaHub] unregister Describe failed"); }
        try { _getAll?.UnregisterFunc(); } catch (Exception ex) { _log.Warning(ex, "[LalaHub] unregister GetAll failed"); }
        try { _set?.UnregisterFunc(); } catch (Exception ex) { _log.Warning(ex, "[LalaHub] unregister Set failed"); }
        try { _invoke?.UnregisterFunc(); } catch (Exception ex) { _log.Warning(ex, "[LalaHub] unregister Invoke failed"); }
        _describe = null;
        _getAll = null;
        _set = null;
        _invoke = null;
        _registered = false;
    }
}
