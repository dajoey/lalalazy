using System.Collections.Generic;
using System.Linq;

namespace Lalalazy.Crucible;

// Crucible of the Unbroken (Beastmaster-only duty, 7.56). Pure data, no Dalamud types: shared SOURCE
// (GluttonyCombo + LazyCrucible + both harnesses). The per-enemy panel table (BST_CrucibleData.Generated.cs) comes
// straight from the game's own XBM sheets; the sets in this file are hand-authored from the Crucible
// guides (Icy Veins, consolegameswiki, nettoge, asellog) and a public reaction set, with status ids
// confirmed against the 7.56 Status sheet.

/// <summary> The weakness shown on an enemy's Crucible panel (XBMElement row). </summary>
public enum CrucibleWeakness : byte
{
    None = 0,
    Fire = 1,
    Wind = 2,
    Earth = 3,
    Lightning = 4,
    Ice = 5,
    Water = 6,
    Blunt = 7,
    Piercing = 8,
    Slashing = 9,
}

/// <summary> What an enemy's panel casts call for. </summary>
[System.Flags]
public enum CrucibleNeeds : byte
{
    None = 0,
    /// <summary> A cast can be interrupted (Soul Crush). </summary>
    Interrupt = 1 << 0,
    /// <summary> A cast grants the enemy a buff that can be dispelled (Quelling Wave, Bloodcurdling Caw). </summary>
    Dispel = 1 << 1,
    /// <summary> A cast inflicts a debuff that can be cleansed (Scouring Ash). </summary>
    Cleanse = 1 << 2,
}

/// <summary> How Snarl / Challenge automation behaves. </summary>
public enum CrucibleAggroMode : byte
{
    Off = 0,
    /// <summary> Decide, but only record the choice in telemetry. </summary>
    Shadow = 1,
    On = 2,
}

/// <summary> Board space type of a battle (XBMContentStageEvent). </summary>
public enum CrucibleRole : byte
{
    Enemy = 0,
    EliteEnemy = 1,
    Boss = 2,
}

/// <summary> One battle (XBMContentBattle row = board, subrow = battle; battle 0 is the boss). </summary>
public readonly record struct CrucibleBattleInfo(byte Board, byte Battle, CrucibleRole Role, bool RandomOnly);

/// <summary>
///     A familiar's Crucible profile (XBMPet). <see cref="Stats"/> is STR, INT, PHY R, MAG R, CON at beast ranks
///     5, 10, 15, 20, 25, flattened rank-first (25 values).
/// </summary>
public readonly record struct CrucibleBeastProfile(byte Row, CrucibleWeakness AutoElement, bool AutoMagic, ushort Inflicts, ushort[] Stats)
{
    public const int Str = 0, Int = 1, PhysRes = 2, MagRes = 3, Con = 4;

    /// <summary> A stat at the rank sync of board 1-5 (ranks 5/10/15/20/25). </summary>
    public int StatAtBoard(int board, int stat) =>
        Stats is null || board is < 1 or > 5 ? 0 : Stats[(board - 1) * 5 + stat];
}

/// <summary> One Crucible board (XBMContent row). </summary>
public readonly record struct CrucibleBoard(
    byte Board,
    uint Territory,
    uint ContentFinderCondition,
    byte Level,
    ushort ItemLevel,
    byte BeastRank,
    byte Roster,
    byte Battles,
    string Name);

/// <summary>
///     One enemy from a Crucible panel (XBMBattleDetail). <see cref="Vulnerable"/> bit i set = the enemy is
///     vulnerable to: 0 Slow, 1 Petrify, 2 Paralysis, 3 Interrupt, 4 Blind, 5 Poison, 6 Stun, 7 Sleep,
///     8 Bind, 9 Heavy, 10 Doom. <see cref="Stars"/> packs the panel's 1-5 star ratings, 3 bits each:
///     STR, INT, PHY R, MAG R, CON (high to low bits).
/// </summary>
public readonly record struct CrucibleEnemy(
    uint NameId,
    byte Board,
    byte Battle,
    byte Sub,
    CrucibleWeakness Weakness,
    ushort Vulnerable,
    CrucibleNeeds Needs,
    ushort Stars,
    string Name)
{
    public int StarStr => (Stars >> 12) & 7;
    public int StarInt => (Stars >> 9) & 7;
    public int StarPhysRes => (Stars >> 6) & 7;
    public int StarMagRes => (Stars >> 3) & 7;
    public int StarCon => Stars & 7;
}

internal static partial class BST_CrucibleData
{
    /// <summary> TerritoryType of the First Board; boards 1-5 are 1339-1343 (the only territories with intended use 62). </summary>
    public const uint FirstTerritory = 1339;

