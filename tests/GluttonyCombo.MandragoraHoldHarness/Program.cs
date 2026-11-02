// Offline proof for the treasure-dungeon mandragora AoE hold.
//
// Compiles the REAL src/GluttonyCombo/GluttonyCombo/AutoRotation/MandragoraHold.cs - the exact
// file that ships - with no Dalamud and no ECommons. The treasure dungeons' five-piece mandragora
// pack (onion, egg, garlic, tomato, queen) has to die one at a time, in order, for the full
// reward; an AoE that hits the clumped pack breaks the order.
//
//   dotnet build tests\GluttonyCombo.MandragoraHoldHarness -c Release
//   dotnet tests\GluttonyCombo.MandragoraHoldHarness\bin\Release\net10.0\GluttonyCombo.MandragoraHoldHarness.dll
//
// Action rows below are the live Action sheet values (CastType, EffectRange, hostile-effect flag
// Unknown14) read from XIVAPI v2 on 2026-11-02.
//
// Prints PASS/FAIL per case, "OK" and exit 0 when every case passes.

using GluttonyCombo.AutoRotation;
using static GluttonyCombo.AutoRotation.MandragoraHold;

var failures = 0;
var total = 0;

void Check<T>(string name, T actual, T expected)
{
    total++;
    var ok = EqualityComparer<T>.Default.Equals(actual, expected);
    if (!ok) failures++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-82} => {actual}, expected {expected}");
}

Console.WriteLine("== Group 1: the pack is recognized in every treasure dungeon, by base id or name id ==");
(uint Base, uint Name, string Label)[] packs =
[
    (0x1FCD, 6847, "canal onion (Hidden Canals of Uznair)"),
    (0x254A, 7608, "altar queen (Shifting Altars of Uznair)"),
    (0x2A08, 8686, "dungeon garlic (Dungeons of Lyhe Ghiah)"),
    (0x3020, 9804, "secret tomato (Shifting Oubliettes of Lyhe Ghiah)"),
    (0x3809, 10836, "exciting egg (Excitatron 6000)"),
    (0x3D50, 12037, "gymnastic eggplant (Shifting Gymnasion Agonon)"),
    (0x4304, 13246, "Tuligora queen (Cenote Ja Ja Gural)"),
    (0x48B9, 14016, "vault onion (Vault Oneiron)"),
];
foreach (var p in packs)
{
    Check($"{p.Label} by base id", IsPackMember(p.Base, 0), true);
    Check($"{p.Label} by name id", IsPackMember(0, p.Name), true);
}
Check("every family has five base ids (8 x 5)", PackBaseIds.Count, 40);
Check("every family has five name ids (8 x 5)", PackNameIds.Count, 40);
Check("Onion Prince (the field mandragora, 2827) is not the pack", IsPackMember(0, 2827), false);
Check("Mandragora Queen (2831) is not the pack", IsPackMember(0, 2831), false);
Check("a Crucible mandragora familiar (name 14531) is not the pack", IsPackMember(0, 14531), false);
Check("an ordinary mob is not the pack", IsPackMember(0x2547 + 0x1000, 1234), false);

Console.WriteLine();
Console.WriteLine("== Group 2: the hold lasts until the whole pack is dead ==");
Check("no npcs: no hold", IsHolding([]), false);
Check("only ordinary mobs: no hold", IsHolding([(100u, 200u, false), (101u, 201u, false)]), false);
Check("five live pack members: hold", IsHolding([(0x2546u, 7604u, false), (0x2547u, 7605u, false), (0x2548u, 7606u, false), (0x2549u, 7607u, false), (0x254Au, 7608u, false)]), true);
Check("onion dead, four alive: hold", IsHolding([(0x2546u, 7604u, true), (0x2547u, 7605u, false), (0x2548u, 7606u, false), (0x2549u, 7607u, false), (0x254Au, 7608u, false)]), true);
Check("only the queen alive: hold", IsHolding([(0x2546u, 7604u, true), (0x254Au, 7608u, false), (100u, 200u, false)]), true);
Check("whole pack dead, other mobs alive: no hold", IsHolding([(0x2546u, 7604u, true), (0x2547u, 7605u, true), (0x254Au, 7608u, true), (100u, 200u, false)]), false);

