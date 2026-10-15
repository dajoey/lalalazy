using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

internal static partial class Program
{
    /// <summary>
    ///     The Abaddon idle hold (shipped 1.0.4.305, 0d32b98d0; the build before it is ce67410e4 = 1.0.4.304). On 304 the rule was "a Morpho alive: the Abaddon is held
    ///     back at any HP", so the Abaddon alone with a Morpho alive left no legal target and the rotation sat idle for 83.5 s (2026-10-14 20:54:57 to 20:56:20 ET, 93 CR| lines,
    ///     all ne=1, t=14655). The shipped rule: held while a Morpho is alive AND (another enemy is up OR its HP is at or below the 35 percent floor).
    ///     Every case reaches the rule through <see cref="R4ShippedForbidden"/>, so the same file runs on 304.
    /// </summary>
    private static void R4ShippedAbaddon()
    {
        Console.WriteLine("-- r4-shipped: abaddon idle hold --");
        var cfg = BstSettings.Defaults();
        const uint abaddon = 14655, drake = 14651, barbmole = 14653;
        // othersUp = the Drake or a Barbmole is still up; false = the Abaddon is the only enemy left (the CR| ne=1 of the recorded hold).
        bool Forbidden(float hp, bool morphoAlive = true, bool othersUp = false) => R4ShippedForbidden(abaddon, morphoAlive, hp, othersUp);

        // The recorded state: board 5 battle 5, the Abaddon targeted at 100%, the character at 64%, the familiar at 100%, a GCD ready (CR| 20:54:57.132 ET).
        var live = CrucibleState(50) with
        {
            CrucibleBoard = 5, CrucibleBattle = 5, CrucibleNeeds = BST_CrucibleLogic.FightNeeds(5, 5), EnemyCount = 1,
            HasHostileTarget = true, TargetNameId = abaddon, TargetHpPercent = 100f, HighestEnemyHpPercent = 100f,
            PlayerHpPercent = 64f, PetHpPercent = 100f, GcdReady = true, CanWeave = false, ProtectedNearTarget = true,
        };
        bool Held(BstState s) => Decide(s, cfg) is { ActionId: BST_CrucibleLogic.Hold, Reason: "crucible:hold-do-not-attack" };

        // ---- fails on 304: the Abaddon alone with a Morpho alive, above the floor
        Check("r4-shipped: abaddon alone, a Morpho alive, 100% HP: the rule lets it be attacked (304 returned 'a Morpho alive' = held)", !Forbidden(100f));
        Check("r4-shipped: abaddon ... at 98%, 60% and just above the floor (35.1%): attackable", !Forbidden(98f) && !Forbidden(60f) && !Forbidden(35.1f));
        var attack = Decide(live with { TargetDoNotAttack = Forbidden(100f) }, cfg);
        Check("r4-shipped: abaddon the recorded 20:54:57 tick (Abaddon 100%, Morpho alive, ne=1): the rotation attacks, it does not hold (304 held 93 CR| lines in a row)",
            attack.Reason != "crucible:hold-do-not-attack" && attack.ActionId != BST_CrucibleLogic.Hold, $"{attack.ActionId} {attack.Reason} [{attack.Declines}]");
        Check("r4-shipped: abaddon ... and the attack is the weaponskill chain", attack.ActionId is BST.SmashAxe or BST.AxebladeBite or BST.Shieldsplitter, $"{attack.ActionId} {attack.Reason}");
        Check("r4-shipped: abaddon auto-targeting: the Abaddon alone with a Morpho alive is picked (304 left an empty list: nothing to pick)",
            BST_CrucibleLogic.AllowedTargets([R4ShippedCandidate(abaddon, 100f, forbidden: Forbidden(100f))]).SequenceEqual(new[] { 0 }));

        // ---- the same rule, unchanged by the fix: these pass on 304 and on the head
        Check("r4-shipped: abaddon at the floor (35%) and below with a Morpho alive: held back, the Abaddon is not killed ahead of the Morphos", Forbidden(35f) && Forbidden(20f) && Forbidden(1f));
        Check("r4-shipped: abaddon ... the held tick is the hold with its reason", Held(live with { TargetHpPercent = 30f, TargetDoNotAttack = Forbidden(30f) }));
        Check("r4-shipped: abaddon the Drake or a Barbmole still up and a Morpho alive: held at any HP (the Abaddon last)",
            Forbidden(100f, true, true) && Forbidden(60f, true, true) && Forbidden(0f, true, true));
        Check("r4-shipped: abaddon ... auto-targeting then picks the Drake and leaves the Abaddon",
            BST_CrucibleLogic.AllowedTargets([R4ShippedCandidate(abaddon, 100f, forbidden: Forbidden(100f, true, true)), R4ShippedCandidate(drake)]).SequenceEqual(new[] { 1 }));
        Check("r4-shipped: abaddon every Morpho gone: attackable at any HP, with or without other enemies", !Forbidden(100f, false) && !Forbidden(10f, false) && !Forbidden(100f, false, true));
        Check("r4-shipped: abaddon the Drake and the Barbmole are never held by this rule",
            !R4ShippedForbidden(drake, true, 100f, true) && !R4ShippedForbidden(barbmole, true, 100f, true));
        Check("r4-shipped: abaddon a Morpho targeted itself stays held (the do-not-attack table, not this rule)",
            Held(live with { TargetNameId = 14656, TargetDoNotAttack = true }));
    }
}
