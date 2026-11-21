using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// WahTools hand-off over its public IPC (0.1.7.14). Gates and signatures from WahTools' <c>IPC.md</c> and source
/// (git.wahapp.com/wah/WahTools, 20.0.0.64):
/// <list type="bullet">
/// <item><c>WahTools.IsBusy() -> bool</c>: any WahTools module is working.</item>
/// <item><c>WahTools.Feesh.Catch(uint fishItemId, int amount) -> bool</c>: catch that many of a fish (item id; no ocean
///   fish). The catch is held - protected from WahTools' own discard and desynth - until
///   <c>WahTools.Feesh.ReleaseHeldFish()</c>.</item>
/// <item><c>WahTools.Hunter.Hunt(uint bnpcNameId, int kills) -> bool</c>: kill that many of a monster. Hunter counts
///   kills, not drops; combat runs through the WrathCombo IPC lease.</item>
/// <item><c>CatchStatus()</c> / <c>HuntStatus()</c> -> status line; <c>Feesh.Stop()</c> / <c>Hunter.Stop()</c>.</item>
/// </list>
/// A <c>false</c> answer is a refusal; WahTools logs the reason in its own log and the status line usually repeats it.
/// Every call is wrapped: an absent or older WahTools reads as "not installed" / refused, never as an exception.
/// </summary>
public sealed class WahToolsDispatch
{
    public const string InternalName = "WahTools";

    private readonly IDalamudPluginInterface _pi;
    private readonly IPluginLog _log;
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<uint, int, bool> _catch;
    private readonly ICallGateSubscriber<string> _catchStatus;
    private readonly ICallGateSubscriber<bool> _feeshStop;
    private readonly ICallGateSubscriber<bool> _releaseHeld;
    private readonly ICallGateSubscriber<uint, int, bool> _hunt;
    private readonly ICallGateSubscriber<string> _huntStatus;
    private readonly ICallGateSubscriber<bool> _hunterStop;

    public WahToolsDispatch(IDalamudPluginInterface pi, IPluginLog log)
    {
        _pi = pi;
        _log = log;
        _isBusy = pi.GetIpcSubscriber<bool>($"{InternalName}.IsBusy");
        _catch = pi.GetIpcSubscriber<uint, int, bool>($"{InternalName}.Feesh.Catch");
        _catchStatus = pi.GetIpcSubscriber<string>($"{InternalName}.Feesh.CatchStatus");
        _feeshStop = pi.GetIpcSubscriber<bool>($"{InternalName}.Feesh.Stop");
        _releaseHeld = pi.GetIpcSubscriber<bool>($"{InternalName}.Feesh.ReleaseHeldFish");
        _hunt = pi.GetIpcSubscriber<uint, int, bool>($"{InternalName}.Hunter.Hunt");
        _huntStatus = pi.GetIpcSubscriber<string>($"{InternalName}.Hunter.HuntStatus");
        _hunterStop = pi.GetIpcSubscriber<bool>($"{InternalName}.Hunter.Stop");
    }

    /// <summary>WahTools is loaded and answers its IsBusy gate.</summary>
    public bool Installed => _pi.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded) && IsBusy() is not null;

    /// <summary><c>true</c> while any WahTools module works; <c>null</c> when the IPC is unavailable.</summary>
    public bool? IsBusy()
    {
        try { return _isBusy.InvokeFunc(); }
        catch (Exception ex) { _log.Debug("WahTools.IsBusy unavailable: {Msg}", ex.Message); return null; }
    }

    /// <summary>Ask Feesh to catch <paramref name="amount"/> of fish item <paramref name="itemId"/>. Framework thread.</summary>
    public bool Catch(uint itemId, int amount) => Call("Feesh.Catch", () => _catch.InvokeFunc(itemId, amount));

    /// <summary>Ask Hunter to kill <paramref name="kills"/> of monster <paramref name="bnpcNameId"/>. Framework thread.</summary>
    public bool Hunt(uint bnpcNameId, int kills) => Call("Hunter.Hunt", () => _hunt.InvokeFunc(bnpcNameId, kills));

    public string CatchStatus() => Text(_catchStatus);
    public string HuntStatus() => Text(_huntStatus);

    public void StopFeesh() => Call("Feesh.Stop", () => _feeshStop.InvokeFunc());
    public void StopHunter() => Call("Hunter.Stop", () => _hunterStop.InvokeFunc());

    /// <summary>Let WahTools treat the fish it caught for this run like any other fish again (its discard / desynth rules).</summary>
    public void ReleaseHeldFish() => Call("Feesh.ReleaseHeldFish", () => _releaseHeld.InvokeFunc());

    private bool Call(string gate, Func<bool> invoke)
    {
        try { return invoke(); }
        catch (Exception ex)
        {
            _log.Warning("WahTools.{Gate} failed: {Msg}", gate, ex.InnerException?.Message ?? ex.Message);
            return false;
        }
    }

    private static string Text(ICallGateSubscriber<string> gate)
    {
        try { return gate.InvokeFunc() ?? ""; }
        catch { return ""; }
    }
}
