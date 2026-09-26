using System.Globalization;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace LazyCrucible;

/// <summary>
///     The selection screens the player opens during a run: Beast Feed picker, shop, treasure coffer, spoils and the
///     campsite's rest picker. Each has its own switch. A screen is acted on once per open, one input at a time, and every
///     input is checked before it is sent (<see cref="ActuationGuard"/>), confirmed only on the Yes/No prompt that input
///     opened (<see cref="PromptGuard"/>), and read back from the screen afterwards; any mismatch, an input the player
///     made on the same screen, or AutoDuty running stands the automation down for the rest of that open. Nothing here
///     walks, enters, commences, starts a battle, presses Rest, suspends, forfeits, sells or closes a screen.
///     Log lines: <c>SL|ms|screen=..|...</c> (decision, input, read-back) through <see cref="CrucibleLog"/>.
/// </summary>
internal static unsafe class SelectionScreens
{
    private const long PromptWindowMs = 3000;
    private const long ReadbackWindowMs = 3000;
    private const long StepGapMs = 700;
    private const long SettleMs = 400;

    /// <summary> Recent decisions for the windows: (when, screen, text). </summary>
    internal static readonly List<(DateTime At, string Screen, string Text)> Recent = [];

    /// <summary> One-line status for the main window. </summary>
    internal static string Status { get; private set; } = "";

    private static readonly ScreenLatch Shop = new(), Feed = new(), Camp = new(), Treasure = new(), Booty = new();
    private static readonly FeedMemory Memory = new();
    private static readonly HashSet<int> ShopFailed = [];

    private static Pending? _pending;
    private static long _nextStepMs;
    private static long _openedMs;
    private static ShopScreen? _lastShop;
    private static int _offeredFeedRow;
    private static int _plannedFeedFamiliar;
    private static int _shopPurchases;
    private static string? _externalEdit;
    private static int _runId;
    private static string _lastSuggestion = "";

    /// <summary> Suggestion shown over a screen (addon name → text) when the automation leaves the choice to the player. </summary>
    internal static readonly Dictionary<string, string> Suggestions = [];

    private sealed class Pending
    {
        public required string Screen;
        public required string What;
        public required long FiredMs;
        public required PromptGuard.Kind Prompt;
        public required string ExpectName;
        public string[] AlsoExpect = [];
        public required Func<bool> Done;
        public Action? OnSuccess;
        public bool Answered;
        public long AnsweredMs;
        public ScreenLatch? Latch;
    }

    private static long Now => Environment.TickCount64;
    private static long UnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void Reset()
    {
        _pending = null;
        Memory.Reset();
        ShopFailed.Clear();
        _lastShop = null;
        _offeredFeedRow = 0;
        _plannedFeedFamiliar = 0;
        _shopPurchases = 0;
        Suggestions.Clear();
    }

    /// <summary>
    ///     The player clicked a shop entry (callback [2, index], seen by the FireCallback hook with no input of ours on
    ///     the stack). A feed entry opens the Beast Feed picker, which never names the feed, so this is how the picker
    ///     knows what it is feeding; any purchase by hand also hands the rest of this shop visit to the player.
    /// </summary>
    internal static void OnPlayerShopCallback(int kind, int index)
    {
        if (kind != 2)
            return;
        // Inside the click (game thread): the shop's values still show the entry as it was clicked.
        var shop = _lastShop ?? (ScreenReader.TryCells("XBMContentsItemShop", out var cells) ? Screens.ReadShop(cells) : null);
        if (shop is null)
            return;
        _lastShop ??= shop;
        var entry = shop.Stock.FirstOrDefault(o => o.Index == index);
        if (entry.Row <= 0)
            return;
        if (CrucibleItems.Get(entry.Row).Type == CrucibleItemType.Feed)
        {
            _offeredFeedRow = entry.Row;
            _plannedFeedFamiliar = 0;
        }
        if (Shop.Open && !Shop.StoodDown)
        {
            Shop.StandDown("player purchase");
            _pending = null;
        }
        Log($"screen=shop|note=player_click|idx={index}|row={entry.Row}");
    }

