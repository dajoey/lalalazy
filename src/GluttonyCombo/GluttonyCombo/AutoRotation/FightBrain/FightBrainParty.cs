// Fork (Fight Brain, code rules for party data — card 609a863c, Part B): the shadow rules
// that read what the plugin can already see exactly and log what a position-aware rule
// would have done, beside what actually happened. LOG ONLY: nothing here presses an
// action or moves anyone.
//   FB|party-mit   which party member used which mitigation and when (status-gain diff),
//                  so our own mitigation choice (FB|mitigate) can be judged against overlap
//   FB|pos-zone    while a forecast zone is active or incoming: party spread, how many
//                  members stand in an unsafe position, and what a spread rule would do
// Member labels are job + slot index only — player names are never logged.

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using GluttonyCombo.Core;
using GluttonyCombo.Extensions;
using GluttonyCombo.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GluttonyCombo.AutoRotation;

internal static class FightBrainParty
{
    private static FightBrainSettings Cfg => Service.Configuration.RotationConfig.FightBrain;

    /// <summary> Party-wide mitigations this tally watches, by status name (English sheet names). </summary>
    private static readonly HashSet<string> MitNames = new(StringComparer.Ordinal)
    {
        "Rampart", "Reprisal", "Feint", "Addle", "Arm's Length", "Vengeance",
        "Shield Samba", "Troubadour", "Warden's Paean", "Nature's Minne",
        "Nebula", "Great Nebula", "Camouflage", "Heart of Light", "Dark Missionary",
        "Divine Veil", "Shake It Off", "Mantra", "Riddle of Earth",
        "Temperance", "Divine Aura", "Liturgy of the Bell", "Plenary Indulgence",
        "Neutral Sect", "Collective Unconscious", "Horoscope",
        "Expedient", "Sacred Soil", "Panhoisma", "Kerachole", "Ixochole", "Holos", "Pneuma",
    };

    /// <summary> Per member: the status ids already seen, so only gains log. Keyed by member index. </summary>
    private static readonly Dictionary<int, HashSet<uint>> Seen = new();
    private static readonly Dictionary<int, string> Labels = new();

    private static long _nextMitMs;
    private static long _nextPosMs;
    private static long _lastPosKey = -1;

    /// <summary> Called from the Fight Brain tick, after the forecast read. </summary>
    internal static void Tick(long nowMs)
    {
        // Party Shadow governs both party-data line families (party-mit tally and the
        // position lines) - 1.0.4.330: before the switch existed in the window the tally
        // ran whenever the Fight Brain ran, which contradicted the setting's description.
        if (!Cfg.Enabled || !Cfg.PartyShadow)
            return;
        if (nowMs >= _nextMitMs)
        {
            _nextMitMs = nowMs + 500;
            PartyMitigationDiff();
        }
        if (nowMs >= _nextPosMs)
        {
            _nextPosMs = nowMs + 2000;
            PositionShadow();
        }
    }

    // ------------------------------------------------------------------ party mitigation

    private static void PartyMitigationDiff()
    {
        try
        {
            if (Svc.Party is not { } party || party.Count == 0)
                return;
            var seenIds = new HashSet<int>();
            for (var i = 0; i < party.Count; i++)
            {
                if (party[i].GameObject is not IBattleChara a)
                    continue;
                seenIds.Add((int)a.GameObjectId);
                var label = MemberLabel(a, i);
                if (!Seen.TryGetValue(i, out var known))
                    Seen[i] = known = [];
                var current = new HashSet<uint>();
                var statuses = a.SafeStatusList;
                if (statuses != null)
                {
                    foreach (var status in statuses)
                    {
                        if (status.StatusId == 0)
                            continue;
                        current.Add(status.StatusId);
                        if (!known.Add(status.StatusId))
                            continue;
                        // A new status on this member: log it only when it is a watched mitigation.
                        var name = StatusName(status.StatusId);
                        if (name is null || !MitNames.Contains(name))
                            continue;
                        FightBrain.Log($"party-mit member={label} status={name} ends_in={status.RemainingTime:F0}s");
                    }
                }
                // Statuses that ended drop out of the seen set so a re-use logs again.
                known.RemoveWhere(id => !current.Contains(id));
            }
            // Members that left: forget their sets.
            foreach (var gone in Seen.Keys.Where(k => !seenIds.Contains(k)).ToList())
                Seen.Remove(gone);
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] party-mit diff failed: {e.Message}");
        }
    }

    private static string MemberLabel(IBattleChara a, int index)
    {
        try
        {
            var job = a.ClassJob.Value.Abbreviation.ToString();
            return $"{job}{index}";
        }
        catch
        {
            return $"m{index}";
        }
    }

    private static string? StatusName(uint statusId)
    {
        try
        {
            return Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>()?.GetRow(statusId).Name.ToString();
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ position shadow

    /// <summary>
    ///     While a forecast zone is under or near the player, log the party spread and what a
    ///     position-aware rule would do: move the members standing inside an unsafe position,
    ///     toward the party centroid if they are stacked closer than 3 yalms. One line per
    ///     zone activation, refreshed at most every 2 s.
    /// </summary>
    private static void PositionShadow()
    {
        try
        {
            var me = Player.Object;
            if (me is null || Svc.Party is not { } party || party.Count == 0)
                return;
            var (zoneIn, standingInZone) = FightBrainForecast.ZoneUnderPlayer();
            if (zoneIn is null && !standingInZone)
                return;
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var key = (long)Math.Round((zoneIn ?? 0) * 2); // bucket halves a second; new zone = new line
            if (key == _lastPosKey)
                return;
            _lastPosKey = key;

            var members = new List<(string Label, System.Numerics.Vector3 Pos)>();
            for (var i = 0; i < party.Count; i++)
                if (party[i].GameObject is IBattleChara a)
                    members.Add((MemberLabel(a, i), a.Position));

            var spread = 0f;
            var unsafeCount = 0;
            foreach (var m in members)
            {
                if (FightBrainForecast.IsPositionUnsafe(m.Pos) == true)
                    unsafeCount++;
                foreach (var other in members)
                    if (other.Pos != m.Pos)
                        spread = Math.Max(spread, VectorDistance(m.Pos, other.Pos));
            }
            FightBrain.Log($"pos-zone zone_in={(zoneIn ?? 0):F1} standing={standingInZone} spread={spread:F0}y unsafe={unsafeCount} would_move={unsafeCount} rule=spread");
        }
        catch (Exception e)
        {
            PluginLog.Debug($"[FightBrain] pos-zone failed: {e.Message}");
        }
    }

    private static float VectorDistance(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
