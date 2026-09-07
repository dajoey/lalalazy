using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyFashionReport.Adapters;

/// <summary>
/// Artisan's public crafting IPC, called directly (no LazyCrafter dependency) - the same surface
/// LazyCrafter's ArtisanDispatch drives: <c>Artisan.CraftItem(ushort recipeId, int amount)</c>,
/// <c>Artisan.IsBusy()</c>, <c>Artisan.SetStopRequest(bool)</c> / <c>GetStopRequest()</c>.
/// Names and signatures from Artisan's IPC.cs (4.0.5.x). CraftItem selects the recipe in the
/// crafting log, switches job when needed and starts Endurance "craft X times"; it does nothing
/// visible when the crafting log cannot open (in combat, mounted, unknown recipe id), so the
/// caller checks <see cref="IsBusy"/> after the call and reports an honest failure when nothing
/// started. Framework thread only.
/// </summary>
internal sealed class ArtisanCraft
{
    public const string InternalName = "Artisan";

    private readonly IDalamudPluginInterface _pi;
    private readonly IPluginLog _log;
    private readonly ICallGateSubscriber<ushort, int, object>? _craftItem;
    private readonly ICallGateSubscriber<bool>? _isBusy;
    private readonly ICallGateSubscriber<bool, object>? _setStopRequest;
    private readonly ICallGateSubscriber<bool>? _getStopRequest;

    public ArtisanCraft(IDalamudPluginInterface pi, IPluginLog log)
    {
        _pi = pi;
        _log = log;

        // Artisan is optional: probe the IPC lazily instead of throwing when it is absent.
        try
        {
            _craftItem = pi.GetIpcSubscriber<ushort, int, object>($"{InternalName}.CraftItem");
            _isBusy = pi.GetIpcSubscriber<bool>($"{InternalName}.IsBusy");
            _setStopRequest = pi.GetIpcSubscriber<bool, object>($"{InternalName}.SetStopRequest");
            _getStopRequest = pi.GetIpcSubscriber<bool>($"{InternalName}.GetStopRequest");
        }
        catch (Exception ex)
        {
            log.Debug($"Artisan IPC not available at construction: {ex.Message}");
        }
    }

    /// <summary>Artisan installed AND loaded - a missing plugin must read as exactly that.</summary>
    public bool Installed =>
        _pi.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded);

    /// <summary><c>true</c> while Artisan is crafting / has queued tasks; null when unavailable.</summary>
    public bool? IsBusy()
    {
        var sub = _isBusy;
        if (sub is null) return null;
        try { return sub.InvokeFunc(); }
        catch (Exception ex) { _log.Debug($"Artisan.IsBusy unavailable: {ex.Message}"); return null; }
    }

    /// <summary>
    /// Ask Artisan to craft one run of <paramref name="recipeId"/>. Framework thread.
    /// Returns an error string, or null when the request was handed over (the started/not-started
    /// check is the caller's separate pending-isBusy poll).
    /// </summary>
    public string? Craft(uint recipeId)
    {
        var gate = _craftItem;
        if (gate is null) return "Artisan IPC not available (is Artisan installed and loaded?)";
        if (!Installed) return "Artisan is not installed or not loaded";
        if (recipeId > ushort.MaxValue) return $"recipe id {recipeId} does not fit Artisan's ushort recipe parameter";
        try
        {
            // A lingering external stop request makes Endurance refuse to start; clear it first
            // (same order LazyCrafter's dispatch uses).
            if (StopRequested()) _setStopRequest?.InvokeAction(false);
            gate.InvokeAction((ushort)recipeId, 1);
            return null;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, $"[LFR] Artisan.CraftItem({recipeId}, 1) failed");
            return ex.InnerException?.Message ?? ex.Message;
        }
    }

    private bool StopRequested()
    {
        try { return _getStopRequest?.InvokeFunc() ?? false; }
        catch { return false; }
    }
}
