using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Vendor and market hand-offs through Lifestream (Plan Â§Phase 5 task 4, Scope Â§3.4 "Vendor" / "Market").
/// <para>
/// Vendor: <see cref="VendorLocator"/> picks the vendor nearest a teleportable aetheryte; we check
/// <c>Lifestream.IsBusy()</c>, call <c>Lifestream.Teleport(aetheryteId, 0)</c> (raw <c>Telepo</c> - returns false when not
/// attuned / in combat; enqueues nothing, so <c>IsBusy</c> is not its completion signal - see the P6 spike), set the map
/// flag on the NPC with <c>IGameGui.OpenMapWithMapLink</c>, and print the shopping list with a clickable map link.
/// Market: <c>Lifestream.ExecuteCommand("mb")</c> (= <c>/li mb</c>, nearest market board) and the list in chat.
/// Names from <c>Lifestream/IPC/IPCProvider.cs</c> (installed 2.5.4.16). vnavmesh walking is Phase 6 (toggle hidden).
/// </para>
/// <para>
/// 0.1.6.14 (Helm t-joey-1788793199911): <see cref="GoToVendor"/> is now also the cart-run vendor walk - one
/// vendor group per stop, the player buys and presses Resume, the re-plan walks to the next group. The walk
/// needs the SAME flag, link, list and refusal behaviour as the per-item button, and the summoning-bell walk
/// already proved Lifestream.Teleport as mid-run travel, so this is called as-is from
/// <c>DispatchService.StartVendorWalk</c> with <paramref name="teleport"/> left true.
/// </para>
/// <para>
/// 0.1.6.15 (Helm t-joey-1788808881825): the summoning-bell walk has its own destination,
/// <see cref="GoToSummoningBell"/> - <c>Lifestream.ExecuteCommand("inn")</c> (= <c>/li inn</c>, the nearest unlocked
/// inn room's bell). Lifestream's <c>mb</c> command was never a bell trip: read from its source
/// (<c>Tasks/Shortcuts/TaskMBShortcut.cs</c>), <c>/li mb</c> is a fixed Uldah alias - teleport to the aetheryte,
/// walk two points, then <b>Interact with the market board NPC</b> (data id 2000442, alias command Kind 6) - and
/// that interact OPENS the market board. That is the whole 0.1.6.14 incident: the character was brought to the
/// counter and the counter was switched on. The inn command ends at the inn keeper with no auto-interact, and
/// every inn room contains a summoning bell (which the auto-gather-retainers and auto-fish features of other
/// plugins already rely on), so <c>inn</c> is the same kind of "existing destination" the <c>mb</c> walk was -
/// just one that does not open anything.
/// </para>
/// </summary>
public sealed class LifestreamDispatch
{
    public const string InternalName = "Lifestream";

    private readonly IDalamudPluginInterface _pi;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chat;
    private readonly IPluginLog _log;
    private readonly ICallGateSubscriber<uint, byte, bool> _teleport;
    private readonly ICallGateSubscriber<bool> _isBusy;
    private readonly ICallGateSubscriber<string, object> _executeCommand;

    public LifestreamDispatch(IDalamudPluginInterface pi, IGameGui gameGui, IChatGui chat, IPluginLog log)
    {
        _pi = pi;
        _gameGui = gameGui;
        _chat = chat;
        _log = log;
        _teleport = pi.GetIpcSubscriber<uint, byte, bool>($"{InternalName}.Teleport");
        _isBusy = pi.GetIpcSubscriber<bool>($"{InternalName}.IsBusy");
        _executeCommand = pi.GetIpcSubscriber<string, object>($"{InternalName}.ExecuteCommand");
    }

    public bool Installed => _pi.InstalledPlugins.Any(p => p.InternalName == InternalName && p.IsLoaded);

    public bool? IsBusy()
    {
        try { return _isBusy.InvokeFunc(); }
        catch { return null; }
    }

