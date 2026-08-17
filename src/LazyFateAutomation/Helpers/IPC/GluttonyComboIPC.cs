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
    private long _nextWarnMs;     // throttle warnings - Enable() runs every frame

    private static int ToInt(object o) => o is null ? 0 : Convert.ToInt32(o);

    private void Warn(string message) {
        if (Environment.TickCount64 < _nextWarnMs)
            return;
        _nextWarnMs = Environment.TickCount64 + 10_000;
        Svc.Log.PrintWarning($"Gluttony Combo: {message}");
    }

    /// <summary>Forget the lease handle (Gluttony has already removed it) so a throttled re-acquire can happen.</summary>
    private void ForgetLease() {
        _lease = null;
        _configured = false;
        _autoOn = false;
        _readyJob = 0;
    }

    /// <summary>True (and forgets the lease) when Gluttony reports our lease is no longer valid.</summary>
    private bool LeaseInvalid(object result) {
        if (ToInt(result) != SetResultInvalidLease)
            return false;
        // No duplicate risk - an invalid lease is no longer registered on Gluttony's side.
        ForgetLease();
        return true;
    }

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
            Warn($"RegisterForLease failed: {ex.Message}");
            _lease = null;
            return false;
        }
        if (_lease is null) {
            Warn("RegisterForLease returned null (lease busy, revoked, or IPC disabled).");
            return false;
        }
        _configured = false;
        _autoOn = false;
        _readyJob = 0;
        return true;
    }

    /// <summary>Applies the FATE-grinding config overlay. Returns false if the lease turned out to be invalid.</summary>
    private bool ApplyConfig(Guid lease) {
        // Gluttony owns combat targeting for FATE grinding; BossMod only moves + dodges.
        (int option, object value)[] overlay = [
            (CfgDPSRotationMode, DpsNearest),   // auto-acquire nearest enemy
            (CfgFATEPriority, true),            // prefer mobs in the current FATE
            (CfgDPSAlwaysHardTarget, true),     // hard-target so BossMod movement follows
            (CfgDPSAoETargets, 3),              // switch to AoE when 3+ are in range
            (CfgInCombatOnly, false),           // engage FATE mobs before we are in combat
            (CfgBypassFATE, true),              // bypass the in-combat-only gate inside a FATE
            (CfgOnlyAttackInCombat, false),
        ];
        foreach (var (option, value) in overlay) {
            if (LeaseInvalid(SetAutoRotationConfigState(lease, option, value)))
                return false;
        }
        return true;
    }

    /// <summary>Enable Gluttony Combo Auto-Rotation for the current job. Safe to call every frame; only state transitions hit IPC.</summary>
    public void Enable() {
        if (!EnsureLease())
            return;
        var lease = _lease!.Value;
        try {
            // ORDER MATTERS: turn Auto-Rotation on BEFORE registering any config. Gluttony's
            // AddRegistrationForAutoRotation (Leasing.cs) checked `AutoRotationConfigsControlled.Count > 0`
            // and then indexed `AutoRotationControlled[0]` unguarded, so a lease that registered configs
            // first threw KeyNotFoundException on every SetAutoRotationState call - LFA never actually
            // controlled Auto-Rotation and logged "Enable failed" every frame (2026-08-17). Fixed in
            // Gluttony 1.0.4.142; this ordering keeps us working against older builds too.
            if (!_autoOn) {
                if (LeaseInvalid(SetAutoRotationState(lease, true)))
                    return;
                _autoOn = true;
            }

            if (!_configured) {
                if (!ApplyConfig(lease))
                    return;
                _configured = true;
            }

            var job = Player.Available ? (uint)Player.Job : 0u;
            if (job != 0 && job != _readyJob) {
                if (LeaseInvalid(SetCurrentJobAutoRotationReady(lease)))
                    return;
                _readyJob = job;
            }
        }
        catch (Exception ex) {
            // Transient IPC hiccup - KEEP the lease. Re-registering here is what corrupted Gluttony.
            Warn($"Enable failed: {ex.Message}");
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
            LeaseInvalid(SetAutoRotationState(lease, false));
        }
        catch (Exception ex) {
            Warn($"Disable failed: {ex.Message}");
        }
    }

    /// <summary>Whether we currently hold a lease handle (config overlay + auto-rotation control may be live).</summary>
    public bool HasLease => _lease is not null;

    /// <summary>
    /// Release our lease entirely (plugin shutdown, Stop, or entering a duty). Frees the registration on
    /// Gluttony's side, which drops the whole config overlay and hands Auto-Rotation control back to the
    /// user's own settings. The next Enable() re-registers from scratch.
    /// </summary>
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
