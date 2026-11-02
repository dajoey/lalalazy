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
    private static long _mandragoraHoldReadAt;
    private static bool _mandragoraHoldHasRead;
    private static bool _mandragoraHoldActive;

    /// <summary>
    ///     True while any of a treasure dungeon's ordered mandragora pack is alive in the object
    ///     table. Read at most every 100 ms: readiness checks call it many times per frame.
    /// </summary>
    public static bool MandragoraHoldActive
    {
        get
        {
            var now = Environment.TickCount64;
            if (MandragoraHold.CacheFresh(now, _mandragoraHoldReadAt, _mandragoraHoldHasRead, MandragoraHoldCacheMs))
                return _mandragoraHoldActive;

            _mandragoraHoldReadAt = now;
            _mandragoraHoldHasRead = true;

            bool active;
            try
            {
                active = MandragoraHold.IsHolding(Svc.Objects
                    .OfType<IBattleNpc>()
                    .Select(x => (x.BaseId, x.NameId, x.IsDead)));
            }
            catch (Exception)
            {
                // Keep the last reading: a failed read must neither start nor end a hold.
                return _mandragoraHoldActive;
            }

            if (active != _mandragoraHoldActive)
                Svc.Log.Information(active
                    ? "[MandragoraHold] mandragora pack alive: multi-target actions held"
                    : "[MandragoraHold] mandragora pack dead: multi-target actions released");

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
    ///     <paramref name="fallback"/> is the single-target action to use instead (the button's
    ///     current form, else its base action), 0 when there is none.
    /// </summary>
    internal static MandragoraHold.Outcome MandragoraHoldResolve(uint resolved, uint button, out uint fallback)
    {
        fallback = 0;
        if (!MandragoraHoldActive || !ActionHitsSeveral(resolved))
            return MandragoraHold.Outcome.Keep;

        var hooked = OriginalHook(button);
        fallback = MandragoraHold.PickFallback(resolved, hooked, ActionHitsSeveral(hooked), button, ActionHitsSeveral(button));
        return MandragoraHold.Decide(
            holding: true,
            resolvedHitsSeveral: true,
            resolvedIsAbility: GetAttackType(resolved) == ActionAttackType.Ability,
            hasFallback: fallback != 0);
    }

    /// <summary>
    ///     What the action-replacer hook hands the game for <paramref name="button"/> while the
    ///     hold may be on: the resolved action, or its single-target fallback when the resolved
    ///     one is held (a held weave also shows the fallback: the press queues the GCD). With no
    ///     single-target fallback (an AoE button) the resolved action stays: a hand press of an
    ///     AoE button is the player's call.
    /// </summary>
    internal static uint MandragoraHoldForButton(uint resolved, uint button) =>
        MandragoraHoldResolve(resolved, button, out var fallback) is not MandragoraHold.Outcome.Keep && fallback != 0
            ? fallback
            : resolved;
}
