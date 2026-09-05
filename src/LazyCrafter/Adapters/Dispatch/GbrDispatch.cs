using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Hands a gather list to GatherBuddyReborn (Plan §Phase 5 task 2, Scope §3.4 "Gather").
/// <para>
/// GBR has no "add item + quantity" IPC, so the list is created by reflection into the loaded plugin:
/// <c>GatherBuddy.Crafting.CraftingGatherBridge.CreatePersistentGatherList(string, Dictionary&lt;uint,int&gt;)</c>
/// (public static; builds an <c>AutoGatherList</c> named <paramref name="listName"/>, <c>Enabled = false</c>, resolves each
/// id through <c>GameData.Gatherables</c> then <c>Fishes</c>, adds it via <c>AutoGatherListsManager.AddList</c>).
/// The list is then found through <c>GatherBuddy.AutoGatherListsManager</c> (internal instance field) → <c>.Lists</c>, its
/// <c>Enabled</c> flipped on, <c>SetActiveItems()</c> + <c>Save()</c> called, and auto-gather started with the public IPC
/// <c>GatherBuddyReborn.SetAutoGatherEnabled(true)</c>. Any earlier list of the same name is deleted first
/// (<c>DeleteList</c>) so re-dispatching does not stack quantities.
/// </para>
/// Member names pinned in <see cref="Pin"/> against GBR 7.5.0 source (github FFXIV-CombatReborn/GatherBuddyReborn @ 4d16b9d,
/// 2026-07-29); installed 7.5.5 on Joey's client verified by the guard at run time.
/// </summary>
public sealed class GbrDispatch
{
    public const string InternalName = "GatherBuddyReborn";
    public const string ListName = "LazyCrafter";

    private const string Bridge = "GatherBuddy.Crafting.CraftingGatherBridge";
    private const string PluginType = "";                                    // the plugin instance type (GatherBuddy.GatherBuddy)
    internal const string ListsManager = "GatherBuddy.AutoGather.Lists.AutoGatherListsManager";
    private const string GatherList = "GatherBuddy.AutoGather.Lists.AutoGatherList";

    public static readonly ReflectionGuard.Pin Pin = new(
        InternalName,
        MinVersion: new Version(7, 5, 0),
        MaxVerified: new Version(7, 6, 0),
        VerifiedAgainst: "GBR 7.5.0 source (4d16b9d, 2026-07-29); installed 7.5.5",
        Members:
        [
            new(Bridge, "CreatePersistentGatherList", ReflectionGuard.MemberKind.StaticMethod, [typeof(string), typeof(Dictionary<uint, int>)]),
            new(PluginType, "AutoGatherListsManager", ReflectionGuard.MemberKind.Field),
            new(ListsManager, "Lists", ReflectionGuard.MemberKind.Property),
            new(ListsManager, "DeleteList", ReflectionGuard.MemberKind.Method),
            new(ListsManager, "SetActiveItems", ReflectionGuard.MemberKind.Method),
            new(ListsManager, "Save", ReflectionGuard.MemberKind.Method),
            new(GatherList, "Name", ReflectionGuard.MemberKind.Property),
            new(GatherList, "Enabled", ReflectionGuard.MemberKind.Property),
            new(GatherList, "Items", ReflectionGuard.MemberKind.Property),
        ]);

    private readonly ReflectionGuard _guard;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    private readonly ICallGateSubscriber<bool, object> _setEnabled;
    private readonly ICallGateSubscriber<bool> _isEnabled;
    private readonly ICallGateSubscriber<string> _statusText;
    private readonly ICallGateSubscriber<bool> _isWaiting;

    public GbrDispatch(IDalamudPluginInterface pi, ReflectionGuard guard, IChatGui chat, IPluginLog log)
    {
        _guard = guard;
        _chat = chat;
        _log = log;
        _setEnabled = pi.GetIpcSubscriber<bool, object>($"{InternalName}.SetAutoGatherEnabled");
        _isEnabled = pi.GetIpcSubscriber<bool>($"{InternalName}.IsAutoGatherEnabled");
        _statusText = pi.GetIpcSubscriber<string>($"{InternalName}.GetAutoGatherStatusText");
        _isWaiting = pi.GetIpcSubscriber<bool>($"{InternalName}.IsAutoGatherWaiting");
    }

    public bool Installed => _guard.InstalledVersion(InternalName, out var loaded) is not null && loaded;

    /// <summary>Auto-gather running (IPC). <c>false</c> when GBR is absent or the call fails.</summary>
    public bool IsAutoGatherEnabled()
    {
        try { return _isEnabled.InvokeFunc(); }
        catch (Exception ex) { _log.Debug("GatherBuddyReborn.IsAutoGatherEnabled unavailable: {Msg}", ex.Message); return false; }
    }

    public string StatusText()
    {
        try { return _statusText.InvokeFunc(); }
        catch { return ""; }
    }

    public bool IsWaiting()
    {
        try { return _isWaiting.InvokeFunc(); }
        catch { return false; }
    }

