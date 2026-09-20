using System;
using System.Collections.Generic;
using System.Linq;

namespace GluttonyCombo.Combos.PvE;

/// <summary> One recommended familiar for a Crucible battle. </summary>
public readonly record struct CrucibleBeastPick(int Row, int Score, bool Captured, string Why);

/// <summary>
///     Beast picks for Crucible battles, from the game's own panel data (weakness, star ratings, vulnerabilities,
///     what the casts call for) and familiar data (auto-attack element, statuses inflicted, kin, stats at the
///     board's rank sync). PURE: compiled into tests/GluttonyCombo.BSTRotationHarness. Picks are advice for the
///     beast-selection screen; the rotation itself never chooses beasts.
/// </summary>
internal static class BST_CrucibleAdvisor
{
    public const int VultureRow = 11;
    public const int BatRow = 19;

    /// <summary>
    ///     Run-state danger line for horn ranking (Crucible Plan A1 / pet-save intent): at or below this HP% a
    ///     familiar loses to a healthy alternative of similar battle fit. Live pet-save now swaps below
    ///     <c>CruciblePetSwapHp</c> and Parting-Blows at half that (default 27.5%); this constant stays at the
    ///     plan's 15% so ranking does not put a nearly-dead familiar on a horn. Distinct from
    ///     <c>BST_CrucibleLogic.PartyHpLow</c> (95%, party-agent lag filter only).
    /// </summary>
    public const int DangerHpPercent = 15;

    /// <summary>
    ///     Floor of the HP multiplier at the brink of KO (exclusive of 0%, which is never picked).
    ///     With <see cref="HpFactor"/> = floor + (1-floor)*(hp/100), at <see cref="DangerHpPercent"/> the
    ///     factor is 0.32 — a top battle score loses to a healthy similar second pick but still beats a
    ///     healthy familiar that answers nothing the battle needs.
    /// </summary>
    public const double HpFactorFloor = 0.20;

    /// <summary> Vulnerability bit 3 is "interrupt": answered by the Soulkin need, not counted as crowd control. </summary>
    private const ushort CrowdControlBits = 0x7FF & ~(1 << 3);

    private static readonly string[] ElementNames = ["", "fire", "wind", "earth", "lightning", "ice", "water", "blunt", "piercing", "slashing"];

    /// <summary> Panel needs this familiar answers (Soul Crush, Quelling Wave / Bloodcurdling Caw, Scouring Ash / Ultrasonics). </summary>
    public static CrucibleNeeds Answers(int row)
    {
        if (BST_Beasts.ByRow(row) is not { } beast)
            return CrucibleNeeds.None;

        var needs = CrucibleNeeds.None;
        if (beast.Kin == BeastmasterKinType.Soulkin)
            needs |= CrucibleNeeds.Interrupt;
        if (beast.Kin == BeastmasterKinType.Wavekin || row == VultureRow)
            needs |= CrucibleNeeds.Dispel;
        if (beast.Kin == BeastmasterKinType.Ashkin || row == BatRow)
            needs |= CrucibleNeeds.Cleanse;
        return needs;
    }