    /// <summary> Board 1-5 for a TerritoryType, 0 outside the Crucible. </summary>
    public static int BoardOfTerritory(uint territory) =>
        territory is >= FirstTerritory and < FirstTerritory + 5 ? (int)(territory - FirstTerritory + 1) : 0;

    // ------------------------------------------------------------------ statuses

    /// <summary>
    ///     Counter / reflect stances: attacking the enemy while it has one hurts the attacker.
    ///     5434 Paralyzing Spikes (Third Board Sahagin), 2528 Ice Spikes (First Master's snoll),
    ///     5465 Blaze Spikes (Second Master's drake), 5145 Needles Out (Second Master's spinemoles).
    /// </summary>
    public static readonly HashSet<uint> StanceStatuses = [5434, 2528, 5465, 5145];

    /// <summary> One research dispel row: the fight, the buff's status id, and what the id rests on. </summary>
    internal readonly record struct DispelRow(int Board, int Battle, uint StatusId, string Buff, string Basis);

    /// <summary>
    ///     Every dispel counter in the fight research (CrucibleGuide.json, 13 rows) with the status id the game puts on the enemy.
    ///     The rotation dispels a buff whose id is here (and on the panel's list), on whichever enemy carries it. Basis:
    ///     "panel" = the enemy panel flags the cast's buff dispellable; "live" = the id was recorded on the enemy in a real
    ///     run (ffxivdb status_events) and the guides say to dispel it, but the panel does not flag it; "guide" = the guides
    ///     say so and the id comes from the panel's cast table, never seen on an enemy in a run; "disproven" = a run sent dispels and the
    ///     status stood (989 Regen: 12 Quelling Waves at the Abaddon 2026-10-03 19:34-19:35, effects damage only, no removal; the same
    ///     cast on the Drake's Blaze Spikes carried the removal effect), or the cross-checked research says the game does not honour a
    ///     dispel on it (3129 Impassion, Medusa: the panel does not flag it, S4 and S7 say none of the three borrowed answers removes
    ///     it, and no dispel was ever recorded landing on it), so it is never tried. A "live" / "guide" id has never been
    ///     dispelled in a run, so the rotation gives up on it after <see cref="BST_CrucibleLogic.DispelMaxTries"/> dispels that left it standing.
    ///     The test in BSTRotationHarness asserts one row per guide counter, so a new dispel need cannot ship without an id.
    /// </summary>
    public static readonly DispelRow[] DispelRows =
    [
        new(1, 0, 1225, "Damage Up on the boss from Fanaticism", "panel"),
        new(1, 1, 2074, "Physical Damage Up from Ossify (Bone Knight)", "panel"),
        new(1, 3, 1225, "Clear Mind (Damage Up on the Piscodemon)", "panel"),
        new(2, 0, 5423, "Popoto Skin from Kinborrow (Loosefrox Inkyjots)", "panel"),
        new(2, 5, 1225, "Damage Up on the Elder from Rallying Cheer", "panel"),
        new(3, 5, 1572, "Might on the Golem", "live"),
        new(4, 1, 5020, "Ultimate Focus (Magic Damage Up, Strix Piece)", "panel"),
        new(4, 8, 390, "Growing from Grab and Grow (Sapling Piece)", "live"),
        new(4, 9, 2528, "Ice Spikes (Snoll Piece)", "panel"),
        new(5, 5, 5465, "Blaze Spikes (Drake Piece)", "panel"),
        new(5, 5, 989, "Regen (Rehabilitation) on the Abaddon from eaten Morphos", "disproven"),
        new(5, 7, 3129, "Impassion Damage Up (Medusa Piece)", "disproven"),
        new(5, 11, 1225, "Spirit of Pompetition (Kinged Swordsmog)", "panel"),
    ];

    /// <summary>
    ///     Enemy buffs worth a dispel: the panel's dispellable buffs plus every status id a research dispel row names
    ///     (<see cref="DispelRows"/>) unless a run disproved it. 989 Regen was added on 2026-10-03 on the guides' word and taken out the
    ///     same evening: the game does not dispel it.
    /// </summary>
    /// <remarks> Lazy: static initializers in different files of a partial class run in no guaranteed order. </remarks>
    public static HashSet<uint> DispellableBuffs => _dispellableBuffs ??= [.. PanelDispellableBuffs, .. DispelRows.Where(r => r.Basis != "disproven").Select(r => r.StatusId)];

    private static HashSet<uint>? _dispellableBuffs;

