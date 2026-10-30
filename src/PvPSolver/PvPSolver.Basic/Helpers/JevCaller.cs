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
    private static double _fireUntil = double.MinValue;
    private static ulong? _fireTarget;
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

    /// <summary>How long one Jev "fire" stays live: the rotation only gets some frames to act, so a single frame is not enough.</summary>
    public const double FireWindowSeconds = 1.0;

    /// <summary>The enemy a live Jev Limit Break fire is aimed at (null when none, or the Limit Break has no enemy target).</summary>
    public static ulong? PendingLimitBreakTarget => Now < _fireUntil ? _fireTarget : null;

    /// <summary>
    /// The job's PvP Limit Break is usable as itself (not turned into its follow-up action) on this target, by the game's
    /// own check. Casting and the GCD do not count against it: PvP Solver's action timing handles those. A placed
    /// (ground) Limit Break is checked with no target, as the game does for placed actions.
    /// </summary>
    public static unsafe bool LimitBreakUsable(IBaseAction? limitBreak, ulong targetId)
    {
        ActionManager* manager = ActionManager.Instance();
        if (limitBreak == null || manager == null || limitBreak.Info.AdjustedID != limitBreak.ID)
        {
            return false;
        }

        ulong onWhom = limitBreak.Info.TargetAreaAction ? NoTarget : targetId;
        return manager->GetActionStatus(ActionType.Action, limitBreak.ID, onWhom, checkRecastActive: false, checkCastingActive: false) == 0;
    }

    private const ulong NoTarget = 0xE0000000;

    /// <summary>
    /// Called by the rotation before it picks its next action. A fresh Jev "fire" opens a window of
    /// <see cref="FireWindowSeconds"/>: on every frame of it, while the game says the Limit Break is usable on Jev's
    /// target, it is handed back as the next action (PvP Solver's own timing decides the frame it goes off). The window
    /// closes as soon as the Limit Break is used or unusable, and the next window opens no sooner than
    /// <see cref="JevCall.FireRetrySeconds"/> after it, so a use the game rejects never stalls the rotation.
    /// </summary>
    public static bool TryLimitBreak(IBaseAction? limitBreak, out IAction? act)
    {
        act = null;
        double now = Now;
        if (limitBreak == null || Player.Object == null)
        {
            return false;
        }

        if (now >= _fireUntil)
        {
            if (!JevCall.ShouldFireLimitBreak(Latest, now, _lastFireAttempt, out ulong? target))
            {
                return false;
            }

            _fireUntil = now + FireWindowSeconds;
            _fireTarget = target;
            _lastFireAttempt = _fireUntil; // the retry gap counts from the end of this window
        }

        ulong? aim = _fireTarget;
        if (!LimitBreakUsable(limitBreak, aim ?? Player.Object.GameObjectId))
        {
            _fireUntil = now; // used (it turned into its follow-up, or the gauge emptied) or not usable: close the window
            return false;
        }

        // The relay sends the Limit Break target as the focus too, and FindHostileRaw puts a pending target first.
        if (!limitBreak.CanUse(out act, skipStatusProvideCheck: true, skipTargetStatusNeedCheck: true, usedUp: true,
                skipAoeCheck: true, skipTTKCheck: true))
        {
            act = null;
            return false;
        }

        if (aim != null && !limitBreak.Info.TargetAreaAction && limitBreak.Target.Target?.GameObjectId != aim)
        {
            act = null;
            return false;
        }

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

    private static string? BuildSnapshot()
    {
        IBattleChara? me = Player.Object;
        if (me == null)
        {
            return null;
        }

        IBaseAction? lb = (DataCenter.CurrentRotation as CustomRotation)?.JevLimitBreak;
        bool LbOk(ulong id) => LimitBreakUsable(lb, id);
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