    /// <summary>
    ///     How well one familiar fits one battle. <paramref name="covered"/> are needs an earlier pick already answers;
    ///     <paramref name="weaknessPicks"/> is how many earlier picks already hit the weakness (a second one counts 60%,
    ///     a third 20%: the guides trade the third slot for utility). Weakness match 4 (x3 on the battle's main enemy),
    ///     damage type vs star resistances 1, crowd control an enemy is vulnerable to 1 each (max 3), an unanswered
    ///     interrupt / dispel / cleanse 6 / 5 / 4, main stat up to 3 and constitution up to 2 at the board's rank sync,
    ///     Final Sting on a boss 1.
    /// </summary>
    public static int Score(int board, int battle, int row, CrucibleNeeds covered, List<string>? why = null, int weaknessPicks = 0)
    {
        if (row is < 1 or > BST_Beasts.Count)
            return int.MinValue;

        var profile = BST_CrucibleData.BeastProfiles[row];
        var beast = BST_Beasts.All[row];
        var weaknessFactor = weaknessPicks switch { 0 => 1.0, 1 => 0.6, _ => 0.2 };
        var weaknessScore = 0.0;
        var score = 0;
        var weaknessHits = 0;
        var cc = 0;
        var needs = CrucibleNeeds.None;
        var boss = false;

        foreach (var e in BST_CrucibleData.Enemies)
        {
            if (e.Board != board || e.Battle != battle)
                continue;

            needs |= e.Needs;
            var weight = e.Sub == 0 ? 3 : 1;
            if (e.Sub == 0 && battle == 0)
                boss = true;

            if (profile.AutoElement != CrucibleWeakness.None && profile.AutoElement == e.Weakness)
            {
                weaknessScore += 4 * weight * weaknessFactor;
                weaknessHits++;
            }
            else if (e.Weakness == CrucibleWeakness.None
                     && (profile.AutoMagic ? e.StarMagRes < e.StarPhysRes : e.StarPhysRes < e.StarMagRes))
            {
                score += weight;
            }

            if (cc < 3 && (profile.Inflicts & e.Vulnerable & CrowdControlBits) != 0)
                cc++;
        }

        score += (int)Math.Round(weaknessScore);
        if (weaknessHits > 0)
            why?.Add(weaknessHits > 1 ? $"{ElementNames[(int)profile.AutoElement]} x{weaknessHits}" : ElementNames[(int)profile.AutoElement]);

        score += cc;
        if (cc > 0)
            why?.Add("crowd control");

        var answered = needs & ~covered & Answers(row);
        if ((answered & CrucibleNeeds.Interrupt) != 0)
        {
            score += 6;
            why?.Add("Soul Crush");
        }
        if ((answered & CrucibleNeeds.Dispel) != 0)
        {
            score += 5;
            why?.Add(row == VultureRow ? "Bloodcurdling Caw" : "Quelling Wave");
        }
        if ((answered & CrucibleNeeds.Cleanse) != 0)
        {
            score += 4;
            why?.Add(row == BatRow ? "Ultrasonics" : "Scouring Ash");
        }

        var stat = profile.AutoMagic ? CrucibleBeastProfile.Int : CrucibleBeastProfile.Str;
        score += (int)Math.Round(3.0 * profile.StatAtBoard(board, stat) / MaxStat(board, stat));
        score += (int)Math.Round(2.0 * profile.StatAtBoard(board, CrucibleBeastProfile.Con) / MaxStat(board, CrucibleBeastProfile.Con));

        if (battle == 5 && board == 1 && beast.Kin == BeastmasterKinType.Wavekin)
        {
            // Ogre: Quelling Wave one-shots the lesser wisps during Burning Ward (a MagitekRoutine user's rule).
            score += 4;
            why?.Add("Quelling Wave for wisps");
        }

        if (boss && (beast.Release & BeastmasterReleaseTraits.Exit) != 0)
        {
            score += 1;
            why?.Add("Final Sting");
        }

        return score;
    }

    private static readonly Dictionary<(int, int), int> MaxStats = [];

    private static int MaxStat(int board, int stat)
    {
        if (MaxStats.TryGetValue((board, stat), out var max))
            return max;

        max = 1;
        for (var row = 1; row <= BST_Beasts.Count; row++)
            max = Math.Max(max, BST_CrucibleData.BeastProfiles[row].StatAtBoard(board, stat));
        MaxStats[(board, stat)] = max;
        return max;
    }

    /// <summary>
    ///     Multiplier on battle <see cref="Score"/> from remembered run HP. Missing map entries are treated as
    ///     full (100) by the caller. At 100% → 1.0; at <see cref="DangerHpPercent"/> → 0.32; approaches
    ///     <see cref="HpFactorFloor"/> as HP nears 0. Shape is linear so the harness can pin exact effective scores.
    /// </summary>
    public static double HpFactor(int hpPercent)
    {
        if (hpPercent >= 100)
            return 1.0;
        if (hpPercent <= 0)
            return 0.0;
        return HpFactorFloor + (1.0 - HpFactorFloor) * (hpPercent / 100.0);
    }

