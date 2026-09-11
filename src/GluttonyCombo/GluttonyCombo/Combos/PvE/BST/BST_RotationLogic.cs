namespace GluttonyCombo.Combos.PvE;

/// <summary>
///     Pure Beastmaster rotation decision logic (t_02fe2681). No Dalamud/game types - only
///     primitives and the <see cref="BeastmasterAffinity"/>/<see cref="BeastmasterKinType"/>
///     enums declared alongside the gauge overlay in BST_Gauge.cs (same namespace) - so this
///     file compiles standalone into <c>tests/GluttonyCombo.BSTRotationHarness</c> and is
///     proven offline before shipping, per "prove selection logic in a throwaway console app"
///     (skill lalalazy-ffxiv references/rotation-logic-and-preset-actions.md).
/// </summary>
/// <remarks>
///     <see cref="BST"/> (BST.cs) is the live half: it reads the real gauge/pet/status state
///     and calls into these pure functions to decide what to press - the same "pure half
///     compiled into the harness, live half reads the game" split
///     <c>BeastmasterTelemetryFormat</c>/<c>BeastmasterTelemetry</c> already use.
/// </remarks>
internal static class BST_RotationLogic
{
    // ------------------------------------------------------------------
    // GCD chain: Smash Axe -> Axeblade Bite -> Shieldsplitter. A combo that isn't currently
    // running (ComboTimer expired, or the last GCD wasn't the expected predecessor) restarts at
    // Smash Axe and grants no TP - RULES OF THE JOB.
    // ------------------------------------------------------------------

    /// <summary> Chooses the next single-target GCD in the 1-2-3 chain. </summary>
    public static uint ChooseGcdChain(uint lastComboAction, bool comboTimerActive,
        uint smashAxe, uint axebladeBite, uint shieldsplitter)
    {
        if (comboTimerActive && lastComboAction == smashAxe)
            return axebladeBite;

        if (comboTimerActive && lastComboAction == axebladeBite)
            return shieldsplitter;

        return smashAxe;
    }

    // ------------------------------------------------------------------
    // Compass: clockwise Volant -> Rampant -> Durant -> Eldritch -> Volant.
    // ------------------------------------------------------------------

    /// <summary> The next affinity clockwise from <paramref name="current"/>. </summary>
    public static BeastmasterAffinity NextClockwise(BeastmasterAffinity current) => current switch
    {
        BeastmasterAffinity.Volant => BeastmasterAffinity.Rampant,
        BeastmasterAffinity.Rampant => BeastmasterAffinity.Durant,
        BeastmasterAffinity.Durant => BeastmasterAffinity.Eldritch,
        BeastmasterAffinity.Eldritch => BeastmasterAffinity.Volant,
        _ => BeastmasterAffinity.None,
    };

    /// <summary> True when <paramref name="second"/> is the clockwise successor of <paramref name="first"/>. </summary>
    public static bool IsClockwisePair(BeastmasterAffinity first, BeastmasterAffinity second) =>
        first != BeastmasterAffinity.None && NextClockwise(first) == second;

    /// <summary>
    ///     Which of Sunstrider / Moonstalker an intentional (clockwise) combo grants, keyed on
    ///     which affinity the CHAIN STARTED at - Volant/Durant grant Sunstrider,
    ///     Rampant/Eldritch grant Moonstalker (RULES OF THE JOB).
    /// </summary>
    public static BeastmasterAffinity IntentionalComboResult(BeastmasterAffinity chainStart) => chainStart switch
    {
        BeastmasterAffinity.Volant or BeastmasterAffinity.Durant => BeastmasterAffinity.Sunstrider,
        BeastmasterAffinity.Rampant or BeastmasterAffinity.Eldritch => BeastmasterAffinity.Moonstalker,
        _ => BeastmasterAffinity.None,
    };

    /// <summary> The base instinctual action id for a given compass affinity, or 0 for a non-compass affinity. </summary>
    public static uint InstinctualActionFor(BeastmasterAffinity affinity,
        uint avalancheAxe, uint mistralAxe, uint spinningAxe, uint galeAxe) => affinity switch
    {
        BeastmasterAffinity.Rampant => avalancheAxe,
        BeastmasterAffinity.Durant => mistralAxe,
        BeastmasterAffinity.Eldritch => spinningAxe,
        BeastmasterAffinity.Volant => galeAxe,
        _ => 0,
    };

