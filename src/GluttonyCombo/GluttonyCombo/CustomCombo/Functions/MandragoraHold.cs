using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System;
using System.Linq;
using GluttonyCombo.AutoRotation;
using static GluttonyCombo.Data.ActionWatching;

namespace GluttonyCombo.CustomComboNS.Functions;

internal abstract partial class CustomComboFunctions
{
    // Fork (1.0.4.325): the treasure-dungeon mandragora AoE hold. Decision core:
    // AutoRotation/MandragoraHold.cs (offline harness tests/GluttonyCombo.MandragoraHoldHarness).

    private const long MandragoraHoldCacheMs = 100;
    private static long _mandragoraHoldReadAt = long.MinValue;
    private static bool _mandragoraHoldActive;
    private static bool _mandragoraHoldLogged;

    /// <summary>
    ///     True while any of a treasure dungeon's ordered mandragora pack is alive in the object
    ///     table. Read at most every 100 ms: readiness checks call it many times per frame.
    /// </summary>
    public static bool MandragoraHoldActive
    {
        get
        {
            var now = Environment.TickCount64;
            if (now - _mandragoraHoldReadAt < MandragoraHoldCacheMs)
                return _mandragoraHoldActive;

            _mandragoraHoldReadAt = now;
            bool active;
            try
            {
                active = MandragoraHold.IsHolding(Svc.Objects
                    .OfType<IBattleNpc>()
                    .Select(x => (x.BaseId, x.NameId, x.IsDead)));
            }
            catch (Exception)
            {
                active = false;
            }

            if (active != _mandragoraHoldLogged)
            {
                _mandragoraHoldLogged = active;
                Svc.Log.Information(active
                    ? "[MandragoraHold] mandragora pack alive: multi-target actions held"
                    : "[MandragoraHold] mandragora pack dead: multi-target actions released");
            }

            _mandragoraHoldActive = active;
            return active;
        }
    }

    /// <summary> Whether the action can hit more than one enemy (Action sheet shape and hostile-effect flag). </summary>
    internal static bool ActionHitsSeveral(uint actionId) =>
        ActionSheet.TryGetValue(actionId, out var row)
        && MandragoraHold.HitsSeveral(actionId, row.CastType, row.EffectRange, row.Unknown14);

    /// <summary> Whether the mandragora hold forbids this action right now. </summary>
    public static bool MandragoraHoldBlocks(uint actionId) =>
        MandragoraHoldActive && ActionHitsSeveral(actionId);

    /// <summary>
    ///     Applies the hold to an action a rotation resolved for <paramref name="button"/>.
    ///     <paramref name="fallback"/> is the button's own action (the job's single-target
    ///     action) when the outcome is <see cref="MandragoraHold.Outcome.Fallback"/>.
    /// </summary>
    internal static MandragoraHold.Outcome MandragoraHoldResolve(uint resolved, uint button, out uint fallback)
    {
        fallback = 0;
        if (!MandragoraHoldActive || !ActionHitsSeveral(resolved))
            return MandragoraHold.Outcome.Keep;

        fallback = OriginalHook(button);
        return MandragoraHold.Decide(
            holding: true,
            resolvedHitsSeveral: true,
            resolvedIsAbility: GetAttackType(resolved) == ActionAttackType.Ability,
            fallbackHitsSeveral: ActionHitsSeveral(fallback),
            fallbackIsResolved: fallback == resolved);
    }
}