    /// <summary> Battle score after the run-state HP multiplier. 0% HP yields <see cref="int.MinValue"/>. </summary>
    public static int EffectiveScore(int battleScore, int hpPercent)
    {
        if (hpPercent <= 0 || battleScore == int.MinValue)
            return int.MinValue;
        return (int)Math.Floor(battleScore * HpFactor(hpPercent));
    }

    /// <summary>
    ///     Best horn fills for one battle from an explicit candidate roster and per-familiar HP%. PURE: never
    ///     reads the game. 0% HP or rows absent from <paramref name="candidateRows"/> are never picked; HP missing
    ///     from the map is treated as full and noted in <c>Why</c>. Greedy coverage and weakness saturation match
    ///     <see cref="Pick"/>. Tie-break: higher effective score, then higher HP, then lower row (deterministic).
    /// </summary>
    public static List<CrucibleBeastPick> PickSlots(
        int board,
        int battle,
        IReadOnlyList<int> candidateRows,
        IReadOnlyDictionary<int, int> hpPercentByRow,
        int slots = 3)
    {
        var picks = new List<CrucibleBeastPick>(slots);
        var covered = CrucibleNeeds.None;
        var taken = new HashSet<int>();
        var weaknessPicks = 0;
        var candidates = new List<int>(candidateRows.Count);
        var seen = new HashSet<int>();
        foreach (var row in candidateRows)
        {
            if (row is < 1 or > BST_Beasts.Count || !seen.Add(row))
                continue;
            candidates.Add(row);
        }

        for (var n = 0; n < slots; n++)
        {
            var bestRow = 0;
            var bestEff = int.MinValue;
            var bestHp = -1;
            var bestHpKnown = true;

            foreach (var row in candidates)
            {
                if (taken.Contains(row))
                    continue;

                var hpKnown = hpPercentByRow.TryGetValue(row, out var hp);
                if (!hpKnown)
                    hp = 100;
                if (hp <= 0)
                    continue;

                var raw = Score(board, battle, row, covered, null, weaknessPicks);
                if (raw == int.MinValue)
                    continue;
                var eff = EffectiveScore(raw, hp);

                var better = eff > bestEff
                             || (eff == bestEff && hp > bestHp)
                             || (eff == bestEff && hp == bestHp && (bestRow == 0 || row < bestRow));
                if (!better)
                    continue;

                bestEff = eff;
                bestRow = row;
                bestHp = hp;
                bestHpKnown = hpKnown;
            }

            if (bestRow == 0)
                break;

            var why = new List<string>(5);
            Score(board, battle, bestRow, covered, why, weaknessPicks);
            if (!bestHpKnown)
                why.Add("HP assumed full");
            else if (bestHp < 100)
                why.Add($"HP {bestHp}%");

            picks.Add(new(bestRow, bestEff, true, string.Join(", ", why)));
            taken.Add(bestRow);
            covered |= Answers(bestRow);
            if (HitsWeakness(board, battle, bestRow))
                weaknessPicks++;
        }

        return picks;
    }

