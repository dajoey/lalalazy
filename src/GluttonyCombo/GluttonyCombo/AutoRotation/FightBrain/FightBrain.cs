// Fork (Fight Brain 1a): the Fight Brain tick — Stage 1 ships it LOG-ONLY, per notebook
// Projects/Jev Fight Brain ("code owns facts, log first, act later"):
//   FB|forecast <add|drop>   one line when a forecast entry appears or expires
//   FB|cast-gate             one would-block line when the forecast-aware cast gate would have
//                            blocked a hard cast (shadow: the cast still starts)
//   FB|mitigate              the healer's raidwide mitigation question: the plain code rule's
//                            answer and Jev's answer in the SAME line (Jev in shadow via the
//                            ~/ops/pvp-jev relay, mode pve, its own daily cap)
// and the countdown strip (Window/FightBrainStrip.cs) draws code forecasts only.
// Nothing here presses an action. Jev is never asked under AutoDuty or any other external
// rotation driver, and every ask is throttled: at most one a second, at most three per hit.

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using GluttonyCombo.Core;
using GluttonyCombo.Services;
using Lalalazy.Telemetry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

namespace GluttonyCombo.AutoRotation;

internal static class FightBrain
{
    /// <summary> Local-only Fight Brain settings (no IPC lease can drive them): the fork's rotation config owns them. </summary>
    private static AutoRotationConfig Config => Service.Configuration.RotationConfig;

    /// <summary> The forecast as of the last read, for the strip and the ask flow. </summary>
    private static List<FightBrainHit> _current = [];

    /// <summary> The last read that produced a change, so the strip can age entries without re-reading IPC. </summary>
    internal static long LastReadMs;

    /// <summary> True while the last forecast read saw BossMod answering. </summary>
    internal static bool ForecastSeen;

    internal static bool Enabled => Config.FightBrain.Enabled;

    // ------------------------------------------------------------------ logging

    internal static void Log(string detail)
    {
        var line = $"FB|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}|{detail}";
        Svc.Log.Information(line);
        LalaTelemetry.Record(line);
    }

    // ------------------------------------------------------------------ tick

    private static long _nextReadMs;
    private static long _lastAskMs;
    private static FightBrainHit? _mitigateHit;
    private static int _mitigateAsksThisHit;
    private static ulong _mitigateHitKey;

    /// <summary> Called from the framework update, after the player checks. One cheap bool read when disabled. </summary>
    internal static void Tick()
    {
        if (!Enabled)
            return;
        var nowMs = Environment.TickCount64;
        if (nowMs < _nextReadMs)
            return;
        _nextReadMs = nowMs + FightBrainForecast.ReadEveryMs;

        var reading = FightBrainForecast.Read(nowMs);
        ForecastSeen = reading.Seen;
        var fresh = FightBrainForecastCore.Merge(_current, [], nowMs); // drop overdue entries from the kept list
        fresh = FightBrainForecastCore.Merge(fresh, reading.Hits, nowMs);
        LogForecastChange(_current, fresh, nowMs);
        _current = fresh;
        LastReadMs = nowMs;

        MaybeAskMitigate(nowMs);

        // Card 609a863c: the pre-pull plan reader (Part A) and the party-data shadow rules
        // (Part B) ride the same tick and the same tick guard; both are log-only.
        FightBrainPlan.Tick(nowMs);
        FightBrainParty.Tick(nowMs);
        // Card 271c4b4e: the party-callout reader (resolution tracking + outcome lines), log-only.
        FightBrainCallout.Tick(nowMs);
    }

    /// <summary> The kept forecast entries, for the plan reader's candidate build. </summary>
    internal static IReadOnlyList<FightBrainHit> CurrentHits() => _current;

    private static void LogForecastChange(IReadOnlyList<FightBrainHit> before, IReadOnlyList<FightBrainHit> after, long nowMs)
    {
        foreach (var hit in after)
        {
            if (!before.Any(b => b.Kind == hit.Kind && Math.Abs(b.AtMs - hit.AtMs) <= FightBrainHit.MergeWindowMs))
                Log($"forecast add kind={hit.Kind} in={hit.Countdown(nowMs)} src={hit.Source}");
        }
        foreach (var hit in before)
        {
            if (!after.Any(a => a.Kind == hit.Kind && Math.Abs(a.AtMs - hit.AtMs) <= FightBrainHit.MergeWindowMs))
                Log($"forecast drop kind={hit.Kind} src={hit.Source}");
        }
    }