    /// <summary>
    ///     The enemy panel itself flags this buff dispellable. Those are the ones the game is known to honour; every other
    ///     dispellable id (the guides' "live" / "guide" rows) is unproven until a dispel is seen to take it off.
    /// </summary>
    public static bool PanelFlagsDispellable(uint statusId) => PanelDispellableBuffs.Contains(statusId);

    /// <summary>
    ///     Crucible damage-immune states on enemies, on top of the plugin's general invulnerability check (BossmodReborn
    ///     Crucible modules + MagitekRoutine): 4175 Burning Ward (First Board ogre), 4410 Invincibility (First Master's
    ///     progenitrix), 2198 Vulnerability Down (Third Board ymir, Second Master's sphinx), 2413 Covered (an enemy the
    ///     guardia covers: hit the guardia first).
    /// </summary>
    public static readonly HashSet<uint> InvulnerableStatuses = [4175, 4410, 2198, 2413, 616];

    /// <summary>
    ///     Whether a status makes a Crucible enemy immune to damage. The native actor
    ///     invincibility flag does not cover these encounter-specific statuses.
    /// </summary>
    public static bool IsDamageImmunityStatus(uint statusId) =>
        InvulnerableStatuses.Contains(statusId);

    /// <summary>
    ///     Petrify on an enemy: Stone Curse 437, what the Medusa's Petrifying Regard applies to the target its baited
    ///     cone hits (the guides' play is to bait it onto one of the four Cyclopes, and a petrified Cyclops dies to
    ///     any hit — M2 pieces 5.7). The state was not read before round 4: the petrified enemy sat in the ordinary
    ///     kill order instead of being taken at once.
    /// </summary>
    public static readonly HashSet<uint> PetrifiedStatuses = [437];

    /// <summary> Directional Parry (Forward Guard). 680 is the named status everywhere. </summary>
    public static readonly HashSet<uint> ParryStatuses = [680];

    /// <summary>
    ///     2552 is an unnamed permanent status the First Board bone knight carries during Forward Guard, but Guttler and the
    ///     ice dragon carry it too: count it as a parry only on the bone knight.
    /// </summary>
    public const uint BoneKnightParryStatus = 2552;

    public static readonly HashSet<uint> BoneKnightNameIds = [14531, 14566];

    /// <summary>
    ///     Forward Guard casts (Bone Knight): directional parry that blocks hits from the front.
    ///     Parting Blow recalls the familiar before the guard lands so pets are not locked or killed on it.
    /// </summary>
    public static readonly HashSet<uint> ForwardGuardCasts = [46864];

    /// <summary>
    ///     Curtains for Rank 5 (King Ahriman): a 6.0 s cast that KOs the familiar whatever its HP. The game starts it as TWO simultaneous
    ///     casts, 49428 and 49429, and the target's castbar reads 49428 (live 2026-10-03: 47.6 s and 164.3 s, both familiars died to the
    ///     effect because the rule only knew 49429). Parting Blow recalls the familiar before either resolves.
    /// </summary>
    public static readonly HashSet<uint> CurtainsCasts = [49428, 49429];

    /// <summary> Physical Vulnerability Up from the mantis's Eerie Soundwave: Final Sting lands inside this window. </summary>
    public const uint PhysicalVulnerabilityUp = 5180;

    /// <summary> Familiars whose Tempered Release sets up burst (vulnerability up / resistance down): summoned before the others. </summary>
    public static readonly HashSet<int> SetUpBeasts = [16, 20, 41, 9, 23, 33, 34, 38, 46];

    /// <summary> Bat: Ultrasonics removes a detrimental effect from nearby party members. </summary>
    public const int BatRow = 19;

    // ------------------------------------------------------------------ objects

    /// <summary>
    ///     Enemies that must not be attacked, with how far from the target an AoE may land before it hits them
    ///     (yalms; float.MaxValue = anywhere in the arena).
    ///     Zu eggs 14575/14576 hatch when hit, AoE included (Third Board). Morpho pieces 14656 set off
    ///     room-wide confusion when they die (Second Master's Board).
    /// </summary>
    public static readonly Dictionary<uint, float> DoNotAttack = new()
    {
        [14575] = 10f,
        [14576] = 10f,
        [14656] = float.MaxValue,
    };

    /// <summary>
    ///     Pairs that must die together: killing one first enrages the other (elder / younger tablitaur, Second
    ///     Board elite; Loosefrox Inkyjots / Chewchum Popoto, Second Board boss), or a survivor revives the one
    ///     that died first — the Summon's two Lamias share one id and Raise 49287 revives a dead Lamia, so they
    ///     are killed evenly: halve one, switch, finish both. A pair whose members share one id constrains two
    ///     actors of that id; the lower-HP one is dropped while they are more than PairHpGap apart.
    /// </summary>
    public static readonly (uint A, uint B)[] Pairs = [(14555, 14556), (14561, 14562), (14661, 14661)];