Console.WriteLine();
Console.WriteLine("== Group 3: which actions can hit more than one enemy (live Action sheet rows) ==");
// (id, name, CastType, EffectRange, Unknown14 hostile-effect flag, expected)
(uint Id, string Name, byte Ct, byte Er, bool Hostile, bool Expected)[] rows =
[
    // multi-target damage: held
    (23, "Circle of Scorn (self circle, physical)", 2, 5, true, true),
    (41, "Overpower (self circle)", 2, 5, true, true),
    (16159, "Bow Shock (self circle)", 2, 5, true, true),
    (25760, "Double Down (self circle)", 2, 5, true, true),
    (139, "Holy (self circle, magic)", 2, 8, true, true),
    (3571, "Assize (self circle, damage and heal)", 2, 20, true, true),
    (7444, "Lord of Crowns (self circle)", 2, 20, true, true),
    (37009, "Glare IV (target circle)", 2, 5, true, true),
    (24289, "Phlegma (target circle)", 2, 5, true, true),
    (36989, "Flare Star (target circle)", 2, 5, true, true),
    (7426, "Ruin IV (target circle)", 2, 5, true, true),
    (16499, "Bioblaster (cone)", 3, 12, true, true),
    (16487, "Shoha (line)", 4, 10, true, true),
    (16196, "Quadruple Technical Finish (self circle)", 2, 15, true, true),
    (36985, "Dance of the Dawn (target circle)", 2, 5, true, true),
    (25874, "Macrocosmos (self circle)", 2, 20, true, true),
    (2270, "Doton (ground area, unflagged)", 7, 5, false, true),
    (3639, "Salted Earth (placed area, unflagged)", 2, 5, false, true),
    (7418, "Flamethrower (channel, unflagged)", 1, 12, false, true),
    (15997, "Standard Step (its finish is an AoE)", 1, 0, false, true),
    (15998, "Technical Step (its finish is an AoE)", 1, 0, false, true),
    (16191, "Single Standard Finish (self circle)", 2, 15, true, true),
    (25838, "Summon Ifrit II (Inferno is an AoE)", 1, 0, true, true),
    (25839, "Summon Titan II (Earthen Fury is an AoE)", 1, 0, true, true),
    (25840, "Summon Garuda II (Aerial Blast is an AoE)", 1, 0, true, true),
    (7429, "Enkindle Bahamut (Akh Morn)", 1, 0, true, true),
    (16516, "Enkindle Phoenix (Revelation)", 1, 0, true, true),
    (36998, "Enkindle Solar Bahamut (Exodus)", 1, 0, true, true),
    (16472, "Living Shadow (Abyssal Drain, Shadowbringer)", 1, 0, false, true),
    (24318, "Pneuma (line, damage and heal)", 4, 25, true, true),
    // single-target damage: not held
    (3538, "Goring Blade", 1, 0, true, false),
    (16, "Shield Bash", 1, 0, true, false),
    (7387, "Upheaval", 1, 0, true, false),
    (25865, "Broil IV", 1, 0, true, false),
    (25797, "Paradox", 1, 0, true, false),
    (3643, "Carve and Spit", 1, 0, true, false),
    (7436, "Chain Stratagem (single-target debuff)", 1, 0, true, false),
    // heals, mitigation, party buffs: never held
    (124, "Medica (heal)", 2, 20, false, false),
    (133, "Medica II (heal)", 2, 20, false, false),
    (131, "Cure III (heal)", 2, 10, false, false),
    (3600, "Helios (heal)", 2, 20, false, false),
    (16534, "Afflatus Rapture (heal)", 2, 20, false, false),
    (7445, "Lady of Crowns (heal)", 2, 20, false, false),
    (3540, "Divine Veil (party shield)", 2, 30, false, false),
    (7388, "Shake It Off (party shield)", 2, 30, false, false),
    (7385, "Passage of Arms (ground mitigation)", 7, 8, false, false),
    (7396, "Brotherhood (party buff)", 2, 30, false, false),
    (16553, "Celestial Opposition", 2, 20, false, false),
    (7439, "Earthly Star", 7, 20, false, false),
    (7531, "Rampart", 1, 0, false, false),
    (7535, "Reprisal (mitigation shaped like an AoE attack)", 2, 5, true, false),
    (7560, "Addle (single-target mitigation)", 1, 0, true, false),
    (7549, "Feint (single-target mitigation)", 1, 0, true, false),
    (16012, "Shield Samba (party mitigation)", 2, 30, false, false),
    (7427, "Summon Bahamut (Wyrmwave is single-target)", 1, 0, false, false),
    (7561, "Swiftcast", 1, 0, false, false),
    (24394, "Enshroud", 1, 0, false, false),
];
foreach (var r in rows)
    Check($"{r.Name} ({r.Id})", HitsSeveral(r.Id, r.Ct, r.Er, r.Hostile), r.Expected);

