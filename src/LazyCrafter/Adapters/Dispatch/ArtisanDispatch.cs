using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Artisan hand-off over its public IPC (Plan §Phase 5 task 1): <c>Artisan.CraftItem(ushort recipeId, int amount)</c>,
/// <c>Artisan.IsBusy()</c>, <c>Artisan.SetStopRequest(bool)</c>. Names and signatures from <c>Artisan/IPC/IPC.cs</c>
/// (4.0.5.19 source; installed 4.0.5.18). <c>CraftItem</c> selects the recipe in the crafting log, switches job when
/// needed, sets Endurance's "craft X times" and starts it; it throws on an unknown recipe id and does nothing visible
/// when the crafting log cannot be opened (in combat, mounted). <c>IsBusy</c> is true while Endurance / a list / any task
/// queue is active or the craft state is not idle - poll it between recipes. <c>SetStopRequest(true)</c> aborts.
/// </summary>
public sealed class ArtisanDispatch
{
    public const string InternalName = "Artisan";

    private readonly ICallGateSubscriber<ushort, int, object> _craftItem;
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<bool, object> _setStopRequest;
    private readonly ICallGateSubscriber<bool> _getStopRequest;
    private readonly IDalamudPluginInterface _pi;
    private readonly IPluginLog _log;

    public ArtisanDispatch(IDalamudPluginInterface pi, IPluginLog log)
    {
        _pi = pi;
        _log = log;
        _craftItem = pi.GetIpcSubscriber<ushort, int, object>($"{InternalName}.CraftItem");
        _isBusy = pi.GetIpcSubscriber<bool>($"{InternalName}.IsBusy");
        _setStopRequest = pi.GetIpcSubscriber<bool, object>($"{InternalName}.SetStopRequest");
        _getStopRequest = pi.GetIpcSubscriber<bool>($"{InternalName}.GetStopRequest");
    }

    public bool Installed => _pi.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded);

    /// <summary><c>true</c> when Artisan is crafting / has queued tasks; <c>null</c> when the IPC is unavailable.</summary>
    public bool? IsBusy()
    {
        try { return _isBusy.InvokeFunc(); }
        catch (Exception ex) { _log.Debug("Artisan.IsBusy unavailable: {Msg}", ex.Message); return null; }
    }

    public bool StopRequested()
    {
        try { return _getStopRequest.InvokeFunc(); }
        catch { return false; }
    }

    /// <summary>Ask Artisan to craft <paramref name="crafts"/> runs of <paramref name="recipeId"/>. Framework thread. Returns an error string or <c>null</c>.</summary>
    public string? Craft(uint recipeId, int crafts)
    {
        if (recipeId > ushort.MaxValue) return $"recipe id {recipeId} does not fit Artisan's ushort recipe parameter";
        if (crafts <= 0) return "nothing to craft";
        try
        {
            // A lingering external stop request would make Endurance refuse to start; clear it first.
            if (StopRequested()) _setStopRequest.InvokeAction(false);
            _craftItem.InvokeAction((ushort)recipeId, crafts);
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Artisan.CraftItem({Recipe}, {Crafts}) failed", recipeId, crafts);
            return ex.InnerException?.Message ?? ex.Message;
        }
    }

    /// <summary>Abort whatever Artisan is doing (external stop request). Safe when Artisan is absent.</summary>
    public void Stop()
    {
        try { _setStopRequest.InvokeAction(true); }
        catch (Exception ex) { _log.Debug("Artisan.SetStopRequest(true) failed: {Msg}", ex.Message); }
    }

    /// <summary>Clear the stop request so Artisan can be driven again.</summary>
    public void ClearStop()
    {
        try { if (StopRequested()) _setStopRequest.InvokeAction(false); }
        catch { /* absent */ }
    }
}