    /// <summary>
    ///     The pairs whose first kill is held back until the other is close (r4-target1 2.5-06): the Second Board elite's brothers.
    ///     The Loosefrox / Chewchum kill order is unsettled in the dossier (G8 against AS), so that pair keeps the plain balancing.
    /// </summary>
    public static readonly (uint A, uint B)[] FinalBlowPairs = [(14555, 14556)];

    /// <summary> The other member of a <see cref="FinalBlowPairs"/> pair, 0 when <paramref name="nameId"/> is in none. </summary>
    public static uint FinalBlowPartner(uint nameId)
    {
        foreach (var (a, b) in FinalBlowPairs)
        {
            if (nameId == a)
                return b;
            if (nameId == b)
                return a;
        }

        return 0;
    }

    /// <summary> Recharge (Demon / Evil Weapon / Devilet Piece): one stack every 2 s on the enemy that gorges; Damage Up (5409) comes with the fifth. </summary>
    public const uint RechargeStatus = 5406;

    /// <summary>
    ///     Adds the guides kill fast that are not part of the pinned game-data <see cref="PriorityAdds"/> set (the harness asserts that set
    ///     exactly): they join it for targeting. Seedling Piece 14600 (First Master's, the Morbol fight): "kill them, the poison is not
    ///     worth leaving up".
    /// </summary>
    public static readonly HashSet<uint> FastKillAdds = [14600];

    /// <summary>
    ///     Hard hits on the character (or the highest-enmity target) that a familiar with enough HP can take under Snarl: castbar
    ///     action ids bound from BossmodReborn's Crucible modules and the guides (two sources, or one real log), 2026-09-17.
    ///     Snarl -> Parting Blow no longer dodges them: the 7.56 hotfix of 2026-09-17 removes Cover before the hit, and the
    ///     recorded runs agree (about 92% of such hits went back to the character), so Parting Blow before the hit is not an answer.
    ///     Master's Board entries are the casts the recorded runs measured as single-target hits that follow the enmity holder
    ///     (<see cref="HeavyCasts"/>, kind Tankbuster: victim == holder in every logged cast); the two Second Master's ids in
    ///     <see cref="GuideBoundTankbusters"/> were never seen cast and stay guide-derived.
    /// </summary>
    public static readonly HashSet<uint> Tankbusters =
    [
        46935, 46934, 46872, 46906, 46920,        // First Board: Cold Caress, Blood Sword, Skullsplinter, Deadly Thrust, Straight Punch
        48138, 48204, 48247,                      // Second Board: Deadly Hold, 100-tonze Slash, Void Paralyze
        48517,                                    // Second Board: Goblin Rush (guide 5.0 s cast, Loosefrox Inkyjots; 0 recorded rows - never cast in the logs)
        48620, 48471, 48489, 50465, 48563,        // Third Board: Thunderbolt, Crushing Blade, Caustic Vomit, Flying Frenzy, Song of Torment
        48485,                                    // Third Board battle 2: Dreadwash (follows the enmity holder in 64 of 64 completed casts; HeavyCasts row)
        48809, 48822, 48689, 48730,               // First Master's (measured): Toxic Vomit, Salivous Snap, Final Sting, Grim Fate (five-hit string)
        48717, 50649, 48669,                      // First Master's (measured): Sweeping Evisceration, Obliterate, On the Properties of Darkness
        49188, 49254,                             // Second Master's (measured): Erratic Blaster, Mangling Fang
        49470, 49205,                             // Second Master's (guide only, never seen cast): Thunderbolt, Void Thunder III
    ];

    /// <summary>
    ///     Registered tankbusters the logs measured on the character but never on a familiar (R4 rows 3.1-08a, 2.0-07): the
    ///     cover's familiar-HP gate uses a documented conservative estimate, the basis hit (the cast's own largest measured hit
    ///     on the character, or where the cast itself was never seen the largest measured character hit of the same board's
    ///     tankbusters as a stand-in) times the worst measured familiar-to-character ratio among the measured Tankbuster rows
    ///     (Sweeping Evisceration 2021/1225 = 1.65). The estimate never takes the death-cover: no log says a familiar took the
    ///     cast, so nothing says a dying familiar would survive it.
    /// </summary>
    public static readonly Dictionary<uint, (int BasisOnCharacter, int EstimatedFamiliarHit, string Evidence)> EstimatedTankbusterHits = new()
    {
        [48471] = (455, 751, "Crushing Blade: 88 of 88 recorded hits on the character, max 455; never hit a familiar (ffxivdb 09-24..10-15). Estimate 455 x 1.65 = 751."),
        [48517] = (549, 906, "Goblin Rush: 0 recorded casts (Chewchum Popoto died first in every logged pull); the basis is the stand-in from the same board's measured tankbusters (100-tonze Slash 549). Estimate 549 x 1.65 = 906. Guide: 5.0 s cast, Loosefrox Inkyjots."),
    };

