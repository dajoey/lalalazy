using System.Text.RegularExpressions;
using LazyFateAutomation.Helpers.Utils;
using Newtonsoft.Json;

namespace LazyFateAutomation.Harness;

/// <summary>
///     Offline assertions for the 0.0.3.3 features:
///     1. zone classification + currency mapping (FateZoneLogic) against real TerritoryType/Aetheryte
///        fixtures (xivapi v2, pulled 2026-10-05),
///     2. the FATE-type filter decision (FateEligibility) that FateToolKit.FateConditions delegates to,
///     3. config migration: an old (pre-0.0.3.3) config deserializes with the new fields defaulted,
///        so existing installs load with unchanged behavior.
///     4. the server info bar (DTR) toggle decision (FateDtrLogic): the click action (stop /
///        arm / confirm / none), the 10-second confirm window, and the entry text.
///     5. the BossMod Reborn preset the grind creates and activates (FateBossModPreset): its own name, only
///        MiscAI movement/targeting modules (never a job rotation that would run beside Gluttony Combo), and
///        every module FateGrind sets a transient strategy on.
///     6. releasing that preset: only BMR's stock Presets.GetActive/SetActive/ClearActive are used, the active
///        preset is cleared only when it is the plugin's own (never AutoDuty's or the player's), and a BMR IPC
///        endpoint that is not registered is reported once and skipped, never once per loop iteration.
///     7. approaching FATE mobs: the preset carries BMR's move-to-target module (MiscAI.StayCloseToTarget) with a
///        role range in BMR's option format, and the vnavmesh stuck fallback stays armed as a backstop while
///        the preset is active.
///     8. sort-order parity: the settings window's "Reset to Default" list equals the shipped default
///        sort order (same entries, same order, same directions) - the pre-fix reset dropped the
///        TimeRemaining criterion and sorted TimeRemainingUrgent ascending (urgent FATEs last).
/// </summary>
internal static class Program {
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool condition, string detail = "") {
        if (condition) {
            _pass++;
            Console.WriteLine($"PASS  {name}");
        }
        else {
            _fail++;
            Console.WriteLine($"FAIL  {name}{(detail.Length > 0 ? $"  [{detail}]" : "")}");
        }
    }

    /// <summary>Real TerritoryType rows (id, name, intendedUse, exVersion, mount, pvp, inUse, hasPrimaryAetheryte). xivapi v2, 2026-10-05.</summary>
    private static readonly (uint Id, string Name, uint Use, uint ExVersion, int Mount, int Pvp, int InUse, int Aetheryte)[] Fixture =
    [
        (134, "Middle La Noscea", 1, 0, 1, 0, 1, 1),
        (135, "Lower La Noscea", 1, 0, 1, 0, 1, 1),
        (137, "Eastern La Noscea", 1, 0, 1, 0, 1, 1),
        (138, "Western La Noscea", 1, 0, 1, 0, 1, 1),
        (139, "Upper La Noscea", 1, 0, 1, 0, 1, 1),
        (140, "Western Thanalan", 1, 0, 1, 0, 1, 1),
        (141, "Central Thanalan", 1, 0, 1, 0, 1, 1),
        (145, "Eastern Thanalan", 1, 0, 1, 0, 1, 1),
        (146, "Southern Thanalan", 1, 0, 1, 0, 1, 1),
        (147, "Northern Thanalan", 1, 0, 1, 0, 1, 1),
        (148, "Central Shroud", 1, 0, 1, 0, 1, 1),
        (152, "East Shroud", 1, 0, 1, 0, 1, 1),
        (153, "South Shroud", 1, 0, 1, 0, 1, 1),
        (154, "North Shroud", 1, 0, 1, 0, 1, 1),
        (155, "Coerthas Central Highlands", 1, 0, 1, 0, 1, 1),
        (156, "Mor Dhona", 1, 0, 1, 0, 1, 1),
        (180, "Outer La Noscea", 1, 0, 1, 0, 1, 1),
        (250, "Wolves' Den Pier", 1, 0, 0, 1, 1, 1),
        (397, "Coerthas Western Highlands", 1, 1, 1, 0, 1, 1),
        (398, "The Dravanian Forelands", 1, 1, 1, 0, 1, 1),
        (399, "The Dravanian Hinterlands", 1, 1, 1, 0, 1, 0),
        (400, "The Churning Mists", 1, 1, 1, 0, 1, 1),
        (401, "The Sea of Clouds", 1, 1, 1, 0, 1, 1),
        (402, "Azys Lla", 1, 1, 1, 0, 1, 1),
        (612, "The Fringes", 1, 2, 1, 0, 1, 1),
        (613, "The Ruby Sea", 1, 2, 1, 0, 1, 1),
        (614, "Yanxia", 1, 2, 1, 0, 1, 1),
        (619, "Kugane (unresolvable PlaceName in fixture)", 0, 0, 0, 0, 0, 0),
        (620, "The Peaks", 1, 2, 1, 0, 1, 1),
        (621, "The Lochs", 1, 2, 1, 0, 1, 1),
        (622, "The Azim Steppe", 1, 2, 1, 0, 1, 1),
        (732, "Eureka Anemos", 41, 2, 1, 0, 1, 0),
        (813, "Lakeland", 1, 3, 1, 0, 1, 1),
        (814, "Kholusia", 1, 3, 1, 0, 1, 1),
        (815, "Amh Araeng", 1, 3, 1, 0, 1, 1),
        (816, "Il Mheg", 1, 3, 1, 0, 1, 1),
        (817, "The Rak'tika Greatwood", 1, 3, 1, 0, 1, 1),
        (818, "The Tempest", 1, 3, 1, 0, 1, 1),
        (901, "The Diadem", 47, 1, 1, 0, 1, 0),
        (920, "Bozjan Southern Front", 48, 3, 1, 0, 1, 0),
        (956, "Labyrinthos", 1, 4, 1, 0, 1, 1),
        (957, "Thavnair", 1, 4, 1, 0, 1, 1),
        (958, "Garlemald", 1, 4, 1, 0, 1, 1),
        (959, "Mare Lamentorum", 1, 4, 1, 0, 1, 1),
        (960, "Ultima Thule", 1, 4, 1, 0, 1, 1),
        (961, "Elpis", 1, 4, 1, 0, 1, 1),
        (1187, "Urqopacha", 1, 5, 1, 0, 1, 1),
        (1188, "Kozama'uka", 1, 5, 1, 0, 1, 1),
        (1189, "Yak T'el", 1, 5, 1, 0, 1, 1),
        (1190, "Shaaloani", 1, 5, 1, 0, 1, 1),
        (1191, "Heritage Found", 1, 5, 1, 0, 1, 1),
        (1192, "Living Memory", 1, 5, 1, 0, 1, 1),
        (1237, "Sinus Ardorum", 60, 4, 1, 0, 1, 0),
        (1252, "South Horn", 61, 5, 1, 0, 1, 0),
    ];

    private static FateZoneLogic.ZoneClass Classify((uint Id, string Name, uint Use, uint ExVersion, int Mount, int Pvp, int InUse, int Aetheryte) row)
        => FateZoneLogic.ClassifyZone(row.InUse != 0, row.Mount != 0, row.Pvp != 0, row.Use, row.Aetheryte != 0);

    private static int Main() {
        ZoneClassificationTests();
        CurrencyMappingTests();
        FateEligibilityTests();
        FocusFallbackTests();
        ConfigMigrationTests();
        DtrToggleTests();
        BossModPresetTests();
        BossModReleaseTests();
        ApproachTests();
        SortOrderResetTests();

        Console.WriteLine($"{_pass} pass, {_fail} fail");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    private static void ZoneClassificationTests() {
        Console.WriteLine("-- zone classification (real TerritoryType fixtures) --");

        var classes = Fixture.ToDictionary(r => r.Id, Classify);
        var fateZones = classes.Where(kv => kv.Value == FateZoneLogic.ZoneClass.FateZone).Select(kv => kv.Key).ToHashSet();
        var noTeleport = classes.Where(kv => kv.Value == FateZoneLogic.ZoneClass.NoTeleportFateZone).Select(kv => kv.Key).ToHashSet();
        var foray = classes.Where(kv => kv.Value == FateZoneLogic.ZoneClass.ForayZone).Select(kv => kv.Key).ToHashSet();
        var notFate = classes.Where(kv => kv.Value == FateZoneLogic.ZoneClass.NotAFateZone).Select(kv => kv.Key).ToHashSet();

        Check("fixture covers 54 real rows", Fixture.Length == 54, Fixture.Length.ToString());
        Check("46 overworld FATE zones have a teleport aetheryte", fateZones.Count == 46, string.Join(",", fateZones));
        Check("exactly one FATE zone is aethernet-only (Dravanian Hinterlands)",
            noTeleport.SetEquals([399]), string.Join(",", noTeleport));
        Check("foray zones (Eureka, Diadem, Bozja, cosmic, Occult Crescent) are a class of their own",
            foray.SetEquals([732, 901, 920, 1237, 1252]), string.Join(",", foray));
        Check("PvP arena and towns/unused rows are not FATE zones", notFate.SetEquals([250, 619]), string.Join(",", notFate));

        // Bozja/Zadnor are ExVersion 3 (Shadowbringers content) but reward no standard currency.
        Check("Bozja (use 48, exv 3) never classifies as a currency zone",
            Classify(Fixture.Single(r => r.Id == 920)) == FateZoneLogic.ZoneClass.ForayZone
            && FateZoneLogic.ZoneCurrency(3, FateZoneLogic.ZoneClass.ForayZone) == null);
        Check("aethernet-only zone carries no currency pool entry",
            FateZoneLogic.ZoneCurrency(1, FateZoneLogic.ZoneClass.NoTeleportFateZone) == null);
        Check("unused/mount-less territory rows classify as not-FATE",
            FateZoneLogic.ClassifyZone(false, true, false, FateZoneLogic.UseOverworld, true) == FateZoneLogic.ZoneClass.NotAFateZone
            && FateZoneLogic.ClassifyZone(true, false, false, FateZoneLogic.UseOverworld, true) == FateZoneLogic.ZoneClass.NotAFateZone);
    }

    private static void CurrencyMappingTests() {
        Console.WriteLine("-- currency mapping (mirrors FateZones.CurrencyZones over the real fixture) --");

        var classes = Fixture.ToDictionary(r => r.Id, Classify);
        var sealPool = Fixture
            .Where(r => classes[r.Id] == FateZoneLogic.ZoneClass.FateZone && FateZoneLogic.ZoneCurrency(r.ExVersion, classes[r.Id]) == FateCurrency.CompanySeals)
            .Select(r => r.Id).ToHashSet();
        var gemPool = Fixture
            .Where(r => classes[r.Id] == FateZoneLogic.ZoneClass.FateZone && FateZoneLogic.ZoneCurrency(r.ExVersion, classes[r.Id]) == FateCurrency.BicolorGemstones)
            .Select(r => r.Id).ToHashSet();

        // ARR 17 + HW 5 (Hinterlands aethernet-only) + SB 6 = 28 seals zones; ShB/EW/DT 6+6+6 = 18 gemstone zones.
        Check("seals pool = 28 zones (ARR/HW/SB, reachable)", sealPool.Count == 28, string.Join(",", sealPool.OrderBy(x => x)));
        Check("gemstone pool = 18 zones (ShB/EW/DT)", gemPool.Count == 18, string.Join(",", gemPool.OrderBy(x => x)));
        Check("pools are disjoint and cover every reachable FATE zone",
            sealPool.Count + gemPool.Count == 46 && !sealPool.Overlaps(gemPool));
        Check("the aethernet-only zone is in neither pool", !sealPool.Contains(399) && !gemPool.Contains(399));
        Check("Bozja (exv 3) is in neither pool", !sealPool.Contains(920) && !gemPool.Contains(920));

        Check("zone currency split matches the sheet evidence",
            FateZoneLogic.ZoneCurrency(0, FateZoneLogic.ZoneClass.FateZone) == FateCurrency.CompanySeals
            && FateZoneLogic.ZoneCurrency(2, FateZoneLogic.ZoneClass.FateZone) == FateCurrency.CompanySeals
            && FateZoneLogic.ZoneCurrency(3, FateZoneLogic.ZoneClass.FateZone) == FateCurrency.BicolorGemstones
            && FateZoneLogic.ZoneCurrency(5, FateZoneLogic.ZoneClass.FateZone) == FateCurrency.BicolorGemstones);
    }

    private static void FateEligibilityTests() {
        Console.WriteLine("-- FATE-type filter (FateEligibility) --");

        var allRules = new[] { FateRule.Normal, FateRule.Collect, FateRule.Escort, FateRule.Defend, FateRule.EventFate, FateRule.Chase, FateRule.ConcertedWorks, FateRule.Fete };
        var empty = new HashSet<FateRule>();

        foreach (var rule in allRules) {
            var ok = FateEligibility.IsEligible(900, 900, 0, 90, 300, 120, false, false, rule, empty);
            Check($"default config (empty exclusion set) allows {rule}", ok);
        }

        var excludeCollect = new HashSet<FateRule> { FateRule.Collect };
        Check("excluding Collect blocks a Collect FATE",
            !FateEligibility.IsEligible(900, 900, 0, 90, 300, 120, false, false, FateRule.Collect, excludeCollect));
        Check("excluding Collect still allows a kill & boss FATE",
            FateEligibility.IsEligible(900, 900, 0, 90, 300, 120, false, false, FateRule.Normal, excludeCollect));
        Check("excluding Collect still allows an escort FATE",
            FateEligibility.IsEligible(900, 900, 0, 90, 300, 120, false, false, FateRule.Escort, excludeCollect));

        var excludeNormal = new HashSet<FateRule> { FateRule.Normal };
        Check("excluding Normal blocks the foray-classified skirmish FATEs",
            !FateEligibility.IsEligible(900, 900, 0, 90, 300, 120, false, false, FateRule.Normal, excludeNormal));

        var failed = new List<string>();
        FateEligibility.AppendFailedConditions(failed, 900, 900, 0, 90, 300, 120, false, false, FateRule.Collect, excludeCollect);
        Check("the details report names the excluded type",
            failed.Count == 1 && failed[0] == "Collect FATEs are excluded in settings", string.Join("; ", failed));

        // the other eligibility conditions must behave exactly as before the refactor
        Check("duration cap still applies", !FateEligibility.IsEligible(901, 900, 0, 90, 300, 120, false, false, FateRule.Normal, empty));
        Check("progress cap still applies", !FateEligibility.IsEligible(900, 900, 91, 90, 300, 120, false, false, FateRule.Normal, empty));
        Check("time-remaining floor still applies (and unactivated fates pass)",
            !FateEligibility.IsEligible(900, 900, 0, 90, 100, 120, false, false, FateRule.Normal, empty)
            && FateEligibility.IsEligible(900, 900, 0, 90, -1, 120, false, false, FateRule.Normal, empty));
        Check("blacklist and pending still apply",
            !FateEligibility.IsEligible(900, 900, 0, 90, 300, 120, true, false, FateRule.Normal, empty)
            && !FateEligibility.IsEligible(900, 900, 0, 90, 300, 120, false, true, FateRule.Normal, empty));
    }

    /// <summary>
    ///     The currency-focus fallback decision (FateFocusSwap.Decide, the function
    ///     FateGrind.HandleNoFates defers to): the two fallback options must actually diverge once
    ///     the focused pool is exhausted — the 0.0.3.3 bug was that both settings rotated the pool
    ///     identically, so neither option changed anything.
    /// </summary>
    private static void FocusFallbackTests() {
        Console.WriteLine("-- currency-focus fallback decision (FateFocusSwap) --");

        Check("exhausted pool + Continue normally leaves the pool for the usual chain",
            FateFocusSwap.Decide(focusSteersPool: true, effectivePoolHasZones: true, poolExhausted: true,
                CurrencyFocusFallback.NormalSelection) == FocusSwapAction.LeavePool);
        Check("exhausted pool + Idle in zone stays and waits",
            FateFocusSwap.Decide(true, true, true, CurrencyFocusFallback.Idle) == FocusSwapAction.IdleAndWait);
        Check("unexhausted pool keeps rotating under Continue normally",
            FateFocusSwap.Decide(true, true, false, CurrencyFocusFallback.NormalSelection) == FocusSwapAction.RotateInPool);
        Check("unexhausted pool keeps rotating under Idle in zone",
            FateFocusSwap.Decide(true, true, false, CurrencyFocusFallback.Idle) == FocusSwapAction.RotateInPool);
        Check("every focused zone excluded (empty effective pool) + Continue normally leaves the pool",
            FateFocusSwap.Decide(true, false, true, CurrencyFocusFallback.NormalSelection) == FocusSwapAction.LeavePool);
        Check("every focused zone excluded (empty effective pool) + Idle in zone waits",
            FateFocusSwap.Decide(true, false, true, CurrencyFocusFallback.Idle) == FocusSwapAction.IdleAndWait);
        Check("mode zone list wins over the focus fallback",
            FateFocusSwap.Decide(false, true, true, CurrencyFocusFallback.Idle) == FocusSwapAction.RotateInPool);
        Check("focus without zones does not steer the swap",
            FateFocusSwap.Decide(false, false, true, CurrencyFocusFallback.Idle) == FocusSwapAction.RotateInPool);
    }

    private static void ConfigMigrationTests() {
        Console.WriteLine("-- config migration (old config -> new defaults) --");

        // A 0.0.3.2-era config: no ExcludedFateRules / CurrencyFocus / CurrencyFocusFallback keys.
        const string oldConfig = """
            {
              "Version": 0,
              "MaxDuration": 900,
              "MinTimeRemaining": 120,
              "MaxProgress": 90,
              "SwapZones": true,
              "DisplayNameFormat": "[{Level}] {Name}",
              "BarColour": { "X": 0.404, "Y": 0.259, "Z": 0.541, "W": 1.0 },
              "Blacklist": { "2": [1234] },
              "SortOrder": [
                { "Criteria": 0, "Descending": true },
                { "Criteria": 1, "Descending": true },
                { "Criteria": 2, "Descending": true },
                { "Criteria": 3, "Descending": true },
                { "Criteria": 5, "Descending": false },
                { "Criteria": 4, "Descending": false }
              ],
              "SelectedSwapZones": [148, 152],
              "ExcludedSwapZones": [],
              "SelectedModeId": "Gemstones",
              "PrioritizeForlornMaidens": true,
              "LastSeenChangelogVersion": "0.0.3.2"
            }
            """;

        var migrated = JsonConvert.DeserializeObject<Configuration>(oldConfig);
        Check("old config deserializes", migrated != null);
        if (migrated == null) return;

        Check("old values survive: selected zones", migrated.SelectedSwapZones.SetEquals([148, 152]));
        Check("old values survive: mode id + durations", migrated.SelectedModeId == "Gemstones" && migrated.MaxDuration == 900);
        Check("old values survive: blacklist", migrated.Blacklist.TryGetValue(FateType.MechaEvent, out var bl) && bl.SetEquals([1234u]));
        Check("old values survive: forlorn + changelog gate", migrated.PrioritizeForlornMaidens && migrated.LastSeenChangelogVersion == "0.0.3.2");

        Check("new FATE-type filter defaults to empty (every type runs)", migrated.ExcludedFateRules.Count == 0);
        Check("new currency focus defaults to None", migrated.CurrencyFocus == FateCurrency.None);
        Check("new currency fallback defaults to NormalSelection", migrated.CurrencyFocusFallback == CurrencyFocusFallback.NormalSelection);

        Check("SortOrder is exactly the 6 saved entries (ObjectCreationHandling.Replace guard)",
            migrated.SortOrder.Count == 6, migrated.SortOrder.Count.ToString());

        // New-shape roundtrip through the same serializer path.
        var updated = JsonConvert.DeserializeObject<Configuration>(oldConfig)!;
        updated.ExcludedFateRules = [FateRule.Collect, FateRule.Escort];
        updated.CurrencyFocus = FateCurrency.BicolorGemstones;
        updated.CurrencyFocusFallback = CurrencyFocusFallback.Idle;
        var serialized = JsonConvert.SerializeObject(updated);
        Check("new fields serialize (enums as ints, Newtonsoft default)",
            Regex.IsMatch(serialized, "\"ExcludedFateRules\":\\[\\s*2,\\s*3\\s*\\]")
            && Regex.IsMatch(serialized, "\"CurrencyFocus\":\\s*2")
            && Regex.IsMatch(serialized, "\"CurrencyFocusFallback\":\\s*1"),
            serialized.Length.ToString());

        var roundtripped = JsonConvert.DeserializeObject<Configuration>(serialized)!;
        Check("new fields roundtrip", roundtripped.ExcludedFateRules.SetEquals([FateRule.Collect, FateRule.Escort])
            && roundtripped.CurrencyFocus == FateCurrency.BicolorGemstones
            && roundtripped.CurrencyFocusFallback == CurrencyFocusFallback.Idle);
        Check("roundtrip keeps old values", roundtripped.SelectedSwapZones.SetEquals([148, 152]) && roundtripped.MaxDuration == 900);

        // DedupeSortOrder still cleans a list grown by the pre-0.0.3.1 append bug.
        var grown = JsonConvert.DeserializeObject<Configuration>(oldConfig)!;
        grown.SortOrder.AddRange([.. grown.SortOrder, .. grown.SortOrder]); // simulate 72-entry drift
        Check("DedupeSortOrder still collapses a grown list", grown.DedupeSortOrder() && grown.SortOrder.Count == 6);
    }

    private static void DtrToggleTests() {
        Console.WriteLine("-- server bar (DTR) toggle: click decision, confirm window, entry text --");

        // Click decision: stop a running bot; arm-then-confirm to start; nothing without a character.
        Check("running bot: a click stops it", FateDtrLogic.DecideClick(true, confirmPending: false, playerAvailable: true) == FateDtrLogic.ClickAction.Stop);
        Check("running bot: a click stops it even with a stale armed start", FateDtrLogic.DecideClick(true, confirmPending: true, playerAvailable: true) == FateDtrLogic.ClickAction.Stop);
        Check("stopped bot, first click arms the start", FateDtrLogic.DecideClick(false, confirmPending: false, playerAvailable: true) == FateDtrLogic.ClickAction.ArmStart);
        Check("stopped bot, second click confirms the start", FateDtrLogic.DecideClick(false, confirmPending: true, playerAvailable: true) == FateDtrLogic.ClickAction.StartNow);
        Check("no character: clicks never arm or start", FateDtrLogic.DecideClick(false, confirmPending: false, playerAvailable: false) == FateDtrLogic.ClickAction.None);
        Check("no character: an armed start cannot be confirmed", FateDtrLogic.DecideClick(false, confirmPending: true, playerAvailable: false) == FateDtrLogic.ClickAction.None);
        Check("Ctrl+click on a running bot arms the soft stop", FateDtrLogic.DecideClick(true, confirmPending: false, playerAvailable: true, ctrlHeld: true) == FateDtrLogic.ClickAction.StopWhenSafe);
        Check("Ctrl is ignored while stopped (a Ctrl+click still arms the start)", FateDtrLogic.DecideClick(false, confirmPending: false, playerAvailable: true, ctrlHeld: true) == FateDtrLogic.ClickAction.ArmStart);

        // Confirm window: live inside it, expired at and past it, never live when unarmed.
        Check("armed start is live right after arming", FateDtrLogic.IsConfirmLive(true, 1000, 1000));
        Check("armed start is live just inside the window", FateDtrLogic.IsConfirmLive(true, 1000, 1000 + FateDtrLogic.ConfirmSeconds * 1000L - 1));
        Check("armed start expires exactly at the window end", !FateDtrLogic.IsConfirmLive(true, 1000, 1000 + FateDtrLogic.ConfirmSeconds * 1000L));
        Check("armed start expires well past the window", !FateDtrLogic.IsConfirmLive(true, 1000, 1000 + FateDtrLogic.ConfirmSeconds * 1000L * 60));
        Check("an unarmed start is never live", !FateDtrLogic.IsConfirmLive(false, 1000, 1000));

        // Entry text: Off / confirm prompt / On, with the soft-stop and live state annotations.
        Check("stopped entry reads ': Off'", FateDtrLogic.EntryText(false, stopWhenSafePending: false, confirmPending: false, currentState: "Idle") == ": Off");
        Check("armed entry asks for the confirming click", FateDtrLogic.EntryText(false, false, true, "Idle") == ": Start? (click again)");
        Check("running entry reads ': On'", FateDtrLogic.EntryText(true, false, false, "Idle") == ": On");
        Check("soft stop pending reads ': On (stopping)'", FateDtrLogic.EntryText(true, true, false, "Idle") == ": On (stopping)");
        Check("a live non-idle state is shown", FateDtrLogic.EntryText(true, false, false, "Paused (in instance)") == ": On (Paused (in instance))");
        Check("the stopping annotation wins over the state text", FateDtrLogic.EntryText(true, true, false, "Paused (in instance)") == ": On (stopping)");
        Check("a null state reads ': On'", FateDtrLogic.EntryText(true, false, false, null!) == ": On");

        // Icon: unsheathed while running, sheathed while stopped (the sibling entries' pair).
        Check("the entry shows the boss FATE icon while running", FateDtrLogic.IconGlyph == FateDtrIcon.FateBoss);
        Check("the entry shows the same boss FATE icon while stopped - the state lives in the text, no icon pair", FateDtrLogic.IconGlyph == FateDtrIcon.FateBoss);
        Check("the boss glyph carries Dalamud's BitmapFontIcon.FateBoss value 90, not a None cast", (int)FateDtrLogic.IconGlyph == 90);
    }
    private static void BossModPresetTests() {
        Console.WriteLine("-- BossMod Reborn preset: own name, MiscAI modules only, covers every module FateGrind drives --");

        using var doc = System.Text.Json.JsonDocument.Parse(FateBossModPreset.Json);
        var root = doc.RootElement;
        var name = root.TryGetProperty("Name", out var n) ? n.GetString() : null;
        var modules = root.TryGetProperty("Modules", out var m) ? m.EnumerateObject().Select(p => p.Name).ToList() : [];

        Check("preset JSON names the preset FateGrind activates", name == FateBossModPreset.Name, name ?? "<none>");
        Check("preset has its own name, not the shared \"Gluttony\" preset", !string.Equals(FateBossModPreset.Name, "Gluttony", StringComparison.OrdinalIgnoreCase), FateBossModPreset.Name);

        var notMiscAi = modules.Where(k => !k.StartsWith(FateBossModPreset.MiscAiPrefix, StringComparison.Ordinal)).ToList();
        Check("preset holds no module outside BossMod.Autorotation.MiscAI (no job rotations)", notMiscAi.Count == 0,
            $"{notMiscAi.Count} offending: {string.Join(", ", notMiscAi.Take(6))}{(notMiscAi.Count > 6 ? ", ..." : "")}");

        // BMR's AddTransientStrategy only applies to a module the preset already contains
        // (IPCProvider.cs addTransientStrategy: Modules.Find(m => m.Type == mt) == null -> false).
        var grindSrc = File.ReadAllText(FindSource("FateGrind.cs"));
        var driven = Regex.Matches(grindSrc, "\"(BossMod\\.Autorotation\\.[A-Za-z0-9_.]+)\"").Select(x => x.Groups[1].Value).Distinct().ToList();
        var missing = driven.Where(k => !modules.Contains(k)).ToList();
        Check("FateGrind drives at least the AutoTarget and NormalMovement modules", driven.Contains("BossMod.Autorotation.MiscAI.AutoTarget") && driven.Contains("BossMod.Autorotation.MiscAI.NormalMovement"), string.Join(", ", driven));
        Check("every module FateGrind sets a transient strategy on is in the preset", missing.Count == 0, string.Join(", ", missing));

        // The MiscAI settings the grind relied on are kept: FATE mob priority, FATE level sync, pathfinding.
        string? Option(string module, string track) {
            if (!root.GetProperty("Modules").TryGetProperty(module, out var arr)) return null;
            foreach (var e in arr.EnumerateArray())
                if (e.GetProperty("Track").GetString() == track) return e.GetProperty("Option").GetString();
            return null;
        }
        Check("AutoTarget prioritizes FATE mobs", Option("BossMod.Autorotation.MiscAI.AutoTarget", "FATE") == "Enabled");
        Check("FateUtils keeps FATE level sync on", Option("BossMod.Autorotation.MiscAI.FateUtils", "Sync") == "Enable");
        Check("NormalMovement pathfinds (dodging + positioning)", Option("BossMod.Autorotation.MiscAI.NormalMovement", "Destination") == "Pathfind");

        // The grind writes this preset itself (overwrite) and activates it by the same name.
        Check("FateGrind no longer embeds its own compressed preset", !grindSrc.Contains("_presetCompressed"));
        Check("FateGrind creates the preset from FateBossModPreset.Json with overwrite", grindSrc.Contains("Create(FateBossModPreset.Json, true)"));
        Check("FateGrind activates the preset by FateBossModPreset.Name (through FateBossModControl)",
            grindSrc.Contains("FateBossModControl.Activate()")
            && File.ReadAllText(FindSource("FateBossModControl.cs")).Contains("SetActive(FateBossModPreset.Name)"));
    }

    private static void BossModReleaseTests() {
        Console.WriteLine("-- BossMod preset release: stock IPC only, clear only our own preset, missing endpoint reported once --");

        // Only clear our own preset: AutoDuty's presets, the player's presets and "nothing active" are left alone.
        Check("our own active preset is cleared", FateBossModRelease.ShouldClearActive(FateBossModPreset.Name));
        Check("AutoDuty's preset is never cleared", !FateBossModRelease.ShouldClearActive("AutoDuty"));
        Check("AutoDuty Passive is never cleared", !FateBossModRelease.ShouldClearActive("AutoDuty Passive"));
        Check("the player's old \"Gluttony\" preset is never cleared", !FateBossModRelease.ShouldClearActive("Gluttony"));
        Check("a player preset with a similar name is never cleared", !FateBossModRelease.ShouldClearActive(FateBossModPreset.Name.ToLowerInvariant()));
        Check("no active preset (null) -> nothing to clear", !FateBossModRelease.ShouldClearActive(null));
        Check("empty active name -> nothing to clear", !FateBossModRelease.ShouldClearActive(""));

        // A BMR endpoint that is not registered (IpcNotReadyError) is reported once and then skipped.
        var latch = new BossModEndpointLatch();
        var reports = 0;
        var calls = 0;
        for (var i = 0; i < 5000; i++) { // 5000 loop iterations against a missing endpoint
            if (!latch.ShouldCall("Presets.Activate")) continue;
            calls++;
            if (latch.ReportFailure("Presets.Activate", endpointMissing: true)) reports++;
        }
        Check("missing endpoint: reported once over 5000 loop iterations", reports == 1, reports.ToString());
        Check("missing endpoint: called once, then skipped", calls == 1, calls.ToString());
        Check("another endpoint is unaffected", latch.ShouldCall("Presets.ClearActive"));

        // Any other failure is reported once but the endpoint keeps being called (it exists; the failure may pass).
        var other = 0;
        for (var i = 0; i < 5000; i++)
            if (latch.ShouldCall("Presets.SetActive") && latch.ReportFailure("Presets.SetActive", endpointMissing: false)) other++;
        Check("other failure: reported once over 5000 calls", other == 1, other.ToString());
        Check("other failure: endpoint is still called", latch.ShouldCall("Presets.SetActive"));

        latch.Reset();
        Check("a new run calls a previously missing endpoint again", latch.ShouldCall("Presets.Activate"));
        Check("a new run reports it again, once", latch.ReportFailure("Presets.Activate", true) && !latch.ReportFailure("Presets.Activate", true));

        // Source: no fork-only Presets.Activate/Deactivate, and every ClearActive goes through the guarded release.
        var srcDir = Path.GetDirectoryName(FindSource("FateGrind.cs"))!;
        var files = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetRelativePath(srcDir, f).Replace('\\', '/'), File.ReadAllText);
        var forkCalls = files.Where(kv => Regex.IsMatch(kv.Value, @"\bBossMod\.(Activate|Deactivate)\(")).Select(kv => kv.Key).ToList();
        Check("no call to BMR's non-stock Presets.Activate/Deactivate", forkCalls.Count == 0, string.Join(", ", forkCalls));
        Check("BossModIPC no longer declares Presets.Activate/Deactivate",
            !Regex.IsMatch(files["Helpers/IPC/BossModIPC.cs"], @"Func<bool>\s+(Activate|Deactivate)\s*;"));
        var directClear = files.Where(kv => kv.Key != "FateBossModControl.cs" && kv.Value.Contains(".ClearActive(")).Select(kv => kv.Key).ToList();
        Check("ClearActive is only called by the guarded release (FateBossModControl)", directClear.Count == 0, string.Join(", ", directClear));
        var control = files.TryGetValue("FateBossModControl.cs", out var c) ? c : "";
        Check("the release asks GetActive and the ownership rule before ClearActive",
            control.Contains("FateBossModRelease.ShouldClearActive(") && control.Contains("GetActive"));
    }

    private static void ApproachTests() {
        Console.WriteLine("-- approach: preset walks to the target (StayCloseToTarget), stuck fallback stays armed --");
        const string stay = "BossMod.Autorotation.MiscAI.StayCloseToTarget";

        using var doc = System.Text.Json.JsonDocument.Parse(FateBossModPreset.Json);
        var mods = doc.RootElement.GetProperty("Modules");
        var hasStay = mods.TryGetProperty(stay, out var stayArr);
        Check("preset contains a move-to-target module (MiscAI.StayCloseToTarget)", hasStay);

        // BMR StayCloseToTarget.cs (origin/main be8ff771c == tag 7.5.6.34 bbfa7192a): track "range", options
        // "OnHitbox" then 1.1..30 in 0.1 steps named with InvariantCulture (so "2.6", "10", never "2.60" or "10.0").
        var options = new HashSet<string> { "OnHitbox" };
        for (var f = 1.1f; f <= 30f; f = MathF.Round(f + 0.1f, 1))
            options.Add(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string? baseRange = null;
        if (hasStay)
            foreach (var e in stayArr.EnumerateArray())
                if (e.GetProperty("Track").GetString() == "range") baseRange = e.GetProperty("Option").GetString();
        Check("preset base range is a valid BMR option", baseRange != null && options.Contains(baseRange), baseRange ?? "<none>");
        Check("preset base range is melee reach (2.6, AutoDuty's default), so a failed per-role update never strands melee",
            baseRange == "2.6", baseRange ?? "<none>");

        // Per-role range (AutoDuty BMRoleChecks: Role 1 tank / 2 melee -> 2.6, everything else -> 10).
        Check("tank (Role 1) range 2.6", FateBossModPreset.StayCloseRange(1) == "2.6", FateBossModPreset.StayCloseRange(1) ?? "<null>");
        Check("melee (Role 2) range 2.6", FateBossModPreset.StayCloseRange(2) == "2.6", FateBossModPreset.StayCloseRange(2) ?? "<null>");
        Check("ranged/caster (Role 3) range 10, never melee", FateBossModPreset.StayCloseRange(3) == "10", FateBossModPreset.StayCloseRange(3) ?? "<null>");
        Check("healer (Role 4) range 10, never melee", FateBossModPreset.StayCloseRange(4) == "10", FateBossModPreset.StayCloseRange(4) ?? "<null>");
        Check("every role range is a valid BMR option",
            new byte[] { 0, 1, 2, 3, 4 }.All(r => FateBossModPreset.StayCloseRange(r) is { } v && options.Contains(v)));

        // Line endings normalized: the method-body extraction below matches "\n    }\n", which a CRLF
        // (Windows) checkout of FateGrind.cs never contains.
        var grindSrc = File.ReadAllText(FindSource("FateGrind.cs")).Replace("\r\n", "\n");
        Check("FateGrind sets the role range on StayCloseToTarget each FATE",
            Regex.IsMatch(grindSrc, @"SetTransient\(\s*""BossMod\.Autorotation\.MiscAI\.StayCloseToTarget""\s*,\s*""range""\s*,\s*FateBossModPreset\.StayCloseRange\("));

        // Stuck fallback: still armed while BossMod drives (a backstop after BMR had time to path/dodge).
        Check("preset active, still for 10 s -> fallback fires", FateStuckBackstop.IsStuck(10_000, bossModDriving: true));
        Check("preset active, still for 2 s -> BossMod gets time first", !FateStuckBackstop.IsStuck(2_000, bossModDriving: true));
        Check("no BossMod, still for 2 s -> fallback fires as before (1.5 s)", FateStuckBackstop.IsStuck(2_000, bossModDriving: false));
        Check("no BossMod, still for 1 s -> not yet", !FateStuckBackstop.IsStuck(1_000, bossModDriving: false));
        // Reach: while BossMod drives, anywhere inside its role range (+1 y slack) is "at the target", so the backstop
        // never drags a ranged job to melee; without BossMod the old max(3, hitbox + 2) rule is unchanged.
        Check("BossMod driving, caster 9 y out (hitbox 1, range 10) counts as at the target",
            9f <= FateStuckBackstop.ReachRange(1f, FateBossModPreset.StayCloseRangeYalms(3)));
        Check("BossMod driving, melee at its goal edge (hitbox 1, 3.5 y) counts as at the target",
            3.5f <= FateStuckBackstop.ReachRange(1f, FateBossModPreset.StayCloseRangeYalms(2)));
        Check("BossMod driving, melee 8 y out is still out of reach (backstop can fire)",
            8f > FateStuckBackstop.ReachRange(1f, FateBossModPreset.StayCloseRangeYalms(2)));
        Check("no BossMod: reach is the old max(3, hitbox + 2)",
            FateStuckBackstop.ReachRange(0.5f, null) == 3f && FateStuckBackstop.ReachRange(4f, null) == 6f);

        var stuckBody = grindSrc.Contains("private async Task HandleCombatStuckDetection")
            ? grindSrc[grindSrc.IndexOf("private async Task HandleCombatStuckDetection", StringComparison.Ordinal)..] : "";
        stuckBody = stuckBody.Length > 0 ? stuckBody[..Math.Max(0, stuckBody.IndexOf("\n    }\n", StringComparison.Ordinal))] : "";
        Check("HandleCombatStuckDetection no longer bails out while the preset is active",
            stuckBody.Length > 0 && !stuckBody.Contains("if (FateBossModControl.IsOwnActive())"));
        Check("HandleCombatStuckDetection decides through FateStuckBackstop.IsStuck", stuckBody.Contains("FateStuckBackstop.IsStuck("));
    }

    /// <summary>
    ///     The settings window's "Reset to Default" must restore exactly the shipped default sort
    ///     order. The shipped default is pinned here as data; the window's reset list is read from
    ///     FateToolKitWindow.cs source - resolved through the shared provider when the reset delegates
    ///     to one (via reflection, so this harness still compiles against a Configuration without it),
    ///     parsed out of the button's inline list otherwise. The pre-fix reset (5 entries, no
    ///     TimeRemaining criterion, TimeRemainingUrgent ascending) fails the parity check.
    /// </summary>
    private static void SortOrderResetTests() {
        Console.WriteLine("-- sort order: window Reset to Default == shipped default --");

        (FateSortCriteria Criteria, bool Descending)[] expected =
        [
            (FateSortCriteria.HasBonusWithTwist, true),
            (FateSortCriteria.Progress, true),
            (FateSortCriteria.HasBonus, true),
            (FateSortCriteria.TimeRemainingUrgent, true),
            (FateSortCriteria.TimeRemaining, false),
            (FateSortCriteria.Distance, false),
        ];

        static string Describe(IEnumerable<(FateSortCriteria Criteria, bool Descending)> list)
            => string.Join(", ", list.Select(e => $"{e.Criteria}:{(e.Descending ? "desc" : "asc")}"));

        var shipped = new Configuration().SortOrder.Select(e => (e.Criteria, e.Descending)).ToList();
        Check("shipped default sort order is the pinned six-entry list",
            shipped.SequenceEqual(expected), Describe(shipped));

        var windowSrc = File.ReadAllText(FindSource("FateToolKitWindow.cs"));
        List<(FateSortCriteria Criteria, bool Descending)>? reset = null;
        var source = "inline list in the reset button";
        if (windowSrc.Contains("DefaultSortOrder")) {
            source = "shared default provider";
            var provider = typeof(Configuration).GetMethod("DefaultSortOrder",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            if (provider?.Invoke(null, null) is List<FateSortOrder> provided)
                reset = provided.Select(e => (e.Criteria, e.Descending)).ToList();
        }
        else {
            var block = Regex.Match(windowSrc, @"Reset to Default""\)\s*\)\s*\{(?<body>.*?)\]\s*;", RegexOptions.Singleline);
            if (block.Success)
                reset = Regex.Matches(block.Groups["body"].Value,
                        @"Criteria\s*=\s*FateSortCriteria\.(?<crit>\w+)\s*,\s*Descending\s*=\s*(?<dir>true|false)")
                    .Select(m => ((FateSortCriteria)Enum.Parse(typeof(FateSortCriteria), m.Groups["crit"].Value),
                        m.Groups["dir"].Value == "true"))
                    .ToList();
        }

        Check("the window's reset list could be read from source", reset != null, source);
        Check("Reset to Default restores exactly the shipped default (same entries, same order, same directions)",
            reset != null && reset.SequenceEqual(expected),
            reset == null ? "<unreadable>" : $"{source}: {Describe(reset)}");
    }

    private static string FindSource(string file) {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null) {
            var p = Path.Combine(dir.FullName, "src", "LazyFateAutomation", file);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(file);
    }
}
