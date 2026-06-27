using ECommons.EzIpcManager;

namespace LazyFateAutomation.Helpers.IPC;

#nullable disable
#pragma warning disable CS8632
// IPC subscriber for Gluttony Combo (lalalazy fork of Wrath Combo).
// Hands combat rotation + target selection to Gluttony Combo's lease-based Auto-Rotation,
// leaving BossMod to handle only movement and danger avoidance.
// Modeled on GluttonyCombo/docs/IPCExample.cs. IPC return values are taken as `object`
// (a boxed SetResult) to avoid cross-plugin enum unboxing; codes are read via Convert.ToInt32.
//
// CRITICAL: register the lease EXACTLY ONCE and never churn. Gluttony's CreateRegistration
// dedups on `PluginName == internalPluginName`, but PluginName stores the *display* name, so
// the dedup never matches and every RegisterForLease call creates a NEW registration. Two
// registrations with the same display name make Gluttony's AllJobsControlled ToDictionary throw
// every frame (dead FPS + broken settings UI). So: only register when we hold no lease, throttle
// registration attempts, and NEVER drop the lease on a transient error - only when Gluttony
// itself reports the lease invalid (by then it is already removed, so re-acquiring cannot dup).
[Ipc(Ipc.GluttonyCombo)]
public class GluttonyComboIPC : BaseIPC {
    public override string Name => "GluttonyCombo";
    public override string Repo => Main;
    public GluttonyComboIPC() => EzIPC.Init(this, Name);

    // Raw IPC (prefix "GluttonyCombo")
    [EzIPC] public readonly Func<string, string, Guid?> RegisterForLease;
    [EzIPC] public readonly Func<Guid, bool, object> SetAutoRotationState;
    [EzIPC] public readonly Func<Guid, object> SetCurrentJobAutoRotationReady;
    [EzIPC] public readonly Func<Guid, object, object, object> SetAutoRotationConfigState;
    [EzIPC] public readonly Func<bool> GetAutoRotationState;
    [EzIPC] public readonly Action<Guid> ReleaseControl;

    // SetResult codes (GluttonyCombo.Services.IPC.Enums.SetResult)
    private const int SetResultInvalidLease = 11;

    // AutoRotationConfigOption codes (GluttonyCombo.API.Enum.AutoRotationConfigOption)
    private const int CfgInCombatOnly = 0;
    private const int CfgDPSRotationMode = 1;
    private const int CfgFATEPriority = 3;
    private const int CfgOnlyAttackInCombat = 13;
    private const int CfgDPSAoETargets = 16;
    private const int CfgDPSAlwaysHardTarget = 19;
    private const int CfgBypassFATE = 22;

    // DPSRotationMode values (GluttonyCombo.API.Enum.DPSRotationMode)
    private const int DpsNearest = 6;

    private Guid? _lease;
    private bool _configured;
    private bool _autoOn;
    private uint _readyJob;
    private long _nextRegisterMs; // throttle so RegisterForLease can never be spammed

    private static int ToInt(object o) => o is null ? 0 : Convert.ToInt32(o);

    private bool EnsureLease() {
        if (!IsLoaded)
            return false;
        if (_lease is not null)
            return true;
        // Register at most once every 10s, and only when we hold no lease.
        if (Environment.TickCount64 < _nextRegisterMs)
            return false;
        _nextRegisterMs = Environment.TickCount64 + 10_000;
        try {
            _lease = RegisterForLease("LazyFateAutomation", Plugin.Name);
        }
        catch (Exception ex) {
            Svc.Log.PrintWarning($"Gluttony Combo: RegisterForLease failed: {ex.Message}");
            _lease = null;
            return false;
        }
        if (_lease is null) {
            Svc.Log.PrintWarning("Gluttony Combo: RegisterForLease returned null (lease busy, revoked, or IPC disabled).");
            return false;
        }
        _configured = false;
        _autoOn = false;
        _readyJob = 0;
        return true;
    }

    private void ApplyConfig(Guid lease) {
        // Gluttony owns combat targeting for FATE grinding; BossMod only moves + dodges.
        SetAutoRotationConfigState(lease, CfgDPSRotationMode, DpsNearest);  // auto-acquire nearest enemy
        SetAutoRotationConfigState(lease, CfgFATEPriority, true);           // prefer mobs in the current FATE
        SetAutoRotationConfigState(lease, CfgDPSAlwaysHardTarget, true);    // hard-target so BossMod movement follows
        SetAutoRotationConfigState(lease, CfgDPSAoETargets, 3);             // switch to AoE when 3+ are in range
        SetAutoRotationConfigState(lease, CfgInCombatOnly, false);          // engage FATE mobs before we are in combat
        SetAutoRotationConfigState(lease, CfgBypassFATE, true);            // bypass the in-combat-only gate inside a FATE
        SetAutoRotationConfigState(lease, CfgOnlyAttackInCombat, false);
    }

    /// <summary>Enable Gluttony Combo Auto-Rotation for the current job. Safe to call every frame; only state transitions hit IPC.</summary>
    public void Enable() {
        if (!EnsureLease())
            return;
        var lease = _lease!.Value;
        try {
            if (!_configured) {
                ApplyConfig(lease);
                _configured = true;
            }

            var job = Player.Available ? (uint)Player.Job : 0u;
            if (job != 0 && job != _readyJob) {
                SetCurrentJobAutoRotationReady(lease);
                _readyJob = job;
            }

            if (!_autoOn) {
                var code = ToInt(SetAutoRotationState(lease, true));
                if (code == SetResultInvalidLease) {
                    // Gluttony already dropped this lease; clear our handle so a throttled re-acquire
                    // can happen. No duplicate risk - an invalid lease is no longer registered.
                    _lease = null;
                    _configured = false;
                    _autoOn = false;
                    _readyJob = 0;
                    return;
                }
                _autoOn = true;
            }
        }
        catch (Exception ex) {
            // Transient IPC hiccup - KEEP the lease. Re-registering here is what corrupted Gluttony.
            Svc.Log.PrintWarning($"Gluttony Combo: Enable failed: {ex.Message}");
        }
    }

    /// <summary>Disable Gluttony Combo Auto-Rotation (between FATEs / out of combat). Keeps the lease.</summary>
    public void Disable() {
        if (!_autoOn)
            return;
        _autoOn = false;
        if (!IsLoaded || _lease is not { } lease)
            return;
        try {
            SetAutoRotationState(lease, false);
        }
        catch (Exception ex) {
            Svc.Log.PrintWarning($"Gluttony Combo: Disable failed: {ex.Message}");
        }
    }

    /// <summary>Release our lease entirely (plugin shutdown). Frees the registration on Gluttony's side.</summary>
    public void Release() {
        if (_lease is { } lease) {
            try {
                if (IsLoaded)
                    ReleaseControl(lease);
            }
            catch {
                // ignore
            }
        }
        _lease = null;
        _configured = false;
        _autoOn = false;
        _readyJob = 0;
        _nextRegisterMs = 0;
    }
}
