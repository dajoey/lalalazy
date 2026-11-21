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

	private IAction? GCD()
	{
		IAction? act = DataCenter.CommandNextAction;

		IBaseAction.ForceEnable = true;
		if (act is IBaseAction a && a.Info.IsRealGCD
			&& a.CanUse(out _, usedUp: true, skipAoeCheck: true, skipStatusProvideCheck: true))
		{
			return act;
		}

		IBaseAction.ForceEnable = false;

		if (DataCenter.Job == ECommons.ExcelServices.Job.NIN && StatusHelper.PlayerHasStatus(true, StatusID.Mudra) && DataCenter.DefaultGCDRemain >= 0.625f)
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
			if (EmergencyGCD(act, out act))
			{
				return act;
			}

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.Interrupt))
			{
				if (MyInterruptGCD(out IAction? action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = TargetType.Dispel;
			if (DataCenter.MergedStatus.HasFlag(AutoStatus.Dispel))
			{
				if (DispelGCD(out IAction? action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = null;

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.MoveForward))
			{
				if (MoveForwardGCD(out IAction? action))
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
				IBaseAction.AutoHealCheck = false;
			}
			if (DataCenter.AutoStatus.HasFlag(AutoStatus.HealAreaSpell))
			{
				IBaseAction.AutoHealCheck = true;
				IBaseAction.AutoHealCheck = false;
			}

			if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealSingleSpell))
			{
				IBaseAction.AutoHealCheck = true;
				IBaseAction.AutoHealCheck = false;
			}
			if (DataCenter.AutoStatus.HasFlag(AutoStatus.HealSingleSpell))
			{
				IBaseAction.AutoHealCheck = true;

				if (CanHealSingleSpell)
				{
					if (HealSingleGCD(out IAction? action))
					{
						return action;
					}
				}

				IBaseAction.AutoHealCheck = false;
			}

			IBaseAction.TargetOverride = null;

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseArea))
			{
				if (DefenseAreaGCD(out var action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = TargetType.BeAttacked;

			if (DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseSingle))
			{
				if (DefenseSingleGCD(out var action))
				{
					return action;
				}
			}

			IBaseAction.TargetOverride = null;

			IBaseAction.ShouldEndSpecial = false;
			IBaseAction.TargetOverride = null;

			if (!DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting))
			{
				// The attack chooser: this call was deleted together with the engine removal of
				// v0.1.2.23 and every job's attack GCDs were unreachable code until v0.1.2.34, so
				// the chooser could only ever return heals or nothing (the healing-not-attacking bug).
				if (GeneralGCD(out var action))
				{
					return action;
				}
			}

			if (Service.Config.HealWhenNothingTodo && InCombat)
			{
				// Please don't tell me someone's fps is less than 1!!
				if (DateTime.Now - _nextTimeToHeal > TimeSpan.FromSeconds(1))
				{
					float min = Service.Config.HealWhenNothingTodoDelay.X;
					float max = Service.Config.HealWhenNothingTodoDelay.Y;
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
							int count = 0;
							foreach (float hp in DataCenter.PartyMembersHP)
							{
								if (hp < 1)
								{
									count++;
								}
							}
							if (count > 2 && HealAreaGCD(out act))
							{
								return act;
							}
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
			// Log the exception or handle it as needed
			Console.WriteLine($"Exception in GCD method: {ex.Message}");
		}
		finally
		{
			// Ensure these are reset
			IBaseAction.ShouldEndSpecial = false;
			IBaseAction.TargetOverride = null;
		}

		return null;
	}

	/// <summary>
	/// Attempts to use the Interrupt GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool MyInterruptGCD(out IAction? act)
	{
		act = null;
		act = null; return false;
	}

	/// <summary>
	/// Attempts to use the Dispel GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool DispelGCD(out IAction? act)
	{
		act = null;
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
			if (GuardPvP.CanUse(out act) && Player is { } self && RealHp.HpRatio(self.CurrentHp, self.MaxHp) <= Service.Config.HealthForGuard && !StatusHelper.PlayerHasStatus(true, StatusID.UndeadRedemption) && !StatusHelper.PlayerHasStatus(true, StatusID.InnerRelease_1303))
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
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.MoveForward))
		{
			IBaseAction.ShouldEndSpecial = true;
		}

		IBaseAction.ShouldEndSpecial = false;
		act = null; return false;
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
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealSingleSpell))
		{
			IBaseAction.ShouldEndSpecial = true;
		}

		IBaseAction.ShouldEndSpecial = false;
		act = null; return false;
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
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.HealAreaSpell))
		{
			IBaseAction.ShouldEndSpecial = true;
		}

		IBaseAction.ShouldEndSpecial = false;
		action = null!; return false;
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
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.DefenseSingle))
		{
			IBaseAction.ShouldEndSpecial = true;
		}

		IBaseAction.ShouldEndSpecial = false;
		act = null!; return false;
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
		if (DataCenter.CommandStatus.HasFlag(AutoStatus.DefenseArea))
		{
			IBaseAction.ShouldEndSpecial = true;
		}

		IBaseAction.ShouldEndSpecial = false;
		act = null; return false;
	}

	/// <summary>
	/// Attempts to use the General GCD action.
	/// </summary>
	/// <param name="act">The action to be performed.</param>
	/// <returns>True if the action can be used; otherwise, false.</returns>
	protected virtual bool GeneralGCD(out IAction? act)
	{
		act = null;
		act = null; return false;
	}
}
