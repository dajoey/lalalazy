using ECommons.DalamudServices;
using RotationSolver.Decisions;

namespace RotationSolver.Basic.Rotations;

public partial class CustomRotation
{
	private const float TrueNorthLastWeaveRemain = 1.35f;

	private bool Ability(IAction nextGCD, out IAction? act)
	{
		act = DataCenter.CommandNextAction;

		if (Player == null)
		{
			return false;
		}

		if (DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting))
		{
			return false;
		}

		if (WeaponRemain <= 0.5f && WeaponRemain > 0f)
		{
			return false;
		}

		if (DataCenter.Orbonne && DataCenter.IsAgriasCastingSpecialIndicator())
		{
			return false;
		}

		if (Service.Config.PldlockCasting && DataCenter.Job == Job.PLD && !DataCenter.IsMoving && IsLastAction(ActionID.PassageOfArmsPvE) && StatusHelper.PlayerHasStatus(true, StatusID.PassageOfArms) && DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseArea))
		{
			return false;
		}

		if (Service.Config.AstlockCasting && DataCenter.Job == Job.AST && !DataCenter.IsMoving && IsLastAction(ActionID.CollectiveUnconsciousPvE) && StatusHelper.PlayerHasStatus(true, StatusID.CollectiveUnconscious_848) && DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseArea))
		{
			return false;
		}

		if (DataCenter.Job == ECommons.ExcelServices.Job.BLU && !DataCenter.IsMoving && StatusHelper.PlayerHasStatus(true, StatusID.PhantomFlurry) && !StatusHelper.PlayerWillStatusEnd(1, true, StatusID.PhantomFlurry))
		{
			return false;
		}

		using (new ForceEnableScope())
		{
			if (act is IBaseAction a && !a.Info.IsRealGCD && a.CanUse(out _, usedUp: true, skipAoeCheck: true, skipStatusProvideCheck: true))
			{
				return true;
			}
		}

		if (DataCenter.Job == Job.NIN && StatusHelper.PlayerHasStatus(true, StatusID.Mudra))
		{
			return false;
		}

		if (DataCenter.IsPvP && Service.Config.PvpGuardControl && HasPVPGuard)
		{
			return false;
		}

		if (act is IBaseItem i && i.CanUse(out _, true))
		{
			return true;
		}

		if (!Service.Config.UseAbility || Player.TotalCastTime > 0 || (StatusHelper.PlayerHasStatus(false, StatusID.ShackledAbilities) && DataCenter.NumberOfPartyMembersInRangeOf(8) > 1))
		{
			act = null;
			return false;
		}

		if (!DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting) && ActiveDutyRotation?.EmergencyAbility(nextGCD, out act) == true)
		{
			return true;
		}
		if (!DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting) && EmergencyAbility(nextGCD, out act))
		{
			return true;
		}

		var role = DataCenter.Role;

		IBaseAction.TargetOverride = TargetType.Interrupt;
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.Interrupt) && !StatusHelper.PlayerHasStatus(true, StatusID.Mudra))
		{
			if (ActiveDutyRotation?.InterruptAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (MyInterruptAbility(role, nextGCD, out act))
			{
				return true;
			}
		}

		IBaseAction.TargetOverride = TargetType.Dispel;
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.Dispel))
		{
			if (ActiveDutyRotation?.DispelAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (DispelAbility(nextGCD, out act))
			{
				return true;
			}
		}

		IBaseAction.TargetOverride = TargetType.Tank;
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.Shirk))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.Shirk))
		{
			return false;
		}
		IBaseAction.ShouldEndSpecial = false;

		IBaseAction.TargetOverride = null;
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.TankStance))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.TankStance) && (TankStance?.CanUse(out act) ?? false))
		{
			return true;
		}
		IBaseAction.ShouldEndSpecial = false;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.AntiKnockback))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.AntiKnockback) && !StatusHelper.PlayerHasStatus(true, StatusID.Mudra))
		{
			if (ActiveDutyRotation?.AntiKnockbackAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (AntiKnockback(role, nextGCD, out act))
			{
				return true;
			}
		}
		IBaseAction.ShouldEndSpecial = false;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.Positional))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.Positional))
		{
			return false;
		}
		IBaseAction.ShouldEndSpecial = false;

		IBaseAction.TargetOverride = TargetType.Heal;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealAreaAbility))
		{
			IBaseAction.AllEmpty = true;
			IBaseAction.ShouldEndSpecial = true;
			if (ActiveDutyRotation?.HealAreaAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (CanCastHeals)
			{
				if (HealAreaAbility(nextGCD, out act))
				{
					return true;
				}
			}
			IBaseAction.AllEmpty = false;
			IBaseAction.ShouldEndSpecial = false;
		}

		if (DataCenter.AutoStatus.HasFlag(AutoStatus.HealAreaAbility) && (CanHealAreaAbility || DataCenter.IsInOccultCrescentOp))
		{
			IBaseAction.AutoHealCheck = true;
			if (ActiveDutyRotation?.HealAreaAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (CanCastHeals)
			{
				if (HealAreaAbility(nextGCD, out act))
				{
					return true;
				}
			}
			IBaseAction.AutoHealCheck = false;
		}

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealSingleAbility))
		{
			IBaseAction.AllEmpty = true;
			IBaseAction.ShouldEndSpecial = true;
			if (ActiveDutyRotation?.HealSingleAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (CanCastHeals)
			{
				if (HealSingleAbility(nextGCD, out act))
				{
					return true;
				}
			}
			IBaseAction.AllEmpty = false;
			IBaseAction.ShouldEndSpecial = false;
		}

		if (DataCenter.AutoStatus.HasFlag(AutoStatus.HealSingleAbility) && (CanHealSingleAbility || DataCenter.IsInOccultCrescentOp))
		{
			IBaseAction.AutoHealCheck = true;
			if (ActiveDutyRotation?.HealSingleAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (CanCastHeals)
			{
				if (HealSingleAbility(nextGCD, out act))
				{
					return true;
				}
			}
			IBaseAction.AutoHealCheck = false;
		}

		IBaseAction.TargetOverride = null;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.Speed))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.Speed))
		{
			if (ActiveDutyRotation?.SpeedAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (SpeedAbility(nextGCD, out act))
			{
				return true;
			}
		}
		IBaseAction.ShouldEndSpecial = false;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.Provoke))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.Provoke))
		{
			if (!HasTankStance && (TankStance?.CanUse(out act) ?? false))
			{
				return true;
			}

			IBaseAction.TargetOverride = TargetType.Provoke;
			if (ProvokeAbility(nextGCD, out act))
			{
				return true;
			}
		}
		IBaseAction.ShouldEndSpecial = false;

		IBaseAction.TargetOverride = TargetType.BeAttacked;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.DefenseArea))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseArea))
		{
			if (ActiveDutyRotation?.DefenseAreaAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (DefenseAreaAbility(nextGCD, out act) || (role is JobRole.Melee or JobRole.RangedPhysical or JobRole.RangedMagical && DefenseSingleAbility(nextGCD, out act)))
			{
				return true;
			}
		}
		IBaseAction.ShouldEndSpecial = false;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.DefenseSingle))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseSingle))
		{
			if (ActiveDutyRotation?.DefenseSingleAbility(nextGCD, out act) == true)
			{
				return true;
			}
			bool defenseSingleFound = DefenseSingleAbility(nextGCD, out act);
			if (RotationSolver.Decisions.DefenseSingleGate.EndsStage(defenseSingleFound, DataCenter.IsPvP, DataCenter.IsHostileCastingToTank,
				StatusHelper.PlayerHasStatus(true, StatusID.Vengeance), StatusHelper.PlayerHasStatus(true, StatusID.Damnation)))
			{
				return true;
			}
		}
		IBaseAction.ShouldEndSpecial = false;

		IBaseAction.TargetOverride = null;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.MoveForward))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		IBaseAction.AllEmpty = true;
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.MoveForward) && Player != null && !StatusHelper.PlayerHasStatus(true, StatusID.Bind))
		{
			if (ActiveDutyRotation?.MoveForwardAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (MoveForwardAbility(nextGCD, out act))
			{
				return true;
			}
		}
		IBaseAction.ShouldEndSpecial = false;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.MoveBack))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.MoveBack))
		{
			if (ActiveDutyRotation?.MoveBackAbility(nextGCD, out act) == true)
			{
				return true;
			}
			if (MoveBackAbility(nextGCD, out act))
			{
				return true;
			}
		}
		IBaseAction.ShouldEndSpecial = false;
		IBaseAction.AllEmpty = false;

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealSingleAbility))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.HealSingleAbility) && UseHpPotion(nextGCD, out act))
		{
			return true;
		}
		IBaseAction.ShouldEndSpecial = false;

		// Attack abilities: melee jobs in PvP consider them with an enemy within 5 yalms (their weaponskill reach)
		// instead of JobRange; JobRange itself is shared and unchanged.
		bool hasHostilesForAttack = DataCenter.NumberOfHostilesInRangeOf(RotationSolver.Decisions.AttackRangeGate.Radius(
			DataCenter.IsPvP, DataCenter.Role == JobRole.Melee, DataCenter.JobRange)) > 0;
		if (hasHostilesForAttack && DataCenter.CurrentDutyRotation?.AttackAbility(nextGCD, out act) == true)
		{
			return true;
		}
		if (hasHostilesForAttack && AttackAbility(nextGCD, out act))
		{
			return true;
		}

		if (ActiveDutyRotation?.GeneralAbility(nextGCD, out act) == true)
		{
			return true;
		}
		if (GeneralAbility(nextGCD, out act))
		{
			return true;
		}

		return UseMpPotion(nextGCD, out act) || GeneralUsingAbility(role, nextGCD, out act) || (DataCenter.AutoStatus.HasFlag(AutoStatus.Speed) && SpeedAbility(nextGCD, out act));
	}

	private bool MyInterruptAbility(JobRole role, IAction nextGCD, out IAction? act)
	{
		switch (role)
		{
			case JobRole.Tank:
				break;

			case JobRole.Melee:
				break;

			case JobRole.RangedPhysical:
				break;

			default:
				break;
		}
		return InterruptAbility(nextGCD, out act);
	}

	/// <summary>
	/// Determines if an interrupt ability can be used.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the interrupt ability can be used; otherwise, false.</returns>
	protected virtual bool InterruptAbility(IAction nextGCD, out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Determines if an interrupt ability can be used.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the interrupt ability can be used; otherwise, false.</returns>
	protected virtual bool DispelAbility(IAction nextGCD, out IAction? act)
	{
		act = null;
		return false;
	}

	private bool AntiKnockback(JobRole role, IAction nextGCD, out IAction? act)
	{
		switch (role)
		{
			default:
				break;
		}

		return AntiKnockbackAbility(nextGCD, out act);
	}

	/// <summary>
	/// Determines if an anti-knockback ability can be used.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the anti-knockback ability can be used; otherwise, false.</returns>
	protected virtual bool AntiKnockbackAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	/// <summary>
	/// Determines if a provoke ability can be used.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the provoke ability can be used; otherwise, false.</returns>
	protected virtual bool ProvokeAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	private bool GeneralUsingAbility(JobRole role, IAction nextGCD, out IAction? act)
	{
		act = null;
		switch (role)
		{
			default:
				break;
		}
		return false;
	}


	/// <summary>
	/// Determines if an emergency ability can be used.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the emergency ability can be used; otherwise, false.</returns>
	protected virtual bool EmergencyAbility(IAction nextGCD, out IAction? act)
	{
		if (TryPvPEmergency(out act))
		{
			if (PurifyPvP.CanUse(out act))
			{
				if (Service.Config.PvpPurifyStun && StatusHelper.PlayerHasStatus(false, StatusID.Stun_1343))
				{
					return true;
				}

				if (Service.Config.PvpPurifyHeavy && StatusHelper.PlayerHasStatus(false, StatusID.Heavy_1344))
				{
					return true;
				}

				if (Service.Config.PvpPurifyBind && StatusHelper.PlayerHasStatus(false, StatusID.Bind_1345))
				{
					return true;
				}

				if (Service.Config.PvpPurifySilence && StatusHelper.PlayerHasStatus(false, StatusID.Silence_1347))
				{
					return true;
				}

				if (Service.Config.PvpPurifyDeepFreeze && StatusHelper.PlayerHasStatus(false, StatusID.DeepFreeze_3219))
				{
					return true;
				}

				if (Service.Config.PvpPurifyMiracleOfNature && StatusHelper.PlayerHasStatus(false, StatusID.MiracleOfNature))
				{
					return true;
				}
			}

			// Self-defence reads REAL HP (current over max): GetHealthRatio prefers the predicted
			// value with pending heals added, which read 84% one second before a death at ~40% real
			// HP, so Guard never fired. JevCall.HpRatio is the same real-HP read the v3 snapshot uses.
			if (GuardPvP.CanUse(out act) && Player is { } self && JevCall.HpRatio(self.CurrentHp, self.MaxHp) <= Service.Config.HealthForGuard && !StatusHelper.PlayerHasStatus(true, StatusID.UndeadRedemption) && !StatusHelper.PlayerHasStatus(true, StatusID.InnerRelease_1303))
			{
				return true;
			}

			if (GenericDefensives.TryFire(this, out act))
			{
				return true;
			}

			if (RecuperatePvP.CanUse(out act) && GenericDefensives.RecuperateGate(Player))
			{
				if (!nextGCD.IsTheSameTo(false, ActionID.MegaflarePvE, ActionID.ThunderstormPvE, ActionID.JudgmentBoltPvE, ActionID.HellfirePvE, ActionID.EarthenWallPvE))
				{
					return true;
				}
			}

			if (StandardissueElixirPvP.CanUse(out act))
			{
				return true;
			}
		}
		#endregion

		if (Role is JobRole.Melee or JobRole.RangedPhysical && StatusHelper.PlayerHasStatus(false, StatusHelper.DoomHealStatus)
			&& SecondWindPvE.CanUse(out act))
		{
			return true;
		}

		act = null;
		return false;
	}

	private bool TryPvPEmergency(out IAction? act)
	{
		act = null;
		if (!DataCenter.IsPvP)
		{
			return false;
		}

		if (DataCenter.Job != Job.BRD && DataCenter.Job != Job.WHM && PurifyPvP.CanUse(out act)
			&& ((Service.Config.PvpPurifyStun && StatusHelper.PlayerHasStatus(false, StatusID.Stun_1343))
				|| (Service.Config.PvpPurifyHeavy && StatusHelper.PlayerHasStatus(false, StatusID.Heavy_1344))
				|| (Service.Config.PvpPurifyBind && StatusHelper.PlayerHasStatus(false, StatusID.Bind_1345))
				|| (Service.Config.PvpPurifySilence && StatusHelper.PlayerHasStatus(false, StatusID.Silence_1347))
				|| (Service.Config.PvpPurifyDeepFreeze && StatusHelper.PlayerHasStatus(false, StatusID.DeepFreeze_3219))
				|| (Service.Config.PvpPurifyMiracleOfNature && StatusHelper.PlayerHasStatus(false, StatusID.MiracleOfNature))))
		{
			return true;
		}

		if (GuardPvP.CanUse(out act) && Player?.GetHealthRatio() <= Service.Config.HealthForGuard
			&& !StatusHelper.PlayerHasStatus(true, StatusID.UndeadRedemption) && !StatusHelper.PlayerHasStatus(true, StatusID.InnerRelease_1303))
		{
			return true;
		}

		return RecuperatePvP.CanUse(out act) || StandardissueElixirPvP.CanUse(out act);
	}

	/// <summary>
	/// The ability that makes the character move forward.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	[RotationDesc(DescType.MoveForwardAbility)]
	protected virtual bool MoveForwardAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	/// <summary>
	/// The ability that makes the character move back.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	[RotationDesc(DescType.MoveBackAbility)]
	protected virtual bool MoveBackAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	/// <summary>
	/// The ability that heals a single character.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	[RotationDesc(DescType.HealSingleAbility)]
	protected virtual bool HealSingleAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	/// <summary>
	/// The ability that heals an area.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	[RotationDesc(DescType.HealAreaAbility)]
	protected virtual bool HealAreaAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	/// <summary>
	/// The ability that defends a single character.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	[RotationDesc(DescType.DefenseSingleAbility)]
	protected virtual bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	/// <summary>
	/// The ability that defends an area.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	[RotationDesc(DescType.DefenseAreaAbility)]
	protected virtual bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
	{

		act = null;
		return false;
	}

	/// <summary>
	/// The ability that speeds up the character.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	[RotationDesc(DescType.SpeedAbility)]
	[RotationDesc(ActionID.SprintPvP)]
	protected virtual bool SpeedAbility(IAction nextGCD, out IAction? act)
	{
		if (SprintPvP.CanUse(out act))
		{
			return true;
		}

		if (DataCenter.IsPvP && (!DataCenter.InCombat || (Service.Config.PvpAllowSprintWithoutTarget && Svc.Targets.Target == null)) && SprintPvP.CanUse(out act))
		{
			return true;
		}

		act = null;
		return false;
	}

	/// <summary>
	/// The ability that can be used anywhere.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	protected virtual bool GeneralAbility(IAction nextGCD, out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// The ability that attacks an enemy.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The resulting action.</param>
	/// <returns>True if the ability can be used; otherwise, false.</returns>
	protected virtual bool AttackAbility(IAction nextGCD, out IAction? act)
	{
		act = null;
		return false;
	}
}