    /// <summary>
    ///     Casters whose single-target hit follows enmity but has no castbar (an instant press) — the Third Board bone
    ///     bishop's Blizzard 48621 (pinned to the ice sprite 14607 and the kupocaster 14679 by the 7.56 action sheets'
    ///     matching text) and the Second Master's Hapalit 14691's Straight Punch 49435 (R4 rows 3.1-09, 5.13): the Snarl
    ///     holds the caster while the familiar's HP is at least the largest hit on a familiar the logs recorded for the
    ///     cast, scaled and margined like the tankbuster cover.
    /// </summary>
    public static readonly Dictionary<uint, (uint HitId, int MaxOnFamiliar, string Evidence)> PanelBurstCasters = new()
    {
        [14567] = (48621, 48, "Blizzard: familiars max 48 (Diremite/Bat/Colibri/Puk), character 1288-1280 in 16 hits (ffxivdb 09-24..10-15); the sheet text pins the same cast to the ice sprite and kupocaster."),
        [14607] = (48621, 48, "Blizzard: the ice sprite's sheet text matches the bone bishop's; no separate recording."),
        [14679] = (48621, 48, "Blizzard: the kupocaster's sheet text matches the bone bishop's; no separate recording."),
        [14691] = (49435, 909, "Straight Punch: familiars max 909 (Ziz), character 10 hits max 652 (ffxivdb 09-24..10-15); no castbar (0 cast lines, sheet 0.0 s)."),
    };

    /// <summary>
    ///     Seconds from the moment the plugin's castbar remaining reaches 0 to the hit landing (negative = the hit lands first). Measured
    ///     for the Master's Board casts in <see cref="HeavyCasts"/>; 0 for every other id (no measurement: Boards 1-3 and the guide-only
    ///     Master's ids).
    /// </summary>
    public static float TankbusterHitDelay(uint castId) =>
        HeavyCast(castId) is { Kind: CrucibleHitKind.Tankbuster } row ? row.HitDelay : 0f;

    /// <summary>
    ///     Crucible heal potions as the actions the character performs, strongest first (a G3 healed 39% to 75% of a
    ///     7,950 HP bar on 2026-10-03). The HUD slot binding and the stock are the game's: a press with none held fails
    ///     silently, so the live layer offers only ids with stock in the HUD (see <see cref="HealPotionItemRows"/>) and
    ///     a clear recast, and steps down a grade when a press does not land. Crucible Ash G3-G1 (46965-46963: 40 / 25 / 10% of HP to
    ///     the character and nearby allies, familiar included) is the next grade after every Beast Potion; it is drunk through the same
    ///     item menu (AutoDuty's Crucible item engine uses the same open-slot, first-entry steps for rows 80-82).
    /// </summary>
    public static readonly uint[] HealPotionActions = [46962, 46961, 46960, 46959, 46965, 46964, 46963];

    /// <summary>
    ///     The Blessed Horn (XBMItem sheet row 96) as its use action, from the 7.56 sheet (fetched live 2026-10-15).
    ///     A KO'd familiar cannot be summoned again by any means except this item (S9 asellog L260, S4 CGW Crucible
    ///     guide), so the rotation offers it through the item HUD exactly like a heal potion.
    /// </summary>
    public const uint BlessedHornAction = 46979;

    /// <summary> XBMItem sheet row -> revive action, for the same HUD stock walk the heal potions use. </summary>
    public static readonly Dictionary<uint, uint> ReviveItemRows = new() { [96] = BlessedHornAction };

    /// <summary>
    ///     BNpcName -> status bits an enemy resists, beyond what its Vulnerable bits say (same bit order; bit 5 =
    ///     poison). No sheet carries enemy resists, so this is hand-authored knowledge from the guides: the banemite
    ///     piece (1.4) shrugs poison off, and S17 lists only Ice familiars for that fight.
    /// </summary>
    public static readonly Dictionary<uint, ushort> ResistedStatuses = new() { [14536] = 1 << 5 };