    /// <summary> A familiar-agent event the plugin did not send (from <see cref="AgentProbe"/>). </summary>
    internal static void OnAgentEvent(string tag, ulong kind, uint valueCount, int? firstInt)
    {
        if (OwnCalls.Depth == 0 && tag == "pp" && FormationLogic.IsSelectionEditEvent(tag, kind, valueCount, firstInt))
            _externalEdit = $"pp kind={kind} v0={firstInt?.ToString(CultureInfo.InvariantCulture) ?? ""}";
    }

    public static void Tick(string? yieldReason)
    {
        if (RunTracker.RunId != _runId)
        {
            _runId = RunTracker.RunId;
            Reset();
        }

        var cfg = Plugin.Config;
        var (ppMode, _) = ScreenReader.IsVisible("XBMPetParty") ? PetSelect.ReadPetPartyAddonMode() : (-1, -1);
        var feedOpen = ppMode == 3;
        var campOpen = ppMode == 4;
        var shopOpen = ScreenReader.IsVisible("XBMContentsItemShop");
        var treasureOpen = ScreenReader.IsVisible("XBMContentsTreasure");
        var bootyOpen = ScreenReader.IsVisible("XBMContentsBooty");

        if (Feed.Update(feedOpen) | Camp.Update(campOpen) | Shop.Update(shopOpen) | Treasure.Update(treasureOpen) | Booty.Update(bootyOpen))
            _openedMs = Now;
        if (!shopOpen)
        {
            _lastShop = null;
            _shopPurchases = 0;
            ShopFailed.Clear();
        }
        foreach (var name in Suggestions.Keys.ToList())
            if (!ScreenReader.IsVisible(name))
                Suggestions.Remove(name);

        // Remember what each familiar ate whenever a familiar screen is up (feeds the shop's planning).
        if ((feedOpen || campOpen) && ScreenReader.TryCells("XBMPetParty", out var ppCells))
            Memory.Update(Screens.ReadPetParty(ppCells));

        ConsumeExternalEdit(feedOpen ? Feed : campOpen ? Camp : null);

        if (yieldReason is not null)
        {
            if (_pending is not null)
                Log($"screen={_pending.Screen}|abort=yield|why={yieldReason}");
            _pending = null;
            foreach (var l in new[] { Shop, Feed, Camp, Treasure, Booty })
                if (l.Open && !l.StoodDown)
                    l.StandDown(yieldReason);
            return;
        }

        try
        {
            if (_pending is not null)
            {
                Advance(_pending);
                return;
            }
            if (Now < _nextStepMs || Now - _openedMs < SettleMs || ScreenReader.IsVisible("SelectYesno"))
                return;

            if (feedOpen)
            {
                if (cfg.AutoFeed && Feed.MayAct)
                    FeedPass();
            }
            else if (campOpen)
            {
                if (cfg.AutoCamp && Camp.MayAct)
                    CampPass();
            }
            else if (shopOpen)
            {
                if (cfg.AutoShop && Shop.MayAct)
                    ShopPass();
                else
                    WatchShop();
            }
            else if (treasureOpen)
            {
                if (cfg.AutoTreasure && Treasure.MayAct)
                    TreasurePass();
            }
            else if (bootyOpen)
            {
                if (cfg.AutoSpoils && Booty.MayAct)
                    BootyPass();
            }
        }
        catch
        {
            // Drop the input in flight, then let the plugin's tick.screens breaker (src/Shared/LalaTelemetry) write the
            // ER| line and, after repeated failures, pause the selection screens with one chat notice.
            _pending = null;
            throw;
        }
    }

    // ------------------------------------------------------------------ pending input: prompt, then read-back