    /// <summary>
    ///     Chooses the next instinctual base action to press, or 0 when none should fire this
    ///     tick. Hard rules, in order:
    ///     <list type="number">
    ///         <item> TP must be at least 100 (the skill spends the whole pool). </item>
    ///         <item>
    ///             <c>comboState == 7</c> is Wavering Heart - a hard lockout, never fire.
    ///         </item>
    ///         <item>
    ///             If a compass window is open (<paramref name="currentAffinity"/> is not
    ///             <see cref="BeastmasterAffinity.None"/> - the gauge already clears this ~7s
    ///             after the last instinctual skill, so it IS the "within 7s" signal), continue
    ///             the chain clockwise rather than opening a new one: ChainCount raises combo
    ///             potency, so continuing beats restarting.
    ///         </item>
    ///         <item>
    ///             Otherwise open fresh at the LOWEST-level unlocked axe, not always Volant - a
    ///             sub-16 player has no Gale Axe/Volant yet (unlock order L4 Rampant &lt; L8
    ///             Durant &lt; L14 Eldritch &lt; L16 Volant). Fixed t_f04d4c83: the prior
    ///             "open fresh: Volant" fallback returned <paramref name="galeAxe"/>
    ///             unconditionally, which is unlearned (<c>ActionReady</c> false, no-op) for
    ///             every player below L16 - confirmed live via a sub-16 corpus sample (player
    ///             level 15, TP 196, compass closed) that fell all the way through to the
    ///             GCD-chain fallback instead of firing an instinctual axe it had actually
    ///             earned (Rampant, learned since L4).
    ///         </item>
    ///     </list>
    /// </summary>
    /// <param name="durantLearned"> Whether Mistral Axe (Durant, lv8) is unlocked. </param>
    /// <param name="eldritchLearned"> Whether Spinning Axe (Eldritch, lv14) is unlocked. </param>
    /// <param name="volantLearned"> Whether Gale Axe (Volant, lv16) is unlocked. </param>
    public static uint ChooseInstinctual(byte tp, byte comboState, BeastmasterAffinity currentAffinity,
        uint avalancheAxe, uint mistralAxe, uint spinningAxe, uint galeAxe,
        bool durantLearned = true, bool eldritchLearned = true, bool volantLearned = true)
    {
        if (tp < 100)
            return 0;

        if (comboState == 7) // Wavering Heart lockout
            return 0;

        if (currentAffinity != BeastmasterAffinity.None)
        {
            var next = NextClockwise(currentAffinity);
            if (next != BeastmasterAffinity.None)
                return InstinctualActionFor(next, avalancheAxe, mistralAxe, spinningAxe, galeAxe);
        }

        // Open fresh at the highest-level (most recently learned) unlocked axe. Rampant
        // (Avalanche Axe, L4) is the ultimate fallback and never needs its own "learned" flag -
        // Wild Heart (the trait that turns on TP accumulation at all) is also granted at L4, so
        // TP cannot reach 100 before Avalanche Axe itself is available.
        if (volantLearned) return galeAxe; // open fresh: Volant
        if (eldritchLearned) return spinningAxe; // open fresh: Eldritch
        if (durantLearned) return mistralAxe; // open fresh: Durant
        return avalancheAxe; // open fresh: Rampant
    }

    // ------------------------------------------------------------------
    // Lv50 finishers. GetAdjustedActionId already resolves 44884/44887/44888/44889 to
    // 44930-44933 in the live client when TP==250 and the matching prior affinity is up
    // (Universality = a Sunstrider skill fired under Moonstalker, or vice versa). This function
    // does not decide the swap - it labels which finisher intent a resolved action id
    // represents, for the BT| decision tap's "reason" string and for the offline harness.
    // ------------------------------------------------------------------

    /// <summary> Labels a resolved (post-<c>GetAdjustedActionId</c>) instinctual action id. </summary>
    public static string FinisherReason(uint adjustedActionId,
        uint brutalRage, uint hawkishTalons, uint risenFall, uint calamity)
    {
        if (adjustedActionId == brutalRage) return "brutalrage:sunstrider";
        if (adjustedActionId == hawkishTalons) return "hawkishtalons:moonstalker";
        if (adjustedActionId == risenFall) return "risenfall:sunstrider";
        if (adjustedActionId == calamity) return "calamity:moonstalker";
        return "instinctual:precombo";
    }

    // ------------------------------------------------------------------
    // Familiar loop: Battlehorn -> Borrow -> Tempered Release -> Trick -> (Lingering Vantage up
    // and familiar TP spent) Parting Blow -> next Battlehorn. Summoning RESETS Borrow + Tempered
    // Release, so each fresh summon re-arms both.
    // ------------------------------------------------------------------

    public enum FamiliarStep
    {
        /// <summary> Nothing to do this tick - not this step's turn, or blocked by config. </summary>
        None,
        Battlehorn,
        Borrow,
        TemperedRelease,
        Trick,
        PartingBlow,
    }