    /// <summary>
    ///     Best horn fills when the upcoming battle is unknown but the board is known (pre-entry / Bentbranch).
    ///     PURE. Scores each candidate as the sum of <see cref="Score"/> across every battle on that board, then
    ///     greedy-picks like <see cref="PickSlots"/>. Every Why string starts with <c>coverage board N</c> so a
    ///     coverage guess is never presented as a targeted pick.
    /// </summary>
    public static List<CrucibleBeastPick> PickSlotsCoverage(
        int board,
        IReadOnlyList<int> candidateRows,
        IReadOnlyDictionary<int, int> hpPercentByRow,
        int slots = 3)
    {
        var battles = new List<int>(8);
        foreach (var info in BST_CrucibleData.Battles)
        {
            if (info.Board == board)
                battles.Add(info.Battle);
        }

        var picks = new List<CrucibleBeastPick>(slots);
        var covered = CrucibleNeeds.None;
        var taken = new HashSet<int>();
        var weaknessPicks = 0;
        var candidates = new List<int>(candidateRows.Count);
        var seen = new HashSet<int>();
        foreach (var row in candidateRows)
        {
            if (row is < 1 or > BST_Beasts.Count || !seen.Add(row))
                continue;
            candidates.Add(row);
        }

        if (battles.Count == 0)
            return picks;

        for (var n = 0; n < slots; n++)
        {
            var bestRow = 0;
            var bestEff = int.MinValue;
            var bestHp = -1;
            var bestHpKnown = true;

            foreach (var row in candidates)
            {
                if (taken.Contains(row))
                    continue;

                var hpKnown = hpPercentByRow.TryGetValue(row, out var hp);
                if (!hpKnown)
                    hp = 100;
                if (hp <= 0)
                    continue;

                var raw = 0;
                var any = false;
                foreach (var battle in battles)
                {
                    var s = Score(board, battle, row, covered, null, weaknessPicks);
                    if (s == int.MinValue)
                        continue;
                    raw += s;
                    any = true;
                }
                if (!any)
                    continue;
                var eff = EffectiveScore(raw, hp);

                var better = eff > bestEff
                             || (eff == bestEff && hp > bestHp)
                             || (eff == bestEff && hp == bestHp && (bestRow == 0 || row < bestRow));
                if (!better)
                    continue;

                bestEff = eff;
                bestRow = row;
                bestHp = hp;
                bestHpKnown = hpKnown;
            }

            if (bestRow == 0)
                break;

            var why = new List<string>(6) { $"coverage board {board}, battle unidentified" };
            // Prefer the first non-boss battle's reasons as the readable sample; always keep the coverage tag.
            var sampleBattle = battles.Contains(1) ? 1 : battles[0];
            Score(board, sampleBattle, bestRow, covered, why, weaknessPicks);
            if (!bestHpKnown)
                why.Add("HP assumed full");
            else if (bestHp < 100)
                why.Add($"HP {bestHp}%");

            picks.Add(new(bestRow, bestEff, true, string.Join(", ", why)));
            taken.Add(bestRow);
            covered |= Answers(bestRow);
            if (HitsWeakness(board, sampleBattle, bestRow))
                weaknessPicks++;
        }

        return picks;
    }

    /// <summary>
    ///     Latch for one Crucible formation-phase autograb pass. PURE: the live layer feeds screen-open and
    ///     identified-battle signals; this type never reads game memory. <see cref="Aborted"/> stays set until
    ///     the screen closes (re-arm on battle change does not clear an abort).
    ///     <see cref="SurfaceKey"/> distinguishes pre-entry from in-board so a Bentbranch pass and the first
    ///     in-board pass are two phases.
    /// </summary>
    public readonly record struct FormationArmState(
        bool ScreenOpen, int BattleKey, bool PassDone, bool Aborted, int SurfaceKey = 0);

    /// <summary>
    ///     Advance the formation-phase latch. Screen close clears everything. Screen open after close, a
    ///     battle-key change, or a surface change while the screen stays open and the phase was not aborted,
    ///     re-arms the pass. Territory alone is not a re-arm signal. PURE.
    /// </summary>
    public static FormationArmState NextFormationArm(
        FormationArmState prev, bool screenOpen, int battleKey, int surfaceKey = 0)
    {
        if (!screenOpen)
            return new(false, 0, false, false, 0);

        if (!prev.ScreenOpen)
            return new(true, battleKey, false, false, surfaceKey);

        if ((battleKey != prev.BattleKey || surfaceKey != prev.SurfaceKey) && !prev.Aborted)
            return new(true, battleKey, false, false, surfaceKey);

        return new(true, battleKey, prev.PassDone, prev.Aborted, surfaceKey);
    }