    private static void Advance(Pending p)
    {
        if (p.Latch is { Open: false })
        {
            // The screen closed under the input (the feed picker and the spoils close once confirmed). Only an input
            // this plugin confirmed (or one that needs no prompt) can succeed that way.
            if ((p.Answered || p.Prompt == PromptGuard.Kind.None) && p.Done())
                Succeed(p);
            else
            {
                Log($"screen={p.Screen}|{p.What}|readback=closed");
                _pending = null;
            }
            return;
        }

        if (!p.Answered && p.Prompt != PromptGuard.Kind.None)
        {
            if (ScreenReader.TryGet("SelectYesno", out var yes) && yes->IsReady)
            {
                var text = ScreenReader.YesNoText();
                var (ok, why) = PromptGuard.Check(p.Prompt, text, [p.ExpectName, .. p.AlsoExpect]);
                if (!ok)
                {
                    Log($"screen={p.Screen}|{p.What}|prompt=refused|why={why}|text={Clean(text)}");
                    SendYesNo(yes, yes: false);
                    Fail(p, $"the confirmation did not match ({why})");
                    return;
                }
                SendYesNo(yes, yes: true);
                p.Answered = true;
                p.AnsweredMs = Now;
                Log($"screen={p.Screen}|{p.What}|prompt=yes|text={Clean(text)}");
                return;
            }
            if (Now - p.FiredMs > PromptWindowMs)
            {
                if (p.Done())
                    Succeed(p);
                else
                    Fail(p, "no confirmation appeared");
            }
            return;
        }

        if (p.Done())
        {
            Succeed(p);
            return;
        }
        if (Now - (p.Answered ? p.AnsweredMs : p.FiredMs) > ReadbackWindowMs)
            Fail(p, "the screen did not change as expected");
    }

    private static void Succeed(Pending p)
    {
        Log($"screen={p.Screen}|{p.What}|readback=ok");
        _pending = null;
        _nextStepMs = Now + StepGapMs;
        p.OnSuccess?.Invoke();
    }

    private static void Fail(Pending p, string why)
    {
        Log($"screen={p.Screen}|{p.What}|readback=fail|why={why}");
        _pending = null;
        p.Latch?.StandDown(why);
        Say(p.Screen, $"Stopped: {why}. The rest of this screen is yours.", error: true);
    }

    private static void ConsumeExternalEdit(ScreenLatch? latch)
    {
        var edit = _externalEdit;
        _externalEdit = null;
        if (edit is null || latch is not { Open: true } || latch.StoodDown || PickerTiming.IsOpenEcho(Now - _openedMs))
            return;
        latch.StandDown("player edit");
        _pending = null;
        Log($"screen=pp|note=player_edit|{edit}");
    }

    // ------------------------------------------------------------------ passes

    private static RunContext Context() => RunTracker.Context(Plugin.Guide, Plugin.Config.ScoreGoal);

    private static List<FamiliarState> RosterFamiliars() => Memory.Familiars(RunTracker.Roster, RunTracker.RosterHp);

