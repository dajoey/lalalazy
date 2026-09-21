#region Dependencies

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lalalazy.Telemetry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

#endregion

namespace GluttonyCombo.Combos.PvE;

// Crucible of the Unbroken, LIVE helpers beyond the rotation read (BST_Crucible.cs): auto-targeting filter,
// the captured-familiar roster for the beast-pick advisor, and a one-time capture of the Crucible screens'
// values (their layouts are not mapped in ClientStructs yet).
internal partial class BST
{
    // ------------------------------------------------------------------ auto-targeting

    /// <summary> Auto-rotation DPS targeting consults <see cref="RestrictCrucibleTargets"/> while this holds. </summary>
    internal static bool CrucibleTargetingActive =>
        Player.Job is Job.BST && Config.BST_Crucible && Config.BST_CrucibleTargeting
        && BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType) != 0;

    /// <summary> BST_CrucibleLogic.AllowedTargets over live candidates: no eggs / morphos, stances last, priority adds, pairs balanced. </summary>
    internal static List<IBattleChara> RestrictCrucibleTargets(List<IBattleChara> targets)
    {
        if (targets.Count == 0)
            return targets;

        var candidates = new List<BST_CrucibleLogic.TargetCandidate>(targets.Count);
        foreach (var t in targets)
        {
            var inStance = false;
            foreach (var status in t.StatusList)
            {
                if (BST_CrucibleData.StanceStatuses.Contains(status.StatusId))
                {
                    inStance = true;
                    break;
                }
            }
            candidates.Add(new(t.NameId, t.MaxHp == 0 ? 0f : 100f * t.CurrentHp / t.MaxHp, inStance));
        }

        var allowed = BST_CrucibleLogic.AllowedTargets(candidates);
        var result = new List<IBattleChara>(allowed.Count);
        foreach (var i in allowed)
            result.Add(targets[i]);
        return result;
    }

    // ------------------------------------------------------------------ roster

    /// <summary> Whether the game has sent the captured-familiar list (it arrives with the Master's Bestiary). </summary>
    internal static unsafe bool CrucibleRosterLoaded
    {
        get
        {
            var manager = XBMManager.Instance();
            return manager is not null && manager->State == XBMManager.DataState.Received;
        }
    }

    /// <summary> Captured familiar check for the advisor; every beast counts as captured while the list is not loaded. </summary>
    internal static unsafe bool CrucibleBeastCaptured(int row)
    {
        var manager = XBMManager.Instance();
        if (manager is null || manager->State != XBMManager.DataState.Received)
            return true;
        return manager->IsPetUnlocked((uint)row);
    }

    // ------------------------------------------------------------------ trends

    private static readonly Queue<(long Ms, uint Hp)> PlayerHpSamples = new();
    private static readonly Queue<(long Ms, float Hp)> TargetHpSamples = new();
    private static ulong _ttdTarget;

    /// <summary> HP the character lost per second over the last 10 s (losses only; heals do not offset). </summary>
    private static float TrackIntake(long nowMs, uint hp)
    {
        if (PlayerHpSamples.Count == 0 || nowMs - PlayerHpSamples.Last().Ms >= 250)
            PlayerHpSamples.Enqueue((nowMs, hp));
        while (PlayerHpSamples.Count > 0 && nowMs - PlayerHpSamples.Peek().Ms > 10_000)
            PlayerHpSamples.Dequeue();

        long lost = 0;
        uint? previous = null;
        foreach (var (_, sample) in PlayerHpSamples)
        {
            if (previous is { } p && sample < p)
                lost += p - sample;
            previous = sample;
        }
        return lost / 10f;
    }

    /// <summary> Seconds until the target dies at its HP trend over the last 6 s (needs 4 s of samples); 0 = unknown. </summary>
    private static float TrackTimeToDeath(long nowMs, ulong targetId, float hpPercent)
    {
        if (targetId != _ttdTarget)
        {
            TargetHpSamples.Clear();
            _ttdTarget = targetId;
        }
        if (TargetHpSamples.Count == 0 || nowMs - TargetHpSamples.Last().Ms >= 250)
            TargetHpSamples.Enqueue((nowMs, hpPercent));
        while (TargetHpSamples.Count > 0 && nowMs - TargetHpSamples.Peek().Ms > 6_000)
            TargetHpSamples.Dequeue();

        var first = TargetHpSamples.Peek();
        var span = (nowMs - first.Ms) / 1000f;
        var dropped = first.Hp - hpPercent;
        if (span < 4f || dropped <= 0f)
            return 0f;
        return Math.Max(0.01f, hpPercent / (dropped / span));
    }

    // ------------------------------------------------------------------ familiar party HP (AgentXBMPetParty)

    /// <summary> The party agent's HP matched a summoned familiar's live HP this run, so its values are used. </summary>
    internal static bool PartyHpVerified;

    // Offsets from FFXIVClientStructs PR #1952 (AgentXBMPetParty, not merged yet) and Dalamud-DailyRoutines, which reads
    // the same fields in production: SelectedPets StdVector<{uint PetId, uint SortKey}> at 0x78, current HP uint[15] at
    // 0xC0, max HP uint[15] at 0xFC. Index i of the HP arrays is assumed to follow SelectedPets; the read is only
    // trusted once it agrees with a live familiar (PartyHpVerified).
    private const int PartySelectedPets = 0x78;
    private const int PartyCurrentHp = 0xC0;
    private const int PartyMaxHp = 0xFC;
    private const int PartySlots = 15;

    /// <summary> XBMPet row -> HP % from the familiar party agent; empty when unavailable or implausible. </summary>
    internal static unsafe Dictionary<int, float> ReadPartyHp()
    {
        var result = new Dictionary<int, float>();
        try
        {
            var module = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentModule.Instance();
            if (module is null)
                return result;
            var agent = (nint)module->GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.XBMPetParty);
            if (agent == 0)
                return result;

            var first = *(nint*)(agent + PartySelectedPets);
            var last = *(nint*)(agent + PartySelectedPets + 8);
            if (first == 0 || last < first)
                return result;
            var count = (int)((last - first) / 8);
            if (count is <= 0 or > PartySlots)
                return result;

            for (var i = 0; i < count; i++)
            {
                var row = (int)*(uint*)(first + i * 8);
                var cur = *(uint*)(agent + PartyCurrentHp + i * 4);
                var max = *(uint*)(agent + PartyMaxHp + i * 4);
                if (row is < 1 or > BST_Beasts.Count || max == 0 || cur > max)
                    return new Dictionary<int, float>();
                result[row] = 100f * cur / max;
            }
        }
        catch (Exception)
        {
            result.Clear();
        }
        return result;
    }

    // ------------------------------------------------------------------ Crucible screen capture (XB| lines)

    private static readonly Dictionary<string, (int Hash, long Ms)> CrucibleUiSeen = [];

    /// <summary>
    ///     With the collector on: every visible addon whose name starts with "XBM" is written once
    ///     per change (at most once a second per addon) as <c>XB|unixms|name|n=count|index:type=value;...</c>, split
    ///     across <c>XB+|</c> lines. Territory is not a gate — Bentbranch Meadows captures land here too.
    ///     Read-only; this is how the beast party HP and board map get mapped later.
    /// </summary>
    internal static unsafe void CaptureCrucibleUi(long nowMs)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager is null)
            return;

        var list = manager->AtkUnitManager.AllLoadedUnitsList;
        var count = Math.Min((int)list.Count, list.Entries.Length);
        for (var i = 0; i < count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit is null || !unit->IsVisible)
                continue;

            var name = unit->NameString;
            if (name is null || !name.StartsWith("XBM", StringComparison.Ordinal))
                continue;

            var text = DescribeValues(unit);
            var hash = text.GetHashCode();
            if (CrucibleUiSeen.TryGetValue(name, out var seen) && (seen.Hash == hash || nowMs - seen.Ms < 1000))
                continue;
            CrucibleUiSeen[name] = (hash, nowMs);

            if (name == "XBMPetParty" || name == "XBMContentsMainHUD")
            {
                var party = ReadPartyHp();
                var xp = $"XP|{nowMs}|{name}|verified={(PartyHpVerified ? 1 : 0)}|" +
                         string.Join(",", party.Select(kv => $"{kv.Key}:{kv.Value:0}"));
                Svc.Log.Information(xp);
                LalaTelemetry.Record(xp);
            }

            const int chunk = 900;
            for (int offset = 0, part = 0; offset < text.Length && part < 8; offset += chunk, part++)
            {
                var prefix = part == 0 ? $"XB|{nowMs}|{name}|n={unit->AtkValuesCount}|" : $"XB+|{nowMs}|{name}|{part}|";
                var xb = prefix + text.Substring(offset, Math.Min(chunk, text.Length - offset));
                Svc.Log.Information(xb);
                LalaTelemetry.Record(xb);
            }
        }
    }

    private static unsafe string DescribeValues(AtkUnitBase* unit)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(256);
        var values = unit->AtkValues;
        var n = Math.Min((int)unit->AtkValuesCount, 400);
        if (values is null)
            return "";

        for (var i = 0; i < n; i++)
        {
            var v = values[i];
            switch (v.Type)
            {
                case AtkValueType.Undefined:
                    continue;
                case AtkValueType.Bool:
                    sb.Append(i).Append(":b=").Append(v.Byte != 0 ? '1' : '0');
                    break;
                case AtkValueType.Int:
                    sb.Append(i).Append(":i=").Append(v.Int.ToString(inv));
                    break;
                case AtkValueType.UInt:
                    sb.Append(i).Append(":u=").Append(v.UInt.ToString(inv));
                    break;
                case AtkValueType.Float:
                    sb.Append(i).Append(":f=").Append(v.Float.ToString("0.###", inv));
                    break;
                case AtkValueType.String:
                case AtkValueType.ConstString:
                case AtkValueType.ManagedString:
                    var s = v.Pointer is null ? "" : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)v.Pointer) ?? "";
                    if (s.Length > 40)
                        s = s[..40];
                    sb.Append(i).Append(":s=").Append(s.Replace('|', '/').Replace(';', ',').Replace('\n', ' '));
                    break;
                default:
                    sb.Append(i).Append(":t").Append(((int)v.Type).ToString(inv));
                    break;
            }
            sb.Append(';');
        }

        return sb.ToString();
    }
}
