using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyCrafter.Adapters;

/// <summary>
/// Asks the one question the craft path never asked before card t_0b4d8b2c: <b>can the client accept a command
/// right now, and if not, what is holding it?</b> Card t_ee6f7bf5 turns the answer into behaviour: before every
/// craft, <c>DispatchService</c> calls <see cref="BusyBecause"/>; a non-null answer holds the cart in
/// <c>Phase.WaitClientFree</c> ("waiting - close the market board to continue") until the window is gone or the
/// five-minute cap stops the run cleanly.
///
/// <para>
/// <b>Two independent signals, and the addon one is checked first</b>, because it can NAME the window and the
/// condition flags cannot. Addon names are the game's own addon identifiers, each confirmed present as a UTF-16
/// literal in an installed plugin's assembly (Lifestream: <c>ItemSearch</c>, <c>RetainerList</c>,
/// <c>InventoryRetainer</c>, <c>Talk</c>, <c>SelectString</c>, <c>SelectYesno</c>; InventoryTools:
/// <c>ItemSearch</c>, <c>ItemSearchResult</c>, <c>RetainerList</c>, <c>Shop</c>, <c>GrandCompanySupplyList</c>;
/// ECommons: <c>Shop</c>, <c>SelectString</c>, <c>SelectIconString</c>, <c>SelectYesno</c>, <c>Bank</c>,
/// <c>RetainerSell</c>, <c>RetainerTaskAsk</c>, <c>RetainerTaskResult</c>, <c>MaterializeDialog</c>,
/// <c>Repair</c>, <c>MateriaAttachDialog</c>, <c>SelectOk</c>) or in ECommons' AddonMaster set
/// (<c>ShoppingCart</c>, <c>RetainerTaskAsk</c>, <c>InputNumeric</c>, <c>RetainerSellList</c>). A window only
/// counts as blocking when its addon is loaded AND visible (<c>AtkUnitBase->IsVisible</c>, the same idiom as
/// <c>Spike/VendorSpike.cs</c>): some addons stay loaded after being closed, and holding the cart for a window
/// that is already gone would be exactly the "walked away" failure mode the 5-minute cap exists to bound.
/// </para>
///
/// <para>
/// <b>The flag half is Artisan's own refusal set.</b> The first nine names in
/// <see cref="LazyCrafter.Core.ClientWaitPolicy.BlockingConditionNames"/> are verbatim from Artisan's
/// <c>PreCrafting.Occupied()</c> - the states the game is in when it answers a craft request with the exact
/// error in Joey's 2026-09-06 11:58 log, five times in seven seconds: "Unable to execute command while
/// occupied". The names are resolved against the live <see cref="ConditionFlag"/> enum once, at construction;
/// a name that no longer exists upstream is skipped with a warning rather than crashing - an upstream rename
/// degrades to "window not named", never a broken dispatcher.
/// </para>
///
/// <para>
/// <b>The crafting conditions are excluded on purpose.</b> <c>Crafting</c> / <c>PreparingToCraft</c> /
/// <c>ExecutingCraftingAction</c> are what a WORKING craft looks like - Artisan is inside them for the entire
/// craft. ECommons' broader <c>IsOccupied()</c> includes them; Artisan's craft gate deliberately does not, and
/// neither do we: gating on them would hold the dispatcher against its own craft forever. The exclusion is
/// pinned by harness checks against <see cref="LazyCrafter.Core.ClientWaitPolicy"/>.
/// </para>
/// </summary>
public sealed class ClientReadiness
{
    /// <summary>
    /// Windows that own the client's input and would make the game refuse a craft command, in the order we
    /// would like to name them. <c>ItemSearch</c> is the market board itself - the window that caused the
    /// original bug. The label is what the player is told to close.
    /// </summary>
    public static readonly (string Addon, string Label)[] BlockingAddons =
    [
        ("ItemSearch", "the market board"),
        ("ItemSearchResult", "the market board"),
        ("ShoppingCart", "the market board purchase window"),
        ("RetainerList", "the retainer bell"),
        ("InventoryRetainer", "a retainer's inventory"),
        ("RetainerSellList", "your retainer's sale list"),
        ("RetainerSell", "a market-board listing window"),
        ("RetainerTaskAsk", "a retainer venture prompt"),
        ("RetainerTaskResult", "a retainer venture result"),
        ("Shop", "a shop window"),
        ("GrandCompanySupplyList", "the grand company supply window"),
        ("MaterializeDialog", "the desynthesis window"),
        ("Repair", "the repair window"),
        ("MateriaAttachDialog", "the materia meld window"),
        ("Bank", "the bank window"),
        ("Trade", "a trade window"),
        ("InputNumeric", "a quantity input prompt"),
        ("Talk", "a dialogue box"),
        ("SelectOk", "a dialogue box"),
        ("SelectString", "a dialogue choice"),
        ("SelectIconString", "a dialogue choice"),
        ("SelectYesno", "a yes/no prompt"),
    ];

