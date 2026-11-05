// Fork (Fight Brain, pre-pull plan reader — card 609a863c, Part A): collect the human text
// the scripts cannot parse — the party finder listing name and description of the group the
// player is in, plus an optional one-line note (/gluttony plan <note>) — and at each pull
// start let the relay (mode "plan", its own daily cap) ask Jev ONE question: which candidate
// plan fits that text. The pick is logged next to the code default and the text it saw.
// LOG ONLY: nothing here presses an action or moves anyone. Jev is never asked under
// AutoDuty or any other external rotation driver, never in PvP, at most once per pull.

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using GluttonyCombo.Core;
using GluttonyCombo.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GluttonyCombo.AutoRotation;

internal static class FightBrainPlan
{
    private static AutoRotationConfig Config => Service.Configuration.RotationConfig;
    private static FightBrainSettings Cfg => Config.FightBrain;

    /// <summary> Party finder listings seen while the browser received them, keyed by leader name; the newest wins. </summary>
    private static readonly Dictionary<string, (string Name, string Description, long ReceivedMs)> Listings = new();

    /// <summary> The player's own one-line note for the next pull (/gluttony plan <note>). </summary>
    internal static string Note = string.Empty;

    private static long _lastPullStartMs;
    private static uint _pullNumber;
    private static int _inFlight;
    private static bool _subscribed;

    // ------------------------------------------------------------------ party finder text

