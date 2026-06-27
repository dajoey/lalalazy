using ECommons.EzIpcManager;

namespace LazyFateAutomation.Helpers.IPC;

#nullable disable
#pragma warning disable CS8632
// IPC subscriber for Gluttony Combo (lalalazy fork of Wrath Combo).
// Hands combat rotation + target selection to Gluttony Combo's lease-based Auto-Rotation,
// leaving BossMod to handle only movement and danger avoidance.
// Modeled on GluttonyCombo/docs/IPCExample.cs. IPC return values are taken as `object`
// (a boxed SetResult) to avoid cross-plugin enum unboxing; codes are read via Convert.ToInt32.
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
    private const int SetResultIPCDisabled = 10;
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

    private bool EnsureLease() {
        if (!IsLoaded)
            return false;
        if (_lease is not null)
            return true;
        try {
            _lease = RegisterForLease("LazyFateAutomation", Plugin.Name);
            if (_lease is null)
                Svc.Log.PrintWarning("Gluttony Combo: RegisterForLease returned null (lease busy, revoked, or IPC disabled).");
        }
        catch (Exception ex) {
            Svc.Log.PrintWarning($"Gluttony Combo: RegisterForLease failed: {ex.Message}");
            _lease = null;
        }
        return _lease is not null;
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
                var result = SetAutoRotationState(lease, true);
                var code = result is null ? 0 : Convert.ToInt32(result);
                if (code is SetResultInvalidLease or SetResultIPCDisabled) {
                    Reset();
                    return;
                }
                _autoOn = true;
            }
        }
        catch (Exception ex) {
            Svc.Log.PrintWarning($"Gluttony Combo: Enable failed: {ex.Message}");
            Reset();
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

    /// <summary>Release our lease entirely (plugin shutdown).</summary>
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
        Reset();
    }

    private void Reset() {
        _lease = null;
        _configured = false;
        _autoOn = false;
        _readyJob = 0;
    }
}