    /// <summary>
    /// Teleport to the aetheryte nearest <paramref name="where"/>, flag the NPC on the map, print the list. Framework thread.
    /// Returns an error string (already printed) or <c>null</c>.
    /// </summary>
    public string? GoToVendor(VendorLocator.Location where, IReadOnlyList<(uint ItemId, int Quantity)> items, Func<uint, string> itemName, bool teleport = true)
    {
        var list = string.Join(", ", items.Select(i => $"{itemName(i.ItemId)} x{i.Quantity}"));
        try
        {
            // Map flag + clickable link first: useful even when the teleport is refused.
            var payload = new MapLinkPayload(where.TerritoryId, where.MapId, where.MapCoords.X, where.MapCoords.Y);
            _gameGui.OpenMapWithMapLink(payload);
            var sb = new SeStringBuilder()
                .AddText("[LazyCrafter] Buy from ")
                .AddUiForeground(0x0225).AddUiGlow(0x0226).Add(payload)
                .AddUiForeground(500).AddUiGlow(501).AddText($"{(char)Dalamud.Game.Text.SeIconChar.LinkMarker}").AddUiGlowOff().AddUiForegroundOff()
                .AddText($"{where.NpcName} ({where.TerritoryName} {where.MapCoords.X:0.0}, {where.MapCoords.Y:0.0})")
                .Add(RawPayload.LinkTerminator).AddUiGlowOff().AddUiForegroundOff()
                .AddText($": {list}");
            _chat.Print(sb.Build());

            if (!teleport) return null;
            if (!Installed) return Refuse("Lifestream is not installed - the vendor is flagged on your map; teleport manually.");
            if (IsBusy() == true) return Refuse("Lifestream is busy (another teleport / world change in progress).");
            var ok = _teleport.InvokeFunc(where.AetheryteId, 0);
            if (!ok) return Refuse($"Lifestream.Teleport({where.AetheryteName}) returned false - not attuned, in combat, or occupied.");
            _chat.Print($"[LazyCrafter] Lifestream: teleporting to {where.AetheryteName} ({where.TerritoryName}); {where.NpcName} is {where.MapDistance:0.0} map units from the aetheryte.");
            _log.Information("Lifestream teleport to aetheryte {Aetheryte} for vendor {Npc} in {Territory}: {List}", where.AetheryteId, where.NpcId, where.TerritoryId, list);
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "vendor hand-off failed");
            return Refuse($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Print the market shopping list and send the character to the nearest market board (<c>/li mb</c>).
    /// <para>
    /// <paramref name="also"/> is the optional "or buy it from X for Y" clause per item (card t_b431de3a part C):
    /// a currency vendor the plugin located but deliberately did not route to, because the player cannot afford it
    /// or the reroute is switched off. Naming it is the whole point - the complaint that started that card was
    /// being sent to the market board with no hint that a vendor existed. Omit it and this method behaves exactly
    /// as it did in 0.1.6.6.
    /// </para>
    /// <para>
    /// NOTE (0.1.6.15): <c>/li mb</c> ENDS with an interact that opens the market board (Lifestream's own
    /// <c>UldahMarketboard</c> alias, final command Kind 6 Interact on the board NPC). Every caller of this
    /// method wants that - a shopping trip - so it is unchanged. The summoning-bell walk must never use it; it
    /// has <see cref="GoToSummoningBell"/>. This comment exists so the next caller does not re-fuse them.
    /// </para>
    /// </summary>
    public string? GoToMarket(IReadOnlyList<(uint ItemId, int Quantity)> items, Func<uint, string> itemName, Func<uint, long?> unitPrice, bool teleport = true, Func<uint, string>? also = null)
    {
        if (items.Count == 0) return null;
        // The line itself is built in Core (LazyCrafter.Core.PlanReport) so the offline harness asserts on the
        // sentence the player actually reads rather than on a copy of it - the two most recent defects on this
        // feature were both renderer bugs that an internal-value test stayed green through.
        var purchases = items.Select(i => new Core.DispatchPlan.Purchase(i.ItemId, i.Quantity, also?.Invoke(i.ItemId) ?? "")).ToList();
        var plan = new Core.DispatchPlan.Plan([], [], [], [], purchases, [], []);
        _chat.Print("[LazyCrafter] " + Core.PlanReport.MarketLine(plan, itemName, unitPrice));
        if (!teleport) return null;
        return GoToMarketBoard();
    }

    /// <summary>
    /// Flag a currency (special) shop NPC on the map and print what to trade for there (card t_b431de3a).
    /// <para>
    /// Deliberately <b>naming and flagging only</b> - it never opens the shop window and never makes the trade.
    /// Spending the player's Grand Company seals or beast-tribe tokens for them is explicitly out of scope: the
    /// plugin has no exchange rate between a currency and gil and no business inventing one. The routing already
    /// guarantees the player can afford this offer before it reaches here (decision D2); what it does not do is
    /// decide that they SHOULD, and that judgement stays with them at the counter.
    /// </para>
    /// <para>
    /// Same map-flag and clickable-link mechanics as <see cref="GoToVendor"/>, because a currency vendor is a
    /// placed NPC like any other; only the printed clause differs (it carries the price in currency, not gil).
    /// </para>
    /// </summary>
    public string? GoToCurrencyShop(uint territoryId, uint mapId, float mapX, float mapY, string what, bool teleport = false, uint aetheryteId = 0, string aetheryteName = "")
    {
        try
        {
            var payload = new MapLinkPayload(territoryId, mapId, mapX, mapY);
            _gameGui.OpenMapWithMapLink(payload);
            var sb = new SeStringBuilder()
                .AddText("[LazyCrafter] Trade at ")
                .AddUiForeground(0x0225).AddUiGlow(0x0226).Add(payload)
                .AddUiForeground(500).AddUiGlow(501).AddText($"{(char)Dalamud.Game.Text.SeIconChar.LinkMarker}").AddUiGlowOff().AddUiForegroundOff()
                .AddText(what)
                .Add(RawPayload.LinkTerminator).AddUiGlowOff().AddUiForegroundOff();
            _chat.Print(sb.Build());

            if (!teleport || aetheryteId == 0) return null;
            if (!Installed) return Refuse("Lifestream is not installed - the currency vendor is flagged on your map; teleport manually.");
            if (IsBusy() == true) return Refuse("Lifestream is busy (another teleport / world change in progress).");
            if (!_teleport.InvokeFunc(aetheryteId, 0)) return Refuse($"Lifestream.Teleport({aetheryteName}) returned false - not attuned, in combat, or occupied.");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "currency shop hand-off failed");
            return Refuse($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The travel half of <see cref="GoToMarket"/> with no shopping list: <c>/li mb</c>, "go to market board"
    /// (verified in Lifestream 2.5.4.16's own command help). Returns an error string (already printed) or <c>null</c>.
    /// <para>
    /// <b>This trip ends with the market board OPEN.</b> Lifestream's <c>mb</c> command runs its fixed Uldah
    /// alias (<c>Tasks/Shortcuts/TaskMBShortcut.cs</c> -> <c>Data/StaticAlias.cs</c> <c>UldahMarketboard</c>):
    /// teleport to the aetheryte, walk to the board, Interact (data id 2000442). Since 0.1.6.15 the only
    /// legitimate caller is the shopping trip in <see cref="GoToMarket"/>; every summoning-bell use must call
    /// <see cref="GoToSummoningBell"/> instead (Helm t-joey-1788808881825 - the 0.1.6.14 run that walked the
    /// character into the market board mid-run was this method called as a "bell" trip).
    /// </para>
    /// </summary>
    public string? GoToMarketBoard()
    {
        if (!Installed) return Refuse("Lifestream is not installed - open a market board manually.");
        if (IsBusy() == true) return Refuse("Lifestream is busy.");
        try
        {
            _executeCommand.InvokeAction("mb");
            _chat.Print("[LazyCrafter] Lifestream: heading to the nearest market board (/li mb).");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Lifestream.ExecuteCommand(mb) failed");
            return Refuse($"Lifestream.ExecuteCommand failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The summoning-bell trip (0.1.6.15, Helm t-joey-1788808881825): <c>/li inn</c>, Lifestream's
    /// "go to inn" shortcut (<c>Tasks/Shortcuts/TaskPropertyShortcut.cs</c>, <c>PropertyType.Inn</c>), which
    /// teleports to the aetheryte, aethernet-hops to the inn aetheryte and walks to the inn keeper - where a
    /// summoning bell stands. It ends at the keeper WITHOUT interacting (the last task stops when the inn NPC
    /// is targeted and in range), so nothing opens and the player is not at the market board.
    /// <para>
    /// Three guards, each with its own recovery: an inn that has never been unlocked refuses with the manual
    /// instruction (Lifestream checks the unlock quests itself and logs "Inn is not unlocked" - this is not an
    /// error, it is a real destination we cannot use); the market board already being open (the state this fix
    /// walks out of) is CLOSED first, because <c>inn</c> is issued through the same command channel and
    /// Lifestream's own <c>Player.Interactable</c> gate would swallow it while a window owns the client - the
    /// closed board re-enables that; a refused or absent Lifestream returns the error for the caller to handle.
    /// </para>
    /// </summary>
    public string? GoToSummoningBell(bool closeBoardFirst)
    {
        if (!Installed) return "Lifestream is not installed - walk to a summoning bell yourself (they stand in every inn room and at any aetheryte plaza).";
        if (IsBusy() == true) return "Lifestream is busy - walk to a summoning bell yourself (they stand in every inn room).";
        try
        {
            if (closeBoardFirst && IsMarketBoardOpen())
            {
                CloseMarketBoard();
                _chat.Print("[LazyCrafter] closed the market board first (the inn bell trip cannot start under a window).");
            }
            _executeCommand.InvokeAction("inn");
            _chat.Print("[LazyCrafter] Lifestream: heading to the summoning bell in the inn (/li inn).");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Lifestream.ExecuteCommand(inn) failed");
            return $"the inn bell trip failed: {ex.Message} - walk to a summoning bell yourself (they stand in every inn room).";
        }
    }

    /// <summary>True when the game's market board window (ItemSearch) is loaded AND visible - the VendorSpike idiom; existence alone is not visibility (card t_ee6f7bf5's lesson).</summary>
    public unsafe bool IsMarketBoardOpen()
    {
        var ptr = _gameGui.GetAddonByName("ItemSearch", 1);
        if (ptr.Address == nint.Zero) return false;
        return ((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)ptr.Address)->IsVisible;
    }

    /// <summary>Close the market board window with its own callback (AtkUnitBase vtable index 4) - the same click the player's X makes. Never throws; a failure only leaves the window open for the hold to name.</summary>
    public unsafe void CloseMarketBoard()
    {
        try
        {
            var ptr = _gameGui.GetAddonByName("ItemSearch", 1);
            if (ptr.Address == nint.Zero) return;
            ((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)ptr.Address)->Close(true);
        }
        catch (Exception ex)
        {
            _log.Debug("closing the market board failed: {Msg}", ex.Message);
        }
    }

    private string Refuse(string why)
    {
        var line = $"[LazyCrafter] Lifestream hand-off refused: {why}";
        _log.Warning("{Line}", line);
        _chat.PrintError(line);
        return why;
    }
}
