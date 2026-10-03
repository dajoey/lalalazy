using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyHub;

/// <summary>
/// Read-only look at Gluttony Combo's auto-rotation state through its existing IPC
/// (<c>GluttonyCombo.GetAutoRotationState</c>, no lease needed to read).
///
/// This class must NEVER register a lease or call a setter: every <c>RegisterForLease</c> call
/// creates a new registration on Gluttony's side, and duplicates break its settings UI. Controls
/// need a non-leased endpoint in Gluttony first, so this release only reads.
/// </summary>
internal sealed class GluttonyProbe(IDalamudPluginInterface pi, IPluginLog log)
{
    public enum Reading
    {
        /// <summary>Gluttony Combo is not running.</summary>
        NotLoaded,

        /// <summary>Gluttony Combo is running but its IPC did not answer.</summary>
        Unavailable,

        Off,
        On,
    }

    private ICallGateSubscriber<bool>? _subscriber;
    private bool _loggedUnavailable;

    public Reading Read(bool gluttonyLoaded)
    {
        if (!gluttonyLoaded)
        {
            _loggedUnavailable = false;
            return Reading.NotLoaded;
        }

        try
        {
            _subscriber ??= pi.GetIpcSubscriber<bool>("GluttonyCombo.GetAutoRotationState");
            return _subscriber.InvokeFunc() ? Reading.On : Reading.Off;
        }
        catch (Exception ex)
        {
            // The provider can be missing for a moment while Gluttony Combo loads. Say it once.
            if (!_loggedUnavailable)
            {
                log.Information("Gluttony Combo auto-rotation state is not readable yet: {Message}", ex.Message);
                _loggedUnavailable = true;
            }
            return Reading.Unavailable;
        }
    }
}
