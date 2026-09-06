using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace LazyCrafter.Adapters;

/// <summary>
/// Asks the one question the craft path never asked (card t_0b4d8b2c): <b>can the client accept a command right
/// now, and if not, what is holding it?</b>
///
/// <para>
/// Before this class, <c>ConditionFlag</c> appeared nowhere in LazyCrafter outside the inert <c>Spike/</c> folder.
/// Artisan's <c>CraftItem</c> is fire-and-forget over IPC: with a market-board window open the game answers
/// <i>"Unable to execute command while occupied"</i>, Artisan disables its crafting mode, and LazyCrafter measured
/// only "made 0" - a fact indistinguishable from a genuine material shortage. That ambiguity is what produced the
/// invented "retrieve these from elsewhere" errand in Joey's 11:58 run on 0.1.6.6.
/// </para>
///
/// <para>
/// <b>This class only OBSERVES.</b> It never waits, retries or stops - whether the craft path should wait for the
/// window to close or stop cleanly is Joey's decision and is deliberately not taken here. Its whole job is to
/// answer "was the client busy, and what window was it" so the failure can be reported truthfully.
/// </para>
///
/// <para>
/// <b>Two independent signals, and the addon one is checked first</b>, because it can NAME the window and the
/// condition flags cannot. Addon names are checked with <c>IGameGui.GetAddonByName(name, 1)</c>, which returns a
/// pointer wrapper in API 15 (<c>.Address</c>, not <c>T*</c>) - a non-zero address means the window exists. The
/// names are the game's own addon identifiers, each confirmed present as a UTF-16 literal in an installed plugin's
/// assembly (Lifestream: <c>ItemSearch</c>, <c>RetainerList</c>, <c>InventoryRetainer</c>, <c>Talk</c>,
/// <c>SelectString</c>, <c>SelectYesno</c>; InventoryTools: <c>ItemSearch</c>, <c>ItemSearchResult</c>,
/// <c>RetainerList</c>, <c>Shop</c>, <c>GrandCompanySupplyList</c>; ECommons: <c>Shop</c>, <c>SelectString</c>,
/// <c>SelectIconString</c>, <c>SelectYesno</c>) - so they are not invented.
/// </para>
///
/// <para>
/// <b>The crafting addons are excluded on purpose.</b> <c>Synthesis</c> / <c>RecipeNote</c> / <c>PreparingToCraft</c>
/// / <c>ExecutingCraftingAction</c> are what a WORKING craft looks like; treating them as "busy" would mark every
/// successful craft as refused. Likewise <c>ConditionFlag.Crafting</c> is not in the flag list.
/// </para>
/// </summary>
public sealed class ClientReadiness
{
    /// <summary>
    /// Windows that own the client's input and would make the game refuse a craft command, in the order we would
    /// like to name them. <c>ItemSearch</c> is the market board itself - the one Joey actually hit.
    /// </summary>
    public static readonly (string Addon, string Label)[] BlockingAddons =
    [
        ("ItemSearch", "the market board"),
        ("ItemSearchResult", "the market board"),
        ("RetainerList", "the retainer bell"),
        ("InventoryRetainer", "a retainer's inventory"),
        ("RetainerSellList", "your retainer's sale list"),
        ("RetainerSell", "a market-board listing window"),
        ("Shop", "a shop window"),
        ("GrandCompanySupplyList", "the grand company supply window"),
        ("Talk", "a dialogue box"),
        ("SelectString", "a dialogue choice"),
        ("SelectIconString", "a dialogue choice"),
        ("SelectYesno", "a yes/no prompt"),
        ("Bank", "the bank window"),
        ("Trade", "a trade window"),
    ];

    /// <summary>
    /// Condition flags that mean "the game is not taking commands", with no crafting flag among them.
    /// Used as the fallback signal when no addon in <see cref="BlockingAddons"/> is open but the client is
    /// nonetheless occupied - so a window we do not know by name still produces a truthful (if vaguer) answer.
    /// </summary>
    public static readonly ConditionFlag[] BusyFlags =
    [
        ConditionFlag.Occupied,
        ConditionFlag.Occupied30,
        ConditionFlag.Occupied33,
        ConditionFlag.Occupied38,
        ConditionFlag.Occupied39,
        ConditionFlag.OccupiedInEvent,
        ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.OccupiedSummoningBell,
        ConditionFlag.TradeOpen,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
    ];

    private readonly ICondition _condition;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;

    public ClientReadiness(ICondition condition, IGameGui gameGui, IPluginLog log)
    {
        _condition = condition;
        _gameGui = gameGui;
        _log = log;
    }

    /// <summary>
    /// The window/state currently holding the client's input, or <c>null</c> when the client can accept a command.
    /// Never throws: an unexpected failure reads as "not busy", so this can only ever soften a diagnosis, never
    /// invent one.
    /// </summary>
    public string? BusyBecause()
    {
        try
        {
            foreach (var (addon, label) in BlockingAddons)
                if (_gameGui.GetAddonByName(addon, 1).Address != nint.Zero)
                    return label;

            foreach (var flag in BusyFlags)
                if (_condition[flag])
                    return LabelFor(flag);

            return null;
        }
        catch (Exception ex)
        {
            _log.Debug("ClientReadiness.BusyBecause failed: {Msg}", ex.Message);
            return null;
        }
    }

    /// <summary>Readable wording for a condition flag we had to fall back to, so the chat line still says something true.</summary>
    private static string LabelFor(ConditionFlag flag) => flag switch
    {
        ConditionFlag.TradeOpen => "a trade window",
        ConditionFlag.OccupiedSummoningBell => "the summoning bell",
        ConditionFlag.WatchingCutscene or ConditionFlag.WatchingCutscene78 or ConditionFlag.OccupiedInCutSceneEvent => "a cutscene",
        ConditionFlag.BetweenAreas or ConditionFlag.BetweenAreas51 => "a zone change",
        ConditionFlag.OccupiedInQuestEvent or ConditionFlag.OccupiedInEvent => "a quest event",
        _ => "a game window",
    };
}
