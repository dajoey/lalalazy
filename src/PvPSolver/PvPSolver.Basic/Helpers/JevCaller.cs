using System.Net.Http;
using System.Text;
using ECommons.GameHelpers;
using ECommons.Logging;
using RotationSolver.Decisions;

namespace RotationSolver.Basic.Helpers;

/// <summary>
/// "Let Jev call the shots": once a second in a PvP fight, send a jobs-and-numbers snapshot to the PvP Jev
/// relay and keep its latest answer for <see cref="JevCall.FocusPick"/>. Off unless <c>JevCallsShots</c> is on.
/// One request in flight at a time, started on a pool thread (so no part of the HTTP call, proxy lookup
/// included, runs on the game thread); any failure just leaves the previous answer to go stale, so PvP
/// Solver falls back to its own logic.
/// </summary>
public static class JevCaller
{
    // The relay is a LAN address: no proxy, so Windows proxy auto-detection (WPAD) never runs for it.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromMilliseconds(800) })
    {
        Timeout = TimeSpan.FromMilliseconds(1500),
    };
    private static JevAnswer? _latest;
    private static double _lastSent = double.MinValue;
    private static int _inFlight;

    /// <summary>The relay's latest answer (may be stale; the readers check freshness).</summary>
    public static JevAnswer? Latest => Volatile.Read(ref _latest);

    /// <summary>Monotonic seconds, the clock every answer is stamped with.</summary>
    public static double Now => Environment.TickCount64 / 1000.0;

    /// <summary>Called from the plugin's frame update.</summary>
    public static void Update()
    {
        if (!Service.Config.JevCallsShots || !DataCenter.IsPvP || !DataCenter.InCombat || Player.Object == null)
        {
            return;
        }

        double now = Now;
        if (now - _lastSent < JevCall.IntervalSeconds || Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return;
        }

        _lastSent = now;
        string? body;
        try
        {
            body = BuildSnapshot();
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"[JevCaller] snapshot failed: {ex.Message}");
            body = null;
        }

        if (body == null)
        {
            Interlocked.Exchange(ref _inFlight, 0);
            return;
        }

        string url = Service.Config.JevRelayUrl;
        _ = Task.Run(() => SendAsync(body, url));
    }

    private static async Task SendAsync(string body, string url)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(url, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (JevCall.Parse(json, Now) is { } answer)
            {
                Volatile.Write(ref _latest, answer);
            }
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"[JevCaller] relay call failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    private static string? BuildSnapshot()
    {
        IBattleChara? me = Player.Object;
        if (me == null)
        {
            return null;
        }

        var enemies = new List<JevEnemy>();
        foreach (IBattleChara b in DataCenter.AllHostileTargets)
        {
            if (b == null || b.IsDead || b.MaxHp == 0)
            {
                continue;
            }

            enemies.Add(new JevEnemy(
                b.GameObjectId,
                b.ClassJob.Value.Abbreviation.ToString(),
                b.GetHealthRatio(),
                b.DistanceToPlayer(),
                b.HasStatus(false, StatusID.Guard),
                HasInvincibleStatus(b),
                b.HasStatus(false, StatusID.Stun_1343, StatusID.Bind_1345, StatusID.Heavy_1344, StatusID.DeepFreeze_3219, StatusID.MiracleOfNature),
                b.TargetObjectId == me.GameObjectId));
        }

        if (enemies.Count == 0)
        {
            return null;
        }

        var allies = new List<JevAlly>();
        foreach (IBattleChara a in DataCenter.PartyMembers)
        {
            if (a == null || a.GameObjectId == me.GameObjectId || a.IsDead || a.MaxHp == 0)
            {
                continue;
            }

            allies.Add(new JevAlly(a.ClassJob.Value.Abbreviation.ToString(), a.GetHealthRatio(), a.DistanceToPlayer()));
        }

        return JevCall.SnapshotJson(me.ClassJob.Value.Abbreviation.ToString(), me.GetHealthRatio(), enemies, allies);
    }

    private static bool HasInvincibleStatus(IBattleChara b)
    {
        if (b.StatusList == null)
        {
            return false;
        }

        foreach (var status in b.StatusList)
        {
            if (status != null && status.IsInvincible())
            {
                return true;
            }
        }

        return false;
    }
}
