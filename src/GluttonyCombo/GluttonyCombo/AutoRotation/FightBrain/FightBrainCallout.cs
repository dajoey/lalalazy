// Fork (Fight Brain, party-callout reader — card 271c4b4e): read party and alliance chat
// in combat, join each human callout to the exact live state, and ask Jev ONE typed choice
// question per line: which candidate response the callout means. Jev never reads a callout
// on its own — the code gathers the state (head markers and on whom, tethers, party
// positions, the Stage 1 forecast's next mechanic, who sent the line and how long ago the
// last mechanic resolved), builds the candidates from that state, and computes the
// code-only answer first.
//
//   FB|callout          one line per accepted callout: the text, the state, the code answer
//   FB|callout-pick     the relay's answer beside the code answer (mode "callout", its own
//                       daily cap; an unavailable relay logs jev=unavailable and nothing else)
//   FB|callout-outcome  about 8 s later: party deaths and HP drops since the ask, with both
//                       picks repeated, so the line stands alone for grading
//
// Conflict order in code and in every line: safety rules in code first (never stand in a
// telegraph - safe-first), then the live callout, then the pre-pull plan. LOG ONLY: nothing
// here presses an action or moves anyone. Jev is never asked under AutoDuty or any other
// external rotation driver, never in PvP, never in a Crucible board, at most one ask a
// second and one in flight, and unclear words are not asked at all (the code schedule is
// the answer, as the card settled).

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;
using GluttonyCombo.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GluttonyCombo.AutoRotation;

internal static class FightBrainCallout
{
    private static AutoRotationConfig Config => Service.Configuration.RotationConfig;
    private static FightBrainSettings Cfg => Config.FightBrain;

    /// <summary> Party and alliance channels the reader listens to (chat types only; the sender filter is below). </summary>
    private static readonly Dalamud.Game.Text.XivChatType[] Channels =
    [
        Dalamud.Game.Text.XivChatType.Party,
        Dalamud.Game.Text.XivChatType.Alliance,
        Dalamud.Game.Text.XivChatType.CrossParty,
    ];

    /// <summary> A callout is short: past this many sanitized characters it is conversation, not a callout. </summary>
    internal const int MaxCalloutLength = 60;

    /// <summary> How long after an ask the outcome line waits before it reads deaths and HP drops. </summary>
    internal const long OutcomeAfterMs = 8000;

    /// <summary> At most one Jev ask starts per this many ms (the mitigate throttle). </summary>
    private const long AskEveryMs = 1000;

    private static bool _subscribed;
    private static int _inFlight;
    private static long _lastAskMs;
    private static uint _askNumber;

    /// <summary> The forecast as of the last tick, so an entry that vanishes marks its mechanic resolved. </summary>
    private static List<FightBrainHit> _lastHits = [];
    private static long _lastResolvedUnixMs = -1;
    private static string _lastResolvedKind = string.Empty;

    private sealed class PendingOutcome
    {
        internal uint Ask;
        internal long AskUnixMs;
        internal string Code = "-";
        internal string Jev = "-";
        internal List<(int Slot, uint Hp, bool Dead)> Hp = [];
    }

    private static readonly List<PendingOutcome> Pending = [];

    // ------------------------------------------------------------------ lifecycle

