using System.Net.Http;
using System.Text;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Decisions;

namespace RotationSolver.Basic.Helpers;

/// <summary>
/// "Let Jev call the shots": once a second in a PvP fight, send a snapshot (jobs, numbers and relative positions,
/// no names) to the PvP Jev relay and keep its latest answer: <see cref="JevCall.FocusPick"/> for the target order
/// and <see cref="TryLimitBreak"/> for firing the job's PvP Limit Break. Off unless <c>JevCallsShots</c> is on.
/// One request in flight at a time, started on a pool thread (so no part of the HTTP call, proxy lookup included,
/// runs on the game thread); any failure just leaves the previous answer to go stale, so PvP Solver falls back to
/// its own logic.
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
    private static double _lastFireAttempt = double.MinValue;
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

    /// <summary>
    /// Called by the rotation before it picks its next action: when Jev's fresh answer says fire the PvP Limit Break,
    /// and the game confirms it can be used on that exact target right now, hand it back as the next action. One
    /// attempt per <see cref="JevCall.FireRetrySeconds"/> at most, so a use the game rejects never stalls the rotation.
    /// </summary>
    public static unsafe bool TryLimitBreak(IBaseAction? limitBreak, out IAction? act)
    {
        act = null;
        double now = Now;
        if (limitBreak == null || Player.Object == null
            || !JevCall.ShouldFireLimitBreak(Latest, now, _lastFireAttempt, out ulong? target))
        {
            return false;
        }

        _lastFireAttempt = now;
        ulong onWhom = limitBreak.Info.TargetAreaAction || target == null ? Player.Object.GameObjectId : target.Value;
        ActionManager* manager = ActionManager.Instance();
        if (manager == null || manager->GetActionStatus(ActionType.Action, limitBreak.Info.AdjustedID, onWhom) != 0)
        {
            return false;
        }

        // The relay sends the Limit Break target as the focus too, so the action's own targeting picks it first.
        if (!limitBreak.CanUse(out act, skipStatusProvideCheck: true, skipTargetStatusNeedCheck: true, usedUp: true,
                skipAoeCheck: true, skipTTKCheck: true))
        {
            act = null;
            return false;
        }

        if (target != null && !limitBreak.Info.TargetAreaAction && limitBreak.Target.Target?.GameObjectId != target)
        {
            act = null;
            return false;
        }

        PluginLog.Debug($"[JevCaller] firing Limit Break {limitBreak.ID} on {(target == null ? "self/area" : "Jev's target")}");
        return true;
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

    private static unsafe string? BuildSnapshot()
    {
        IBattleChara? me = Player.Object;
        if (me == null)
        {
            return null;
        }

        IBaseAction? lb = (DataCenter.CurrentRotation as CustomRotation)?.JevLimitBreak;
        ActionManager* manager = ActionManager.Instance();
        bool LbOk(ulong id) => lb != null && manager != null && manager->GetActionStatus(ActionType.Action, lb.Info.AdjustedID, id) == 0;
        bool selfOk = LbOk(me.GameObjectId);
        bool areaLb = lb?.Info.TargetAreaAction ?? false;
        float lbRange = lb?.TargetInfo.Range ?? 0f;
        bool sam = me.ClassJob.Value.Abbreviation.ToString() == "SAM";

        var party = new List<IBattleChara>();
        foreach (IBattleChara a in DataCenter.PartyMembers)
        {
            if (a != null && a.GameObjectId != me.GameObjectId && !a.IsDead && a.MaxHp != 0)
            {
                party.Add(a);
            }
        }

        var hostiles = new List<IBattleChara>();
        foreach (IBattleChara b in DataCenter.AllHostileTargets)
        {
            if (b != null && !b.IsDead && b.MaxHp != 0)
            {
                hostiles.Add(b);
            }
        }

        if (hostiles.Count == 0)
        {
            return null;
        }

        var enemies = new List<JevEnemy>(hostiles.Count);
        foreach (IBattleChara b in hostiles)
        {
            float dist = b.DistanceToPlayer();
            enemies.Add(new JevEnemy(
                b.GameObjectId,
                b.ClassJob.Value.Abbreviation.ToString(),
                b.GetHealthRatio(),
                b.CurrentHp,
                dist,
                b.Position.X - me.Position.X,
                b.Position.Z - me.Position.Z,
                b.HasStatus(false, StatusID.Guard),
                HasInvincibleStatus(b),
                b.HasStatus(false, StatusID.Stun_1343, StatusID.Bind_1345, StatusID.Heavy_1344, StatusID.DeepFreeze_3219, StatusID.MiracleOfNature),
                b.TargetObjectId == me.GameObjectId,
                party.Count(a => a.TargetObjectId == b.GameObjectId),
                sam && b.HasStatus(true, StatusID.Kuzushi),
                // A placed (ground) Limit Break goes where PvP Solver aims it; the game answers for the player.
                areaLb ? selfOk && dist <= lbRange : LbOk(b.GameObjectId)));
        }

        var allies = new List<JevAlly>(party.Count);
        foreach (IBattleChara a in party)
        {
            allies.Add(new JevAlly(a.ClassJob.Value.Abbreviation.ToString(), a.GetHealthRatio(), a.DistanceToPlayer(),
                a.Position.X - me.Position.X, a.Position.Z - me.Position.Z,
                hostiles.Count(b => b.TargetObjectId == a.GameObjectId)));
        }

        var self = new JevSelf(me.ClassJob.Value.Abbreviation.ToString(), me.GetHealthRatio(), me.CurrentHp, me.Rotation,
            me.HasStatus(false, StatusID.Heavy_1344, StatusID.Bind_1345), selfOk);
        return JevCall.SnapshotJson(self, enemies, allies);
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