    /// <summary> Whether the assign pass may run under the current latch. PURE. </summary>
    public static bool IsFormationArmed(in FormationArmState s) => s.ScreenOpen && !s.PassDone && !s.Aborted;

    /// <summary> Mark a completed (including soft) pass for this phase. PURE. </summary>
    public static FormationArmState MarkFormationPassDone(in FormationArmState s) => s with { PassDone = true };

    /// <summary> Mark a hard abort; stays closed until the screen drops. PURE. </summary>
    public static FormationArmState MarkFormationAborted(in FormationArmState s) =>
        s with { PassDone = true, Aborted = true };

    /// <summary> One planned Battlehorn rewrite: slot index 0..2, previous row (0 = empty), desired row. </summary>
    public readonly record struct HornSlotChange(int Slot, int FromRow, int ToRow);

    /// <summary>
    ///     Which horn slots actually need a write given the current horn occupants as familiar rows and a
    ///     <see cref="PickSlots"/> result. PURE: no game types. Slots already matching are omitted so a
    ///     second pass does not oscillate. Empty desired slots (fewer than 3 picks) are left alone.
    ///     Callers on the live horn screen must first map <c>SelectedPetIds</c> indices through
    ///     <see cref="ResolveHornPetRows"/>.
    /// </summary>
    public static List<HornSlotChange> PlanHornChanges(
        IReadOnlyList<int> currentSelectedPetIds,
        IReadOnlyList<CrucibleBeastPick> picks)
    {
        var changes = new List<HornSlotChange>(3);
        for (var slot = 0; slot < 3; slot++)
        {
            var want = slot < picks.Count ? picks[slot].Row : 0;
            if (want == 0)
                continue;
            var have = slot < currentSelectedPetIds.Count ? currentSelectedPetIds[slot] : 0;
            if (have != want)
                changes.Add(new(slot, have, want));
        }
        return changes;
    }

    /// <summary> One planned Crucible run roster rewrite: familiar to remove (FromRow, 0 if none), familiar to add (ToRow, 0 if none). </summary>
    public readonly record struct RosterSlotChange(int FromRow, int ToRow);

    /// <summary>
    ///     Which roster familiars actually need a write given the current roster occupants as familiar rows and a
    ///     <see cref="PickSlotsCoverage"/> result. PURE: no game types. Familiars already present are omitted so a
    ///     second pass does not oscillate. Any current familiar not in desired is planned for removal; any desired
    ///     familiar not in current is planned for addition.
    /// </summary>
    public static List<RosterSlotChange> PlanRosterChanges(
        IReadOnlyList<int> currentRosterPetIds,
        IReadOnlyList<CrucibleBeastPick> picks)
    {
        var desired = new List<int>(picks.Count);
        for (var i = 0; i < picks.Count; i++)
        {
            if (picks[i].Row >= 1 && picks[i].Row <= BST_Beasts.Count && !desired.Contains(picks[i].Row))
                desired.Add(picks[i].Row);
        }

        var changes = new List<RosterSlotChange>();
        // Extras in current that are not in desired -> remove (ToRow = 0)
        foreach (var cur in currentRosterPetIds)
        {
            if (cur >= 1 && cur <= BST_Beasts.Count && !desired.Contains(cur))
                changes.Add(new RosterSlotChange(cur, 0));
        }
        // Missing in current that are in desired -> add (FromRow = 0)
        foreach (var want in desired)
        {
            if (want >= 1 && want <= BST_Beasts.Count && !currentRosterPetIds.Contains(want))
                changes.Add(new RosterSlotChange(0, want));
        }
        return changes;
    }

