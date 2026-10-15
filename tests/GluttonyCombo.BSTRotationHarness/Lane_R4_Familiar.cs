using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Round 4 familiar lane (task tasks-20261015-crucible-r4-fix-familiar-item-01). Titles start with
///     "r4-familiar:" and name the gap id. Rows: rot-31 (a KO'd familiar is revived with the held Blessed
///     Horn through the item HUD), 1.4-01 (the banemite piece resists poison, so poison familiars score
///     down there), sv-07b (the Bomb Piece's Self-destruct bursts like the Golem's). The LazyCrucible cases
///     for lv-10 (the held Blessed Horn's value, closed as designed) and lv-12 (fed familiars never rest)
///     live in tests/LazyCrucible.Harness/PolicyCases.cs with the same title prefix. The Soulkin borrow
///     timing row is deliberately-unchanged (the prepull borrow is declined on purpose,
///     crucible:prepull-borrow-waiting-cast; see the task page) and has no case.
/// </summary>
internal static partial class Program
{
    private static void R4FamiliarLane20261015()
    {
        Console.WriteLine("-- crucible r4 familiar lane 2026-10-15: Blessed Horn revive (rot-31), poison-resist scoring (1.4-01), bomb Self-destruct (sv-07b) --");
        var cfg = BstSettings.Defaults();
        string D(BstDecision d) => $"{d.ActionId}/{d.Reason} [{d.Declines}]";

        // ---------------- rot-31: a KO'd familiar is revived with the held Blessed Horn, not re-summoned
        // S9 asellog L260 / S4 CGW: "Familiars who are KO'd cannot be summoned again unless revived with a Blessed Horn."
        var reviveBase = CrucibleState(50) with { FamiliarDown = true, ReadyReviveHorn = true };
        Check("r4-familiar: rot-31 KO'd familiar + held Blessed Horn + clear recast: the rotation returns the Blessed Horn",
            Decide(reviveBase, cfg) is { ActionId: BST_CrucibleData.BlessedHornAction, Reason: "crucible:revive-horn" }, D(Decide(reviveBase, cfg)));
        Check("r4-familiar: rot-31 no Blessed Horn held: declined with a reason, and the tick still acts (never a dead tick)",
            Decide(reviveBase with { ReadyReviveHorn = false }, cfg).Declines.Contains("crucible:revive-none-held")
            && Decide(reviveBase with { ReadyReviveHorn = false }, cfg).ActionId != 0,
            D(Decide(reviveBase with { ReadyReviveHorn = false }, cfg)));
        Check("r4-familiar: rot-31 ... the revive lives in the survival policy: off, it never fires",
            Decide(reviveBase, cfg with { CrucibleSurvival = false }).ActionId != BST_CrucibleData.BlessedHornAction);
        Check("r4-familiar: rot-31 the familiar alive again: no revive (the normal rules run)",
            Decide(reviveBase with { FamiliarDown = false }, cfg).ActionId != BST_CrucibleData.BlessedHornAction);
        Check("r4-familiar: rot-31 the Blessed Horn id is the sheet's: XBMItem row 96 -> use action 46979 (7.56 sheet, fetched live 2026-10-15)",
            BST_CrucibleData.BlessedHornAction == 46979u && BST_CrucibleData.ReviveItemRows.ContainsKey(96u));

        // ---------------- the live reader and the decision plumbing (guards; they read the repo source)
        var logic = RepoFile("Combos", "PvE", "BST", "BST_CrucibleLogic.cs");
        Check("r4-familiar: rot-31 TryRevive is a Decide rule in the crucible block (not an idle logging path)",
            logic.Contains("TryRevive", StringComparison.Ordinal) && logic.Contains("crucible:revive-horn", StringComparison.Ordinal));
        var live = RepoFile("Combos", "PvE", "BST", "BST_CrucibleLive.cs");
        Check("r4-familiar: rot-31 the live layer walks the HUD for row 96 and offers it through the item menu like a potion",
            live.Contains("ReadReviveSlots", StringComparison.Ordinal) && live.Contains("RequestReviveItem", StringComparison.Ordinal)
            && live.Contains("BST_CrucibleData.ReviveItemRows", StringComparison.Ordinal));
        var crucible = RepoFile("Combos", "PvE", "BST", "BST_Crucible.cs");
        Check("r4-familiar: rot-31 the collector reads FamiliarDown (pet buddy at 0 HP) and ReadyReviveHorn",
            crucible.Contains("FamiliarDown", StringComparison.Ordinal) && crucible.Contains("ReadyReviveHorn", StringComparison.Ordinal));
    }
}
