using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Appends venture work to ARC (AutoRetainer Control, InternalName <c>ARControl</c>) — IPC first (PR #3), reflection fallback (Plan §Phase 5 task 3,
/// Scope §3.4 "Retainers"). Pre-IPC ARC re-saved its whole config after every venture assignment, so editing
/// <c>ARControl.json</c> on disk is clobbered - the only live path is its in-memory object graph.
/// <para>
/// F1 (card t_f2e5cfd7): ARC gained an IPC surface (upstream PR zbee/ARC#3, <c>ARC.AddItem / GetInProgress / GetListCount</c>),
/// which this adapter prefers; the reflection graph below stays as the fallback for builds without it (pinned against
/// ARC 8.6 source @ 9964d7f).
/// </para>
/// <para>
/// Fallback shape (all <c>internal</c>, reached by name): plugin instance <c>ARControl.AutoRetainerControlPlugin</c> → private
/// field <c>_configuration</c> (<c>ARControl.Configuration</c>) → <c>ItemLists</c> (<c>List&lt;Configuration.ItemList&gt;</c>;
/// <c>ItemList {Guid Id; string Name; ListType Type; ListPriority Priority; bool CheckRetainerInventory; List&lt;QueuedItem&gt; Items}</c>)
/// and <c>Characters</c> (<c>List&lt;CharacterConfiguration&gt;</c>; <c>{ulong LocalContentId; CharacterType Type; Guid CharacterGroupId;
/// List&lt;Guid&gt; ItemListIds}</c>) and <c>CharacterGroups</c> (<c>{Guid Id; List&lt;Guid&gt; ItemListIds}</c>); <c>QueuedItem {uint ItemId;
/// int RemainingQuantity}</c>. Persisting: private field <c>_configWindow</c> (<c>ARControl.Windows.ConfigWindow</c>) →
/// public <c>ShouldSave()</c>, which flags a delayed <c>SavePluginConfig</c> on ARC's next draw (only while its window is
/// open) - so we also call <c>IDalamudPluginInterface.SavePluginConfig</c> on ARC's own <c>_pluginInterface</c> directly,
/// which is what ARC's venture loop does.
/// </para>
/// <para>
/// The list "LazyCrafter" is <c>CollectOneTime</c> / <c>InOrder</c>; a re-dispatch adds to an existing entry's
/// <c>RemainingQuantity</c>. It is attached to the current character (Standalone: its <c>ItemListIds</c>; PartOfCharacterGroup:
/// the group's) so ARC's next venture pick sees it. Characters ARC does not manage (<c>NotManaged</c>) are refused with a hint.
/// </para>
/// Pinned against ARC 8.6 source (github zbee/ARC @ 9964d7f, 2026-08-24) and re-read at tag 8.5 (identical shape);
/// installed 8.5 on Joey's client.
/// </summary>
public sealed class ArcDispatch
{
    public const string InternalName = "ARControl";
    public const string ListName = "LazyCrafter";

    private const string PluginType = "";
    private const string Config = "ARControl.Configuration";
    private const string ItemList = "ARControl.Configuration.ItemList";
    private const string QueuedItem = "ARControl.Configuration.QueuedItem";
    private const string Character = "ARControl.Configuration.CharacterConfiguration";
    private const string Group = "ARControl.Configuration.CharacterGroup";
    private const string ListType = "ARControl.Configuration.ListType";
    private const string ListPriority = "ARControl.Configuration.ListPriority";
    private const string CharacterType = "ARControl.Configuration.CharacterType";
    private const string ConfigWindow = "ARControl.Windows.ConfigWindow";

    public static readonly ReflectionGuard.Pin Pin = new(
        InternalName,
        MinVersion: new Version(8, 5),
        MaxVerified: new Version(8, 8),
        VerifiedAgainst: "ARC 8.6 source (9964d7f, 2026-08-24) + tag 8.5; omasky 8.7 build verified member-by-member via GuardProbe 2026-09-04",
        Members:
        [
            new(PluginType, "_configuration", ReflectionGuard.MemberKind.Field),
            new(PluginType, "_configWindow", ReflectionGuard.MemberKind.Field),
            new(PluginType, "_pluginInterface", ReflectionGuard.MemberKind.Field),
            new(Config, "ItemLists", ReflectionGuard.MemberKind.Property),
            new(Config, "Characters", ReflectionGuard.MemberKind.Property),
            new(Config, "CharacterGroups", ReflectionGuard.MemberKind.Property),
            new(ItemList, "Id", ReflectionGuard.MemberKind.Property),
            new(ItemList, "Name", ReflectionGuard.MemberKind.Property),
            new(ItemList, "Type", ReflectionGuard.MemberKind.Property),
            new(ItemList, "Priority", ReflectionGuard.MemberKind.Property),
            new(ItemList, "CheckRetainerInventory", ReflectionGuard.MemberKind.Property),
            new(ItemList, "Items", ReflectionGuard.MemberKind.Property),
            new(QueuedItem, "ItemId", ReflectionGuard.MemberKind.Property),
            new(QueuedItem, "RemainingQuantity", ReflectionGuard.MemberKind.Property),
            new(Character, "LocalContentId", ReflectionGuard.MemberKind.Property),
            new(Character, "Type", ReflectionGuard.MemberKind.Property),
            new(Character, "CharacterGroupId", ReflectionGuard.MemberKind.Property),
            new(Character, "ItemListIds", ReflectionGuard.MemberKind.Property),
            new(Group, "Id", ReflectionGuard.MemberKind.Property),
            new(Group, "ItemListIds", ReflectionGuard.MemberKind.Property),
            new(ListType, "CollectOneTime", ReflectionGuard.MemberKind.Field),
            new(ListPriority, "InOrder", ReflectionGuard.MemberKind.Field),
            new(CharacterType, "Standalone", ReflectionGuard.MemberKind.Field),
            new(CharacterType, "PartOfCharacterGroup", ReflectionGuard.MemberKind.Field),
            new(ConfigWindow, "ShouldSave", ReflectionGuard.MemberKind.Method),
        ]);

    private readonly ReflectionGuard _guard;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    // F1 (card t_f2e5cfd7): ARC's new IPC (PR zbee/ARC#3). Null until first probed; once a probe fails (installed
    // ARC predates the PR) it stays null for the session and the reflection path below remains the hand-off.
    private readonly ICallGateSubscriber<uint, int, string, bool>? _addItem;
    private readonly ICallGateSubscriber<Dictionary<uint, int>>? _inProgress;

    public ArcDispatch(IDalamudPluginInterface pi, ReflectionGuard guard, IChatGui chat, IPluginLog log)
    {
        _guard = guard;
        _chat = chat;
        _log = log;
        try { _addItem = pi.GetIpcSubscriber<uint, int, string, bool>($"{InternalName}.AddItem"); }
        catch (Exception ex) { _log.Debug("ARControl.AddItem unavailable: {Msg}", ex.Message); }
        try { _inProgress = pi.GetIpcSubscriber<Dictionary<uint, int>>($"{InternalName}.GetInProgress"); }
        catch (Exception ex) { _log.Debug("ARControl.GetInProgress unavailable: {Msg}", ex.Message); }
    }

    private bool HasIpc => _addItem is not null;

    public bool Installed => _guard.InstalledVersion(InternalName, out var loaded) is not null && loaded;

    /// <summary>
    /// Queue <paramref name="items"/> (itemId → quantity) on the "LazyCrafter" list for the character with
    /// <paramref name="contentId"/>. Framework thread. Returns the number of items queued, or -1 after a refusal (reported).
    /// <para>F1 (card t_f2e5cfd7): prefers ARC's IPC (<c>ARC.AddItem</c>, upstream PR #3) — one call per item; the IPC
    /// provider attaches the list to the current character and saves exactly like the config window. Falls back to the
    /// pinned reflection path while the installed ARC build predates the IPC (probe failure ⇒ session-sticky fallback).
    /// <paramref name="contentId"/> is only used by the fallback; the IPC acts on the current character.</para>
    /// </summary>
    public int Dispatch(Dictionary<uint, int> items, ulong contentId, Func<uint, string> itemName)
    {
        if (items.Count == 0) return 0;
        if (HasIpc)
        {
            try
            {
                var added = 0;
                foreach (var (itemId, qty) in items)
                {
                    if (qty <= 0) continue;
                    if (_addItem!.InvokeFunc((uint)itemId, qty, ListName)) added++;
                    else return Refuse($"ARC.AddItem refused {itemName(itemId)} x{qty} (character not managed by ARC, or the call failed)");
                }
                if (added > 0)
                {
                    _log.Information("ARC: {Added} item(s) queued on '{List}' via IPC: {Items}", added, ListName,
                        string.Join(", ", items.Select(kv => $"{itemName(kv.Key)} x{kv.Value}")));
                    _chat.Print($"[LazyCrafter] ARC: {added} item{(added == 1 ? "" : "s")} queued on venture list '{ListName}' via ARC IPC: " +
                        string.Join(", ", items.Take(6).Select(kv => $"{itemName(kv.Key)} x{kv.Value}")) + (items.Count > 6 ? $"+{items.Count - 6}" : "") +
                        ". Retainers pick it up on their next venture.");
                }
                return added;
            }
            catch (Exception ipcEx)
            {
                _log.Warning(ipcEx, "ARC IPC call failed; falling back to reflection");
            }
        }
        return DispatchViaReflection(items, contentId, itemName);
    }

    /// <summary>The pre-F1 reflection hand-off, kept as the fallback for ARC builds without the IPC.</summary>
    private int DispatchViaReflection(Dictionary<uint, int> items, ulong contentId, Func<uint, string> itemName)
    {
        var r = _guard.Require(Pin, "ARC venture");
        if (r is null) return -1;

        try
        {
            var config = r.Field(ReflectionGuard.Key(PluginType, "_configuration")).GetValue(r.Plugin);
            if (config is null) return Refuse("ARC's configuration object is null");

            // ---- the character must be managed by ARC
            var characters = r.Property(ReflectionGuard.Key(Config, "Characters")).GetValue(config) as System.Collections.IEnumerable;
            object? character = null;
            if (characters is not null)
                foreach (var c in characters)
                    if (c is not null && r.Property(ReflectionGuard.Key(Character, "LocalContentId")).GetValue(c) is ulong cid && cid == contentId) { character = c; break; }
            if (character is null) return Refuse("this character is not in ARC's character list - open /arc, run 'sync', and mark the character managed");

            var charType = r.Property(ReflectionGuard.Key(Character, "Type")).GetValue(character);
            var standalone = r.Field(ReflectionGuard.Key(CharacterType, "Standalone")).GetValue(null);
            var partOfGroup = r.Field(ReflectionGuard.Key(CharacterType, "PartOfCharacterGroup")).GetValue(null);
            System.Collections.IList? targetListIds;
            if (Equals(charType, standalone))
            {
                targetListIds = r.Property(ReflectionGuard.Key(Character, "ItemListIds")).GetValue(character) as System.Collections.IList;
            }
            else if (Equals(charType, partOfGroup))
            {
                var groupId = r.Property(ReflectionGuard.Key(Character, "CharacterGroupId")).GetValue(character);
                targetListIds = null;
                if (r.Property(ReflectionGuard.Key(Config, "CharacterGroups")).GetValue(config) is System.Collections.IEnumerable groups)
                    foreach (var g in groups)
                        if (g is not null && Equals(r.Property(ReflectionGuard.Key(Group, "Id")).GetValue(g), groupId))
                        { targetListIds = r.Property(ReflectionGuard.Key(Group, "ItemListIds")).GetValue(g) as System.Collections.IList; break; }
                if (targetListIds is null) return Refuse("the character's ARC group could not be found");
            }
            else
            {
                return Refuse("ARC has this character as 'not managed' - set it to Standalone or a character group in /arc first");
            }
            if (targetListIds is null) return Refuse("ARC's ItemListIds list is null");

            // ---- find or create the LazyCrafter list
            var lists = r.Property(ReflectionGuard.Key(Config, "ItemLists")).GetValue(config) as System.Collections.IList;
            if (lists is null) return Refuse("ARC's ItemLists is null");
            var nameProp = r.Property(ReflectionGuard.Key(ItemList, "Name"));
            var idProp = r.Property(ReflectionGuard.Key(ItemList, "Id"));
            object? list = null;
            foreach (var l in lists)
                if (l is not null && nameProp.GetValue(l) is string n && n == ListName) { list = l; break; }
            var createdList = false;
            if (list is null)
            {
                var listType = r.Type(ItemList);
                list = Activator.CreateInstance(listType) ?? throw new InvalidOperationException("could not construct ItemList");
                idProp.SetValue(list, Guid.NewGuid());
                nameProp.SetValue(list, ListName);
                r.Property(ReflectionGuard.Key(ItemList, "Type")).SetValue(list, r.Field(ReflectionGuard.Key(ListType, "CollectOneTime")).GetValue(null));
                r.Property(ReflectionGuard.Key(ItemList, "Priority")).SetValue(list, r.Field(ReflectionGuard.Key(ListPriority, "InOrder")).GetValue(null));
                r.Property(ReflectionGuard.Key(ItemList, "CheckRetainerInventory")).SetValue(list, false);
                lists.Add(list);
                createdList = true;
            }
            var listId = (Guid)idProp.GetValue(list)!;
            if (!targetListIds.Cast<object>().Any(x => Equals(x, listId))) targetListIds.Add(listId);

            // ---- append / merge quantities
            var queued = r.Property(ReflectionGuard.Key(ItemList, "Items")).GetValue(list) as System.Collections.IList;
            if (queued is null) return Refuse("the LazyCrafter list's Items is null");
            var itemIdProp = r.Property(ReflectionGuard.Key(QueuedItem, "ItemId"));
            var remainingProp = r.Property(ReflectionGuard.Key(QueuedItem, "RemainingQuantity"));
            var queuedType = r.Type(QueuedItem);
            var added = 0;
            foreach (var (itemId, qty) in items)
            {
                if (qty <= 0) continue;
                object? existing = null;
                foreach (var q in queued)
                    if (q is not null && itemIdProp.GetValue(q) is uint id && id == itemId) { existing = q; break; }
                if (existing is not null)
                {
                    var cur = remainingProp.GetValue(existing) is int c ? c : 0;
                    remainingProp.SetValue(existing, cur + qty);
                }
                else
                {
                    var q = Activator.CreateInstance(queuedType) ?? throw new InvalidOperationException("could not construct QueuedItem");
                    itemIdProp.SetValue(q, itemId);
                    remainingProp.SetValue(q, qty);
                    queued.Add(q);
                }
                added++;
            }

            // ---- persist: ARC's own SavePluginConfig now, plus ShouldSave() so an open ARC window re-renders.
            var pi = r.Field(ReflectionGuard.Key(PluginType, "_pluginInterface")).GetValue(r.Plugin) as IDalamudPluginInterface;
            if (pi is not null && config is Dalamud.Configuration.IPluginConfiguration cfg) pi.SavePluginConfig(cfg);
            else _log.Warning("ARC: could not save through its plugin interface; relying on ShouldSave()");
            var window = r.Field(ReflectionGuard.Key(PluginType, "_configWindow")).GetValue(r.Plugin);
            if (window is not null) r.Method(ReflectionGuard.Key(ConfigWindow, "ShouldSave")).Invoke(window, null);

            _log.Information("ARC: {Added} item(s) queued on '{List}'{Created}: {Items}", added, ListName, createdList ? " (list created)" : "",
                string.Join(", ", items.Select(kv => $"{itemName(kv.Key)} x{kv.Value}")));
            _chat.Print($"[LazyCrafter] ARC: {added} item{(added == 1 ? "" : "s")} queued on venture list '{ListName}'{(createdList ? " (created)" : "")}: " +
                string.Join(", ", items.Take(6).Select(kv => $"{itemName(kv.Key)} x{kv.Value}")) + (items.Count > 6 ? $" +{items.Count - 6}" : "") +
                ". Retainers pick it up on their next venture.");
            return added;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "ARC dispatch failed");
            return Refuse($"{ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    private int Refuse(string why)
    {
        var line = $"[LazyCrafter] ARC venture hand-off refused: {why}";
        _log.Error("{Line}", line);
        _chat.PrintError(line);
        return -1;
    }
}
