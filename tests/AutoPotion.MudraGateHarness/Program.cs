using AutoPotion;

namespace AutoPotion.MudraGateHarness;

/// <summary>
///     Offline assertions on AutoPotion's uninterruptible-sequence gate (v0.2.5.2).
///     The gate inputs are fake game states built from the two live 2026-10-10
///     collisions (ffxivdb <c>plugin_log_lines</c>: AutoPotion PT fire lines inside
///     GluttonyCombo CT mudra windows, Ninja job 30, Palace of the Dead), so the
///     states the gate must hold on are the ones that actually broke a mudra, not
///     hand-typed guesses.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Replays();
        Characterization();
        TelemetryContract();

        Console.WriteLine(_fail == 0 ? "OK" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { _pass++; Console.WriteLine($"PASS  {name}"); }
        else { _fail++; Console.WriteLine($"FAIL  {name}{(detail.Length > 0 ? $"  [{detail}]" : "")}"); }
    }

    // ---------------------------------------------------------------- the two observed collisions

    private static void Replays()
    {
        // ffxivdb 2026-10-10 12:36:13.847 ET: AutoPotion used HP potion Ultra-Potion (47701) at
        // HP 48.2% (threshold 50%); the GluttonyCombo CT lines either side show mudra seals
        // status 496 held with 5.0 s remaining (12:36:13.730 `496:5.0`, 12:36:13.848 `496:4.9`).
        // 0.2.5.1 fired here; the gate must hold.
        Check("replay 12:36:13.847: Ultra-Potion at 48.2% HP with mudra seals up is HELD",
            ItemUseGate.ShouldHold(mudraSealsUp: true, tenChiJinUp: false, hpRatio: 0.482f),
            $"ShouldHold returned {ItemUseGate.ShouldHold(true, false, 0.482f)}");

        // ffxivdb 2026-10-10 12:36:27.050 ET: AutoPotion used regen potion Sustaining Potion
        // (20309) at HP 69.0%; the CT line 12:36:26.996 shows seals `496:6.0` mid-chain.
        Check("replay 12:36:27.050: Sustaining Potion at 69.0% HP with mudra seals up is HELD",
            ItemUseGate.ShouldHold(mudraSealsUp: true, tenChiJinUp: false, hpRatio: 0.690f),
            $"ShouldHold returned {ItemUseGate.ShouldHold(true, false, 0.690f)}");
    }

    // ---------------------------------------------------------------- what must NOT change

    private static void Characterization()
    {
        // 12:36:15.324 ET, ~1.5 s after the seals dropped mid-chain: CT line shows `496:-`
        // (no seals) at HP 46.2% — below his 50% threshold. The potion must fire as before.
        Check("seals dropped (496 gone) at 46.2% HP: NOT held, fires on the next tick",
            !ItemUseGate.ShouldHold(false, false, 0.462f));

        // The emergency carve-out: at or below the floor a heal fires even mid-seals —
        // a cancelled jutsu is cheaper than dying. Boundary is at-or-below.
        Check("emergency: seals up at 20.0% HP: NOT held, the heal fires",
            !ItemUseGate.ShouldHold(true, false, 0.200f));
        Check("emergency boundary at exactly the 25% floor: NOT held",
            !ItemUseGate.ShouldHold(true, false, ItemUseGate.EmergencyHpFloor));
        Check("just above the 25% floor with seals up: held",
            ItemUseGate.ShouldHold(true, false, ItemUseGate.EmergencyHpFloor + 0.001f));

        // Ten Chi Jin is the other NIN sequence window (GluttonyCombo reads its status
        // param as the seal tracker); an item landing mid-TCJ breaks it the same way.
        Check("Ten Chi Jin (1186) up at 80.0% HP: HELD",
            ItemUseGate.ShouldHold(false, true, 0.800f));
        Check("Ten Chi Jin up at 20.0% HP: NOT held (same emergency carve-out)",
            !ItemUseGate.ShouldHold(false, true, 0.200f));

        // Kassatsu (497) is a persisting buff, not a sequence: no seals, no TCJ — the
        // gate must not hold (Tick only probes 496/1186, so this state cannot hold).
        Check("Kassatsu alone (no seals, no TCJ) at 48.2% HP: NOT held",
            !ItemUseGate.ShouldHold(false, false, 0.482f));

        // No sequence statuses exist outside NIN: for every other job the gate is a
        // no-op — 0.2.5.1 behaviour is unchanged.
        Check("no seals, no TCJ, any HP: NOT held",
            !ItemUseGate.ShouldHold(false, false, 0.05f)
            && !ItemUseGate.ShouldHold(false, false, 0.99f));

        // The status ids ARE GluttonyCombo's (NIN_Helper.cs Buffs.Mudra / Buffs.TenChiJin).
        // If either changes, re-verify against Gluttony's NIN module before shipping.
        Check("status ids match GluttonyCombo NIN_Helper: Mudra=496, TenChiJin=1186",
            ItemUseGate.MudraStatusId == 496u && ItemUseGate.TenChiJinStatusId == 1186u);
    }

    // ---------------------------------------------------------------- the telemetry contract

    private static void TelemetryContract()
    {
        // The hold must be visible in the existing decision tap: the reason code is
        // short, lowercase and pipe-free (the ffxivdb parse contract), and a near-miss
        // line carrying it keeps the exact 14-field shape inside the line budget.
        var reason = PotionTelemetryFormat.ReasonHeldMudra;
        Check("held reason code is short, lowercase and pipe-free",
            reason.Length is > 0 and <= 10 && !reason.Contains('|') && reason == reason.ToLowerInvariant(),
            reason);

        var line = PotionTelemetryFormat.BuildLine(
            unixMs: 1_791_650_173_847, job: 30, ev: PotionTelemetryFormat.EvNearMiss, itemId: 0,
            hpPct: 48.2f, hpThr: 50f, mpPct: 100f, mpThr: 30f,
            inCombat: true, inDuty: true, deepDungeon: true, reason: reason, item: null);
        Check("held near-miss line keeps the 14-field PT| shape",
            line.StartsWith("PT|", StringComparison.Ordinal) && line.Split('|').Length == 14, line);
        Check("held near-miss line is inside the 200-byte budget",
            line.Length <= PotionTelemetryFormat.MaxLineLength, line.Length.ToString());
        Check("held near-miss line carries the held reason",
            line.Split('|')[12] == reason, line);
    }
}
