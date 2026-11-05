using RotationSolver.Decisions;

namespace RotationSolver.Basic.Rotations;

public partial class CustomRotation
{
	/// <summary>
	/// Whether the player is currently doing nothing (and healing).
	/// </summary>
	public static bool HealingWhileDoingNothing =>
		_nextTimeToHeal + TimeSpan.FromSeconds(DataCenter.DefaultGCDTotal) > DateTime.Now;

	private static DateTime _nextTimeToHeal = DateTime.MinValue;
	private static readonly Random _random = new();
	private static Duties.DutyRotation? ActiveDutyRotation =>
		Service.Config.UseDutyActions || DataCenter.IsInQuestBattle ? DataCenter.CurrentDutyRotation : null;

	private IAction? GCD()
	{
		var act = DataCenter.CommandNextAction;

		using (new ForceEnableScope())
		{
			if (act is IBaseAction a && a.Info.IsRealGCD
				&& a.CanUse(out _, usedUp: true, skipAoeCheck: true, skipStatusProvideCheck: true))
			{
				return act;
			}
		}

		if (DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting))
		{
			return null;
		}

		if (DataCenter.Orbonne && DataCenter.IsAgriasCastingSpecialIndicator())
		{
			return null;
		}

		if (Service.Config.PldlockCasting && DataCenter.Job == Job.PLD && IsLastAction(ActionID.PassageOfArmsPvE) && StatusHelper.PlayerHasStatus(true, StatusID.PassageOfArms))
		{
			return null;
		}

		if (Service.Config.AstlockCasting && DataCenter.Job == Job.AST && IsLastAction(ActionID.CollectiveUnconsciousPvE) && StatusHelper.PlayerHasStatus(true, StatusID.CollectiveUnconscious_848))
		{
			return null;
		}

		if (DataCenter.Job == ECommons.ExcelServices.Job.BLU && StatusHelper.PlayerHasStatus(true, StatusID.PhantomFlurry))
		{
			return null;
		}

		if (DataCenter.Job == Job.NIN && StatusHelper.PlayerHasStatus(true, StatusID.Mudra) && DataCenter.DefaultGCDRemain > 0.6f)
		{
			return null;
		}

		if (DataCenter.IsPvP && Service.Config.PvpGuardControl && HasPVPGuard)
		{
			return null;
		}

		if (Player != null && DataCenter.IsPvP && Service.Config.PvpGcdLockControl && Player.CurrentMp >= 2000 && Player.GetEffectiveHpPercent() < 50f)
		{
			return null;
		}