Console.WriteLine();
Console.WriteLine("== Group 4: the single-target fallback and what happens to a resolved action ==");
// ids: Glare III 25859 / Glare IV 37009; Jolt III 37004 shown as Grand Impact 37006; Overpower 41;
// Mythril Tempest 16462; Cascade 15989; Standard Step 15997.
Check("Glare IV from the Stone button: fallback is the button's form (Glare III)", PickFallback(37009, 25859, false), 25859u);
Check("Grand Impact on the Jolt button: no fallback (a queued Jolt III becomes Grand Impact again)", PickFallback(37006, 37006, true), 0u);
Check("Mythril Tempest from the Overpower button: no single-target fallback", PickFallback(16462, 41, true), 0u);
Check("Standard Step from the Cascade button: fallback is Cascade", PickFallback(15997, 15989, false), 15989u);
Check("fallback never equals the held action", PickFallback(41, 41, false), 0u);
Check("no hold: a multi-target GCD is kept", Decide(false, true, false, true), Outcome.Keep);
Check("hold, single-target action: kept", Decide(true, false, false, true), Outcome.Keep);
Check("hold, single-target weave: kept", Decide(true, false, true, true), Outcome.Keep);
Check("hold, multi-target GCD with a single-target fallback: fallback", Decide(true, true, false, true), Outcome.Fallback);
Check("hold, multi-target weave (Circle of Scorn): skipped, the GCD follows", Decide(true, true, true, true), Outcome.Skip);
Check("hold, multi-target GCD with no single-target fallback (AoE button): skipped", Decide(true, true, false, false), Outcome.Skip);

Console.WriteLine();
Console.WriteLine("== Group 4b: the 100 ms reading cache ==");
Check("never read: not fresh (the hold is read on first use)", CacheFresh(5_000, 0, false, 100), false);
Check("read 50 ms ago: fresh", CacheFresh(5_050, 5_000, true, 100), true);
Check("read 100 ms ago: stale", CacheFresh(5_100, 5_000, true, 100), false);
Check("clock went backwards: stale", CacheFresh(4_000, 5_000, true, 100), false);
Check("long.MinValue read time (overflowing subtraction): stale, not stuck fresh", CacheFresh(5_000, long.MinValue, true, 100), false);

Console.WriteLine();
Console.WriteLine("== Group 5: auto-rotation presets while the hold is on ==");
Check("no hold: AoE damage preset runs", MayRunPreset(false, true, false), true);
Check("hold: AoE damage preset is off", MayRunPreset(true, true, false), false);
Check("hold: single-target damage preset runs", MayRunPreset(true, false, false), true);
Check("hold: AoE heal preset runs (heals are not damage)", MayRunPreset(true, true, true), true);
Check("hold: single-target heal preset runs", MayRunPreset(true, false, true), true);

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"OK ({total} cases)" : $"FAILED {failures} of {total}");
return failures == 0 ? 0 : 1;
