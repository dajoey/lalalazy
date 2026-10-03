using System;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using GluttonyCombo.API.Enum;
using GluttonyCombo.AutoRotation;
using GluttonyCombo.Combos.PvE;
using GluttonyCombo.Native;
using GluttonyCombo.Services;
using Lalalazy.Hub;

namespace GluttonyCombo;

/// <summary>
///     Fork (lalalazy hub, 2026-10): the quick controls the lalalazy hub window shows for Gluttony Combo, over the
///     shared hub protocol (src/Shared/LalaHub). Kept in its own fork-owned file so upstream WrathCombo merges never
///     touch it; the only edits elsewhere are two calls in the constructor and Dispose.
///
///     NEVER uses leases (RegisterForLease / SetAutoRotationState): every RegisterForLease call creates a new
///     registration on this side (the dedupe compares a display name with an internal name), and duplicates make
///     AllJobsControlled throw every frame. Auto-rotation goes through <see cref="AutoRotationController.ToggleAutoRotation"/>,
///     the same method the server-bar click, /gluttony auto and the utility buttons call. When ANOTHER plugin holds a
///     lease on a setting, that control reports locked and refuses writes (the settings tab disables its checkbox the same way).
///
///     Hotbar buttons are plain hub buttons: invoking one starts <see cref="CustomActionDragService"/> (click the
///     button, then click a hotbar slot). Per-job presets are deliberately not exposed.
/// </summary>
public sealed partial class GluttonyCombo
{
    private LalaHubProvider? _lalaHub;
    private CustomActionDragService? _lalaDrag;

    private static readonly string[] TargetingModes =
    {
        "Manual", "Highest max HP", "Lowest max HP", "Highest current HP",
        "Lowest current HP", "Tank's target", "Nearest", "Furthest",
    };

    private void InstallLazyHub()
    {
        try
        {
            _lalaDrag = new CustomActionDragService();
            _lalaHub = LalaHubProvider.TryCreate(Svc.PluginInterface, Svc.Log, "GluttonyCombo",
                typeof(GluttonyCombo).Assembly.GetName().Version?.ToString() ?? "", DeclareLazyHub);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[LalaHub] Gluttony Combo hub setup failed");
        }
    }

    private void DisposeLazyHub()
    {
        // Unregister first: a provider must never outlive its plugin.
        try { _lalaHub?.Dispose(); } catch (Exception ex) { Svc.Log.Warning(ex, "[LalaHub] dispose failed"); }
        _lalaHub = null;
        try { _lalaDrag?.Dispose(); } catch (Exception ex) { Svc.Log.Warning(ex, "[LalaHub] drag service dispose failed"); }
        _lalaDrag = null;
    }

    private ControlState LazyHubAutoRotationLock()
    {
        var c = UIHelper?.AutoRotationStateControlled();
        return c is { } v && !string.IsNullOrEmpty(v.controllers)
            ? new ControlState(Locked: true, Why: "Controlled by " + v.controllers + ".")
            : new ControlState();
    }

    private ControlState LazyHubOptionLock(string option)
    {
        var c = UIHelper?.AutoRotationConfigControlled(option);
        return c is { } v && !string.IsNullOrEmpty(v.controllers)
            ? new ControlState(Locked: true, Why: "Controlled by " + v.controllers + ".")
            : new ControlState();
    }

