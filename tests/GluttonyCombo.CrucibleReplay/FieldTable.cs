using System.Reflection;
using GluttonyCombo.Combos.PvE;

namespace GluttonyCombo.CrucibleReplay;

internal enum Kind { Rebuilt, Derivable, Missing }

/// <summary>
///     One row per <c>BstState</c> field: can the CR| / BT| / DS| lines rebuild it?
///     Rebuilt = a logged value maps straight on. Derivable = computed from logged values by a stated rule (approximate).
///     Missing = not logged. A Missing field (and a Rebuilt field whose BT| line is absent) is a gap: the replay sets it to the
///     first candidate (the baseline) and probes the others, so a decision that depends on it is reported as gap-sensitive.
///     <see cref="Check"/> fails the run when this table and the struct disagree, so a new BstState field cannot slip in unclassified.
/// </summary>
internal sealed record FieldRow(string Name, Kind Kind, string Source, string Note, string Proposal, bool Critical, Func<BST_RotationLogic.BstState, object[]> Candidates, bool NeedsBt);

internal static class FieldTable
{
    private static readonly object[] Bools = [false, true];
    private static object[] Of(params object[] v) => v;
    private static Func<BST_RotationLogic.BstState, object[]> C(params object[] v) => _ => v;
    private static readonly Func<BST_RotationLogic.BstState, object[]> TrueFirst = _ => [true, false];
    private static readonly Func<BST_RotationLogic.BstState, object[]> FalseFirst = _ => Bools;
    private static readonly Func<BST_RotationLogic.BstState, object[]> Since = C(float.MaxValue, 0.5f, 5f, 60f);

    private static FieldRow R(string n, string src, Func<BST_RotationLogic.BstState, object[]> c, string note = "", bool bt = false, bool crit = true) =>
        new(n, Kind.Rebuilt, src, note, "", crit, c, bt);
    private static FieldRow D(string n, string src, string note, Func<BST_RotationLogic.BstState, object[]> c, bool bt = false, bool crit = true) =>
        new(n, Kind.Derivable, src, note, "", crit, c, bt);
    private static FieldRow M(string n, string proposal, Func<BST_RotationLogic.BstState, object[]> c, bool crit = true) =>
        new(n, Kind.Missing, "-", "not logged", proposal, crit, c, false);

    private const string PGate = "CR| gate flags f=";

