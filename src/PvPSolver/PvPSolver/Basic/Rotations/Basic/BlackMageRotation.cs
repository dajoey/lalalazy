using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class BlackMageRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Intelligence;

	/// <inheritdoc/>
	public override bool IsBursting()
	{
		if (IsEnochianActive && StatusHelper.PlayerHasStatus(true, StatusID.LeyLines))
		{
			return true;
		}
		return false;
	}

	// Umbral Soul level 35 now
	#region Job Gauge

	/// <summary>
	/// 
	/// </summary>
	public static byte UmbralIceStacks => JobGauge.UmbralIceStacks;

	/// <summary>
	/// 
	/// </summary>
	public static byte AstralFireStacks => JobGauge.AstralFireStacks;

	/// <summary>
	/// 
	/// </summary>
	public static int AstralSoulStacks => JobGauge.AstralSoulStacks;

	/// <summary>
	/// 
	/// </summary>
	public static byte PolyglotStacks => JobGauge.PolyglotStacks;

	/// <summary>
	/// 
	/// </summary>
	public static byte UmbralHearts => JobGauge.UmbralHearts;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsParadoxActive => JobGauge.IsParadoxActive;

	/// <summary>
	/// 
	/// </summary>
	public static bool InUmbralIce => JobGauge.InUmbralIce;

	/// <summary>
	/// 
	/// </summary>
	public static bool InAstralFire => JobGauge.InAstralFire;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsEnochianActive => JobGauge.IsEnochianActive;

	/// <summary>
	/// 
	/// </summary>
	private static float EnochianTimeRaw => JobGauge.EnochianTimer / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float EnochianTime => EnochianTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool EnochianEndAfter(float time)
	{
		return EnochianTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gcdCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool EnochianEndAfterGCD(uint gcdCount = 0, float offset = 0)
	{
		return EnochianEndAfter(GCDTime(gcdCount, offset));
	}

	/// <summary>
	/// 
	/// </summary>
	protected static float Fire4Time { get; private set; }

	/// <summary>
	/// 
	/// </summary>
	protected override void UpdateInfo()
	{
		if (Player?.CastActionId == (uint)ActionID.FireIvPvP && Player.CurrentCastTime < 0.2)
		{
			Fire4Time = Player.TotalCastTime;
		}
		base.UpdateInfo();
	}

	/// <summary>
	/// Returns the higher value between Astral Fire stacks and Umbral Ice stacks.
	/// </summary>
	public static byte SoulStackCount => Math.Max(AstralFireStacks, UmbralIceStacks);

	/// <summary>
	/// A check with variable max stacks of Astral Fire stacks and Umbral Ice based on the trait level.
	/// </summary>
	public static bool IsSoulStacksMaxed => DataCenter.PlayerSyncedLevel() >= 35 ? SoulStackCount == 3 : DataCenter.PlayerSyncedLevel() >= 20 ? SoulStackCount == 2 : SoulStackCount == 1;

	/// <summary>
	/// A check with variable max stacks of Astral Fire stacks and Umbral Ice based on the trait level.
	/// </summary>
	public static byte MaxSoulCount => DataCenter.PlayerSyncedLevel() >= 35 ? (byte)3 : DataCenter.PlayerSyncedLevel() >= 20 ? (byte)2 : (byte)1;

	/// <summary>
	/// A check with variable max stacks of Polyglot based on the trait level.
	/// </summary>
	public static bool IsPolyglotStacksMaxed => EnhancedPolyglotIiTrait.EnoughLevel
				? PolyglotStacks == 3
				: EnhancedPolyglotTrait.EnoughLevel ? PolyglotStacks == 2 : PolyglotStacks == 1;
	#endregion

	#region Status Tracking
	/// <summary>
	/// 
	/// </summary>
	protected static bool HasPvPAstralFire => StatusHelper.PlayerHasStatus(true, StatusID.AstralFire_3212, StatusID.AstralFireIi_3213, StatusID.AstralFireIii_3381);

	/// <summary>
	/// 
	/// </summary>
	protected static bool HasPvPUmbralIce => StatusHelper.PlayerHasStatus(true, StatusID.UmbralIce_3214, StatusID.UmbralIceIi_3215, StatusID.UmbralIceIii_3382);

	/// <summary>
	/// 
	/// </summary>
	protected static bool HasPvPSoulResonance => StatusHelper.PlayerHasStatus(true, StatusID.SoulResonance);

	/// <summary>
	/// 
	/// </summary>
	protected static bool HasFire => StatusHelper.PlayerHasStatus(true, StatusID.Firestarter);

	/// <summary>
	/// 
	/// </summary>
	protected static bool HasLeyLines => StatusHelper.PlayerHasStatus(true, StatusID.LeyLines);

	/// <summary>
	///
	/// </summary>
	protected static bool HasThunder => StatusHelper.PlayerHasStatus(true, StatusID.Thunderhead);

	/// <summary>
	/// Indicates whether the next GCD (Global Cooldown) action is instant.
	/// </summary>
	protected static bool NextGCDisInstant => StatusHelper.PlayerHasStatus(true, StatusID.Triplecast, StatusID.Swiftcast);

	#endregion


	#region Debug
	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("Is next GCD be instant: " + NextGCDisInstant.ToString());
		ImGui.Text("HasFire: " + HasFire.ToString());
		ImGui.Text("HasThunder: " + HasThunder.ToString());
		ImGui.Separator();
		ImGui.Text("PolyglotStacks: " + PolyglotStacks.ToString());
		ImGui.Text("IsPolyglotStacksMaxed: " + IsPolyglotStacksMaxed.ToString());
		ImGui.Separator();
		ImGui.Text("InUmbralIce: " + InUmbralIce.ToString());
		ImGui.Text("InAstralFire: " + InAstralFire.ToString());
		ImGui.Separator();
		ImGui.Text("UmbralIceStacks: " + UmbralIceStacks.ToString());
		ImGui.Text("AstralFireStacks: " + AstralFireStacks.ToString());
		ImGui.Text("AstralSoulStacks: " + AstralSoulStacks.ToString());
		ImGui.Text("Soul Stack Count: " + SoulStackCount.ToString());
		ImGui.Text("Is Soul Stacks Maxed: " + IsSoulStacksMaxed.ToString());
		ImGui.Text("Max Soul Stacks: " + MaxSoulCount.ToString());
		ImGui.Separator();
		ImGui.Text("UmbralHearts: " + UmbralHearts.ToString());
		ImGui.Text("IsParadoxActive: " + IsParadoxActive.ToString());
		ImGui.Text("IsEnochianActive: " + IsEnochianActive.ToString());
		ImGui.Text("EnochianTimeRaw: " + EnochianTimeRaw.ToString());
		ImGui.Text("EnochianTime: " + EnochianTime.ToString());
		ImGui.TextColored(ImGuiColors.DalamudOrange, "PvP Actions");
		ImGui.Text("HasPvPAstralFire: " + HasPvPAstralFire.ToString());
		ImGui.Text("HasPvPUmbralIce: " + HasPvPUmbralIce.ToString());
	}
	#endregion


	#region PvP Actions Unassignable
	/// <summary>
	/// 
	/// </summary>
	public static bool WreathOfFireReady => Service.GetAdjustedActionId(ActionID.ElementalWeavePvP) == ActionID.WreathOfFirePvP;

	/// <summary>
	/// 
	/// </summary>
	public static bool WreathOfIceReady => Service.GetAdjustedActionId(ActionID.ElementalWeavePvP) == ActionID.WreathOfIcePvP;
	#endregion

	#region PvP Actions
	static partial void ModifyFirePvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Paradox, StatusID.AstralFire_3212];
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyBlizzardPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Paradox, StatusID.UmbralIce_3214];
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyBurstPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyParadoxPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.Paradox];
		setting.MPOverride = () => 0;
	}

	static partial void ModifyXenoglossyPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyLethargyPvP(ref ActionSetting setting)
	{
		setting.TargetStatusProvide = [StatusID.Lethargy, StatusID.Heavy_1344];
	}

	static partial void ModifyAetherialManipulationPvP(ref ActionSetting setting)
	{
		setting.SpecialType = SpecialActionType.FriendlyMovingForward;
	}

	static partial void ModifyElementalWeavePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.IsFriendly = true;
	}

	static partial void ModifyFireIiiPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.AstralFire_3212];
		setting.StatusProvide = [StatusID.AstralFireIi_3213];
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyFireIvPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.AstralFireIi_3213];
		setting.StatusProvide = [StatusID.AstralFireIii_3381];
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyHighFireIiPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.AstralFireIii_3381];
		setting.StatusProvide = [StatusID.AstralFire_3212];
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyFlarePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.SoulResonance];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.MPOverride = () => 0;
	}

	static partial void ModifyBlizzardIiiPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.UmbralIce_3214];
		setting.StatusProvide = [StatusID.UmbralIceIi_3215];
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyBlizzardIvPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.UmbralIceIi_3215];
		setting.StatusProvide = [StatusID.UmbralIceIii_3382];
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyHighBlizzardIiPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.UmbralIceIii_3382];
		setting.StatusProvide = [StatusID.UmbralIce_3214];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
			// Removed ShouldCheckStatus = false
		};
		setting.ActionCheck = () => !HasPvPSoulResonance;
	}

	static partial void ModifyFreezePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.SoulResonance];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.MPOverride = () => 0;
	}

	static partial void ModifyWreathOfFirePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => WreathOfFireReady;
	}

	static partial void ModifyWreathOfIcePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => WreathOfIceReady;
		setting.IsFriendly = true;
	}

	static partial void ModifyFlareStarPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.ElementalStar];
		setting.ActionCheck = () => HasPvPAstralFire;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.MPOverride = () => 0;
	}

	static partial void ModifyFrostStarPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.ElementalStar];
		setting.ActionCheck = () => HasPvPUmbralIce;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.MPOverride = () => 0;
	}

	#endregion
}
