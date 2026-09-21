using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lalalazy.Crucible;

namespace LazyCrucible;

/// <summary>
///     Where the run stands, for the selection policies and the guide: the board (from the territory), the battles
///     fought this run (the panel battle of the hostile enemies while in combat), the fight the board's stage detail is
///     focused on (the same identification the Battlehorn pass uses), the run roster with its HP, and the player's HP.
///     Read-only. A run starts when a board territory is entered from outside the boards; a suspended and resumed run
///     starts over (everything on the board then counts as ahead — the safe direction for survival picks).
/// </summary>
internal static class RunTracker
{
    public static int Board { get; private set; }

    /// <summary> Increments on every run start (a board entered from outside the boards). </summary>
    public static int RunId { get; private set; }
    public static int LastBattle { get; private set; } = -1;
    public static List<int> Fought { get; } = [];

    /// <summary> The fight the stage detail / horn screen points at: (board, battle), battle -1 = none. </summary>
    public static (int Board, int Battle) Focus { get; private set; } = (0, -1);

    public static List<int> Roster { get; private set; } = [];
    public static Dictionary<int, int> RosterHp { get; private set; } = [];

    private static long _nextCombatScan;
    private static long _nextFocusScan;
    private static long _nextRosterScan;

    public static void Reset()
    {
        Board = 0;
        LastBattle = -1;
        Fought.Clear();
        Focus = (0, -1);
        Roster = [];
        RosterHp = [];
    }

    public static void Tick()
    {
        var now = Environment.TickCount64;
        var board = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        if (board != 0 && board != Board)
        {
            Board = board;
            RunId++;
            LastBattle = -1;
            Fought.Clear();
            CrucibleLog.Line($"RT|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|run=start|b={board}");
        }
        else if (board == 0 && Board != 0 && Svc.ClientState.TerritoryType != 0)
        {
            Board = 0; // left the boards: the next board entry is a new run
        }

        if (board != 0 && now >= _nextCombatScan && Svc.Condition[ConditionFlag.InCombat])
        {
            _nextCombatScan = now + 1000;
            var battle = CrucibleGame.CurrentBattle();
            if (battle >= 0 && battle != LastBattle)
            {
                LastBattle = battle;
                if (!Fought.Contains(battle))
                    Fought.Add(battle);
                CrucibleLog.Line($"RT|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|fight|b={board}|bt={battle}|fought={string.Join(".", Fought)}");
            }
        }

        if (now >= _nextFocusScan)
        {
            _nextFocusScan = now + 250;
            Focus = ReadFocus(board);
        }

        if (now >= _nextRosterScan)
        {
            _nextRosterScan = now + 1000;
            if (board != 0 && PetSelect.TryReadRunRoster(out var rows, out var hp))
            {
                Roster = rows;
                RosterHp = hp;
            }
        }
    }

    private static unsafe (int, int) ReadFocus(int territoryBoard)
    {
        if (!ScreenReader.IsVisible("XBMStageDetailList") && !ScreenReader.IsVisible("XBMPetParty"))
            return (0, -1);
        var stage = PetSelect.GetAgent(AgentId.XBMStageDetailList);
        if (stage == 0)
            return (0, -1);
        var content = (int)*(uint*)(stage + 0x38);
        var boardHint = territoryBoard != 0 ? territoryBoard : content is >= 1 and <= 5 ? content : 0;
        return PetSelect.TryIdentifyBattle(stage, boardHint, out var b, out var bt, out _, out _) ? (b, bt) : (0, -1);
    }

    public static int PlayerHpPercent
    {
        get
        {
            var me = Svc.Objects.LocalPlayer;
            return me is null || me.MaxHp == 0 ? 100 : (int)Math.Round(100.0 * me.CurrentHp / me.MaxHp);
        }
    }

    /// <summary> The policies' view of the run right now. </summary>
    public static RunContext Context(CrucibleGuide guide, ScoreGoal goal)
    {
        var board = Board != 0 ? Board : Focus.Board;
        var alive = Roster.Where(r => RosterHp.GetValueOrDefault(r, 100) > 0).ToList();
        return RunContext.Build(board, LastBattle, guide.ThreatsOf, alive, RosterHp, PlayerHpPercent, goal);
    }
}