    public static readonly IReadOnlyList<FieldRow> Rows =
    [
        R("Level", "BT lv=", C(50, 30, 20), bt: true),
        D("InCombat", "CR", "true when the tick shows a fight (player/target HP below full, an enemy aimed at the character or familiar, intake > 0); otherwise unknown", C(true, false)),
        R("HasHostileTarget", "CR t=nameId (non-zero)", C(true, false)),
        D("TargetDistance", "CR d=", "d= is the hitbox-edge distance, float.MaxValue when empty", C(3f, 12f, float.MaxValue)),
        R("TargetHpPercent", "CR t=:hp", C(100f)),
        R("TargetInterruptible", PGate + "i", FalseFirst),
        D("EnemiesWithin6y", "CR ne=", "enemy count stands in for the count within 6 y (upper bound)", C(1, 2, 3)),
        D("PlayerTargetedByEnemy", "CR f=y", "y says the CURRENT target attacks the character; any other enemy aimed at the character is not logged", FalseFirst),
        M("PlayerIsCasting", "add `cast=0/1` (player cast bar) to CR|", FalseFirst),
        D("IsMoving", "CR mv=", "mv= above 0.5 yalms/s", FalseFirst),

        R("PlayerTp", "BT gauge byte 0x08", C(0, 100, 250), bt: true),
        R("FamiliarTp", "BT gauge byte 0x09", C(0, 100, 250), bt: true),
        R("ActiveSlot", "BT battlehorn", C(0, 1, 2, 3), bt: true),
        R("ComboState", "BT gauge byte 0x0C", C(BeastmasterAffinity.None, BeastmasterAffinity.WaveringHeart), bt: true),
        R("LastAffinity", "BT affinity", C(BeastmasterAffinity.None, BeastmasterAffinity.Volant), bt: true),
        R("KinshipSlot", "BT kinship & 0x0F", C(0, 1, 2, 3), bt: true),
        R("MasterStacks", "BT gauge byte 0x10 bits 2-3", C(0, 1, 2), bt: true),
        R("NaturalStacks", "BT gauge byte 0x10 bits 0-1", C(0, 1, 2), bt: true),

        R("Slot1Beast", "BT sl= first", C(0), bt: true),
        R("Slot2Beast", "BT sl= second", C(0), bt: true),
        R("Slot3Beast", "BT sl= third", C(0), bt: true),
        D("SlotBeastsKnown", "BT sl=", "any slot row non-zero", FalseFirst, bt: true),
        D("PetObjectPresent", "BT pet=", "pet object logged and (a slot is active or a horn was pressed lately; the press is not logged, so the active slot decides)", FalseFirst, bt: true),
        R("PetObjectBeast", "BT pet= BNpcBase", C(0), "BNpcBase mapped by BST_Beasts.RowFromBNpcBase", bt: true),

        R("OneWithNature", "BT status 4601", FalseFirst, "status list is cut at 10 ids; a cut list makes an absent id a gap", bt: true),
        R("LingeringVantage", "BT status 4614", FalseFirst, bt: true),
        R("WaveringHeart", "BT status 4643", FalseFirst, bt: true),
        R("SunOrMoonActive", "BT status 4599/4600", FalseFirst, bt: true),
        R("SunMoon", "BT status 4599/4600", C(BeastmasterAffinity.None, BeastmasterAffinity.Sunstrider, BeastmasterAffinity.Moonstalker), bt: true),
        D("KinshipHeld", "BT statuses 4602-4609, 4644-4651", "any Kinship status present (the live read tests the first and last id of each set)", FalseFirst, bt: true),

        M("GcdReady", "add `gcd=<remaining s>` to CR|", TrueFirst),
        M("CanWeave", "add `weave=0/1` to CR|", TrueFirst),
        D("SinceSummon", "BT gauge history", "time since ActiveSlot became non-zero, from the BT| series in the window; unknown when the window opens with a familiar already out", C(0f, 5f, 60f), bt: true),
        M("SinceHornPress", "log the last horn press age (`shp=`) in CR|", C(60f, 0.5f, 5f)),
        D("SinceTrick", "BT tk=", "tk=- means no Trick inside the 7 s window: never; otherwise unknown", Since, bt: true),
        M("SinceTempered", "log `stp=` (age of the last Tempered Release) in CR|", Since),
        M("SinceBorrow", "log `sbo=` (age of the last Borrow) in CR|", Since),
        M("SinceAxe", "log `sax=` (age of the last axe) in CR|", Since),
        D("SincePetHeart", "BT tk=", "tk=- means no Trick lately: never; otherwise unknown", Since, bt: true),
        M("LastAxeAffinity", "log the last axe's affinity (`lax=`) in CR|", C(BeastmasterAffinity.None, BeastmasterAffinity.Volant, BeastmasterAffinity.Rampant)),
        M("TemperedRecastRemaining", "log the Tempered Release recast (`trr=`) in CR|", C(0f, 5f, 30f)),

        M("ReadyHorn1", "log a readiness bitmap `rd=` (horns, Trick, Tempered, Borrow, Parting, axe, Rally, Cheer, Beast Mode, Shield Charge) in CR|", FalseFirst),
        M("ReadyHorn2", "same `rd=` bitmap", FalseFirst),
        M("ReadyHorn3", "same `rd=` bitmap", FalseFirst),
        M("ReadyTrick", "same `rd=` bitmap", FalseFirst),
        M("ReadyTempered", "same `rd=` bitmap", FalseFirst),
        M("ReadyBorrow", "same `rd=` bitmap", FalseFirst),
        M("ReadyParting", "same `rd=` bitmap", FalseFirst),
        M("ReadyAxe", "same `rd=` bitmap", FalseFirst),
        M("ReadyRally", "same `rd=` bitmap", FalseFirst),
        M("ReadyCheer", "same `rd=` bitmap", FalseFirst),
        D("BeastModeResolved", "BT bm=", "bm= is the adjusted Beast Mode id; the base id (44886) means none held", C(0u, 44902u, 44900u), bt: true),
        M("ReadyBeastMode", "same `rd=` bitmap", FalseFirst),
        M("ReadyShieldCharge", "same `rd=` bitmap", FalseFirst),
        M("ShieldChargeCharges", "log `scc=` (charges) in CR|", C(0, 1, 2)),
        M("ShieldChargeMax", "log `scm=` (max charges) in CR|", C(1, 3)),
        D("DashLanding", "DS v=", "newest DS| verdict within 2 s; no DS| line means the live code did not read it (Safe)", C(BST_RotationLogic.DashLanding.Safe, BST_RotationLogic.DashLanding.Danger)),
        M("DashHold", "already logged on SG| `dh=`; join the SG| line by time", _ => new object[] { null! }, crit: false),

        M("LastComboAction", "log the combo action id (`lca=`) in CR|", C(0u, 44879u, 44880u)),
        M("ComboTimerActive", "log `ctm=0/1` in CR|", FalseFirst),

        R("CrucibleBoard", "CR b=", C(5)),
        R("CrucibleBattle", "CR bt=", C(-1)),
        D("CrucibleDegreeKnown", "CR dg=", "dg=x means LazyCrucible had not published the degree", FalseFirst),
        D("CrucibleDegreeLevel", "CR dg=", "digit 0-3; unread when dg=x", C(0, 1, 2, 3)),
        R("CrucibleNeeds", "CR nd=", C(CrucibleNeeds.None)),
        R("EnemyCount", "CR ne=", C(1)),
        R("HighestEnemyHpPercent", "CR hi=", C(100f)),
        R("PlayerHpPercent", "CR hp=", C(100f)),
        M("PlayerHp", "log absolute HP and max HP (`hpa=cur/max`) in CR|", s => [s.PlayerHpPercent * 40f, s.PlayerHpPercent * 20f, s.PlayerHpPercent * 80f]),
        M("ReadyHealPotion", "log the heal potion action id the engine would press (`hpot=`) in CR|", C(0u, BST_CrucibleData.HealPotionActions[0])),
        R("PetHpPercent", "CR pet=", C(100f)),
        M("PetHp", "log the familiar's absolute HP (`peta=`) in CR|", s => [s.PetHpPercent * 20f, s.PetHpPercent * 10f, s.PetHpPercent * 40f]),
        R("PlayerIntakePerSecond", "CR in=", C(0f)),
        R("TargetVulnerabilityRemaining", "CR vul= (whole seconds)", C(0f)),
        R("TargetTimeToDeath", "CR ttd= (whole seconds)", C(0f)),
        R("Slot1PetHp", "CR sl= first", C(0f)),
        R("Slot2PetHp", "CR sl= second", C(0f)),
        R("Slot3PetHp", "CR sl= third", C(0f)),
        M("PartingBlowRecast", "fold into the `rd=` readiness bitmap plus the remaining seconds (`pbr=`)", C(0f, 10f, 30f)),
        R("TargetHasDispellableBuff", PGate + "D", FalseFirst),
        D("EnemyHasDispellableBuff", "CR f=D", "implied true when the target carries one; a buff on another enemy in reach is not logged", FalseFirst),
        M("DispelCawReachable", "add flag letter `W` (Caw reachable) to CR f=", FalseFirst),
        R("TargetInStance", PGate + "S", FalseFirst),
        R("TargetDoNotAttack", PGate + "X", FalseFirst),
        M("TargetInvulnerable", "add flag letter `I` (damage-immune target) to CR f=", FalseFirst),
        R("ProtectedNearTarget", PGate + "N", FalseFirst),
        R("TargetHasParry", PGate + "P", FalseFirst),
        R("ParryJustEnded", PGate + "J", FalseFirst),
        R("TargetCastId", "CR c=id", C(0u)),
        R("TargetCastRemaining", "CR c=:remaining", C(0f)),
        R("PlayerHasCleansableDebuff", PGate + "C", FalseFirst),
        R("EnemyTargetsPet", PGate + "p", FalseFirst),
        R("EnemyTargetsPlayer", PGate + "y", FalseFirst),
        R("ReadySnarl", PGate + "s", FalseFirst),
        R("ReadyChallenge", PGate + "c", FalseFirst),
        M("SinceSnarl", "log `ssn=` (age of the last Snarl) in CR|", C(float.MaxValue, 0.5f, 20f)),
        M("SincePartingBlow", "log `spb=` (age of the last Parting Blow) in CR|", Since),
        M("SinceCurtainsCast", "log `scu=` (age of the last Curtains cast) in CR|", Since),
        R("TargetNameId", "CR t=nameId", C(0u)),
        M("ShellJustBroke", "add flag letter `K` (shell just broke) to CR f=", FalseFirst),

        R("BmrSeen", "CR bh= present", FalseFirst),
        R("BmrHitIn", "CR bh=", C(float.MaxValue)),
        R("BmrHitType", "CR bk=", C(0)),
        R("BmrHitOnPlayer", "CR bp=", FalseFirst),
        R("BmrHitOnFamiliar", "CR bf=", FalseFirst),
        R("BmrMaxCast", "CR mct=", C(float.MaxValue)),
        D("PetPoisonStatus", "CR f=V", "V says a shed-eligible poison is on the familiar; the status id is not logged (first shed status stands in)", C(0u, 5140u)),
        M("PetPoisonRemaining", "log the poison's seconds left (`ppr=`) in CR|", C(0f, 5f, 20f)),
        R("ReleaseProtectedNear", PGate + "R", FalseFirst),
        R("PlayerBlink", PGate + "B", FalseFirst),
        M("PetPoisonCastId", "log the shed cast id (`ppc=`) in CR|", C(0u)),
        M("BattleHasKnockback", "static data of (board, battle): readable from BST_CrucibleData in a later step, or log `kb=0/1`", FalseFirst),

        M("PetOrder", "log the familiar's Summon Order (`po=`) in CR|", C(BST_RotationLogic.PetOrder.Unknown, BST_RotationLogic.PetOrder.None, BST_RotationLogic.PetOrder.Trick, BST_RotationLogic.PetOrder.Release)),
        M("KnockbackCastRemaining", "log `kbr=` (soonest knockback cast) in CR|", C(0f, 2f)),
        M("ShieldChargeClipsGcd", "add to the `rd=` bitmap", FalseFirst),
        M("DashSettleRemaining", "log `dsr=` (dash hazard hold left) in CR|", C(0f, 2f)),
    ];

    public static readonly Dictionary<string, FieldRow> ByName = Rows.ToDictionary(r => r.Name);

    /// <summary> BstState public instance fields, in declaration order. </summary>
    public static FieldInfo[] StateFields() =>
        typeof(BST_RotationLogic.BstState).GetFields(BindingFlags.Public | BindingFlags.Instance);

    /// <summary> The table and the struct must list the same fields; returns the problems (empty = in sync). </summary>
    public static List<string> Check()
    {
        var problems = new List<string>();
        var names = StateFields().Select(f => f.Name).ToHashSet();
        foreach (var n in names.Where(n => !ByName.ContainsKey(n)))
            problems.Add($"BstState.{n} has no FieldTable row (classify it)");
        foreach (var r in Rows.Where(r => !names.Contains(r.Name)))
            problems.Add($"FieldTable row {r.Name} is not a BstState field any more");
        return problems;
    }
}