    private static void ShopPass()
    {
        if (!ScreenReader.TryCells("XBMContentsItemShop", out var cells))
            return;
        var shop = Screens.ReadShop(cells);
        if (DetectPlayerPurchase(shop))
            return;
        _lastShop = shop;

        if (_shopPurchases >= ItemPolicies.MaxPurchasesPerVisit)
        {
            Shop.Finish("purchase limit");
            return;
        }
        var ctx = Context();
        var choice = ItemPolicies.NextPurchase(shop.Stock, shop.Tokens, shop.Inventory, RosterFamiliars(), ctx, ShopFailed);
        if (choice is not { } c)
        {
            Shop.Finish();
            Say("Shop", _shopPurchases == 0 ? $"Nothing worth buying ({shop.Tokens} tokens)." : $"Done: {_shopPurchases} bought, {shop.Tokens} tokens left.");
            return;
        }

        if (!ScreenReader.TryGet("XBMContentsItemShop", out var addon))
            return;
        var values = new[] { 2, c.Index };
        if (!ActuationGuard.Allowed("XBMContentsItemShop", ActuationGuard.Input.Callback, values))
        {
            Shop.StandDown("guard refused");
            return;
        }

        var tokensBefore = shop.Tokens;
        var isFeed = CrucibleItems.Get(c.Row).Type == CrucibleItemType.Feed;
        Say("Shop", $"Buying {c.Reason} ({c.Price} of {shop.Tokens} tokens)");
        Log($"screen=shop|decide|idx={c.Index}|row={c.Row}|price={c.Price}|tokens={shop.Tokens}|score={c.Score}|feed={(isFeed ? c.Feed?.FamiliarRow ?? 0 : 0)}");
        Fire(addon, values);
        ShopFailed.Add(c.Index); // tried once per visit whatever happens
        _shopPurchases++;
        if (isFeed)
        {
            _offeredFeedRow = c.Row;
            _plannedFeedFamiliar = c.Feed?.FamiliarRow ?? 0;
        }

        _pending = new Pending
        {
            Screen = "shop",
            What = $"buy|idx={c.Index}|row={c.Row}",
            FiredMs = Now,
            // A feed entry opens the Beast Feed picker straight away; its prompt comes after the familiar is picked.
            Prompt = isFeed ? PromptGuard.Kind.None : PromptGuard.Kind.Purchase,
            ExpectName = CrucibleItems.NameOf(c.Row),
            Latch = Shop,
            Done = () =>
            {
                if (isFeed && ScreenReader.IsVisible("XBMPetParty"))
                    return true; // the feed picker opened: the purchase went through
                if (!ScreenReader.TryCells("XBMContentsItemShop", out var after))
                    return false;
                var s = Screens.ReadShop(after);
                var entry = s.Stock.FirstOrDefault(o => o.Index == c.Index);
                return entry.Bought || s.Tokens < tokensBefore;
            },
            OnSuccess = () =>
            {
                if (ScreenReader.TryCells("XBMContentsItemShop", out var after))
                    _lastShop = Screens.ReadShop(after);
            },
        };
    }

    /// <summary> Watch the shop while its switch is off: a feed bought by hand still tells the picker which feed it is. </summary>
    private static void WatchShop()
    {
        if (ScreenReader.TryCells("XBMContentsItemShop", out var cells))
        {
            var shop = Screens.ReadShop(cells);
            DetectPlayerPurchase(shop, standDown: false);
            _lastShop = shop;
        }
    }

    /// <summary> An entry bought that this plugin did not buy: the player is shopping (and, for feed, it is the offered feed). </summary>
    private static bool DetectPlayerPurchase(ShopScreen shop, bool standDown = true)
    {
        if (_lastShop is null)
            return false;
        foreach (var o in shop.Stock)
        {
            var before = _lastShop.Stock.FirstOrDefault(x => x.Index == o.Index);
            if (!o.Bought || before.Bought || ShopFailed.Contains(o.Index))
                continue; // entries this plugin bought (or tried) are not the player's
            if (CrucibleItems.Get(o.Row).Type == CrucibleItemType.Feed)
            {
                _offeredFeedRow = o.Row;
                _plannedFeedFamiliar = 0;
            }
            _lastShop = shop;
            if (standDown && Shop.Open && !Shop.StoodDown)
            {
                Shop.StandDown("player purchase");
                Log($"screen=shop|note=player_purchase|idx={o.Index}|row={o.Row}");
            }
            return true;
        }
        return false;
    }

