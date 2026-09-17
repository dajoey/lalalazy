#region Dependencies

using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using GluttonyCombo.Extensions;
using GluttonyCombo.Resources.Localization.JobConfigs;
using System;
using System.Collections.Generic;
using System.Numerics;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

#endregion

namespace GluttonyCombo.Combos.PvE;

// Crucible of the Unbroken: the LIVE half. Reads the board, the panel enemies present, the target's casts and
// statuses, familiar and character HP into BstState. The rules are in BST_CrucibleLogic.cs (pure, harness-tested).
internal partial class BST
{
    /// <summary>
    ///     Last HP % seen per familiar (XBMPet row) this run. Familiars do not regenerate and HP carries from node to node;
    ///     only a campsite rest restores it (the party agent read below catches that once it is verified).
    /// </summary>
    private static readonly Dictionary<int, float> CruciblePetHp = [];

    private static uint _crucibleTerritory;
    private static ulong _parryTargetId;
    private static long _parryEndedTick;
    private static string _hornWarningKey = "";

    /// <summary> Seconds a lost Directional Parry still counts as "just ended" (Challenge back). </summary>
    private const long ParryEndedWindowMs = 6000;

    /// <summary> BNpcName of the current target, sampled for the CR| collector. </summary>
    internal static uint LastCrucibleTargetNameId;

    internal static unsafe void ReadCrucible(ref BST_RotationLogic.BstState s)
    {
        var territory = Svc.ClientState.TerritoryType;
        if (territory != _crucibleTerritory)
        {
            CruciblePetHp.Clear();
            _crucibleTerritory = territory;
            _hornWarningKey = "";
            _parryTargetId = 0;
            PartyHpVerified = false;
            PlayerHpSamples.Clear();
            TargetHpSamples.Clear();
        }

        s.CrucibleBoard = BST_CrucibleData.BoardOfTerritory(territory);
        s.CrucibleBattle = -1;
        s.PetHpPercent = 100f;
        LastCrucibleTargetNameId = 0;
        if (s.CrucibleBoard == 0 || LocalPlayer is not { } player)
            return;

        var now = Environment.TickCount64;
        var target = s.HasHostileTarget ? CurrentTarget as IBattleChara : null;

        // Enemies present: count, highest HP, which panel battle they belong to, eggs / morphos near the target.
        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc || npc.IsDead || !npc.IsTargetable || npc.CurrentHp == 0 || !npc.IsHostile())
                continue;

            var nameId = npc.NameId;
            if (BST_CrucibleData.DoNotAttack.TryGetValue(nameId, out var reach))
            {
                if (target is null)
                    continue;
                if (npc.GameObjectId == target.GameObjectId)
                    s.TargetDoNotAttack = true;
                else if (reach == float.MaxValue || Vector3.Distance(npc.Position, target.Position) <= reach + target.HitboxRadius)
                    s.ProtectedNearTarget = true;
                continue;
            }

            s.EnemyCount++;
            var hp = npc.MaxHp == 0 ? 0f : 100f * npc.CurrentHp / npc.MaxHp;
            if (hp > s.HighestEnemyHpPercent)
                s.HighestEnemyHpPercent = hp;