    /// <summary>
    ///     XBMItem sheet row -> heal potion action, for the live HUD stock walk: the Crucible item HUD carries these
    ///     rows in its slots (76-79 = G1-G4 Beast Potion, the same numbering the game's shop and AutoDuty's item engine
    ///     use). Grounded in the captured XBMContentsMainHUD values (XB| lines): slot base 9 + i*5, held byte at +1,
    ///     XBMItem row at +3, name at +4.
    /// </summary>
    public static readonly Dictionary<uint, uint> HealPotionItemRows = new()
    {
        [76] = 46959,
        [77] = 46960,
        [78] = 46961,
        [79] = 46962,
        [80] = 46963, // G1 Crucible Ash
        [81] = 46964, // G2 Crucible Ash
        [82] = 46965, // G3 Crucible Ash
    };

    /// <summary>
    ///     Enemies auto-targeting takes first whenever they are up: the adds the guides kill on sight (succubi, wisps before
    ///     they reach the centre, ahriman after the gaze, zombies, a woken Thanatos, the guardia that covers its allies) and
    ///     the bone bishop before the bone knight, plus every panel add priority across B1-B3, M1 and M2. Identities are
    ///     pinned to the 7.56 sheet decode (wiki Crucible Game Data); the BSTRotationHarness asserts this exact set.
    /// </summary>
    public static readonly HashSet<uint> PriorityAdds =
    [
        // Board 1: bone bishop, miteling, wisp, great wisp, succubus mage + knight
        14532, 14537, 14539, 14748, 14542, 14543,
        // Board 2: ahriman, zombie, evil weapon, devilet
        14548, 14553, 14558, 14559,
        // Board 3: bone bishop, cockerel, pullet, golem, shambling, crawling, flowertender, guardia, Thanatos
        14567, 14573, 14574, 14581, 14585, 14586, 14589, 14591, 14595,
        // M1: queen hawk, ice sprite, biloko, sapling, diremite, grenade, bomb, toxic mass,
        //     Strix Plume (the add that casts the 12 s Aero III knockback; no cast of Aero III was ever flagged interruptible in
        //     five logged casts, so killing the add is the only counter - live 2026-10-01 it stood at 100% beside the boss while
        //     the cast went off in three runs),
        //     the Administrator's adds (bug, footman, dreadnaught, drone, cobra; the Administrator itself, 14610, is the battle's
        //     main enemy and dies last: BMR AdministratorPiece.cs priorities and the fight guide's kill order), pyrobolus
        14605, 14607, 14622, 14620, 14621, 14624, 14625, 14629, 14598,
        14611, 14612, 14613, 14614, 14616, 14627,
        // M2: lightning sprite, deepeye, bomb, atomos wave adds (gremlins, puddings, bavarois, flan, vodoriga, dahak),
        //     barbmole (drake-fight spinemole), spinner-rook, lamia, cyclops x2 (medusa + gigantis), congealed gels,
        //     moogle officers (kinged casters, kinged swordmog, melomog, mogmugger), hapalit, dirty eye, Thanatus statues,
        //     the 5.12 Cross Charge lightning sprite (the Behemoth holds Invincible while sprites live, so the sprite is
        //     ranked and the unranked Behemoth and Giant drop out while it lives)
        14632, 14639, 14640, 14641, 14643, 14644, 14645, 14646, 14647, 14648, 14649, 14653,
        14659, 14661, 14662, 14671, 14672, 14673, 14676, 14677, 14680, 14681, 14683, 14686, 14691, 14692, 14699,
        // M2 King Ahriman: the Final Hourglass the Roulette spawns (live 2026-10-03: with Hapalit and Dirty Eye up it was never attacked
        // and its Death killed the character through Doom; broken within ~12 s in the two roulettes with no adds up)
        14689,
    ];

    /// <summary>
    ///     Bosses whose auto-attacks are a frontal cone that also hits the familiar (guides, two independent sources:
    ///     "the boss of board 3 has this too", "siren autos are a frontal cleave that also hits the pet",
    ///     "Guttler's autos are a frontal cleave that hits the pet", Lauda's 120° 8y cone drawn by the public reaction set).
    ///     Rule (Crucible-wide, C2): the player holds enmity with Challenge so the enemy faces away from the familiar;
    ///     Snarl only to cover a known tankbuster cast, then Challenge back when it resolves.
    /// </summary>
    public static readonly HashSet<uint> CleaveAutoBosses = [14541, 14583, 14592, 14693];