    private static void FeedPass()
    {
        if (!ScreenReader.TryCells("XBMPetParty", out var cells))
            return;
        var screen = Screens.ReadPetParty(cells);
        if (screen.Familiars.Count == 0)
            return;
        var feedRow = _offeredFeedRow;
        if (feedRow == 0 || CrucibleItems.Get(feedRow).Type != CrucibleItemType.Feed)
        {
            Feed.Finish("feed unknown");
            Suggest("XBMPetParty", "Feed picker: LazyCrucible did not see which feed was bought, so the choice is yours.");
            return;
        }

        var ctx = Context();
        var choice = FeedPolicy.BestTarget(feedRow, screen.Familiars, ctx);
        if (_plannedFeedFamiliar != 0 && screen.Familiars.FirstOrDefault(f => f.Row == _plannedFeedFamiliar) is { Row: > 0 } planned
            && FeedPolicy.CanEat(planned, feedRow, out _) && choice is { } best && best.FamiliarRow != planned.Row
            && FeedPolicy.Weighted(feedRow, planned, ctx) >= best.Score)
            choice = best with { FamiliarRow = planned.Row, FamiliarIndex = planned.Index };

        // The player (or the shop pass) chose this feed; it is paid for only when the familiar is confirmed. Anything
        // that does no harm goes to the best candidate; a feed that hurts every candidate is left to the player.
        if (choice is not { Score: >= 0 } c)
        {
            Feed.Finish("no target");
            var why = choice is null ? "no familiar can eat it" : "it would do more harm than good";
            Suggest("XBMPetParty", $"{CrucibleItems.NameOf(feedRow)}: {why}; the choice is yours.");
            Say("Feed", $"{CrucibleItems.NameOf(feedRow)}: {why}; leaving the choice to you.");
            return;
        }

        var pet = PetSelect.GetAgent(AgentId.XBMPetParty);
        var values = new[] { 1, c.FamiliarIndex };
        if (pet == 0 || !PetSelect.EnsureReceiveEvent(pet) || !ActuationGuard.Allowed("XBMPetParty", ActuationGuard.Input.AgentEvent, values))
        {
            Feed.StandDown("no input route");
            return;
        }
        var target = screen.Familiars.First(f => f.Index == c.FamiliarIndex);
        var shownName = cells[Screens.PetBlockStart + c.FamiliarIndex * Screens.PetBlockSize + 3].Text ?? target.Name;
        if (!FeedPolicy.KinFlagsAgree(screen.Familiars, feedRow))
        {
            Feed.Finish("feed mismatch");
            Suggest("XBMPetParty", $"The picker's 'cannot eat' marks do not fit {CrucibleItems.NameOf(feedRow)}; the choice is yours.");
            Log($"screen=feed|abort=kin_flags_disagree|feed={feedRow}");
            return;
        }
        Say("Feed", c.Reason);
        Log($"screen=feed|decide|feed={feedRow}|pet={c.FamiliarRow}|idx={c.FamiliarIndex}|score={c.Score}");
        OwnCalls.Depth++;
        try
        {
            PetSelect.FireEventToggle((AgentInterface*)pet, c.FamiliarIndex);
        }
        finally
        {
            OwnCalls.Depth--;
        }
        Feed.CountAction();

        var fedRow = c.FamiliarRow;
        _pending = new Pending
        {
            Screen = "feed",
            What = $"feed|row={feedRow}|pet={fedRow}|idx={c.FamiliarIndex}",
            FiredMs = Now,
            Prompt = PromptGuard.Kind.Feed,
            ExpectName = CrucibleItems.NameOf(feedRow),
            AlsoExpect = [shownName],
            Latch = Feed,
            Done = () =>
            {
                // The picker closes after a feed; while it is still up the familiar's satiety or feed list shows it.
                if (!ScreenReader.TryCells("XBMPetParty", out var after))
                    return true;
                var s = Screens.ReadPetParty(after);
                if (s.Mode != 3)
                    return true;
                var f = s.Familiars.FirstOrDefault(x => x.Row == fedRow);
                return f.Row == fedRow && (f.Ate(feedRow) || f.SatietyUsed > target.SatietyUsed);
            },
            OnSuccess = () =>
            {
                Memory.RecordFed(fedRow, feedRow);
                _offeredFeedRow = 0;
                _plannedFeedFamiliar = 0;
                Feed.Finish();
            },
        };
    }