    /// <summary>
    ///     One step of the familiar loop, given the current summon's state. "This summon" for
    ///     <paramref name="borrowedThisSummon"/>/<paramref name="temperedReleasedThisSummon"/>
    ///     is decided by the live half comparing action timestamps against the last Battlehorn
    ///     press (see <see cref="BST"/>.ChooseFamiliarAction) - both are RESET by a fresh
    ///     summon (RULES OF THE JOB), so "used more recently than the last summon" is exactly
    ///     "used this summon".
    /// </summary>
    /// <param name="petSummoned"> Whether a familiar is currently out. </param>
    /// <param name="borrowedThisSummon"> Whether Borrow has already been used since the current summon. </param>
    /// <param name="temperedReleasedThisSummon"> Whether Tempered Release has already been used since the current summon. </param>
    /// <param name="familiarTp"> The familiar's TP gauge (0-250). </param>
    /// <param name="lingeringVantage"> Whether Lingering Vantage (4614, from Borrow) is currently up. </param>
    /// <param name="holdPartingBlowForVantage">
    ///     Config: hold Parting Blow until Lingering Vantage is up (1500 vs 1000 potency),
    ///     rather than retreating immediately once Trick has spent the familiar's TP.
    /// </param>
    /// <param name="borrowLearned">
    ///     Whether Borrow (lv22) is unlocked at the player's current level. Below lv22 the
    ///     step is skipped entirely rather than stalling the loop - Trick (lv8) is available
    ///     long before Borrow, and the old hardcoded Battlehorn-Borrow-Tempered-Trick order
    ///     left a sub-22 player's familiar loop stuck forever waiting on an ability they
    ///     hadn't learned (Helm: "Not using trick", 2026-09-10).
    /// </param>
    /// <param name="temperedLearned">
    ///     Whether Tempered Release (lv18) is unlocked at the player's current level. Same
    ///     skip-if-not-learned treatment as <paramref name="borrowLearned"/>.
    /// </param>
    /// <summary>
    ///     Whether the familiar loop should hold Parting Blow for Lingering Vantage, or retreat
    ///     unconditionally the instant Trick spends the familiar's TP.
    /// </summary>
    /// <remarks>
    ///     Lingering Vantage cannot exist below lv22 - Borrow's own unlock is the floor for any
    ///     Vantage grant (skill lalalazy-ffxiv references/beastmaster-kit-by-level.md, traits
    ///     749/750). A player below lv22 can therefore NEVER satisfy a hold-for-Vantage
    ///     condition, so the hold must be gated on <paramref name="borrowLearned"/>
    ///     (lv22 unlock) regardless of Simple/Advanced mode - the caller (BST.cs) previously
    ///     forced this true in Simple Mode unconditionally ("!advanced || config"), which
    ///     stalled the familiar loop forever for any sub-22 Simple Mode player once Trick
    ///     spent the familiar's TP (Helm: "still not casting Trick, pet TP just stays at
    ///     100%", 2026-09-10 / t_4c7923b0 defect 1 / t_3d88b4fd).
    /// </remarks>
    /// <param name="borrowLearned"> Whether Borrow (lv22) is unlocked - the floor for Lingering Vantage. </param>
    /// <param name="holdPartingBlowForVantageConfig"> The raw config toggle value, independent of mode. </param>
    public static bool ComputeHoldForVantage(bool borrowLearned, bool holdPartingBlowForVantageConfig) =>
        borrowLearned && holdPartingBlowForVantageConfig;

    public static FamiliarStep ChooseFamiliarStep(
        bool petSummoned, bool borrowedThisSummon, bool temperedReleasedThisSummon,
        byte familiarTp, bool lingeringVantage, bool holdPartingBlowForVantage,
        bool borrowLearned = true, bool temperedLearned = true)
    {
        if (!petSummoned)
            return FamiliarStep.Battlehorn;

        if (borrowLearned && !borrowedThisSummon)
            return FamiliarStep.Borrow;

        if (temperedLearned && !temperedReleasedThisSummon)
            return FamiliarStep.TemperedRelease;

        if (familiarTp >= 100)
            return FamiliarStep.Trick;

        if (holdPartingBlowForVantage && !lingeringVantage)
            return FamiliarStep.None; // Trick already spent; wait for Vantage before retreating

        return FamiliarStep.PartingBlow;
    }

    /// <summary>
    ///     Next Battlehorn slot to press, rotating 1 -> 2 -> 3 -> 1 so each fresh summon re-arms
    ///     Tempered Release + Borrow (RULES OF THE JOB), or a fixed preferred slot when
    ///     <paramref name="preferredSlot"/> is 1-3.
    /// </summary>
    /// <param name="lastSlot"> The last Battlehorn slot summoned (0 = none yet). </param>
    /// <param name="preferredSlot"> 0 = rotate; 1-3 = always use this slot. </param>
    public static byte NextBattlehornSlot(byte lastSlot, byte preferredSlot)
    {
        if (preferredSlot is >= 1 and <= 3)
            return preferredSlot;

        return lastSlot switch
        {
            1 => 2,
            2 => 3,
            _ => 1,
        };
    }
}