    /// <summary>
    ///     Documented kill orders for priority adds that spawn together: each list is one fight's order, each inner array a
    ///     tier of enemies killed together, most dangerous tier first. Of the members of a list that are up, only the first
    ///     tier present stays targetable until it is dead (see <c>BST_CrucibleLogic.AllowedTargets</c>), so a wave is cleared
    ///     in the guides' order instead of nearest-first (live 2026-09-26: the siren wave's shamblings were attacked ~5 s
    ///     before the crawling piece whose touch breaks Unbeastable). Orders, from the fight guide's kill orders (the
    ///     research-to-behaviour audit of 2026-10-02 checked all 26): siren wave, crawling piece before the shamblings; treant
    ///     wave "Biloko &gt; Sapling &gt; Diremite &gt; Slug / Treant" (the biloko's Natural Nurture heals the wave, the sapling's
    ///     Grab and Grow buffs an ally); the woken Thanatos before its guardia; the progenitrix's grenade, then the pyrobolus, then
    ///     the bombs; the Administrator's bug and cobra, then the dreadnaught, then the drone and footman (2026-10-13, BMR and the
    ///     fight guide agree; no recorded run yet); the
    ///     boogyman's self-destructing bomb, the deepeye, then the light sprite; Pas de Seul's Succubus Mage before the Knights;
    ///     the ogre's small wisps before the great wisp; the pullet (Caustic Vomit) before the cockerel; the cactuar pack's
    ///     flowertender before the guardia; the Atomos summons bavarois, then pudding / flan / dahak, then gremlins and
    ///     vodoriga; the moogle finale's Mogmugger, then the Melomogs, then the kinged healer and caster, then the Kinged
    ///     Swordsmog. A priority add absent from every list is unranked: a wave of only unranked adds keeps the whole tier
    ///     targetable. An order only constrains the members of its own list that are up.
    /// </summary>
    public static readonly uint[][][] PriorityAddOrder =
    [
        [[14586], [14585]],                                    // siren wave: crawling first
        [[14622], [14620], [14621]],                           // treant wave: biloko, sapling, diremite
        [[14595], [14591]],                                    // Thanatos before the guardia that covers it
        [[14624], [14627], [14625]],                           // progenitrix wave: grenade, pyrobolus, bomb (BMR ProgenitrixPiece.cs 5 / 4 / 3)
        [[14640], [14639], [14641]],                           // boogyman wave: self-destructing bomb, deepeye, light sprite
        [[14680], [14683], [14681], [14676, 14677]],           // moogle finale: the Kinged Swordsmog first — killing her ends the fight even with others alive (kill her as soon as reachable); then Mogmugger, the Melomogs, the kinged healer and caster
        [[14542], [14543]],                                    // Pas de Seul: Succubus Mage before the Knights
        [[14539], [14748]],                                    // ogre: small wisps before the great wisp
        [[14574], [14573]],                                    // zu pack: pullet (Caustic Vomit) before the cockerel
        [[14589], [14591]],                                    // cactuar pack: flowertender before the guardia
        [[14646], [14645, 14647, 14649], [14644, 14643, 14648]], // Atomos summons: bavarois; pudding, flan, dahak; gremlins, vodoriga
        [[14689], [14691], [14692]],                           // King Ahriman: the Final Hourglass, then Hapalit (its Burns keeps landing) before the Dirty Eye
        [[14611, 14616], [14613], [14614, 14612]],             // Administrator: bug and cobra, dreadnaught, drone and footman (boss last; BMR + guide)
    ];

    /// <summary>
    ///     Casts whose caster is killed while it casts, because the guide's answer to them is "kill it before the cast ends": the Bone Bishop's
    ///     Soul Douse (50693: "kill the casting Bishop within 5 s"), the Deepeye's Oogle (49214: "kill the Deepeye during the cast") and the
    ///     Devilet's Void Fire III (48222: a 10 s room-wide cast, "open on the Devilet so it never casts"; 13 casts in the one recorded run).
    ///     A candidate casting one of these is the target before any other (an armed interrupt still comes first).
    /// </summary>
    public static readonly HashSet<uint> KillTheCaster = [50693, 49214, 48222];

    private static Dictionary<uint, (int Wave, int Tier)[]>? _priorityAddRanks;

