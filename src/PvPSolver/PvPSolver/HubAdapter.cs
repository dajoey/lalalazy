using System;
using Lalalazy.Hub;
using RotationSolver.Basic;
using RotationSolver.Basic.Configuration;
using RotationSolver.Basic.Data;
using RotationSolver.Commands;

namespace RotationSolver;

/// <summary>
/// Quick controls for the lalalazy hub window (src/Shared/LalaHub).
///
/// The master is the RUNTIME state (DataCenter.State, never serialized), flipped through the same method the
/// server-bar click and /pvpsolver auto use: RSCommands.DoStateCommandType. Never the IPC ChangeOperatingMode: it
/// skips the PvP-only guard. DoStateCommandType(Off) while the state is off would turn it ON in a PvP zone, so Off
/// is only ever sent when the state is on. Turning on outside a PvP zone is refused (it is silently ignored by
/// the command itself), and the control shows disabled there.
///
/// Settings are ConditionBoolean values written through <c>.Value</c> exactly like the settings window. The
/// config object is re-fetched on every call because a reset or restore replaces it wholesale. The settings
/// window saves only on close, so each change saves here (one file write per click, from the main thread).
/// Left out on purpose: per-job settings, state types other than PvP, reset/backup/restore, and the action commands.
/// </summary>
internal static class HubAdapter
{
    private static void Save()
    {
        try { Service.Config.Save(); }
        catch (Exception ex) { ECommons.Logging.PluginLog.Warning($"[LalaHub] PvP Solver config save failed: {ex.Message}"); }
    }

    public static void Declare(HubEndpoint ep)
    {
        const string pvp = "PvP";
        const string purify = "Purify";

        ep.Toggle("state", "PvP Solver", () => DataCenter.State,
            v =>
            {
                if (v == DataCenter.State) return SetOutcome.Success;
                if (v && !DataCenter.IsPvP) return SetOutcome.Refuse("PvP Solver only turns on in a PvP zone.");
                RSCommands.DoStateCommandType(v ? StateCommandType.PvP : StateCommandType.Off);

                // DoStateCommandType returns silently when there is no player object (a zone change), and UpdateState sets
                // DataCenter.State before it returns, so a state that did not move means nothing happened.
                return DataCenter.State == v ? SetOutcome.Success : SetOutcome.Refuse("Could not change PvP Solver right now.");
            },
            group: pvp, master: true, tip: "Turns on only in a PvP zone. It can be turned off anywhere.",
            state: () => DataCenter.State || DataCenter.IsPvP
                ? new ControlState()
                : new ControlState(Enabled: false, Why: "Only available in a PvP zone."));

        void T(string id, string label, string group, Func<ConditionBoolean> setting, string tip = "")
            => ep.Toggle(id, label, () => setting().Value, v => { setting().Value = v; Save(); }, group: group, tip: tip);

        T("auto_on_match_start", "Turn on when a match starts", pvp, () => Service.Config.AutoOnPvPMatchStart);
        T("auto_off_match_end", "Turn off when a match ends", pvp, () => Service.Config.AutoOffPvPMatchEnd);
        T("guard_control", "Use no actions while in Guard", pvp, () => Service.Config.PvpGuardControl);
        T("allow_sprint", "Allow Sprint with no target", pvp, () => Service.Config.PvpAllowSprintWithoutTarget,
            tip: "Experimental.");
        ep.Stepper("guard_hp", "Use Guard below", min: 0, max: 100, step: 5,
            get: () => Math.Round(Service.Config.HealthForGuard * 100),
            set: v => { Service.Config.HealthForGuard = (float)(v / 100.0); Save(); },
            unit: "% HP", group: pvp);

        T("purify_stun", "Purify Stun", purify, () => Service.Config.PvpPurifyStun);
        T("purify_silence", "Purify Silence", purify, () => Service.Config.PvpPurifySilence);
        T("purify_heavy", "Purify Heavy", purify, () => Service.Config.PvpPurifyHeavy);
        T("purify_bind", "Purify Bind", purify, () => Service.Config.PvpPurifyBind);
    }
}