    /// <summary>
    /// Create (replacing) the "LazyCrafter" auto-gather list from <paramref name="materials"/> (itemId → quantity) and start
    /// auto-gather. Must run on the framework thread (GBR mutates its list manager unguarded). Returns the number of
    /// items GBR accepted, or -1 after a refused hand-off (already reported to chat).
    /// </summary>
    public int Dispatch(Dictionary<uint, int> materials, Func<uint, string> itemName)
    {
        if (materials.Count == 0) return 0;
        var r = _guard.Require(Pin, "GBR gather");
        if (r is null) return -1;

        try
        {
            var manager = r.Field(ReflectionGuard.Key(PluginType, "AutoGatherListsManager")).GetValue(r.Plugin);
            if (manager is null) return Refuse("GBR's AutoGatherListsManager is null (plugin still loading?)");

            var listsProp = r.Property(ReflectionGuard.Key(ListsManager, "Lists"));
            var nameProp = r.Property(ReflectionGuard.Key(GatherList, "Name"));
            var enabledProp = r.Property(ReflectionGuard.Key(GatherList, "Enabled"));
            var itemsProp = r.Property(ReflectionGuard.Key(GatherList, "Items"));
            var deleteList = r.Method(ReflectionGuard.Key(ListsManager, "DeleteList"));
            var setActive = r.Method(ReflectionGuard.Key(ListsManager, "SetActiveItems"));
            var save = r.Method(ReflectionGuard.Key(ListsManager, "Save"));

            // Remove any previous LazyCrafter list so quantities do not stack across dispatches.
            foreach (var old in FindLists(manager, listsProp, nameProp).ToList())
            {
                deleteList.Invoke(manager, [old]);
                _log.Information("GBR: deleted previous '{Name}' gather list", ListName);
            }

            r.Method(ReflectionGuard.Key(Bridge, "CreatePersistentGatherList")).Invoke(null, [ListName, materials]);

            var created = FindLists(manager, listsProp, nameProp).FirstOrDefault();
            if (created is null)
            {
                // CreatePersistentGatherList logs and returns silently when none of the ids are gatherable.
                return Refuse($"GBR did not create the list - none of {string.Join(", ", materials.Keys.Take(5).Select(itemName))} is in its gatherable/fish tables");
            }

            var count = itemsProp.GetValue(created) is System.Collections.ICollection col ? col.Count : 0;
            try
            {
                enabledProp.SetValue(created, true);
                // SetActiveArgs already IS the parameter array. 0.1.0.0-0.1.3.0 wrapped it in a second array
                // (`[SetActiveArgs(setActive)]`), so SetActiveItems(bool) received one object[] and MethodBase.Invoke threw
                // "Object of type 'System.Object[]' cannot be converted to type 'System.Boolean'" - the first in-game GBR
                // hand-off (2026-09-04) died here. GuardProbe now checks this argument shape against the installed DLL.
                setActive.Invoke(manager, SetActiveArgs(setActive));
                save.Invoke(manager, null);
            }
            catch
            {
                // Do not leave a half-configured "LazyCrafter" list behind for GBR's own save to persist.
                try { deleteList.Invoke(manager, [created]); save.Invoke(manager, null); }
                catch (Exception cleanup) { _log.Warning(cleanup, "GBR: could not remove the half-created '{Name}' list", ListName); }
                throw;
            }

            var skipped = materials.Count - count;
            _log.Information("GBR: gather list '{Name}' with {Count} item(s){Skipped}", ListName, count, skipped > 0 ? $" ({skipped} not gatherable, skipped)" : "");

            _setEnabled.InvokeAction(true);
            _chat.Print($"[LazyCrafter] GBR: gather list '{ListName}' ({count} item{(count == 1 ? "" : "s")}{(skipped > 0 ? $", {skipped} skipped" : "")}) enabled and auto-gather started.");
            return count;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "GBR dispatch failed");
            return Refuse($"{ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    /// <summary>Stop auto-gather (IPC). Safe when GBR is absent.</summary>
    public void Stop()
    {
        try { _setEnabled.InvokeAction(false); }
        catch (Exception ex) { _log.Debug("GatherBuddyReborn.SetAutoGatherEnabled(false) failed: {Msg}", ex.Message); }
    }

    internal static object?[] SetActiveArgs(System.Reflection.MethodInfo setActive)
    {
        // SetActiveItems(bool removeCompletedItems = false) - pass the default explicitly; Invoke does not fill optionals.
        var ps = setActive.GetParameters();
        return ps.Length == 0 ? [] : ps.Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
    }

    private static IEnumerable<object> FindLists(object manager, System.Reflection.PropertyInfo listsProp, System.Reflection.PropertyInfo nameProp)
    {
        if (listsProp.GetValue(manager) is not System.Collections.IEnumerable lists) yield break;
        foreach (var l in lists)
            if (l is not null && nameProp.GetValue(l) is string n && n == ListName)
                yield return l;
    }

    private int Refuse(string why)
    {
        var line = $"[LazyCrafter] GBR gather hand-off refused: {why}";
        _log.Error("{Line}", line);
        _chat.PrintError(line);
        return -1;
    }
}