    private static Dictionary<uint, (int Wave, int Tier)[]> PriorityAddMap()
    {
        if (_priorityAddRanks == null)
        {
            var map = new Dictionary<uint, List<(int, int)>>();
            for (var w = 0; w < PriorityAddOrder.Length; w++)
                for (var t = 0; t < PriorityAddOrder[w].Length; t++)
                    foreach (var id in PriorityAddOrder[w][t])
                    {
                        if (!map.TryGetValue(id, out var list))
                            map[id] = list = [];
                        list.Add((w, t));
                    }

            _priorityAddRanks = map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

        return _priorityAddRanks;
    }

    /// <summary> Tier of a priority add inside the documented order <paramref name="wave"/> (0 = kill first); <see cref="int.MaxValue"/> when it is not a member of that order. </summary>
    public static int PriorityAddTier(uint nameId, int wave)
    {
        if (PriorityAddMap().TryGetValue(nameId, out var memberships))
            foreach (var (w, t) in memberships)
                if (w == wave)
                    return t;
        return int.MaxValue;
    }

    /// <summary> Tier of a priority add in its (first) documented order; <see cref="int.MaxValue"/> when unranked. </summary>
    public static int PriorityAddRank(uint nameId) =>
        PriorityAddMap().TryGetValue(nameId, out var r) ? r[0].Tier : int.MaxValue;

    /// <summary> The first documented order a priority add belongs to, or -1 when unranked. </summary>
    public static int PriorityAddWave(uint nameId) =>
        PriorityAddMap().TryGetValue(nameId, out var r) ? r[0].Wave : -1;


    // ------------------------------------------------------------------ lookups

    /// <summary>
    ///     XBMContentBattle.BattleDetail → (board, battle). Sheet-derived (7.56); battle 0 is the boss.
    ///     AgentXBMStageDetailList EntryType-3 rows carry this id as BattleDetailId.
    /// </summary>
    private static readonly Dictionary<uint, (byte Board, byte Battle)> BattleByDetailId = new()
    {
        [1] = (1, 1),
        [2] = (1, 2),
        [3] = (1, 3),
        [4] = (1, 4),
        [5] = (1, 5),
        [6] = (1, 0),
        [7] = (2, 1),
        [8] = (2, 2),
        [9] = (2, 3),
        [10] = (2, 4),
        [11] = (2, 5),
        [12] = (2, 6),
        [13] = (2, 0),
        [14] = (3, 1),
        [15] = (3, 2),
        [16] = (3, 3),
        [17] = (3, 4),
        [18] = (3, 5),
        [19] = (3, 6),
        [20] = (3, 7),
        [21] = (3, 0),
        [22] = (4, 1),
        [23] = (4, 2),
        [24] = (4, 3),
        [25] = (4, 4),
        [26] = (4, 5),
        [27] = (4, 6),
        [28] = (4, 7),
        [29] = (4, 8),
        [30] = (4, 9),
        [31] = (4, 0),
        [32] = (5, 1),
        [33] = (5, 2),
        [34] = (5, 3),
        [35] = (5, 4),
        [36] = (5, 5),
        [37] = (5, 6),
        [38] = (5, 7),
        [39] = (5, 8),
        [40] = (5, 9),
        [41] = (5, 10),
        [42] = (5, 11),
        [43] = (5, 12),
        [44] = (5, 13),
        [45] = (5, 0),
    };

    /// <summary> Resolve an XBMBattleDetail row id to (board, battle). False when unknown. </summary>
    public static bool TryBattleOfDetail(uint battleDetailId, out int board, out int battle)
    {
        if (BattleByDetailId.TryGetValue(battleDetailId, out var pair))
        {
            board = pair.Board;
            battle = pair.Battle;
            return true;
        }
        board = 0;
        battle = -1;
        return false;
    }

    private static Dictionary<uint, CrucibleEnemy>? _byNameId;

    /// <summary> The panel enemy for a BNpcName id, or null. Match by id, never by name: six names are reused. </summary>
    public static CrucibleEnemy? Enemy(uint nameId)
    {
        if (_byNameId is null)
        {
            var map = new Dictionary<uint, CrucibleEnemy>(Enemies.Length);
            foreach (var e in Enemies)
                map[e.NameId] = e;
            _byNameId = map;
        }

        return _byNameId.TryGetValue(nameId, out var enemy) ? enemy : null;
    }

    /// <summary> Union of the panel needs of every enemy in one battle. </summary>
    public static CrucibleNeeds BattleNeeds(int board, int battle)
    {
        var needs = CrucibleNeeds.None;
        foreach (var e in Enemies)
            if (e.Board == board && e.Battle == battle)
                needs |= e.Needs;
        return needs;
    }

    /// <summary> The battle's first panel enemy name (subrow 0; for battle 0 that is the boss). </summary>
    public static string BattleLabel(int board, int battle)
    {
        foreach (var e in Enemies)
            if (e.Board == board && e.Battle == battle && e.Sub == 0)
                return e.Name;
        return "?";
    }
}