    /// <summary> The current forecast entries with their countdowns now, for the strip. </summary>
    internal static IReadOnlyList<(FightBrainHit Hit, double Seconds)> StripEntries()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return [.. _current.OrderBy(h => h.AtMs).Take(6).Select(h => (h, (h.AtMs - now) / 1000.0))];
    }

    // ------------------------------------------------------------------ cast gate (shadow)

    /// <summary> A cast that is still running when the player must move is a cast the zone eats: this much, plus the gate's 0.5 s, has to fit before the zone activates. </summary>
    internal const double WalkOutSeconds = 1.0;

    /// <summary> The gate writes at most one would-block line per action per this many ms. </summary>
    private const long GateLogEveryMs = 500;

    private static long _nextGateLogMs;
    private static uint _lastGateAct;

    /// <summary>
    ///     The forecast-aware cast gate, SHADOW: called wherever <see cref="AutoRotationController.MovementBlocksCastStart"/>
    ///     let a hard cast through. It never blocks; when the cast would still run at the moment the player must move
    ///     (the danger zone under the player, from BMR), it writes one FB|cast-gate line so the pass bar ("would-block
    ///     calls match real cancels at least 70% of the time") can be graded against real play.
    /// </summary>
    internal static void ShadowCastGate(float castTimeMs, uint actionId)
    {
        if (castTimeMs <= 0f || !Enabled || !ForecastSeen)
            return;
        var (zoneIn, standingInZone) = FightBrainForecast.ZoneUnderPlayer();
        if (zoneIn is not { } zone)
            return;
        var nowMs = Environment.TickCount64;
        var castEndIn = castTimeMs / 1000.0 + 0.5;
        if (!FightBrainForecastCore.WouldBlockCast(castEndIn, zone, standingInZone, WalkOutSeconds))
            return;
        if (nowMs < _nextGateLogMs && _lastGateAct == actionId)
            return;
        _nextGateLogMs = nowMs + GateLogEveryMs;
        _lastGateAct = actionId;
        Log($"cast-gate would-block act={actionId} cast_end_in={castEndIn:F1} zone_in={zone:F1} walk_out={WalkOutSeconds:F1}");
    }

    // ------------------------------------------------------------------ mitigate (Jev in shadow)

    private static readonly uint[] HealJobs = [(uint)Job.WHM, (uint)Job.AST, (uint)Job.SCH];

    /// <summary> The healer's raidwide mitigations, strongest first: the Fight Brain page's Stage 1 set (WHM, AST, SCH). </summary>
    private static (uint Id, string Name, int Rank, double Mit)[] Mitigations(uint jobId) => jobId switch
    {
        (uint)Job.WHM => [(16536, "Temperance", 1, 0.15), (25862, "Liturgy of the Bell", 2, 0.10), (7433, "Plenary Indulgence", 3, 0.0)],
        (uint)Job.AST => [(16559, "Neutral Sect", 1, 0.20), (3613, "Collective Unconscious", 2, 0.10), (16557, "Horoscope", 3, 0.10)],
        (uint)Job.SCH => [(25868, "Expedient", 1, 0.10), (188, "Sacred Soil", 2, 0.10), (186, "Succor", 3, 0.0)],
        _ => [],
    };

    private static void MaybeAskMitigate(long nowMs)
    {
        var cfg = Config.FightBrain;
        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.RelayUrl) || Player.Object is null)
            return;
        // Jev stays off under AutoDuty and other unattended runs (page decision, 2026-10-31).
        if (!string.IsNullOrEmpty(AutoRotationController.cfg?.ExternalRotationDriver))
            return;
        if (Player.Job is not (Job.WHM or Job.AST or Job.SCH))
            return;
        if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
            return;
        if (nowMs - _lastAskMs < 1000)
            return;

        // The question opens when a raidwide is 3-15 s out (the page's window), at most three asks per hit.
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        FightBrainHit? ask = null;
        foreach (var h in _current)
        {
            if (h.Kind != FightBrainHitKind.Raidwide)
                continue;
            var secondsIn = (h.AtMs - nowUnix) / 1000.0;
            if (secondsIn is >= 3.0 and <= 15.0 && (ask is null || h.AtMs < ask.Value.AtMs))
                ask = h;
        }
        if (ask is not { } hit)
            return;
        var inSeconds = (hit.AtMs - nowUnix) / 1000.0;
        var key = (ulong)hit.AtMs;
        if (_mitigateHitKey == key && _mitigateAsksThisHit >= 3)
            return;
        if (_mitigateHitKey != key)
        {
            _mitigateHitKey = key;
            _mitigateAsksThisHit = 0;
        }

        var body = BuildMitigateSnapshot(hit, inSeconds);
        if (body is null)
            return;
        _lastAskMs = nowMs;
        _mitigateAsksThisHit++;
        var url = cfg.RelayUrl;
        _ = Task.Run(() => SendMitigateAsync(body, url, hit, inSeconds));
    }

    private static string? BuildMitigateSnapshot(FightBrainHit hit, double inSeconds)
    {
        try
        {
            var me = Player.Object!;
            var options = Mitigations((uint)Player.Job)
                .Select(m => new
                {
                    name = m.Name,
                    rank = m.Rank,
                    ready = ActionReady(m.Id),
                    mit = m.Mit,
                })
                .Concat([new { name = "save", rank = 99, ready = true, mit = 0.0 }])
                .ToArray();

            var party = new List<object> { new
            {
                id = me.GameObjectId,
                job = me.ClassJob.Value.Abbreviation.ToString(),
                hp = Math.Round(me.CurrentHp / (double)Math.Max(1u, me.MaxHp), 2),
                hp_now = me.CurrentHp,
                hp_max = me.MaxHp,
            } };
            foreach (var m in Svc.Party)
            {
                if (m.GameObject is IBattleChara a && a.GameObjectId != me.GameObjectId && a.MaxHp > 0)
                    party.Add(new
                    {
                        id = a.GameObjectId,
                        job = a.ClassJob.Value.Abbreviation.ToString(),
                        hp = Math.Round(a.CurrentHp / (double)a.MaxHp, 2),
                        hp_now = a.CurrentHp,
                        hp_max = a.MaxHp,
                    });
            }

            var snapshot = new
            {
                v = 4,
                mode = "pve",
                job = me.ClassJob.Value.Abbreviation.ToString(),
                hp = Math.Round(me.CurrentHp / (double)Math.Max(1u, me.MaxHp), 2),
                hp_now = me.CurrentHp,
                hp_max = me.MaxHp,
                hit = new { kind = "raidwide", @in = Math.Round(inSeconds, 1), src = hit.Source, src_id = hit.SourceId },
                party,
                mitigations = options,
            };
            return JsonSerializer.Serialize(snapshot, Json);
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] mitigate snapshot failed: {e.Message}");
            return null;
        }
    }

    internal static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    // The relay is a LAN address: no proxy, so Windows proxy auto-detection (WPAD) never runs for it.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromMilliseconds(800) })
    {
        Timeout = TimeSpan.FromMilliseconds(1500),
    };

    private static int _inFlight;

    private static async Task SendMitigateAsync(string body, string url, FightBrainHit hit, double inSeconds)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return;
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogMitigate(hit, inSeconds, "unavailable", "-", "-", "unknown");
                return;
            }
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            string? jev = null, conf = "-", rule = "unavailable", dmg = "unknown";
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("mitigate", out var m) && m.ValueKind == JsonValueKind.String)
                    jev = m.GetString();
                if (root.TryGetProperty("mitigate_conf", out var c) && c.ValueKind == JsonValueKind.Number)
                    conf = c.GetDouble().ToString("0.00");
                if (root.TryGetProperty("rule", out var r) && r.ValueKind == JsonValueKind.String)
                    rule = r.GetString();
                if (root.TryGetProperty("hit_damage", out var d) && d.ValueKind == JsonValueKind.Number)
                    dmg = d.GetInt64().ToString();
            }
            LogMitigate(hit, inSeconds, rule, jev ?? "-", conf, dmg);
        }
        catch (Exception e)
        {
            LogMitigate(hit, inSeconds, "unavailable", "-", "-", $"error {e.GetType().Name}");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    private static void LogMitigate(FightBrainHit hit, double inSeconds, string rule, string jev, string conf, string dmg)
    {
        // Both answers in ONE line, the whole of Part 2's grading input for mitigate.
        Log($"mitigate hit={hit.Source.Replace('|', '/')} in={inSeconds:F1}s rule={rule} jev={jev} conf={conf} dmg={dmg}");
    }
}
