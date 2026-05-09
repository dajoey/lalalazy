namespace RotationSolver.Basic.Rotations.Basic;

public partial class RedMageRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Intelligence;


	#region Job Gauge
	/// <summary>
	/// 
	/// </summary>
	public static byte WhiteMana => JobGauge.WhiteMana;

	/// <summary>
	/// 
	/// </summary>
	public static byte BlackMana => JobGauge.BlackMana;

	/// <summary>
	/// 
	/// </summary>
	public static byte ManaStacks => JobGauge.ManaStacks;

	/// <summary>
	/// Is <see cref="WhiteMana"/> larger than <see cref="BlackMana"/>
	/// </summary>
	public static bool MoreWhiteMana => WhiteMana > BlackMana;

	/// <summary>
	/// 
	/// </summary>
	public static bool MoreBlackMana => BlackMana >= WhiteMana;

	/// <summary>
	/// 
	/// </summary>
	public static bool BlackWhiteEqual => WhiteMana == BlackMana;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnoughManaFor1Combo => BlackMana >= 20 && WhiteMana >= 20;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnoughManaFor23Combo => BlackMana >= 15 && WhiteMana >= 15;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEnoughManaFor4Combo => BlackMana >= 5 && WhiteMana >= 5;

	/// <summary>
	/// 
	/// </summary>
	public static bool ManaStacksMaxed => JobGauge.ManaStacks == 3;

	/// <summary>
	/// 
	/// </summary>
	public static bool CanUseFlare => MoreBlackMana && JobGauge.BlackMana - JobGauge.WhiteMana < 18;

	/// <summary>
	/// 
	/// </summary>
	public static bool CanUseHoly => MoreWhiteMana && JobGauge.WhiteMana - JobGauge.BlackMana < 18;

	/// <summary>
	/// 
	/// </summary>
	public int ManaNeededNoPooling()
	{
		if (HasEmbolden) return 50;
		return 50;
	}

	/// <summary>
	/// Asymmetric mana gating for starting melee: separate requirements for White and Black.
	/// Pools to 42|31 when Embolden is approaching, falls back to 50.
	/// </summary>
	public int ManaNeededWhite()
	{
		if (HasEmbolden) return 50;
		return 50;
	}

	/// <summary>
	/// Black mana counterpart for asymmetric pooling.
	/// </summary>
	public int ManaNeededBlack()
	{
		if (HasEmbolden) return 50;
		return 50;
	}

	/// <summary>
	/// Backward-compatible aggregate for callers that still use a single threshold.
	/// Returns the max of both per-color needs to remain conservative.
	/// </summary>
	public int ManaNeeded()
	{
		var w = ManaNeededWhite();
		var b = ManaNeededBlack();
		return Math.Max(w, b);
	}

	/// <summary>
	/// Enough mana for starting melee, respecting asymmetric pooling.
	/// </summary>
	public bool EnoughManaComboPooling => CanMagickedSwordplay || (JobGauge.WhiteMana >= ManaNeededWhite() && JobGauge.BlackMana >= ManaNeededBlack());

	/// <summary>
	/// Enough mana for starting melee, respecting asymmetric pooling.
	/// </summary>
	public bool EnoughManaComboNoPooling => CanMagickedSwordplay || (JobGauge.BlackMana >= ManaNeededNoPooling() && JobGauge.WhiteMana >= ManaNeededNoPooling());

	/// <summary>
	/// 
	/// </summary>
	public static string? VerEndsFirst
	{
		get
		{
			if (!CanVerBoth)
				return null;
			if (VerStoneTime == null || VerFireTime == null)
				return null;
			if (VerStoneTime < VerFireTime)
				return "VerStone";
			if (VerFireTime < VerStoneTime)
				return "VerFire";
			return "Equal";
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public bool IsInMeleeCombo
	{
		get
		{
			return StatusHelper.PlayerHasStatus(true, StatusID.EnchantedRiposte)
				|| StatusHelper.PlayerHasStatus(true, StatusID.EnchantedZwerchhau_3238)
				|| StatusHelper.PlayerHasStatus(true, StatusID.EnchantedRedoublement_3239);
		}
	}
	#endregion

	#region Status Check
	/// <summary>
	/// 
	/// </summary>
	public static bool HasEmbolden => StatusHelper.PlayerHasStatus(true, StatusID.Embolden);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasEmbolden2 => StatusHelper.PlayerHasStatus(true, StatusID.Embolden_1297);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasDualcast => StatusHelper.PlayerHasStatus(true, StatusID.Dualcast);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAccelerate => StatusHelper.PlayerHasStatus(true, StatusID.Acceleration);

	/// <summary>
	/// Time left on VerFire.
	/// </summary>
	public static float? VerFireTime => StatusHelper.PlayerStatusTime(true, StatusID.VerfireReady);

	/// <summary>
	/// Time left on VerStone.
	/// </summary>
	public static float? VerStoneTime => StatusHelper.PlayerStatusTime(true, StatusID.VerstoneReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool CanVerBoth => CanVerStone && CanVerFire;

	/// <summary>
	/// 
	/// </summary>
	public static bool CanVerEither => CanVerFire || CanVerStone;

	/// <summary>
	/// 
	/// </summary>
	public static bool CanVerStone => StatusHelper.PlayerHasStatus(true, StatusID.VerstoneReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool CanVerFire => StatusHelper.PlayerHasStatus(true, StatusID.VerfireReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool CanGrandImpact => StatusHelper.PlayerHasStatus(true, StatusID.GrandImpactReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool CanMagickedSwordplay => StatusHelper.PlayerHasStatus(true, StatusID.MagickedSwordplay);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasManafication => StatusHelper.PlayerHasStatus(true, StatusID.Manafication);

	/// <summary>
	/// 
	/// </summary>
	public static bool CanPrefulgence => StatusHelper.PlayerHasStatus(true, StatusID.PrefulgenceReady);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasThornedFlourish => StatusHelper.PlayerHasStatus(true, StatusID.ThornedFlourish);

	/// <summary>
	/// 
	/// </summary>
	public static bool CanInstantCast => HasSwift || HasAccelerate;
	#endregion

	#region Status Display
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("WhiteMana: " + WhiteMana.ToString());
		ImGui.Text("BlackMana: " + BlackMana.ToString());
		ImGui.Text("ManaStacks: " + ManaStacks.ToString());
		ImGui.Text("MoreWhiteMana: " + MoreWhiteMana.ToString());
		ImGui.Text("Can Heal Single Spell: " + CanHealSingleSpell.ToString());
		ImGui.Spacing();
		ImGui.Text("ManaNeededBlack: " + ManaNeededBlack().ToString());
		ImGui.Text("ManaNeededWhite: " + ManaNeededWhite().ToString());
		ImGui.Text("EnoughManaComboPooling: " + EnoughManaComboPooling.ToString());
		ImGui.Spacing();
		ImGui.Text("ManaNeededNoPooling: " + ManaNeededNoPooling().ToString());
		ImGui.Text("EnoughManaComboNoPooling: " + EnoughManaComboNoPooling.ToString());
	}
	#endregion


	#region PvP Actions
	static partial void ModifyJoltIiiPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Dualcast_1393];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyGrandImpactPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Dualcast_1393];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyEnchantedRipostePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.EnchantedRipostePvP) == ActionID.EnchantedRipostePvP;
		setting.StatusProvide = [StatusID.EnchantedRiposte];
		setting.IgnoreGuard = true;
	}

	static partial void ModifyEnchantedZwerchhauPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => IsLastComboAction(ActionID.EnchantedRipostePvP);
		setting.StatusProvide = [StatusID.EnchantedZwerchhau_3238];
		setting.IgnoreGuard = true;
	}

	static partial void ModifyEnchantedRedoublementPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => IsLastComboAction(ActionID.EnchantedZwerchhauPvP);
		setting.StatusProvide = [StatusID.EnchantedRedoublement_3239];
		setting.IgnoreGuard = true;
	}

	static partial void ModifyScorchPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => IsLastComboAction(ActionID.EnchantedRedoublementPvP);
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyResolutionPvP(ref ActionSetting setting)
	{
		setting.TargetStatusProvide = [StatusID.Silence_1347];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyEmboldenPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Embolden_2282, StatusID.PrefulgenceReady_4322];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyCorpsacorpsPvP(ref ActionSetting setting)
	{
		setting.TargetStatusProvide = [StatusID.Monomachy_3242];
		setting.SpecialType = SpecialActionType.HostileMovingForward;
	}

	static partial void ModifyDisplacementPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Displacement_3243];
	}

	static partial void ModifyFortePvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Forte];
	}

	static partial void ModifyPrefulgencePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.PrefulgenceReady_4322];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyViceOfThornsPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.ThornedFlourish_4321];
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}
	#endregion
}