            if (s.CrucibleBattle < 0 && BST_CrucibleData.Enemy(nameId) is { } enemy && enemy.Board == s.CrucibleBoard)
                s.CrucibleBattle = enemy.Battle;
        }

        if (s.CrucibleBattle >= 0)
            s.CrucibleNeeds = BattleNeedsCached(s.CrucibleBoard, s.CrucibleBattle);

        s.PlayerHpPercent = player.MaxHp == 0 ? 0f : 100f * player.CurrentHp / player.MaxHp;
        s.PlayerIntakePerSecond = TrackIntake(now, player.CurrentHp);
        s.PlayerHasCleansableDebuff = player.HasCleansableDebuff;

        // Familiar HP, remembered per beast so a low familiar is not summoned straight back into danger.
        var party = ReadPartyHp();
        if (s.ActiveSlot != 0 && Svc.Buddies.PetBuddy?.GameObject is IBattleChara pet && pet.MaxHp > 0)
        {
            s.PetHpPercent = 100f * pet.CurrentHp / pet.MaxHp;
            s.PetHp = pet.CurrentHp;
            var row = BST_RotationLogic.SlotBeast(s, s.ActiveSlot);
            if (row == 0)
                row = s.PetObjectBeast;
            if (row != 0)
            {
                CruciblePetHp[row] = Math.Max(s.PetHpPercent, 0.5f);
                // The party agent read is trusted only after it agrees with a summoned familiar's live HP.
                if (party.TryGetValue(row, out var agentHp) && Math.Abs(agentHp - s.PetHpPercent) <= 3f && s.PetHpPercent < 99.5f)
                    PartyHpVerified = true;
            }
        }
        if (PartyHpVerified)
            foreach (var (row, hp) in party)
                if (row != BST_RotationLogic.SlotBeast(s, s.ActiveSlot))
                    CruciblePetHp[row] = Math.Max(hp, 0.5f);
        s.Slot1PetHp = CruciblePetHp.GetValueOrDefault(s.Slot1Beast);
        s.Slot2PetHp = CruciblePetHp.GetValueOrDefault(s.Slot2Beast);
        s.Slot3PetHp = CruciblePetHp.GetValueOrDefault(s.Slot3Beast);

        if (target is not null)
        {
            LastCrucibleTargetNameId = target.NameId;

            foreach (var status in target.StatusList)
            {
                var id = status.StatusId;
                if (id == 0)
                    continue;
                if (BST_CrucibleData.StanceStatuses.Contains(id))
                    s.TargetInStance = true;
                if (BST_CrucibleData.DispellableBuffs.Contains(id))
                    s.TargetHasDispellableBuff = true;
                if (BST_CrucibleData.ParryStatuses.Contains(id)
                    || (id == BST_CrucibleData.BoneKnightParryStatus && BST_CrucibleData.BoneKnightNameIds.Contains(target.NameId)))
                    s.TargetHasParry = true;
                if (id == BST_CrucibleData.PhysicalVulnerabilityUp)
                    s.TargetVulnerabilityRemaining = Math.Max(s.TargetVulnerabilityRemaining, status.RemainingTime);
                if (BST_CrucibleData.InvulnerableStatuses.Contains(id))
                    s.TargetInvulnerable = true;
            }

            if (target.IsInvincible)
                s.TargetInvulnerable = true;

            s.TargetTimeToDeath = TrackTimeToDeath(now, target.GameObjectId, s.TargetHpPercent);

            if (target.IsCasting)
            {
                s.TargetCastId = target.CastActionId;
                s.TargetCastRemaining = Math.Max(0f, target.TotalCastTime - target.CurrentCastTime);
            }

            var petId = Svc.Buddies.PetBuddy?.GameObject?.GameObjectId ?? 0;
            s.EnemyTargetsPet = petId != 0 && target.TargetObjectId == petId;
            s.EnemyTargetsPlayer = target.TargetObjectId == player.GameObjectId;

            if (s.TargetHasParry)
            {
                _parryTargetId = target.GameObjectId;
                _parryEndedTick = 0;
            }
            else if (_parryTargetId == target.GameObjectId)
            {
                if (_parryEndedTick == 0)
                    _parryEndedTick = now;
                s.ParryJustEnded = now - _parryEndedTick < ParryEndedWindowMs;
                if (!s.ParryJustEnded)
                    _parryTargetId = 0;
            }
        }

        s.PartingBlowRecast = GetCooldownRemainingTime(PartingBlow);
        s.ReadySnarl = ActionReady(Snarl);
        var sinceSnarl = TimeSinceActionUsed(Snarl);
        s.SinceSnarl = sinceSnarl < 0 ? float.MaxValue : sinceSnarl;
        s.ReadyChallenge = ActionReady(Challenge);

        WarnEmptyHorns(s);
    }

    private static int _needsBoard = -1, _needsBattle = -1;
    private static CrucibleNeeds _needs;

    private static CrucibleNeeds BattleNeedsCached(int board, int battle)
    {
        if (board != _needsBoard || battle != _needsBattle)
        {
            _needs = BST_CrucibleData.BattleNeeds(board, battle);
            _needsBoard = board;
            _needsBattle = battle;
        }
        return _needs;
    }

    /// <summary> Horns deselect after every Crucible encounter: say so once when a battle is up and none is assigned. </summary>
    private static void WarnEmptyHorns(in BST_RotationLogic.BstState s)
    {
        if (!Config.BST_CrucibleHornWarning || s.InCombat || s.CrucibleBattle < 0 || s.SlotBeastsKnown || s.ActiveSlot != 0)
            return;

        var key = $"{s.CrucibleBoard}:{s.CrucibleBattle}";
        if (key == _hornWarningKey)
            return;
        _hornWarningKey = key;

        var picks = BST_CrucibleAdvisor.Pick(s.CrucibleBoard, s.CrucibleBattle, CrucibleBeastCaptured);
        var names = string.Join(", ", picks.ConvertAll(p =>
        {
            var name = BST_Beasts.All[p.Row].Name;
            return name.Length == 0 ? "?" : char.ToUpperInvariant(name[0]) + name[1..];
        }));
        var message = picks.Count == 0
            ? BST_Config.CrucibleHornWarningMessage
            : string.Format(BST_Config.CrucibleHornWarningPicks0And1, BST_CrucibleData.BattleLabel(s.CrucibleBoard, s.CrucibleBattle), names);

        Svc.Toasts.ShowError(BST_Config.CrucibleHornWarningMessage);
        Svc.Chat.PrintError(message);
    }

    /// <summary> Panel battle of the hostile enemies present on the current board, -1 when none. </summary>
    internal static int CurrentCrucibleBattle()
    {
        var board = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        if (board == 0)
            return -1;
        foreach (var obj in Svc.Objects)
            if (obj is IBattleNpc npc && !npc.IsDead && npc.IsHostile()
                && BST_CrucibleData.Enemy(npc.NameId) is { } enemy && enemy.Board == board)
                return enemy.Battle;
        return -1;
    }

    /// <summary> One-line Crucible readout for the options panel. </summary>
    internal static string CrucibleStatusText()
    {
        var board = BST_CrucibleData.BoardOfTerritory(Svc.ClientState.TerritoryType);
        if (board == 0)
            return BST_Config.CrucibleStatusOutside;

        var info = BST_CrucibleData.Boards[board - 1];
        var battle = -1;
        foreach (var obj in Svc.Objects)
        {
            if (obj is IBattleNpc npc && !npc.IsDead && npc.IsHostile()
                && BST_CrucibleData.Enemy(npc.NameId) is { } enemy && enemy.Board == board)
            {
                battle = enemy.Battle;
                break;
            }
        }

        return battle < 0
            ? string.Format(BST_Config.CrucibleStatusBoard0, info.Name)
            : string.Format(BST_Config.CrucibleStatusBattle0And1And2, info.Name, BST_CrucibleData.BattleLabel(board, battle),
                BST_CrucibleData.BattleNeeds(board, battle));
    }
}