    /// <summary>
    ///     Whether <paramref name="selectedPetIds"/> is the battlehorn index basis (each value indexes
    ///     <paramref name="partyRows"/>) rather than a roster of familiar row ids. PURE.
    ///     Empty is treated as horn-capable when <paramref name="partyCount"/> is at least 1 so a fresh
    ///     empty horn can be filled; a vector longer than 3, or any out-of-range value, is not horn basis.
    ///     Callers that know the open screen must use <see cref="IsHornWriteBasis"/> so a roster-menu
    ///     surface cannot be mis-routed by a transient ≤3 index-looking vector.
    /// </summary>
    public static bool IsHornIndexBasis(IReadOnlyList<int> selectedPetIds, int partyCount)
    {
        if (partyCount <= 0)
            return false;
        if (selectedPetIds.Count > 3)
            return false;
        for (var i = 0; i < selectedPetIds.Count; i++)
        {
            var v = selectedPetIds[i];
            if (v < 0 || v >= partyCount)
                return false;
        }
        return true;
    }

    /// <summary>
    ///     Whether the Auto-fill write path may treat <paramref name="selectedPetIds"/> as battlehorn
    ///     indices. PURE. Screen/shape is checked before the size/index test: the Bentbranch entry
    ///     roster (pre-entry familiar party without the three-slot ActivePet UI) holds familiar row
    ///     ids in SelectedPetIds once settled, and a transient ≤3 index-looking read there must not
    ///     take the horn path (live .222 defect: sl=3.5.6 at roster-menu-open → basis=horn →
    ///     readback=fail, undamaged). Board surfaces and ActivePet keep the index-size test alone.
    /// </summary>
    public static bool IsHornWriteBasis(IReadOnlyList<int> selectedPetIds, int partyCount, bool rosterSurface)
    {
        if (rosterSurface)
            return false;
        return IsHornIndexBasis(selectedPetIds, partyCount);
    }

    /// <summary>
    ///     Map horn-slot indices in <paramref name="selectedPetIds"/> through the party roster to familiar
    ///     rows. Out-of-range indices become 0. PURE.
    /// </summary>
    public static List<int> ResolveHornPetRows(IReadOnlyList<int> selectedPetIds, IReadOnlyList<int> partyRows)
    {
        var rows = new List<int>(selectedPetIds.Count);
        for (var i = 0; i < selectedPetIds.Count; i++)
        {
            var idx = selectedPetIds[i];
            rows.Add(idx >= 0 && idx < partyRows.Count ? partyRows[idx] : 0);
        }
        return rows;
    }

    /// <summary>
    ///     Index of <paramref name="petRow"/> in <paramref name="partyRows"/>, or -1. PURE.
    /// </summary>
    public static int FindPartyIndex(IReadOnlyList<int> partyRows, int petRow)
    {
        if (petRow is < 1 or > BST_Beasts.Count)
            return -1;
        for (var i = 0; i < partyRows.Count; i++)
            if (partyRows[i] == petRow)
                return i;
        return -1;
    }