		try
		{
			IBaseAction.ShouldEndSpecial = false;
			if (DataCenter.CurrentDutyRotation?.EmergencyGCD(act, out act) == true)
			{
				return act;
			}
			if (EmergencyGCD(act, out act))
			{
				return act;
			}

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.Interrupt))
			{
				if (ActiveDutyRotation?.MyInterruptGCD(out act) == true)
				{
					return act;
				}
				if (MyInterruptGCD(out var action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = TargetType.Dispel;
			if (DataCenter.MergedStatus.HasFlag(AutoStatus.Dispel))
			{
				if (ActiveDutyRotation?.DispelGCD(out act) == true)
				{
					return act;
				}
				if (DispelGCD(out var action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = TargetType.Provoke;
			if (DataCenter.MergedStatus.HasFlag(AutoStatus.Provoke))
			{
				if (ActiveDutyRotation?.ProvokeGCD(out act) == true)
				{
					return act;
				}
				if (ProvokeGCD(out var action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = TargetType.Death;
			if (Service.Config.RaisePlayerFirst && TryRaise(out act))
			{
				if (RaiseSpell(out act, false))
				{
					return act;
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastNormal)
				{
					if (RaiseSpell(out act, true))
					{
						return act;
					}
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastSwiftCooldown)
				{
					if (Raise != null && false)
					{
						if (RaiseSpell(out act, true))
						{
							return act;
						}
					}
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastOnlyHealer)
				{
					var deadhealers = new HashSet<IBattleChara>();
					if (DataCenter.PartyMembers != null)
					{
						foreach (var battleChara in DataCenter.PartyMembers.GetDeath())
						{
							if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
							{
								deadhealers.Add(battleChara);
							}
						}
					}

					var allhealers = new HashSet<IBattleChara>();
					if (DataCenter.PartyMembers != null)
					{
						foreach (var battleChara in DataCenter.PartyMembers)
						{
							if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
							{
								allhealers.Add(battleChara);
							}
						}
					}
					if (RaiseSpell(out act, true) && deadhealers.Count == allhealers.Count && deadhealers.Count > 0)
					{
						return act;
					}
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastOnlyHealerSwiftCooldown)
				{
					if (Raise != null && false)
					{
						var deadhealers = new HashSet<IBattleChara>();
						if (DataCenter.PartyMembers != null)
						{
							foreach (var battleChara in DataCenter.PartyMembers.GetDeath())
							{
								if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
								{
									deadhealers.Add(battleChara);
								}
							}
						}

						var allhealers = new HashSet<IBattleChara>();
						if (DataCenter.PartyMembers != null)
						{
							foreach (var battleChara in DataCenter.PartyMembers)
							{
								if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
								{
									allhealers.Add(battleChara);
								}
							}
						}
						if (RaiseSpell(out act, true) && deadhealers.Count == allhealers.Count && deadhealers.Count > 0)
						{
							return act;
						}
					}
				}
			}

			IBaseAction.TargetOverride = null;

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.MoveForward))
			{
				if (ActiveDutyRotation?.MoveForwardGCD(out act) == true)
				{
					if (act is IBaseAction b && ObjectHelper.DistanceToPlayer(b.Target.Target) > 5)
					{
						return act;
					}
				}
				if (MoveForwardGCD(out var action))
				{
					if (action is IBaseAction b && ObjectHelper.DistanceToPlayer(b.Target.Target) > 5)
					{
						return action;
					}
				}
			}

			IBaseAction.TargetOverride = TargetType.Heal;

			if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealAreaSpell))
			{
				IBaseAction.AutoHealCheck = true;
				if (ActiveDutyRotation?.HealAreaGCD(out act) == true)
				{
					return act;
				}

				if (CanCastHeals && HealAreaGCD(out var action))
				{
					return action;
				}
				IBaseAction.AutoHealCheck = false;
			}
			if (DataCenter.AutoStatus.HasFlag(AutoStatus.HealAreaSpell))
			{
				IBaseAction.AutoHealCheck = true;
				if (ActiveDutyRotation?.HealAreaGCD(out act) == true)
				{
					return act;
				}

				if (CanHealAreaSpell && CanCastHeals && HealAreaGCD(out var action))
				{
					return action;
				}

				IBaseAction.AutoHealCheck = false;
			}

			if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealSingleSpell))
			{
				IBaseAction.AutoHealCheck = true;
				if (ActiveDutyRotation?.HealSingleGCD(out act) == true)
				{
					return act;
				}

				if (CanCastHeals && HealSingleGCD(out var action))
				{
					return action;
				}
				IBaseAction.AutoHealCheck = false;
			}
			if (DataCenter.AutoStatus.HasFlag(AutoStatus.HealSingleSpell))
			{
				IBaseAction.AutoHealCheck = true;
				if (ActiveDutyRotation?.HealSingleGCD(out act) == true)
				{
					return act;
				}

				if (CanHealSingleSpell && CanCastHeals && HealSingleGCD(out var action))
				{
					return action;
				}

				IBaseAction.AutoHealCheck = false;
			}

			IBaseAction.TargetOverride = null;

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseArea))
			{
				if (ActiveDutyRotation?.DefenseAreaGCD(out act) == true)
				{
					return act;
				}

				if (DefenseAreaGCD(out var action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = TargetType.BeAttacked;

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseSingle))
			{
				if (ActiveDutyRotation?.DefenseSingleGCD(out act) == true)
				{
					return act;
				}

				if (DefenseSingleGCD(out var action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = TargetType.Death;
			if (!Service.Config.RaisePlayerFirst && TryRaise(out act))
			{
				if (RaiseSpell(out act, false))
				{
					return act;
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastNormal)
				{
					if (RaiseSpell(out act, true))
					{
						return act;
					}
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastSwiftCooldown)
				{
					if (Raise != null && false)
					{
						if (RaiseSpell(out act, true))
						{
							return act;
						}
					}
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastOnlyHealer)
				{
					var deadhealers = new HashSet<IBattleChara>();
					if (DataCenter.PartyMembers != null)
					{
						foreach (var battleChara in DataCenter.PartyMembers.GetDeath())
						{
							if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
							{
								deadhealers.Add(battleChara);
							}
						}
					}

					var allhealers = new HashSet<IBattleChara>();
					if (DataCenter.PartyMembers != null)
					{
						foreach (var battleChara in DataCenter.PartyMembers)
						{
							if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
							{
								allhealers.Add(battleChara);
							}
						}
					}
					if (RaiseSpell(out act, true) && deadhealers.Count == allhealers.Count && deadhealers.Count > 0)
					{
						return act;
					}
				}

				if (hardcastraisetype == HardCastRaiseType.HardCastOnlyHealerSwiftCooldown)
				{
					if (Raise != null && false)
					{
						var deadhealers = new HashSet<IBattleChara>();
						if (DataCenter.PartyMembers != null)
						{
							foreach (var battleChara in DataCenter.PartyMembers.GetDeath())
							{
								if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
								{
									deadhealers.Add(battleChara);
								}
							}
						}

						var allhealers = new HashSet<IBattleChara>();
						if (DataCenter.PartyMembers != null)
						{
							foreach (var battleChara in DataCenter.PartyMembers)
							{
								if (TargetFilter.IsJobCategory(battleChara, JobRole.Healer) && !battleChara.IsPlayer())
								{
									allhealers.Add(battleChara);
								}
							}
						}
						if (RaiseSpell(out act, true) && deadhealers.Count == allhealers.Count && deadhealers.Count > 0)
						{
							return act;
						}
					}
				}
			}

			IBaseAction.ShouldEndSpecial = false;
			IBaseAction.TargetOverride = null;

			if (!DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting))
			{
				if (ActiveDutyRotation?.GeneralGCD(out act) == true)
				{
					return act;
				}

				if (GeneralGCD(out var action))
				{
					return action;
				}
			}

			if (Service.Config.HealWhenNothingTodo && InCombat)
			{
				if (DateTime.Now - _nextTimeToHeal > TimeSpan.FromSeconds(1))
				{
					var min = Service.Config.HealWhenNothingTodoDelay.X;
					var max = Service.Config.HealWhenNothingTodoDelay.Y;
					_nextTimeToHeal = DateTime.Now + TimeSpan.FromSeconds((_random.NextDouble() * (max - min)) + min);
				}
				else if (_nextTimeToHeal < DateTime.Now)
				{
					_nextTimeToHeal = DateTime.Now;

					if (PartyMembersMinHP < Service.Config.HealWhenNothingTodoBelow)
					{
						IBaseAction.TargetOverride = TargetType.Heal;

						if (DataCenter.PartyMembersDifferHP < Service.Config.HealthDifference)
						{
							var count = 0;
							foreach (var hp in DataCenter.PartyMembersHP)
							{
								if (hp < 1)
								{
									count++;
								}
							}
							if (count > 2 && ActiveDutyRotation?.HealAreaGCD(out act) == true)
							{
								return act;
							}
							if (count > 2 && HealAreaGCD(out act))
							{
								return act;
							}
						}
						if (ActiveDutyRotation?.HealSingleGCD(out act) == true)
						{
							return act;
						}
						if (HealSingleGCD(out act))
						{
							return act;
						}

						IBaseAction.TargetOverride = null;
					}
				}
			}
		}
		catch (Exception ex)
		{
			PluginLog.Error($"Exception in GCD method: {ex.Message}");
		}
		finally
		{
			// Ensure these are reset
			IBaseAction.ShouldEndSpecial = false;
			IBaseAction.TargetOverride = null;
		}

		return null;
	}

	private static bool CanCastHeals => !StatusHelper.PlayerHasStatus(false, StatusID.Scalebound)
		&& (!StatusHelper.PlayerHasStatus(false, StatusID.ShackledHealing) || DataCenter.NumberOfPartyMembersInRangeOf(21) == 1);

	private bool TryRaise(out IAction? act)
	{
		act = null;
		if (!DataCenter.MergedStatus.HasFlag(AutoStatus.Raise) || !DataCenter.CanRaise())
		{
			return false;
		}

		if (RaiseSpell(out act, false))
		{
			return true;
		}

		var swiftUnused = !Service.Config.RaisePlayerBySwift || !SwiftcastPvE.EnoughLevel;
		var swiftOnCooldown = SwiftcastPvE.Cooldown.IsCoolingDown;
		var castsBeforeSwift = swiftUnused || (swiftOnCooldown && Raise != null && Raise.Info.CastTime < SwiftcastPvE.Cooldown.RecastTimeRemainOneCharge);
		var mayHardCast = Service.Config.HardCastRaiseType switch
		{
			HardCastRaiseType.HardCastNormal => swiftUnused || swiftOnCooldown,
			HardCastRaiseType.HardCastSwiftCooldown => castsBeforeSwift,
			HardCastRaiseType.HardCastOnlyHealer => AllOtherHealersDead(),
			HardCastRaiseType.HardCastOnlyHealerSwiftCooldown => castsBeforeSwift && AllOtherHealersDead(),
			_ => false,
		};

		return mayHardCast && RaiseSpell(out act, true);
	}

	private static bool AllOtherHealersDead()
	{
		var healers = 0;
		foreach (var member in DataCenter.PartyMembers)
		{
			if (member.IsJobCategory(JobRole.Healer) && !member.IsPlayer())
			{
				healers++;
			}
		}

		var deadHealers = 0;
		foreach (var member in DataCenter.PartyMembers.GetDeath())
		{
			if (member.IsJobCategory(JobRole.Healer) && !member.IsPlayer())
			{
				deadHealers++;
			}
		}

		return deadHealers > 0 && deadHealers == healers;
	}

	private bool RaiseSpell(out IAction? act, bool mustUse)
	{
		act = null;

		if (DataCenter.CanRaise())
		{
			if (DataCenter.CommandStatus.HasFlag(AutoStatus.Raise))
			{
				IBaseAction.ShouldEndSpecial = true;
				if (ActiveDutyRotation?.RaiseGCD(out act) == true)
				{
					return true;
				}

				if (RaiseGCD(out act))
				{
					return true;
				}
			}
			IBaseAction.ShouldEndSpecial = false;

			if (DataCenter.AutoStatus.HasFlag(AutoStatus.Raise))
			{
				if (ActiveDutyRotation?.RaiseGCD(out act) == true)
				{
					return true;
				}

				if (RaiseGCD(out act))
				{
					if (HasSwift || IsLastAction(DataCenter.IsPvP ? ActionID.SwiftPvP : ActionID.SwiftcastPvE))
					{
						return true;
					}

					if (Service.Config.RaisePlayerBySwift && WeaponRemain <= 0.5f)
					{
						return true;
					}

					if (mustUse && !IsMoving)
					{
						return true;
					}
				}
			}

			return false;
		}
		return false;
	}

	/// <summary>
	/// Attempts to use the Interrupt GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool MyInterruptGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the Raise GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool RaiseGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the Dispel GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool DispelGCD(out IAction? act)
	{
		act = null;
		if (ShouldSkipAction())
		{
			return false;
		}

		if (DataCenter.CommandStatus.HasFlag(AutoStatus.Dispel))
		{
			IBaseAction.ShouldEndSpecial = true;
		}
		if (!DataCenter.IsPvP && !HasSwift && PurifyPvP.CanUse(out act))
		{
			return true;
		}

		IBaseAction.ShouldEndSpecial = false;
		return false;
	}

	/// <summary>
	///
	/// </summary>
	protected virtual bool ProvokeGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the Emergency GCD action.
	/// </summary>
	/// <param name="nextGCD">The next GCD action.</param>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool EmergencyGCD(IAction? nextGCD, out IAction? act)
	{
		act = null;
		if (DataCenter.IsPvP)
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

			// Self-defence reads REAL HP, same as the Guard branch in EmergencyAbility: the predicted
			// value (pending heals added) can read healthy while the body dies.
			if (GuardPvP.CanUse(out act) && Player is { } self && JevCall.HpRatio(self.CurrentHp, self.MaxHp) <= Service.Config.HealthForGuard && !StatusHelper.PlayerHasStatus(true, StatusID.UndeadRedemption) && !StatusHelper.PlayerHasStatus(true, StatusID.InnerRelease_1303))
			{
				return true;
			}

			if (RecuperatePvP.CanUse(out act) && GenericDefensives.RecuperateGate(Player))
			{
				return true;
			}

			if (StandardissueElixirPvP.CanUse(out act))
			{
				return true;
			}
		}

		if (ShouldSkipAction())
		{
			return false;
		}

		act = null!; return false;
	}

	/// <summary>
	/// Attempts to use the Move Forward GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	[RotationDesc(DescType.MoveForwardGCD)]
	protected virtual bool MoveForwardGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the Heal Single GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	[RotationDesc(DescType.HealSingleGCD)]
	protected virtual bool HealSingleGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the Heal Area GCD action.
	/// </summary>
	/// <param name="action">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	[RotationDesc(DescType.HealAreaGCD)]
	protected virtual bool HealAreaGCD(out IAction? action)
	{
		action = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the Defense Single GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	[RotationDesc(DescType.DefenseSingleGCD)]
	protected virtual bool DefenseSingleGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the Defense Area GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	[RotationDesc(DescType.DefenseAreaGCD)]
	protected virtual bool DefenseAreaGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	/// <summary>
	/// Attempts to use the General GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool GeneralGCD(out IAction? act)
	{
		act = null;
		return false;
	}

	private bool ShouldSkipAction()
	{
		return DataCenter.CommandStatus.HasFlag(AutoStatus.Raise) && Role is JobRole.Healer && (HasSwift || IsLastAction(DataCenter.IsPvP ? ActionID.SwiftPvP : ActionID.SwiftcastPvE));
	}
}