    private void DeclareLazyHub(HubEndpoint ep)
    {
        static AutoRotationConfig Rot() => Service.Configuration.RotationConfig;
        static void Save() => Service.Configuration.Save();

        // What auto-rotation actually uses right now: another plugin's lease wins over the stored setting (the settings
        // tab shows the same value), so a locked control shows the leased value, not a stale stored one.
        static AutoRotationConfigIPCWrapper Eff() => new(Rot());

        const string rotation = "Auto-Rotation";
        const string targeting = "Targeting";
        const string behavior = "Behavior";
        const string hotbar = "Hotbar buttons";

        ep.Toggle("auto_rotation", "Auto-Rotation",
            () => UIHelper?.AutoRotationStateControlled() is { } v && !string.IsNullOrEmpty(v.controllers) ? v.state : Rot().Enabled,
            v => { AutoRotationController.ToggleAutoRotation(v); },
            group: rotation, master: true, tip: "Same switch as the server bar and /gluttony auto.",
            state: LazyHubAutoRotationLock);
        ep.Toggle("in_combat_only", "Only in combat", () => Eff().InCombatOnly,
            v => { Rot().InCombatOnly = v; Save(); }, group: rotation, state: () => LazyHubOptionLock("InCombatOnly"));
        ep.Toggle("fate_priority", "FATE priority", () => Eff().DPSSettings.FATEPriority,
            v => { Rot().DPSSettings.FATEPriority = v; Save(); }, group: rotation, state: () => LazyHubOptionLock("FATEPriority"));
        ep.Toggle("quest_priority", "Quest priority", () => Eff().DPSSettings.QuestPriority,
            v => { Rot().DPSSettings.QuestPriority = v; Save(); }, group: rotation, state: () => LazyHubOptionLock("QuestPriority"));

        ep.Choice("dps_target", "DPS targeting mode", TargetingModes, () => (int)Eff().DPSRotationMode,
            i => { Rot().DPSRotationMode = (DPSRotationMode)Math.Max(0, Math.Min(TargetingModes.Length - 1, i)); Save(); },
            group: targeting, state: () => LazyHubOptionLock("DPSRotationMode"));
        ep.Toggle("boss_mod_targeting", "Use boss-mod targeting when active", () => Rot().DPSSettings.UseBossModTargeting,
            v => { Rot().DPSSettings.UseBossModTargeting = v; Save(); }, group: targeting,
            tip: "Only has an effect while BossMod Reborn has an active fight module.");
        // In Gluttony a null target count means "AoE off" (a checkbox in its settings that is not exposed here), so there is no
        // number to show or change while it is off.
        ep.Stepper("aoe_targets", "AoE needs at least", min: 0, max: 8, step: 1, get: () => Eff().DPSSettings.DPSAoETargets ?? 0,
            set: v => { Rot().DPSSettings.DPSAoETargets = (int)v; Save(); }, unit: " targets", group: targeting,
            state: () =>
            {
                var lk = LazyHubOptionLock("DPSAoETargets");
                if (lk.Locked) return lk;
                return Rot().DPSSettings.DPSAoETargets == null
                    ? new ControlState(Enabled: false, Why: "AoE is switched off in Gluttony's settings.")
                    : lk;
            });
        ep.Stepper("max_distance", "Max target distance", min: 1, max: 30, step: 1, get: () => Rot().DPSSettings.MaxDistance,
            set: v => { Rot().DPSSettings.MaxDistance = (float)Math.Max(1, Math.Min(30, v)); Save(); }, unit: " y", group: targeting);

        // These two are top-level settings: go through Setting, exactly as the settings window does.
        ep.Toggle("block_move", "Block spells while moving", () => Service.Configuration.BlockSpellOnMove,
            v => { new global::GluttonyCombo.Window.Functions.Setting("BlockSpellOnMove").Value = v; }, group: behavior);
        ep.Toggle("quiet_chat", "Hide the Auto-Rotation chat message", () => Service.Configuration.SuppressAutorotCommand,
            v => { new global::GluttonyCombo.Window.Functions.Setting("SuppressAutorotCommand").Value = v; }, group: behavior);

        ControlState CanPick() => Player.Available && _lalaDrag != null
            ? new ControlState()
            : new ControlState(Enabled: false, Why: "Available once the character is logged in.");
        void Pick(string id, string label, uint actionId)
            => ep.Button(id, label, () => _lalaDrag?.Begin(actionId) ?? SetOutcome.Refuse("Not ready."), group: hotbar,
                tip: "Click it, then click a hotbar slot to place it. Escape cancels.", state: CanPick);

        Pick("pick_st_dps", "Single Target DPS", All.SingleTargetDPS);
        Pick("pick_aoe_dps", "AoE DPS", All.AoEDPS);
        Pick("pick_st_heals", "Single Target Heals", All.SingleTargetHeals);
        Pick("pick_aoe_heals", "AoE Heals", All.AoeHeals);
        Pick("pick_auto_on", "Auto-Rotation On", All.AutoOn);
        Pick("pick_auto_off", "Auto-Rotation Off", All.AutoOff);
        Pick("pick_auto_toggle", "Auto-Rotation Toggle", All.AutoToggle);
    }
}