    /// <summary> Subscribe the listing cache; called once from the plugin constructor and lazily from the tick. </summary>
    internal static void Subscribe()
    {
        if (_subscribed)
            return;
        try
        {
            Svc.PfGui.ReceiveListing += OnReceiveListing;
            _subscribed = true;
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] plan reader PF subscribe failed: {e.Message}");
        }
    }

    internal static void Unsubscribe()
    {
        if (!_subscribed)
            return;
        try
        {
            Svc.PfGui.ReceiveListing -= OnReceiveListing;
        }
        catch
        {
            // detach is best effort; the plugin is unloading anyway
        }
        _subscribed = false;
    }

    private static void OnReceiveListing(Dalamud.Game.Gui.PartyFinder.Types.IPartyFinderListing listing,
        Dalamud.Game.Gui.PartyFinder.Types.IPartyFinderListingEventArgs args)
    {
        try
        {
            if (listing is null)
                return;
            var leader = listing.Name?.ToString() ?? string.Empty;
            var desc = listing.Description?.ToString() ?? string.Empty;
            if (leader.Length == 0 && desc.Length == 0)
                return;
            Listings[leader] = (listing.Name?.ToString() ?? string.Empty, desc,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            // Prune: keep the cache bounded (text only, no ids beyond the leader name key).
            if (Listings.Count > 64)
            {
                var oldest = default((string Key, long Ms));
                var found = false;
                foreach (var kv in Listings)
                    if (!found || kv.Value.ReceivedMs < oldest.Ms)
                        (oldest, found) = ((kv.Key, kv.Value.ReceivedMs), true);
                if (found)
                    Listings.Remove(oldest.Key);
            }
            // Capture line when the player is actually in a party group (joined or joining).
            if (FightBrain.Enabled && Svc.Party?.Length >= 4 && desc.Length > 0)
                FightBrain.Log($"plan-text src=pf {FightBrainPlanCore.LogText(FightBrainPlanCore.BuildText(leader, desc, Note))}");
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] plan reader PF listing failed: {e.Message}");
        }
    }

    /// <summary> The player's own note, set from the chat command; logged so the note itself is on record. </summary>
    internal static void SetNote(string note)
    {
        Note = note.Trim();
        if (FightBrain.Enabled)
            FightBrain.Log($"plan-text src=note {FightBrainPlanCore.LogText(FightBrainPlanCore.BuildText(null, null, Note))}");
    }

    // ------------------------------------------------------------------ tick: one ask per pull

    private static bool _wasInCombat;

    /// <summary> Called from the Fight Brain tick, after the forecast read. </summary>
    internal static void Tick(long nowMs)
    {
        if (!_subscribed)
            Subscribe(); // lazy: the PF gui service may not be injected when the plugin constructor runs
        if (!Cfg.Enabled || !Cfg.PlanReader)
            return;
        var inCombat = Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];
        if (inCombat && !_wasInCombat)
            OnPullStart(nowMs);
        _wasInCombat = inCombat;
    }

    private static void OnPullStart(long nowMs)
    {
        // One ask per pull: the combat edge is the pull start; guard against log spam bursts.
        if (nowMs - _lastPullStartMs < 2000)
            return;
        _lastPullStartMs = nowMs;
        _pullNumber++;

        try
        {
            if (string.IsNullOrWhiteSpace(Cfg.RelayUrl) || Player.Object is null)
                return;
            // Jev stays off under AutoDuty and other unattended runs, and never in PvP.
            if (!string.IsNullOrEmpty(AutoRotationController.cfg?.ExternalRotationDriver))
                return;
            if (Svc.ClientState.IsPvP)
                return;

            // The group's text: the newest party finder listing whose leader stands in the party
            // (fallback: the newest listing seen in the last 30 minutes), plus the note.
            var (pfName, pfDesc) = PickListing();
            var text = FightBrainPlanCore.BuildText(pfName, pfDesc, Note);

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var forecast = new List<object>();
            foreach (var hit in FightBrain.StripEntries())
                forecast.Add(new
                {
                    kind = hit.Hit.Kind.ToString(),
                    @in = Math.Round((hit.Hit.AtMs - nowUnix) / 1000.0, 1),
                    src = hit.Hit.Source,
                });

            var candidates = FightBrainPlanCore.BuildCandidates(FightBrain.CurrentHits());
            if (candidates.Count == 0)
                return;

            var body = JsonSerializer.Serialize(new
            {
                v = 1,
                mode = "plan",
                duty = SafeDutyName(),
                pull = _pullNumber,
                text,
                candidates = candidates.Select(c => new { id = c.Id, does = c.Does }).ToArray(),
                forecast,
                code_default = FightBrainPlanCore.CodeDefault,
            }, FightBrain.Json);

            FightBrain.Log($"plan-ask pull={_pullNumber} {FightBrainPlanCore.LogText(text)}");
            var url = Cfg.RelayUrl;
            _ = Task.Run(() => SendPlanAsync(body, url, _pullNumber, text));
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] plan ask failed: {e.Message}");
        }
    }

    private static (string? Name, string? Description) PickListing()
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            (string Name, string Description, long ReceivedMs)? best = null;
            foreach (var member in Svc.Party)
            {
                if (member.GameObject is not IBattleChara a)
                    continue;
                var leader = a.Name.TextValue;
                if (Listings.TryGetValue(leader, out var hit))
                    best = best is { } b && b.ReceivedMs > hit.ReceivedMs ? best : hit;
            }
            if (best is null)
                foreach (var kv in Listings.Values)
                    if (now - kv.ReceivedMs <= 30 * 60 * 1000L && (best is null || kv.ReceivedMs > best.Value.ReceivedMs))
                        best = kv;
            return best is { } pick ? (pick.Name, pick.Description) : (null, null);
        }
        catch
        {
            return (null, null);
        }
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

    private static async Task SendPlanAsync(string body, string url, uint pull, string text)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return;
        try
        {
            using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await FightBrainHttp.Client.PostAsync(url, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                FightBrain.Log($"plan pick pull={pull} code={FightBrainPlanCore.CodeDefault} jev=unavailable status={(int)response.StatusCode}");
                return;
            }
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            string? plan = null, conf = "-", rule = "-";
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.String)
                    plan = p.GetString();
                if (root.TryGetProperty("plan_conf", out var c) && c.ValueKind == JsonValueKind.Number)
                    conf = c.GetDouble().ToString("0.00");
                if (root.TryGetProperty("rule", out var r) && r.ValueKind == JsonValueKind.String)
                    rule = r.GetString();
            }
            // The one grading line: the code default beside Jev's pick, with the text it saw.
            FightBrain.Log($"plan pick pull={pull} code={FightBrainPlanCore.CodeDefault} jev={plan ?? "-"} conf={conf} rule={rule} {FightBrainPlanCore.LogText(text)}");
        }
        catch (Exception e)
        {
            FightBrain.Log($"plan pick pull={pull} code={FightBrainPlanCore.CodeDefault} jev=unavailable error={e.GetType().Name}");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }
}

/// <summary> Shared plan-reader HTTP client: LAN relay, no proxy, short timeouts (the mitigate pattern). </summary>
internal static class FightBrainHttp
{
    internal static readonly System.Net.Http.HttpClient Client =
        new(new System.Net.Http.SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromMilliseconds(800) })
        {
            Timeout = TimeSpan.FromMilliseconds(1500),
        };
}