    private static void CampPass()
    {
        if (!ScreenReader.TryCells("XBMPetParty", out var cells))
            return;
        var screen = Screens.ReadPetParty(cells);
        if (screen.Familiars.Count == 0)
            return;
        if (Camp.Actions == 0 && screen.Familiars.Any(f => f.RestSelected))
        {
            Camp.StandDown("already selected");
            return;
        }

        var ctx = Context();
        var capacity = CampCapacity();
        var choice = CampPolicy.Choose(screen.Familiars, capacity, ctx);
        var todo = choice.Indices.Where(i => !screen.Familiars.First(f => f.Index == i).RestSelected).ToList();
        if (todo.Count == 0)
        {
            Camp.Finish();
            Say("Campsite", choice.Reason + ". Press Rest when ready.");
            Suggest("XBMPetParty", choice.Reason + " — press Rest.");
            return;
        }

        var idx = todo[0];
        var pet = PetSelect.GetAgent(AgentId.XBMPetParty);
        var values = new[] { 1, idx };
        if (pet == 0 || !PetSelect.EnsureReceiveEvent(pet) || !ActuationGuard.Allowed("XBMPetParty", ActuationGuard.Input.AgentEvent, values))
        {
            Camp.StandDown("no input route");
            return;
        }
        var row = screen.Familiars.First(f => f.Index == idx).Row;
        Log($"screen=camp|decide|pick={row}|idx={idx}|cap={capacity}|plan={string.Join(".", choice.Rows)}");
        OwnCalls.Depth++;
        try
        {
            PetSelect.FireEventToggle((AgentInterface*)pet, idx);
        }
        finally
        {
            OwnCalls.Depth--;
        }
        Camp.CountAction();
        _pending = new Pending
        {
            Screen = "camp",
            What = $"rest|pet={row}|idx={idx}",
            FiredMs = Now,
            Prompt = PromptGuard.Kind.None,
            ExpectName = "",
            Latch = Camp,
            Done = () => ScreenReader.TryCells("XBMPetParty", out var after)
                         && Screens.ReadPetParty(after).Familiars.FirstOrDefault(f => f.Index == idx).RestSelected,
        };
    }

    /// <summary> The campsite's familiar limit: the nearest campsite ahead on the board graph (the screen does not show it). </summary>
    private static int CampCapacity()
    {
        var camps = CrucibleBoards.CampsAhead(RunTracker.Board, RunTracker.LastBattle);
        return camps.Count == 0 ? 1 : camps.Min();
    }

    private static void TreasurePass()
    {
        if (!ScreenReader.TryCells("XBMContentsTreasure", out var cells))
            return;
        var screen = Screens.ReadTreasure(cells);
        if (screen.Choices.Count == 0)
            return;
        var ctx = Context();
        var pick = ItemPolicies.TreasurePick(screen.Choices, screen.Inventory, RosterFamiliars(), ctx);
        if (pick is not { } p)
        {
            Treasure.Finish("nothing worth taking");
            Suggest("XBMContentsTreasure", "Nothing here helps the fights ahead (or your slots are full); the choice is yours.");
            Say("Treasure", "Nothing here helps the fights ahead (or your slots are full); leaving the choice to you.");
            return;
        }
        if (!TreasureActuation.Grounded)
        {
            Treasure.Finish("suggestion only");
            Suggest("XBMContentsTreasure", $"Take {p.Reason}");
            Say("Treasure", $"Suggest: {p.Reason}");
            return;
        }

        if (!ScreenReader.TryGet("XBMContentsTreasure", out var addon))
            return;
        var param = ActuationGuard.TreasureFirstParam + p.Index;
        if (!ActuationGuard.Allowed("XBMContentsTreasure", ActuationGuard.Input.Button, [], eventParam: param)
            || !Buttons.ClickByParam(addon, param))
        {
            Treasure.StandDown("choice button not found");
            Suggest("XBMContentsTreasure", $"Take {p.Reason}");
            return;
        }
        Say("Treasure", $"Taking {p.Reason}");
        Log($"screen=treasure|decide|choice={p.Index}|row={p.Row}|param={param}|score={p.Score}");
        Treasure.CountAction();
        var row = p.Row;
        _pending = new Pending
        {
            Screen = "treasure",
            What = $"take|choice={p.Index}|row={row}",
            FiredMs = Now,
            Prompt = PromptGuard.Kind.Treasure,
            ExpectName = CrucibleItems.NameOf(row),
            Latch = Treasure,
            Done = () => HudHolds(row) || !ScreenReader.IsVisible("XBMContentsTreasure"),
            OnSuccess = () => Treasure.Finish(),
        };
    }