    /// <summary> Subscribe the chat hook; called once from the plugin constructor and lazily from the tick. </summary>
    internal static void Subscribe()
    {
        if (_subscribed)
            return;
        try
        {
            Svc.Chat.ChatMessage += OnChatMessage;
            _subscribed = true;
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] callout reader chat subscribe failed: {e.Message}");
        }
    }

    internal static void Unsubscribe()
    {
        if (!_subscribed)
            return;
        try
        {
            Svc.Chat.ChatMessage -= OnChatMessage;
        }
        catch
        {
            // detach is best effort; the plugin is unloading anyway
        }
        _subscribed = false;
    }

    /// <summary> Called from the Fight Brain tick, after the party shadow rules. </summary>
    internal static void Tick(long nowMs)
    {
        if (!_subscribed)
            Subscribe(); // lazy: the chat service may not be injected when the constructor runs
        if (!Cfg.Enabled || !Cfg.CalloutReader)
            return;
        TrackResolved(nowMs);
        LogOutcomes(nowMs);
    }

    // ------------------------------------------------------------------ forecast resolution tracking

    private static void TrackResolved(long nowMs)
    {
        try
        {
            var current = FightBrain.CurrentHits();
            foreach (var hit in _lastHits)
            {
                if (current.Any(c => c.Kind == hit.Kind && Math.Abs(c.AtMs - hit.AtMs) <= FightBrainHit.MergeWindowMs))
                    continue;
                _lastResolvedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _lastResolvedKind = hit.Kind.ToString();
            }
            _lastHits = [.. current];
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] callout resolved-track failed: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ chat hook

    private static void OnChatMessage(Dalamud.Game.Chat.IHandleableChatMessage message)
    {
        try
        {
            if (!Cfg.Enabled || !Cfg.CalloutReader)
                return;
            if (!Channels.Contains(message.LogKind))
                return;
            // Only lines from party members (or the player): the relation kind is the game's own answer.
            if (message.SourceKind is not (Dalamud.Game.Text.XivChatRelationKind.PartyMember
                or Dalamud.Game.Text.XivChatRelationKind.LocalPlayer))
                return;
            if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
                return;
            if (Svc.ClientState.IsPvP || FightBrainForecast.CrucibleBoard)
                return;
            // Jev stays off under AutoDuty and other unattended runs (page decision, 2026-10-31).
            if (!string.IsNullOrEmpty(AutoRotationController.cfg?.ExternalRotationDriver))
                return;
            if (Player.Object is null)
                return;

            var text = FightBrainPlanCore.Sanitize(message.Message?.TextValue);
            if (text.Length is 0 or > MaxCalloutLength)
                return;

            var nowMs = Environment.TickCount64;
            var state = GatherState(text, message.Sender?.TextValue);
            var intent = FightBrainCalloutCore.Classify(text);
            var candidates = FightBrainCalloutCore.BuildCandidates(state);
            var code = FightBrainCalloutCore.CodeAnswer(state, intent, candidates);

            _askNumber++;
            var ask = _askNumber;
            var askUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            FightBrain.Log(
                $"callout ask={ask} text=\"{FightBrainPlanCore.LogText(text)}\" from={state.SenderRole} " +
                $"markers={state.Members.Count(m => m.Marker != 0)} tethers={state.Members.Count(m => m.Tethered)} " +
                $"next={(state.NextKind is null ? "none" : $"{state.NextKind} in {state.NextIn:F1}s")} " +
                $"since_res={(state.SinceResolved >= 0 ? $"{state.SinceResolved:F1}s" : "none")} " +
                $"safe={(state.StandingUnsafe ? 1 : 0)} intent={intent} code={code} " +
                $"{FightBrainCalloutCore.OrderNote}");

            // Unclear words get no Jev ask at all: the code schedule is the answer (card decision).
            if (intent == CalloutIntent.Unclear)
            {
                FightBrain.Log($"callout-pick ask={ask} code={code} jev=not-asked reason=unclear rule=callout-shadow");
                RecordOutcome(ask, askUnixMs, code, "not-asked");
                return;
            }
            if (string.IsNullOrWhiteSpace(Cfg.RelayUrl))
            {
                FightBrain.Log($"callout-pick ask={ask} code={code} jev=off reason=no-relay rule=callout-shadow");
                RecordOutcome(ask, askUnixMs, code, "off");
                return;
            }
            if (nowMs - _lastAskMs < AskEveryMs || Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            {
                FightBrain.Log($"callout-pick ask={ask} code={code} jev=skipped reason=throttled rule=callout-shadow");
                RecordOutcome(ask, askUnixMs, code, "skipped");
                return;
            }
            _lastAskMs = nowMs;

            var body = JsonSerializer.Serialize(BuildSnapshot(state, intent, candidates, code), FightBrain.Json);
            var url = Cfg.RelayUrl;
            _ = Task.Run(() => SendCalloutAsync(body, url, ask, askUnixMs, code, text));
        }
        catch (Exception e)
        {
            // A chat hook must never break the game or the plugin: log and swallow.
            PluginLog.Debug($"[FightBrain] callout reader failed: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ state gathering

    private static string RoleOf(string job)
    {
        return job switch
        {
            "PLD" or "WAR" or "DRK" or "GNB" => "tank",
            "WHM" or "SCH" or "AST" or "SGE" => "healer",
            "BLM" or "SMN" or "RDM" or "BLU" or "BRD" or "MCH" or "DNC" or "NIN" or "DRG" or "MNK" or "SAM" or "RPR" or "VPR" or "PCT" => "dps",
            _ => "unknown",
        };
    }

    /// <summary> The sender's party label: matches the sender name against the party list (never logs the name). </summary>
    private static (string Role, int Slot) MatchSender(string? sender, IReadOnlyList<(string Job, int Slot, string Name)> party)
    {
        if (string.IsNullOrWhiteSpace(sender))
            return ("unknown", -1);
        // Sender arrives as "First Last" possibly with a world suffix; compare the name part only.
        var name = sender.Split('<', '@')[0].Trim();
        foreach (var (job, slot, memberName) in party)
        {
            if (memberName.Length > 0 && name.StartsWith(memberName, StringComparison.OrdinalIgnoreCase))
                return (job is null ? "unknown" : RoleOf(job), slot);
        }
        return ("unknown", -1);
    }

    private static CalloutState GatherState(string text, string? sender)
    {
        try
        {
            var me = Player.Object!;
            var members = new List<CalloutMember>();
            var partyMeta = new List<(string Job, int Slot, string Name)>();
            var mySlot = -1;

            // Svc.Party iterates the party slot list, which IS the slot order in duty
            // content (the role-sorted default arrangement). The player's own slot is
            // matched by object id.
            for (var i = 0; i < Svc.Party.Length; i++)
            {
                var member = Svc.Party[i];
                if (member.GameObject is not IBattleChara a || a.MaxHp <= 0)
                    continue;
                var job = "unknown";
                try { job = a.ClassJob.Value.Abbreviation.ToString(); } catch { /* sheet miss keeps unknown */ }
                var isMe = a.GameObjectId == me.GameObjectId;
                if (isMe)
                    mySlot = i;
                uint marker = 0;
                try { unsafe { marker = a.GameObject()->NamePlateIconId; } } catch { /* native read is best effort */ }
                var tethered = false;
                try { tethered = a.GetTethers().Count > 0; } catch { /* tether read is best effort */ }
                int[]? offset = null;
                try
                {
                    var d = a.Position - me.Position;
                    offset = [(int)Math.Round(d.X), (int)Math.Round(d.Y), (int)Math.Round(d.Z)];
                }
                catch { /* position read is best effort */ }
                members.Add(new CalloutMember(i, $"{job}{i}", RoleOf(job), marker, tethered, offset));
                partyMeta.Add((job, i, isMe ? me.Name.TextValue : a.Name.TextValue));
            }

            var (senderRole, _) = MatchSender(sender, partyMeta);
            if (mySlot >= 0 && sender == me.Name?.TextValue)
                senderRole = "me";

            // The next mechanic on the Stage 1 forecast: the nearest entry not yet overdue.
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string? nextKind = null;
            double nextIn = -1;
            foreach (var hit in FightBrain.CurrentHits().OrderBy(h => h.AtMs))
            {
                var secondsIn = (hit.AtMs - nowUnix) / 1000.0;
                if (secondsIn < -FightBrainHit.OverdueMs / 1000.0)
                    continue;
                nextKind = hit.Kind.ToString();
                nextIn = Math.Round(secondsIn, 1);
                break;
            }

            var sinceResolved = _lastResolvedUnixMs < 0
                ? -1
                : Math.Round((nowUnix - _lastResolvedUnixMs) / 1000.0, 1);

            var (zoneIn, standingInZone) = FightBrainForecast.ZoneUnderPlayer();

            return new CalloutState(text, senderRole, members, mySlot, nextKind, nextIn, sinceResolved, standingInZone);
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] callout state gather failed: {e.Message}");
            return new CalloutState(text, "unknown", [], -1, null, -1, -1, false);
        }
    }

    // ------------------------------------------------------------------ relay

    private static object BuildSnapshot(CalloutState state, CalloutIntent intent,
        IReadOnlyList<CalloutCandidate> candidates, string code)
    {
        var marked = state.Members.Where(m => m.Marker != 0)
            .Select(m => new { slot = m.Slot, label = m.Label, icon = m.Marker }).ToArray();
        var members = state.Members
            .Select(m => new
            {
                slot = m.Slot,
                label = m.Label,
                role = m.Role,
                marker = m.Marker,
                tether = m.Tethered,
                pos = m.Offset,
            })
            .ToArray();
        var cands = candidates.Select(c => new { id = c.Id, does = c.Does }).ToArray();
        return new
        {
            v = 1,
            mode = "callout",
            duty = SafeDutyName(),
            text = state.Text,
            intent = intent.ToString(),
            sender = state.SenderRole,
            me_slot = state.MySlot,
            members,
            markers = marked,
            next = state.NextKind is null ? null : new { kind = state.NextKind, @in = state.NextIn },
            since_res = state.SinceResolved,
            standing_unsafe = state.StandingUnsafe,
            candidates = cands,
            code_default = code,
        };
    }

    private static string SafeDutyName()
    {
        try
        {
            return Svc.ClientState.TerritoryType.ToString();
        }
        catch
        {
            return "?";
        }
    }

    private static async Task SendCalloutAsync(string body, string url, uint ask, long askUnixMs, string code, string text)
    {
        try
        {
            using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await FightBrainHttp.Client.PostAsync(url, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // A capped (429) or unavailable relay stops cleanly: the code answer already stands.
                FightBrain.Log($"callout-pick ask={ask} code={code} jev=unavailable status={(int)response.StatusCode} rule=callout-shadow");
                RecordOutcome(ask, askUnixMs, code, $"unavailable-{(int)response.StatusCode}");
                return;
            }
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            string? pick = null, conf = "-", rule = "-";
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("callout", out var c) && c.ValueKind == JsonValueKind.String)
                    pick = c.GetString();
                if (root.TryGetProperty("callout_conf", out var cf) && cf.ValueKind == JsonValueKind.Number)
                    conf = cf.GetDouble().ToString("0.00");
                if (root.TryGetProperty("rule", out var r) && r.ValueKind == JsonValueKind.String)
                    rule = r.GetString();
            }
            FightBrain.Log($"callout-pick ask={ask} code={code} jev={pick ?? "-"} conf={conf} rule={rule} text=\"{FightBrainPlanCore.LogText(text)}\"");
            RecordOutcome(ask, askUnixMs, code, pick ?? "-");
        }
        catch (Exception e)
        {
            FightBrain.Log($"callout-pick ask={ask} code={code} jev=unavailable error={e.GetType().Name} rule=callout-shadow");
            RecordOutcome(ask, askUnixMs, code, $"error-{e.GetType().Name}");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    // ------------------------------------------------------------------ outcome watcher

    private static void RecordOutcome(uint ask, long askUnixMs, string code, string jev)
    {
        try
        {
            if (Player.Object is null)
                return;
            var hp = new List<(int Slot, uint Hp, bool Dead)>();
            for (var i = 0; i < Svc.Party.Length; i++)
            {
                if (Svc.Party[i].GameObject is not IBattleChara a)
                    continue;
                hp.Add((i, a.CurrentHp, a.IsDead));
            }
            lock (Pending)
            {
                Pending.Add(new PendingOutcome { Ask = ask, AskUnixMs = askUnixMs, Code = code, Jev = jev, Hp = hp });
                // keep the list bounded: outcomes older than a minute are dropped unseen
                var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Pending.RemoveAll(p => nowUnix - p.AskUnixMs > 60000);
            }
        }
        catch { /* best effort */ }
    }

    private static void LogOutcomes(long nowMs)
    {
        try
        {
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            List<PendingOutcome> due;
            lock (Pending)
            {
                due = Pending.Where(p => nowUnix - p.AskUnixMs >= OutcomeAfterMs).ToList();
                foreach (var d in due)
                    Pending.Remove(d);
            }
            if (due.Count == 0)
                return;
            // Live party HP now, by slot, compared with the ask-time snapshot.
            var nowHp = new Dictionary<int, (uint Hp, bool Dead)>();
            for (var i = 0; i < Svc.Party.Length; i++)
                if (Svc.Party[i].GameObject is IBattleChara a)
                    nowHp[i] = (a.CurrentHp, a.IsDead);
            foreach (var p in due)
            {
                var deaths = 0;
                var drops = 0;
                foreach (var (slot, hpAtAsk, deadAtAsk) in p.Hp)
                {
                    if (!nowHp.TryGetValue(slot, out var now))
                        continue;
                    if (now.Dead && !deadAtAsk)
                        deaths++;
                    else if (now.Hp < hpAtAsk)
                        drops++;
                }
                FightBrain.Log($"callout-outcome ask={p.Ask} code={p.Code} jev={p.Jev} deaths={deaths} hpdrop={drops}");
            }
        }
        catch { /* best effort */ }
    }
}
