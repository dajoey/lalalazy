using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters.Dispatch;

/// <summary>
/// Vendor and market hand-offs through Lifestream (Plan §Phase 5 task 4, Scope §3.4 "Vendor" / "Market").
/// <para>
/// Vendor: <see cref="VendorLocator"/> picks the vendor nearest a teleportable aetheryte; we check
/// <c>Lifestream.IsBusy()</c>, call <c>Lifestream.Teleport(aetheryteId, 0)</c> (raw <c>Telepo</c> - returns false when not
/// attuned / in combat; enqueues nothing, so <c>IsBusy</c> is not its completion signal - see the P6 spike), set the map
/// flag on the NPC with <c>IGameGui.OpenMapWithMapLink</c>, and print the shopping list with a clickable map link.
/// Market: <c>Lifestream.ExecuteCommand("mb")</c> (= <c>/li mb</c>, nearest market board) and the list in chat.
/// Names from <c>Lifestream/IPC/IPCProvider.cs</c> (installed 2.5.4.16). vnavmesh walking is Phase 6 (toggle hidden).
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
    /// (verified in Lifestream 2.5.4.16's own command help). Split out for the summoning-bell walk (card
    /// t_35be7be5) - the bells stand with the market boards at every aetheryte plaza, and Lifestream exposes no
    /// bell-specific IPC, so this existing destination IS the bell trip. Returns an error string (already printed)
    /// or <c>null</c>.
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

    private string Refuse(string why)
    {
        var line = $"[LazyCrafter] Lifestream hand-off refused: {why}";
        _log.Warning("{Line}", line);
        _chat.PrintError(line);
        return why;
    }
}