    private static bool HudHolds(int row)
    {
        if (!ScreenReader.TryCells("XBMContentsMainHUD", out var hud))
            return false;
        for (var s = 0; s < 10; s++)
            if (hud[9 + 5 * s + 3].Int == row)
                return true;
        for (var s = 0; s < 10; s++)
            if (hud[60 + 5 * s + 3].Int == row)
                return true;
        return false;
    }

    private static void BootyPass()
    {
        if (!ScreenReader.TryCells("XBMContentsBooty", out var cells))
            return;
        var screen = Screens.ReadBooty(cells);
        var open = screen.Loot.Where(l => !l.Taken).Select(l => (l.Index, l.Row)).ToList();
        if (open.Count == 0)
        {
            Booty.Finish();
            return;
        }
        var ctx = Context();
        var choice = ItemPolicies.Spoils(open, screen.Inventory, RosterFamiliars(), ctx);
        if (!choice.TakeAll)
        {
            Booty.Finish("not all fit");
            Suggest("XBMContentsBooty", choice.Reason);
            Say("Spoils", choice.Reason);
            return;
        }
        if (!ScreenReader.TryGet("XBMContentsBooty", out var addon))
            return;
        if (!ActuationGuard.Allowed("XBMContentsBooty", ActuationGuard.Input.Button, [], node: ActuationGuard.BootyTakeAllNode)
            || !Buttons.ClickNode(addon, ActuationGuard.BootyTakeAllNode))
        {
            Booty.StandDown("Take all button not found");
            Suggest("XBMContentsBooty", choice.Reason);
            return;
        }
        Say("Spoils", choice.Reason);
        Log($"screen=booty|decide|take_all|rows={string.Join(".", open.Select(o => o.Row))}");
        Booty.CountAction();
        var indices = open.Select(o => o.Index).ToList();
        _pending = new Pending
        {
            Screen = "booty",
            What = $"take_all|n={open.Count}",
            FiredMs = Now,
            Prompt = PromptGuard.Kind.TakeAll,
            ExpectName = "",
            AlsoExpect = open.Select(o => CrucibleItems.NameOf(o.Row)).ToArray(),
            Latch = Booty,
            Done = () =>
            {
                if (!ScreenReader.TryCells("XBMContentsBooty", out var after))
                    return true; // the spoils screen closes once everything is taken
                var s = Screens.ReadBooty(after);
                return indices.All(i => s.Loot.FirstOrDefault(l => l.Index == i).Taken);
            },
            OnSuccess = () => Booty.Finish(),
        };
    }

    // ------------------------------------------------------------------ inputs

    private static void Fire(AtkUnitBase* addon, int[] values)
    {
        OwnCalls.Depth++;
        try
        {
            ECommons.Automation.Callback.Fire(addon, true, values.Cast<object>().ToArray());
        }
        finally
        {
            OwnCalls.Depth--;
        }
    }

    private static void SendYesNo(AtkUnitBase* yesno, bool yes)
    {
        var values = new[] { yes ? 0 : 1 };
        if (!ActuationGuard.Allowed("SelectYesno", ActuationGuard.Input.Callback, values))
            return;
        Fire(yesno, values);
    }

    // ------------------------------------------------------------------ output

    private static void Log(string body) => CrucibleLog.Line($"SL|{UnixMs}|{body}");

    private static void Say(string screen, string text, bool error = false)
    {
        Recent.Add((DateTime.Now, screen, text));
        if (Recent.Count > 60)
            Recent.RemoveAt(0);
        Status = $"{screen}: {text}";
        if (!Plugin.Config.AnnouncePicks && !error)
            return;
        if (error)
            Svc.Chat.PrintError($"[LazyCrucible] {screen}: {text}");
        else
            Svc.Chat.Print($"[LazyCrucible] {screen}: {text}");
    }

    private static void Suggest(string addon, string text)
    {
        Suggestions[addon] = text;
        if (text != _lastSuggestion)
            _lastSuggestion = text;
    }

    private static string Clean(string s)
    {
        var t = s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
        return t.Length > 120 ? t[..120] : t;
    }
}
