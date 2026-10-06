using System.Globalization;
using GluttonyCombo.Data;
using Buff = GluttonyCombo.Data.ComboTelemetryFormat.Buff;
using BstSnapshot = GluttonyCombo.Data.BeastmasterTelemetryFormat.Snapshot;

namespace GluttonyCombo.TelemetryHarness;

/// <summary>
///     Offline assertions on the fork's combo-decision telemetry tap
///     (GluttonyCombo v1.0.4.168). Compiles the real
///     <see cref="ComboTelemetryFormat"/> — no Dalamud, no game — so the wire
///     format the ffxivdb join depends on is proven before shipping.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;
    private static int _canary;

    private static int Main()
    {
        // A representative RDM decision: Jolt III (7524) replaced by Verthunder III (25855).
        var line = ComboTelemetryFormat.BuildLine(
            unixMs: 1_788_636_000_123,
            job: "RDM",
            combo: "RDM_ST_SimpleMode",
            original: 7524,
            chosen: 25855,
            gcdRemaining: 1.87f,
            weaveCount: 0,
            canWeave: true,
            targetHpPct: 63.5f,
            buffs: [new Buff(1249, true, 26.4f), new Buff(1234, true, null), new Buff(3211, false, 8.0f)]);

        Check("prefix is the greppable CT|", line.StartsWith("CT|", StringComparison.Ordinal));
        Check("exact line shape",
            line == "CT|1788636000123|RDM|RDM_ST_SimpleMode|7524|25855|1.87|0+|63.5|1249:26.4;1234:-;t3211:8.0",
            line);

        var fields = line.Split('|');
        Check("10 pipe-separated fields", fields.Length == 10, fields.Length.ToString());
        Check("field 1 is unix ms", fields[1] == "1788636000123");
        Check("originalActionId in field 4", fields[4] == "7524");
        Check("chosenActionId in field 5 (the join key)", fields[5] == "25855");
        Check("gcdRemaining 2dp", fields[6] == "1.87");
        Check("weave slot carries count + can-weave", fields[7] == "0+");
        Check("target HP% 1dp", fields[8] == "63.5");
        Check("absent-but-consulted status renders as id:-", fields[9].Contains("1234:-", StringComparison.Ordinal));
        Check("non-player status is t-prefixed", fields[9].Contains("t3211:8.0", StringComparison.Ordinal));

        // Culture must not be able to turn 1.87 into 1,87 and break the parser.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var german = ComboTelemetryFormat.BuildLine(
                1_788_636_000_123, "RDM", "RDM_ST_SimpleMode", 7524, 25855,
                1.87f, 0, true, 63.5f, [new Buff(1249, true, 26.4f)]);
            Check("invariant decimals under de-DE",
                german == "CT|1788636000123|RDM|RDM_ST_SimpleMode|7524|25855|1.87|0+|63.5|1249:26.4", german);
        }
        finally { CultureInfo.CurrentCulture = previous; }

        // No buffs consulted: the trailing field is simply empty, never malformed.
        var noBuffs = ComboTelemetryFormat.BuildLine(
            1, "WHM", "WHM_ST_MainCombo", 119, 3568, 2.50f, 2, false, 100.0f, []);
        Check("empty keyBuffs still yields 10 fields", noBuffs.Split('|').Length == 10, noBuffs);
        Check("weave slot renders can-weave false", noBuffs.Split('|')[7] == "2-", noBuffs);

        // Budget: a flood of consulted statuses must not blow the ~200 char line.
        var many = Enumerable.Range(0, 40).Select(i => new Buff((uint)(3000 + i), i % 2 == 0, 12.3f)).ToArray();
        var long1 = ComboTelemetryFormat.BuildLine(
            1_788_636_000_123, "SGE", "SGE_ST_DPS", 24283, 24284, 2.44f, 1, true, 12.7f, many);
        Check("line stays within the 200-char budget",
            long1.Length <= ComboTelemetryFormat.MaxLineLength, $"len={long1.Length}");
        Check("truncated line is marked with ~", long1.EndsWith('~'), long1);
        Check("truncation keeps all 10 fields", long1.Split('|').Length == 10, long1);
        Check("truncation never cuts a buff mid-entry",
            long1.TrimEnd('~').Split('|')[9].Split(';').All(e => e.Length == 0 || e.Contains(':')), long1);

        // A long combo name must not silently eat the buff list's structure.
        var longName = ComboTelemetryFormat.BuildLine(
            1_788_636_000_123, "BLU", new string('X', 90), 11385, 11390, 2.20f, 0, false, 99.9f,
            [new Buff(1234, true, 15.0f), new Buff(1235, true, 15.0f)]);
        Check("long combo name still yields 10 fields", longName.Split('|').Length == 10, longName);

        // The emit gate: one line per CHANGE, not per frame.
        var seen = new Dictionary<(uint, uint), uint>();
        Check("first decision emits", ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 25855));
        Check("same decision repeated does not emit", !ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 25855));
        Check("changed choice emits", ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 7524));
        Check("back to the old choice emits", ComboTelemetryFormat.ShouldEmit(seen, 1, 7524, 25855));
        Check("a different button is tracked separately", ComboTelemetryFormat.ShouldEmit(seen, 1, 7503, 25855));
        Check("a different preset is tracked separately", ComboTelemetryFormat.ShouldEmit(seen, 2, 7524, 25855));
        Check("unchanged-action decisions also de-duplicate",
            ComboTelemetryFormat.ShouldEmit(seen, 3, 16457, 16457) &&
            !ComboTelemetryFormat.ShouldEmit(seen, 3, 16457, 16457));

        BeastmasterCases();
        CrucibleCases();
        StallCases();
        LeaseCases();

        Console.WriteLine(_fail == 0
            ? _canary > 0 ? $"OK ({_pass} checks, canary failed as expected)" : $"OK ({_pass} checks)"
            : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    ///     The Beastmaster BT| collector: line shape, the change gate, the 4/s rate floor,
    ///     and a REPLAY of real ffxivdb Beastmaster play to measure the actual line rate.
    /// </summary>
    private static void BeastmasterCases()
    {
        Console.WriteLine("-- BST collector (BT|) --");

        var snap = new BstSnapshot(
            TPGauge: 100, FamiliarTPGauge: 132, FamiliarTPAtLastUse: 168,
            ActiveBattlehorn: 2, InstinctualComboState: 7, CurrentAffinity: 5,
            ChainCount: 3, KinshipState: 0x51, InstinctStacks: 0x0A,
            PetObjectId: 1073741830, PetName: "Cu Sith", PetDataId: 5432,
            AdjustedBeastMode: 44896, AdjustedAvalanche: 44930,
            Statuses: new ushort[] { 4599, 4601, 4621 },
            DecisionActionId: 44887, DecisionReason: "instinctual:compass");

        var line = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577, snap);

        Check("BST prefix is the greppable BT|", line.StartsWith("BT|", StringComparison.Ordinal), line);
        Check("BST exact line shape",
            line == "BT|1788904962577|6484a802070503510a|2|5|3|81|pet=1073741830:Cu Sith:5432|bm=44896|av=44930|dec=44887:instinctual:compass|fd=|sl=|lv=0|tk=-|4599,4601,4621",
            line);
        Check("BST gauge hex is 18 chars (9 bytes)",
            line.Split('|')[2].Length == 18, line);
        // Decode the hex back to the eight bytes it claims to carry: the follow-up cards
        // read these bytes out of SQL, so a byte-order slip here is a silent data defect.
        var hex = line.Split('|')[2];
        var decoded = Enumerable.Range(0, 9)
            .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
            .ToArray();
        Check("BST gauge hex decodes to the source bytes in 0x08..0x10 order",
            decoded.SequenceEqual(new byte[] { 100, 132, 168, 2, 7, 5, 3, 0x51, 0x0A }),
            string.Join(",", decoded));

        // Characterization for the tk= Trick-outcome field (BST-1): everything before it keeps today's
        // exact shape, so the new field can only ever be appended after lv=, never reshape the prefix.
        Check("BST-1: the fields before tk= keep today's exact shape",
            line.StartsWith("BT|1788904962577|6484a802070503510a|2|5|3|81|pet=1073741830:Cu Sith:5432|bm=44896|av=44930|dec=44887:instinctual:compass|fd=|sl=|lv=0|", StringComparison.Ordinal),
            line);
        CheckCanary("BST-1 canary: a pending Trick must not read as timed out",
            Outcome(1.0f, float.MaxValue) == "timeout");

        // No decision recorded (pre-rotation build, or a tick where nothing fired): a stable
        // dec=0: token, never an empty/malformed field.
        var noDecision = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { DecisionActionId = 0, DecisionReason = null });
        Check("BST dec=0: when no decision was recorded", noDecision.Contains("|dec=0:|"), noDecision);

        // fd= (t_f987910c fix 4): the familiar-loop decline rides its own field so the
        // terminal Record() (gcdchain/instinctual) can never overwrite it in dec=.
        var declined = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { FamiliarDecline = "temperedrelease:declined-onewithnature" });
        Check("BST fd= carries the familiar-loop decline when present",
            declined.Contains("|fd=temperedrelease:declined-onewithnature|"), declined);
        Check("BST fd= is empty (stable fd= token) when the loop had nothing to report",
            line.Contains("|fd=|"), line);
        var nastyDecline = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { FamiliarDecline = "battlehorn|declined,evil" });
        Check("BST fd= is sanitised like every other free-text field",
            !nastyDecline.Contains("battlehorn|declined") && nastyDecline.Split('|').Length == line.Split('|').Length,
            nastyDecline);

        // No familiar out: the pet field must be a stable token, not an empty field.
        var noPet = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { PetObjectId = 0, PetName = null, PetDataId = 0 });
        Check("BST pet=none when no familiar is summoned", noPet.Contains("|pet=none|"), noPet);

        // A translated pet name containing the separator must not fabricate a field.
        var nasty = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { PetName = "Cu|Sith,the:Hound" });
        Check("BST pet name is sanitised", !nasty.Contains("Cu|Sith"), nasty);
        Check("BST sanitised line keeps its field count",
            nasty.Split('|').Length == line.Split('|').Length, nasty);

        // A decision reason containing the separator must not fabricate a field either.
        var nastyReason = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { DecisionReason = "battlehorn|slot2,evil" });
        Check("BST decision reason is sanitised", !nastyReason.Contains("slot2,evil"), nastyReason);
        Check("BST sanitised-reason line keeps its field count",
            nastyReason.Split('|').Length == line.Split('|').Length, nastyReason);

        // Invariant culture: a de-DE comma decimal would destroy a split_part parse.
        Check("BST line carries no comma decimals",
            !System.Text.RegularExpressions.Regex.IsMatch(line.Split('|')[^1].Replace(",", ""), @"\d,\d"),
            line);

        // Budget: a flood of statuses must not blow the 200-char line.
        var manyStatuses = Enumerable.Range(0, 40).Select(i => (ushort)(4595 + i)).ToArray();
        var longLine = BeastmasterTelemetryFormat.BuildLine(1_788_904_962_577,
            snap with { Statuses = manyStatuses });
        Check("BST line stays within the line budget",
            longLine.Length <= BeastmasterTelemetryFormat.MaxLineLength, $"len={longLine.Length}");
        Check("BST truncated line is marked with ~", longLine.EndsWith('~'), longLine);
        Check("BST truncation keeps all 16 fields", longLine.Split('|').Length == 16, longLine);

        // tk= (BST-1): the outcome of the most recent Trick rides its own field after lv=, so the
        // Tricks with no completion inside the window can be sorted into heart-seen vs timed-out
        // straight out of SQL instead of being re-derived from the fd= declines.
        Check("BST-1: tk= carries the Trick outcome token",
            BeastmasterTelemetryFormat.BuildLine(1, WithTrick(snap, "heart")).Contains("|tk=heart|")
            && BeastmasterTelemetryFormat.BuildLine(1, WithTrick(snap, "timeout")).Contains("|tk=timeout|")
            && BeastmasterTelemetryFormat.BuildLine(1, WithTrick(snap, "pending")).Contains("|tk=pending|"),
            BeastmasterTelemetryFormat.BuildLine(1, WithTrick(snap, "pending")));
        Check("BST-1: no Trick in the window renders the stable tk=- token",
            BeastmasterTelemetryFormat.BuildLine(1, snap).Contains("|tk=-|"),
            BeastmasterTelemetryFormat.BuildLine(1, snap));
        Check("BST-1: the outcome mapping is heart / timeout / pending / -",
            Outcome(2.0f, 1.0f) == "heart" && Outcome(4.0f, float.MaxValue) == "timeout"
            && Outcome(1.2f, float.MaxValue) == "pending" && Outcome(9.0f, 1.0f) == "-",
            $"{Outcome(2f, 1f)}/{Outcome(4f, float.MaxValue)}/{Outcome(1.2f, float.MaxValue)}/{Outcome(9f, 1f)}");
        Check("BST-1: a Trick outcome exactly at the 3.0 s boundary reads timeout",
            Outcome(3.0f, float.MaxValue) == "timeout" && Outcome(2.99f, float.MaxValue) == "pending",
            $"{Outcome(3f, float.MaxValue)}/{Outcome(2.99f, float.MaxValue)}");
        Check("BST-1: the live sampler is wired (ReadState fills LastTrickOutcome, Sample passes it)",
            RepoFile("Combos", "PvE", "BST", "BST.cs").Contains("LastTrickOutcome = Data.BeastmasterTelemetryFormat.TrickOutcome", StringComparison.Ordinal)
            && RepoFile("Data", "BeastmasterTelemetry.cs").Contains("BST.LastTrickOutcome", StringComparison.Ordinal),
            "the live half must feed tk= the same way dec=/fd= are fed");

        // --- the change gate ---------------------------------------------------------
        var gate = new BeastmasterTelemetryFormat.GateState();
        long t = 1_000_000;

        Check("BST first snapshot emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap));
        t += 1000;
        Check("BST identical snapshot does not emit",
            !BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap));
        t += 1000;
        Check("BST a changed gauge byte emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap with { ChainCount = 4 }));
        t += 1000;
        Check("BST a changed pet emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap with { ChainCount = 4, PetObjectId = 99 }));
        t += 1000;
        Check("BST a changed Beast Mode action emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t, snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900 }));

        // Statuses alone are deliberately NOT part of the key (they flap).
        t += 1000;
        Check("BST a status-only change does not emit",
            !BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, Statuses = new ushort[] { 4643 } }));

        // A decision change (same gauge/pet/beastmode) must still emit - the rotation's
        // choice is part of the change key, not an afterthought riding on gauge bytes.
        t += 1000;
        Check("BST a decision-only change emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, DecisionActionId = 44888, DecisionReason = "instinctual:compass" }));
        t += 1000;
        Check("BST the same decision repeated does not emit",
            !BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, DecisionActionId = 44888, DecisionReason = "instinctual:compass" }));

        // The instinct-stack byte (gauge 0x10) is part of the change key: stack banking
        // must emit so Rally/Rallying Cheer decisions can be graded against real counts.
        t += 1000;
        Check("BST a changed instinct-stack byte emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, DecisionActionId = 44888, DecisionReason = "instinctual:compass", InstinctStacks = 0x12 }));
        t += 1000;
        Check("BST the same instinct-stack byte repeated does not emit",
            !BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, DecisionActionId = 44888, DecisionReason = "instinctual:compass", InstinctStacks = 0x12 }));

        // A changed fd= is part of the change key: resummon suppression state must emit
        // even while the gauge bytes are still (pet dead, TP capped, etc.).
        t += 1000;
        Check("BST a changed familiar-decline emits",
            BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, DecisionActionId = 44888, DecisionReason = "instinctual:compass", InstinctStacks = 0x12, FamiliarDecline = "battlehorn:declined-recast-slot1" }));
        t += 1000;
        Check("BST the same familiar-decline repeated does not emit",
            !BeastmasterTelemetryFormat.ShouldEmit(ref gate, t,
                snap with { ChainCount = 4, PetObjectId = 99, AdjustedBeastMode = 44900, DecisionActionId = 44888, DecisionReason = "instinctual:compass", InstinctStacks = 0x12, FamiliarDecline = "battlehorn:declined-recast-slot1" }));

        // A changed tk= is part of the change key too (BST-1): the pending -> heart / timeout
        // transition must emit on its own, even while every gauge byte sits still - that is the
        // whole point of the field, a Trick that resolves without moving anything else.
        var tkGate = new BeastmasterTelemetryFormat.GateState();
        Check("BST-1: a pending -> heart transition emits on its own",
            BeastmasterTelemetryFormat.ShouldEmit(ref tkGate, t, WithTrick(snap, "pending"))
            && !BeastmasterTelemetryFormat.ShouldEmit(ref tkGate, t + 1000, WithTrick(snap, "pending"))
            && BeastmasterTelemetryFormat.ShouldEmit(ref tkGate, t + 2000, WithTrick(snap, "heart"))
            && !BeastmasterTelemetryFormat.ShouldEmit(ref tkGate, t + 3000, WithTrick(snap, "heart")));

        // --- the rate floor ----------------------------------------------------------
        var rlGate = new BeastmasterTelemetryFormat.GateState();
        long rt = 5_000_000;
        BeastmasterTelemetryFormat.ShouldEmit(ref rlGate, rt, snap with { ChainCount = 0 });
        Check("BST a change inside the rate window is held",
            !BeastmasterTelemetryFormat.ShouldEmit(ref rlGate, rt + 100, snap with { ChainCount = 1 }));
        Check("BST the held change still emits past the window",
            BeastmasterTelemetryFormat.ShouldEmit(ref rlGate, rt + BeastmasterTelemetryFormat.MinIntervalMs, snap with { ChainCount = 1 }),
            "a suppressed change must not be recorded as seen");

        // The adversary: a snapshot that changes on EVERY tick at 100 fps must still be
        // capped at 4 lines/s.
        var advGate = new BeastmasterTelemetryFormat.GateState();
        var advLines = 0;
        for (var i = 0; i < 1000; i++) // 1000 ticks x 10ms = 10 seconds
            if (BeastmasterTelemetryFormat.ShouldEmit(ref advGate, 9_000_000 + i * 10, snap with { ChainCount = (byte)(i % 250) }))
                advLines++;
        Check($"BST worst case is capped at 4 lines/s (10s of per-tick change -> {advLines} lines)",
            advLines <= 41, $"{advLines} lines in 10s");

        // --- REPLAY: real ffxivdb Beastmaster play -----------------------------------
        var tracePath = Path.Combine(AppContext.BaseDirectory, "trace-bst-ffxivdb.csv");
        if (!File.Exists(tracePath))
        {
            Check("BST replay trace is present", false, tracePath);
            return;
        }

        var changeTimes = File.ReadAllLines(tracePath)
            .Where(l => l.Length > 0 && char.IsDigit(l[0]))
            .Select(l => long.Parse(l, CultureInfo.InvariantCulture))
            .OrderBy(x => x)
            .ToArray();

        Check("BST replay trace has real rows", changeTimes.Length > 5000, $"n={changeTimes.Length}");

        // Rebuild ~100 fps ticks across each active window and step the REAL gate.
        const int tickMs = 10;
        const long idleGapMs = 120_000;
        var replayGate = new BeastmasterTelemetryFormat.GateState();
        var lines = 0;
        long ticks = 0;
        long activeMs = 0;
        byte churn = 0;

        var windowStart = 0;
        for (var i = 0; i < changeTimes.Length; i++)
        {
            var isLast = i == changeTimes.Length - 1;
            if (!isLast && changeTimes[i + 1] - changeTimes[i] <= idleGapMs)
                continue;

            var from = changeTimes[windowStart];
            var to = changeTimes[i];
            activeMs += to - from;

            var ci = windowStart;
            for (var now = from; now <= to; now += tickMs)
            {
                ticks++;
                var changed = false;
                while (ci <= i && changeTimes[ci] <= now) { changed = true; ci++; }
                if (changed) churn++;

                // Between changes the snapshot is identical, which is what the gate sees.
                if (BeastmasterTelemetryFormat.ShouldEmit(ref replayGate, now, snap with { ChainCount = churn }))
                    lines++;
            }

            windowStart = i + 1;
        }

        var minutes = activeMs / 60000.0;
        var perMin = lines / minutes;
        Console.WriteLine($"     replay: {changeTimes.Length} real actions, {ticks:N0} ticks, " +
                          $"{minutes:F1} min active -> {lines} lines ({perMin:F2}/min)");

        Check($"BST replay line rate stays modest ({perMin:F2}/min over {minutes:F0} min)",
            perMin < 60, $"{perMin:F2}/min");
        Check("BST replay emitted fewer lines than state changes",
            lines <= changeTimes.Length, $"{lines} lines vs {changeTimes.Length} changes");
        Check("BST replay never exceeded 4 lines/s on average",
            lines <= activeMs / 250 + 1, $"{lines} lines over {activeMs}ms");
    }

    /// <summary> The Crucible of the Unbroken collector (CR|): line shape, flags, the change gate and rate floor. </summary>
    private static void CrucibleCases()
    {
        Console.WriteLine("-- BST Crucible collector (CR|) --");
        var f = CrucibleTelemetryFormat.Flags.TargetStance | CrucibleTelemetryFormat.Flags.TargetOnPlayer
                | CrucibleTelemetryFormat.Flags.SnarlReady | CrucibleTelemetryFormat.Flags.Interruptible;
        var snap = new CrucibleTelemetryFormat.Snapshot(
            Board: 1, Battle: 1, Needs: 3, Enemies: 2, HighestEnemyHp: 87,
            TargetNameId: 14531, TargetHp: 64, CastId: 46871, CastRemaining: 3.2f, Observed: f,
            PlayerHp: 92, PetHp: 40, SlotPetHp: "40.0.0",
            DecisionActionId: 1_000_004, DecisionReason: "crucible:hold-stance", Shadow: "aggro:snarl-parry");

        var line = CrucibleTelemetryFormat.BuildLine(1_788_904_962_577, snap);
        Check("CR| exact line shape",
            line == "CR|1788904962577|b=1|bt=1|nd=ID|ne=2|hi=87|t=14531:64|c=46871:3.2|f=Sysi|hp=92|pet=40|sl=40.0.0|dec=1000004:crucible:hold-stance|sh=aggro:snarl-parry|ttd=0|in=0|vul=0|xp=0|d=|mv=0.0|dg=x",
            line);
        Check("CR| trend fields render", CrucibleTelemetryFormat.BuildLine(1, snap with { TimeToDeath = 12.4f, IntakePerSecond = 350, VulnerabilityRemaining = 8.6f, PartyHpVerified = true })
            .Contains("|ttd=12|in=350|vul=9|xp=1|"));
        Check("CR| no panel battle renders bt=-1", CrucibleTelemetryFormat.BuildLine(1, snap with { Battle = -1, Needs = 0 }).Contains("|bt=-1|nd=|"));
        Check("CR| all flag letters in order",
            CrucibleTelemetryFormat.BuildLine(1, snap with { Observed = (CrucibleTelemetryFormat.Flags)0x0FFF }).Contains("|f=DSXNPJCpysci|"));
        var nasty = CrucibleTelemetryFormat.BuildLine(1, snap with { DecisionReason = "a|b\nc" + new string('x', 200), Shadow = "s|h" });
        Check("CR| reasons cannot fabricate fields", nasty.Split('|').Length == CrucibleTelemetryFormat.BuildLine(1, snap).Split('|').Length, nasty);
        Check("CR| stays within the line budget", nasty.Length <= CrucibleTelemetryFormat.MaxLineLength, $"len={nasty.Length}");

        // The idle-time fields (1.0.4.266): the edge distance to the target and the character's own speed name
        // why the rotation was not attacking, on the lines that are already emitted for other changes.
        Check("CR| edge distance to the target renders",
            CrucibleTelemetryFormat.BuildLine(1, snap with { TargetEdgeDistance = 1.94f }).EndsWith("|xp=0|d=1.9|mv=0.0|dg=x"));
        Check("CR| own movement speed renders",
            CrucibleTelemetryFormat.BuildLine(1, snap with { MoveSpeed = 6.16f }).Contains("|d=|mv=6.2"));
        Check("CR| an out-of-range edge distance is clamped, never negative",
            !CrucibleTelemetryFormat.BuildLine(1, snap with { TargetEdgeDistance = 140f }).Contains("d=140"));

        // The board's degree (1.0.4.288): dg= names it on every line (0 Standard .. 3 Third, x when the plugin has not seen it set), so a run
        // can be graded against the hit sizes its degree puts on the character and the familiars.
        Check("CR| dg= renders each degree", new[] { 0, 1, 2, 3 }.All(d => CrucibleTelemetryFormat.BuildLine(1, snap with { Degree = d }).EndsWith($"|mv=0.0|dg={d}")));
        Check("CR| dg= is x when the degree is unread (the default)", CrucibleTelemetryFormat.BuildLine(1, snap).EndsWith("|dg=x")
            && CrucibleTelemetryFormat.BuildLine(1, snap with { Degree = -1 }).EndsWith("|dg=x") && CrucibleTelemetryFormat.BuildLine(1, snap with { Degree = 9 }).EndsWith("|dg=x"));
        var moving = new CrucibleTelemetryFormat.GateState();
        Check("CR| speed gate: first snapshot emits", CrucibleTelemetryFormat.ShouldEmit(ref moving, 1_000, snap));
        Check("CR| d= and mv= never join the emit key (speed alone must not flood lines)",
            !CrucibleTelemetryFormat.ShouldEmit(ref moving, 2_000, snap with { TargetEdgeDistance = 4.2f, MoveSpeed = 7.7f }));
        Check("CR| dg= never joins the emit key (a degree read mid-board must not add a line)",
            !CrucibleTelemetryFormat.ShouldEmit(ref moving, 3_000, snap with { Degree = 1 }));

        var gate = new CrucibleTelemetryFormat.GateState();
        long t = 1_000;
        Check("CR| first snapshot emits", CrucibleTelemetryFormat.ShouldEmit(ref gate, t, snap));
        t += 1_000;
        Check("CR| unchanged snapshot does not emit", !CrucibleTelemetryFormat.ShouldEmit(ref gate, t, snap));
        Check("CR| HP moving inside a 5% bucket does not emit", !CrucibleTelemetryFormat.ShouldEmit(ref gate, t, snap with { PlayerHp = 94, CastRemaining = 1f }));
        Check("CR| a new cast emits", CrucibleTelemetryFormat.ShouldEmit(ref gate, t, snap with { CastId = 46866 }));
        Check("CR| a change inside 250 ms is held back", !CrucibleTelemetryFormat.ShouldEmit(ref gate, t + 100, snap with { CastId = 0 }));
        Check("CR| and emits once the window passes", CrucibleTelemetryFormat.ShouldEmit(ref gate, t + 260, snap with { CastId = 0 }));
    }

    /// <summary>
    ///     The lease/connector collector (LS|, 2026-10-06). AutoDuty drives Gluttony through the WrathCombo gates (the omasky bridge plugin
    ///     forwards them): it takes a lease, switches Auto-Rotation on and overrides eight options while the lease lives. The IPC channel
    ///     logged all of that at Debug only, so nothing in ffxivdb could say which settings a run really ran with.
    ///     Real values from the 2026-10-06 loop (AutoDuty 0.0.0.380 SetAutoMode against the stored GluttonyCombo.json).
    /// </summary>
    private static void LeaseCases()
    {
        Console.WriteLine("-- lease / connector (LS|) --");
        var reg = LeaseTelemetryFormat.Register(1_791_298_653_858, "AutoDuty", "3fa85f64-5717-4562-b3fc-2c963f66afa6");
        Check("LS| register line shape", reg == "LS|1791298653858|ev=register|plugin=AutoDuty|lease=3fa85f64", reg);
        var state = LeaseTelemetryFormat.State(1_791_298_653_900, "AutoDuty", on: true, stored: false);
        Check("LS| state line shape (stored Auto-Rotation was off, the lease turns it on)", state == "LS|1791298653900|ev=state|plugin=AutoDuty|autorot=on|stored=off", state);
        var cfg = LeaseTelemetryFormat.Config(1_791_298_654_000, "AutoDuty", "OnlyAttackInCombat", 0, 1);
        Check("LS| config line shape (lease False over a stored True: diff=1)", cfg == "LS|1791298654000|ev=config|plugin=AutoDuty|opt=OnlyAttackInCombat|val=0|stored=1|diff=1", cfg);
        var same = LeaseTelemetryFormat.Config(1_791_298_654_001, "AutoDuty", "IgnoreRangeInBoss", 1, 1);
        Check("LS| a lease value equal to the stored one says diff=0", same.EndsWith("|val=1|stored=1|diff=0", StringComparison.Ordinal), same);
        var unknown = LeaseTelemetryFormat.Config(1, "AutoDuty", "DPSRotationMode", 4, null);
        Check("LS| no stored value renders an empty stored field and diff=?", unknown == "LS|1|ev=config|plugin=AutoDuty|opt=DPSRotationMode|val=4|stored=|diff=?", unknown);
        var rel = LeaseTelemetryFormat.Release(1_791_299_000_000, "AutoDuty", "LeaseeReleased");
        Check("LS| release line shape", rel == "LS|1791299000000|ev=release|plugin=AutoDuty|why=LeaseeReleased", rel);
        var dirty = LeaseTelemetryFormat.Register(1, "Auto|Duty\r\nx" + new string('y', 100), "abc");
        Check("LS| a plugin name cannot add fields or lines and stays inside the budget",
            !dirty.Contains('\n') && !dirty.Contains('\r') && dirty.Split('|').Length == 5 && dirty.Length <= LeaseTelemetryFormat.MaxLineLength, dirty);

        // AutoDuty re-sends all twelve settings every 5 s while it runs; only a first or a changed value is a new fact.
        var gate = new LeaseTelemetryFormat.ChangeGate();
        Check("LS| first value of an option emits", gate.ShouldEmit("AutoDuty", "OnlyAttackInCombat", "0"));
        Check("LS| the same value re-sent every 5 s does not", !gate.ShouldEmit("AutoDuty", "OnlyAttackInCombat", "0"));
        Check("LS| another option emits", gate.ShouldEmit("AutoDuty", "InCombatOnly", "0"));
        Check("LS| a changed value emits", gate.ShouldEmit("AutoDuty", "OnlyAttackInCombat", "1"));
        Check("LS| another plugin's lease on the same option is its own fact", gate.ShouldEmit("Questionable", "OnlyAttackInCombat", "1"));
        gate.Forget("AutoDuty");
        Check("LS| after the lease is released the same value is news again", gate.ShouldEmit("AutoDuty", "OnlyAttackInCombat", "1"));
        Check("LS| ... and Forget touched only that plugin", !gate.ShouldEmit("Questionable", "OnlyAttackInCombat", "1"));
    }

    /// <summary> The stalled-GCD collector (SG|): line shape, reason words, and the continuing-stall rate. </summary>
    private static void StallCases()
    {
        Console.WriteLine("-- BST Crucible stalled GCD (SG|) --");
        var snap = new CrucibleStallFormat.Snapshot(
            ActionId: 44884, Why: CrucibleStallFormat.Reason.Range, EdgeDistance: 2.34f,
            TargetNameId: 14531, StallSeconds: 1.4f);

        var line = CrucibleStallFormat.BuildLine(1_788_904_962_577, snap);
        Check("SG| exact line shape",
            line == "SG|1788904962577|act=44884|r=range|d=2.3|t=14531|s=1.4",
            line);
        Check("SG| no target renders the reason with an empty distance",
            CrucibleStallFormat.BuildLine(1, snap with { Why = CrucibleStallFormat.Reason.NoTarget, EdgeDistance = -1f, TargetNameId = 0 })
                == "SG|1|act=44884|r=notarget|d=|t=0|s=1.4");
        Check("SG| an unexplained stall names itself unknown",
            CrucibleStallFormat.BuildLine(1, snap with { Why = CrucibleStallFormat.Reason.Unknown })
                .Contains("|r=unknown|"));
        Check("SG| every reason has a greppable word",
            CrucibleStallFormat.Reason.NoTarget is var _ && new[]
            {
                (CrucibleStallFormat.Reason.NoTarget, "notarget"), (CrucibleStallFormat.Reason.Cast, "cast"),
                (CrucibleStallFormat.Reason.Lock, "lock"), (CrucibleStallFormat.Reason.Queue, "queue"),
                (CrucibleStallFormat.Reason.Range, "range"), (CrucibleStallFormat.Reason.Unselectable, "unselectable"),
                (CrucibleStallFormat.Reason.Unknown, "unknown"),
            }.All(p => CrucibleStallFormat.BuildLine(1, snap with { Why = p.Item1 }).Contains($"|r={p.Item2}|")));
        Check("SG| the first line waits out the stall threshold", CrucibleStallFormat.StallAfterSeconds == 1f,
            CrucibleStallFormat.StallAfterSeconds.ToString(CultureInfo.InvariantCulture));

        var gate = new CrucibleStallFormat.GateState();
        long t = 1_000;
        Check("SG| a new stall emits at once", CrucibleStallFormat.ShouldEmit(ref gate, t, snap));
        Check("SG| the same stall does not re-log inside 2 s", !CrucibleStallFormat.ShouldEmit(ref gate, t + 500, snap with { StallSeconds = 1.9f }));
        Check("SG| a continuing stall re-logs after 2 s with its new duration",
            CrucibleStallFormat.ShouldEmit(ref gate, t + 2_100, snap with { StallSeconds = 3.1f }));
        Check("SG| the reason changing starts a new stall line",
            CrucibleStallFormat.ShouldEmit(ref gate, t + 2_200, snap with { Why = CrucibleStallFormat.Reason.Lock, EdgeDistance = -1f }));
        Check("SG| the line stays within its budget",
            CrucibleStallFormat.BuildLine(1, snap with { StallSeconds = 999f }).Length <= CrucibleStallFormat.MaxLineLength);

        // -- the dash-hold field and the unselectable reason (2026-10-04, live: the top two idle classes were
        //    "range, standing still" and an in-melee "unknown" the lines could not attribute to a cause) --
        Check("SG| a range line names why the gap-close did not fire",
            CrucibleStallFormat.BuildLine(1, snap with { DashHold = "charges" })
                == "SG|1|act=44884|r=range|d=2.3|t=14531|s=1.4|dh=charges",
            CrucibleStallFormat.BuildLine(1, snap with { DashHold = "charges" }));
        Check("SG| a non-range line never carries the dash hold",
            !CrucibleStallFormat.BuildLine(1, snap with { Why = CrucibleStallFormat.Reason.Unknown, DashHold = "charges" })
                .Contains("|dh="));
        Check("SG| a range line with no hold to name omits the field",
            !CrucibleStallFormat.BuildLine(1, snap).Contains("|dh="));
        Check("SG| the longest dash-hold line stays within its budget",
            CrucibleStallFormat.BuildLine(1, snap with { StallSeconds = 999f, DashHold = "unreachable" })
                .Length <= CrucibleStallFormat.MaxLineLength);
        Check("SG| the live sampler names an unselectable held target and carries the dash hold",
            RepoFile("Data", "BeastmasterTelemetry.cs").Contains("IsTargetable", StringComparison.Ordinal)
            && RepoFile("Data", "BeastmasterTelemetry.cs").Contains("s.DashHold", StringComparison.Ordinal)
            && RepoFile("Combos", "PvE", "BST", "BST.cs").Contains("DashHold =", StringComparison.Ordinal),
            "the sampler must branch on the held target's live-target filter and pass s.DashHold; ReadState must fill it");

        // -- the stall clock (2026-10-03, live: every line said s=999.0 while damage flowed) --
        Check("SG| a send half a second ago is not a stall",
            !CrucibleStallFormat.IsStalled(true, true, true, 0.5f));
        Check("SG| the GCD ready without a hostile choice is not a stall",
            !CrucibleStallFormat.IsStalled(true, true, false, 1.5f));
        Check("SG| a send 1.5 s ago with everything else holding is a stall",
            CrucibleStallFormat.IsStalled(true, true, true, 1.5f));
        Check("SG| since-fire clamps into the line budget",
            CrucibleStallFormat.SinceFireSeconds(TimeSpan.FromHours(4)) == 999f
            && CrucibleStallFormat.SinceFireSeconds(TimeSpan.FromSeconds(-5)) == 0f);
        var samplerSrc = ReadSamplerSource();
        Check("SG| the live sampler feeds from the same-kind clock (ActionWatching keeps local time)",
            samplerSrc.Contains("ActionWatching.TimeSinceLastAction", StringComparison.Ordinal)
            && !samplerSrc.Contains("DateTime.UtcNow - ActionWatching.TimeLastActionUsed", StringComparison.Ordinal),
            "the sampler must consume ActionWatching.TimeSinceLastAction, never UtcNow minus the local-time property");
    }

    /// <summary> The live sampler's source, so the harness can pin its clock wiring (2026-10-03: UtcNow
    /// minus a local-time property saturated every line at s=999 while damage flowed). </summary>
    private static string ReadSamplerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "check-preset-ids.py")))
            dir = dir.Parent;
        if (dir is null)
            return "";
        return File.ReadAllText(Path.Combine(dir.FullName, "src", "GluttonyCombo", "GluttonyCombo",
            "Data", "BeastmasterTelemetry.cs"));
    }

    /// <summary> A repo file's text under src/GluttonyCombo/GluttonyCombo, for source-wiring checks. </summary>
    private static string RepoFile(params string[] rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "check-preset-ids.py")))
            dir = dir.Parent;
        if (dir is null)
            return "";
        var parts = new string[rel.Length + 4];
        parts[0] = dir.FullName;
        parts[1] = "src";
        parts[2] = "GluttonyCombo";
        parts[3] = "GluttonyCombo";
        rel.CopyTo(parts, 4);
        return File.ReadAllText(Path.Combine(parts));
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"PASS {what}"); }
        else { _fail++; Console.WriteLine($"FAIL {what}{(detail is null ? "" : $" -> {detail}")}"); }
    }

    /// <summary> A deliberately-wrong assertion that must FAIL: it proves the <see cref="Check"/>
    /// plumbing can fail, so the PASS lines above are not vacuous (canary pattern from the
    /// rot/harness-spike harness). A canary that PASSES counts as a real failure. </summary>
    private static void CheckCanary(string what, bool ok, string? detail = null)
    {
        if (ok)
        {
            _fail++;
            Console.WriteLine($"FAIL CANARY (expected to FAIL, and did not) {what}{(detail is null ? "" : $" -> {detail}")}");
        }
        else
        {
            _canary++;
            Console.WriteLine($"FAIL CANARY (expected to FAIL): {what}");
        }
    }

    /// <summary> Invokes the pure Trick-outcome mapping, or <c>&lt;missing&gt;</c> before the BST-1
    /// fix lands it. Reflection keeps this compiling against the pre-fix source, so the red proof
    /// is a runtime FAIL, not a build error. </summary>
    private static string Outcome(float sinceTrick, float sincePetHeart)
    {
        var m = typeof(BeastmasterTelemetryFormat).GetMethod("TrickOutcome",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return m is null ? "<missing>" : (string)m.Invoke(null, new object[] { sinceTrick, sincePetHeart })!;
    }

    /// <summary> Sets the tk= outcome on a snapshot through reflection, for the same reason as
    /// <see cref="Outcome"/>: the BST-1 red proof must FAIL AT RUNTIME against the pre-fix
    /// formatter (field absent), not fail to compile, so the reviewer's reverse-apply of only src
    /// still shows the FAIL lines. A typo'd name makes the checks fail post-fix, so the reflection
    /// cannot mask a defect. </summary>
    private static BstSnapshot WithTrick(BstSnapshot s, string outcome)
    {
        var boxed = (object)s;
        typeof(BstSnapshot).GetProperty("TrickOutcome")?.SetValue(boxed, outcome);
        return (BstSnapshot)boxed;
    }
}