    /// <summary>Readable wording for a condition flag we fell back to, so the chat line still says something true.</summary>
    private static readonly Dictionary<string, string> FlagLabels = new(StringComparer.Ordinal)
    {
        ["OccupiedSummoningBell"] = "the summoning bell",
        ["TradeOpen"] = "a trade window",
        ["WatchingCutscene"] = "a cutscene",
        ["WatchingCutscene78"] = "a cutscene",
        ["OccupiedInCutSceneEvent"] = "a cutscene",
        ["BetweenAreas"] = "a zone change",
        ["BetweenAreas51"] = "a zone change",
        ["OccupiedInQuestEvent"] = "a quest event",
        ["OccupiedInEvent"] = "a quest event",
    };

    private readonly ICondition _condition;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private readonly ConditionFlag[] _busyFlags;

    public ClientReadiness(ICondition condition, IGameGui gameGui, IPluginLog log)
    {
        _condition = condition;
        _gameGui = gameGui;
        _log = log;
        // Core owns the flag list as NAMES so it stays Dalamud-free and the harness can pin it (card
        // t_ee6f7bf5); resolve each against the live enum once, here, at the edge.
        var live = Enum.GetNames<ConditionFlag>().ToHashSet(StringComparer.Ordinal);
        var resolved = new List<ConditionFlag>();
        foreach (var name in LazyCrafter.Core.ClientWaitPolicy.BlockingConditionNames)
            if (live.Contains(name)) resolved.Add(Enum.Parse<ConditionFlag>(name));
            else log.Warning("ClientReadiness: condition flag {Name} no longer exists upstream - skipped (a window it covered will fall back to the generic wording)", name);
        _busyFlags = resolved.ToArray();
    }

    /// <summary>
    /// The window/state currently holding the client's input, or <c>null</c> when the client can accept a
    /// command. Never throws: an unexpected failure reads as "not busy", so this can only ever soften a
    /// diagnosis or end a hold early, never invent a block.
    /// </summary>
    public string? BusyBecause()
    {
        try
        {
            foreach (var (addon, label) in BlockingAddons)
                if (IsOpen(addon))
                    return label;

            foreach (var flag in _busyFlags)
                if (_condition[flag])
                    return FlagLabels.GetValueOrDefault(flag.ToString(), LazyCrafter.Core.CraftDiagnosis.UnknownWindow);

            return null;
        }
        catch (Exception ex)
        {
            _log.Debug("ClientReadiness.BusyBecause failed: {Msg}", ex.Message);
            return null;
        }
    }

    /// <summary>Loaded AND visible - a hidden-but-loaded addon is not holding anything (VendorSpike idiom).</summary>
    private unsafe bool IsOpen(string addon)
    {
        var ptr = _gameGui.GetAddonByName(addon, 1);
        if (ptr.Address == nint.Zero) return false;
        return ((AtkUnitBase*)ptr.Address)->IsVisible;
    }
}