    /// <summary>
    ///     Whether two selection vectors match (same length and values). PURE. Used for abort-restore.
    /// </summary>
    public static bool SelectionEquals(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    /// <summary>
    ///     Convert AgentXBMPetParty HP uint arrays into the percent map <see cref="PickSlots"/> takes.
    ///     PURE. <c>cur == 0</c> or <c>max == 0</c> → 0% (dead); invalid rows are skipped (absent = assumed full).
    /// </summary>
    public static Dictionary<int, int> HpPercentByRow(IReadOnlyList<(int Row, uint Current, uint Max)> pets)
    {
        var map = new Dictionary<int, int>(pets.Count);
        foreach (var (row, cur, max) in pets)
        {
            if (row is < 1 or > BST_Beasts.Count)
                continue;
            if (max == 0 || cur == 0)
            {
                map[row] = 0;
                continue;
            }
            if (cur > max)
                continue;
            map[row] = (int)Math.Clamp(Math.Round(100.0 * cur / max), 0, 100);
        }
        return map;
    }

    /// <summary>
    ///     The best <paramref name="count"/> captured familiars for a battle, chosen greedily so a later pick is not
    ///     credited for a need an earlier pick already answers. Full-HP view of the unlocked roster (empty-horn
    ///     warning); run-state ranking for autograb is <see cref="PickSlots"/>.
    /// </summary>
    public static List<CrucibleBeastPick> Pick(int board, int battle, Func<int, bool> captured, int count = 3)
    {
        var picks = new List<CrucibleBeastPick>(count);
        var covered = CrucibleNeeds.None;
        var taken = new HashSet<int>();
        var weaknessPicks = 0;

        for (var n = 0; n < count; n++)
        {
            var bestRow = 0;
            var bestScore = int.MinValue;
            for (var row = 1; row <= BST_Beasts.Count; row++)
            {
                if (taken.Contains(row) || !captured(row))
                    continue;
                var s = Score(board, battle, row, covered, null, weaknessPicks);
                if (s > bestScore)
                {
                    bestScore = s;
                    bestRow = row;
                }
            }

            if (bestRow == 0)
                break;

            var why = new List<string>(4);
            Score(board, battle, bestRow, covered, why, weaknessPicks);
            picks.Add(new(bestRow, bestScore, true, string.Join(", ", why)));
            taken.Add(bestRow);
            covered |= Answers(bestRow);
            if (HitsWeakness(board, battle, bestRow))
                weaknessPicks++;
        }

        return picks;
    }

    private static bool HitsWeakness(int board, int battle, int row)
    {
        var element = BST_CrucibleData.BeastProfiles[row].AutoElement;
        if (element == CrucibleWeakness.None)
            return false;
        foreach (var e in BST_CrucibleData.Enemies)
            if (e.Board == board && e.Battle == battle && e.Weakness == element)
                return true;
        return false;
    }

    /// <summary> Uncaptured familiars (capturable by the board's level) that would beat the weakest current pick by 3+. </summary>
    public static List<CrucibleBeastPick> WorthCapturing(int board, int battle, Func<int, bool> captured, IReadOnlyList<CrucibleBeastPick> picks, int count = 2)
    {
        var level = BST_CrucibleData.Boards[board - 1].Level;
        var bar = picks.Count < 3 ? int.MinValue : picks.Min(p => p.Score) + 3;
        var covered = CrucibleNeeds.None;
        var weaknessPicks = 0;
        foreach (var p in picks)
        {
            covered |= Answers(p.Row);
            if (HitsWeakness(board, battle, p.Row))
                weaknessPicks++;
        }

        var result = new List<CrucibleBeastPick>();
        for (var row = 1; row <= BST_Beasts.Count; row++)
        {
            if (captured(row) || BST_Beasts.All[row].CaptureLevel > level)
                continue;
            var why = new List<string>(4);
            var s = Score(board, battle, row, covered, why, Math.Max(0, weaknessPicks - 1));
            if (s >= bar)
                result.Add(new(row, s, false, string.Join(", ", why)));
        }

        return result.OrderByDescending(p => p.Score).Take(count).ToList();
    }

    /// <summary>
    ///     A roster for the whole board (the beast-selection limit): familiars that appear in battles' picks, most
    ///     battles first, then by total score. Battles only a random space leads to count half.
    /// </summary>
    public static List<(int Row, int Battles)> BoardRoster(int board, Func<int, bool> captured)
    {
        var info = BST_CrucibleData.Boards[board - 1];
        var tally = new Dictionary<int, (double Weight, int Battles, int Score)>();

        foreach (var battle in BST_CrucibleData.Battles)
        {
            if (battle.Board != board)
                continue;
            foreach (var pick in Pick(board, battle.Battle, captured))
            {
                var t = tally.GetValueOrDefault(pick.Row);
                tally[pick.Row] = (t.Weight + (battle.RandomOnly ? 0.5 : 1.0), t.Battles + 1, t.Score + pick.Score);
            }
        }

        return tally
            .OrderByDescending(kv => kv.Value.Weight)
            .ThenByDescending(kv => kv.Value.Score)
            .Take(info.Roster)
            .Select(kv => (kv.Key, kv.Value.Battles))
            .ToList();
    }
}
