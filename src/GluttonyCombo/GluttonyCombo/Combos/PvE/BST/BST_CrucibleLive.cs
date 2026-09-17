#region Dependencies

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Globalization;
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

    // ------------------------------------------------------------------ Crucible screen capture (XB| lines)

    private static readonly Dictionary<string, (int Hash, long Ms)> CrucibleUiSeen = [];

    /// <summary>
    ///     With the collector on, on a Crucible board: every visible addon whose name starts with "XBM" is written once
    ///     per change (at most once a second per addon) as <c>XB|unixms|name|n=count|index:type=value;...</c>, split
    ///     across <c>XB+|</c> lines. Read-only; this is how the beast party HP and board map get mapped later.
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

            const int chunk = 900;
            for (int offset = 0, part = 0; offset < text.Length && part < 8; offset += chunk, part++)
            {
                var prefix = part == 0 ? $"XB|{nowMs}|{name}|n={unit->AtkValuesCount}|" : $"XB+|{nowMs}|{name}|{part}|";
                Svc.Log.Information(prefix + text.Substring(offset, Math.Min(chunk, text.Length - offset)));
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
