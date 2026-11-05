using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class SummonerRotation : CustomRotation
{
	/// <inheritdoc/>
	public override MedicineType MedicineType => MedicineType.Intelligence;


	#region JobGauge

	/// <summary>
	/// 
	/// </summary>
	public static ushort SummonTimerRemaining => JobGauge.SummonTimerRemaining;

	/// <summary>
	/// 
	/// </summary>
	public static ushort AttunementTimerRemaining => JobGauge.AttunementTimerRemaining;

	/// <summary>
	/// 
	/// </summary>
	public static SummonPet ReturnSummon => JobGauge.ReturnSummon;

	/// <summary>
	/// 
	/// </summary>
	public static bool RubyAttunement => JobGauge.AttunementType == SummonAttunement.Ifrit && JobGauge.AttunementCount > 0;

	/// <summary>
	///
	/// </summary>
	public static bool TopazAttunement => JobGauge.AttunementType == SummonAttunement.Titan && JobGauge.AttunementCount > 0;

	/// <summary>
	///
	/// </summary>
	public static bool EmeraldAttunement => JobGauge.AttunementType == SummonAttunement.Garuda && JobGauge.AttunementCount > 0;

	/// <summary>
	/// 
	/// </summary>
	public static byte AttunementCount => JobGauge.AttunementCount;

	/// <summary>
	/// 
	/// </summary>
	public static SummonAttunement AttunementType => JobGauge.AttunementType;

	/// <summary>
	/// 
	/// </summary>
	public static bool GarudaActive => JobGauge.AttunementType == SummonAttunement.Garuda;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsSolarBahamutReady => JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)8) || JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)12);

	/// <summary>
	/// 
	/// </summary>
	public static AetherFlags AetherFlags => JobGauge.AetherFlags;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsPhoenixReady => JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)4) && !JobGauge.AetherFlags.HasFlag((Dalamud.Game.ClientState.JobGauge.Enums.AetherFlags)8);

	/// <summary>
	/// 
	/// </summary>
	public static bool IsPhoenixReady => JobGauge.IsPhoenixReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsIfritReady => JobGauge.IsIfritReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsTitanReady => JobGauge.IsTitanReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool IsGarudaReady => JobGauge.IsGarudaReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAetherflowStacks => JobGauge.HasAetherflowStacks;

	/// <summary>
	/// 
	/// </summary>
	public static byte AetherflowStacks => JobGauge.AetherflowStacks;

	/// <summary>
	/// 
	/// </summary>
	/// <summary>
	/// 
	/// </summary>
	public static bool NoElementalSummon => JobGauge.AttunementType == SummonAttunement.None && !InPhoenix && !InBahamut && !InSolarBahamut;

	/// <summary>
	/// 
	/// </summary>
	public static float SummonTimeRaw => JobGauge.SummonTimerRemaining / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float SummonTime => SummonTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool SummonTimeEndAfter(float time)
	{
		return SummonTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gcdCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool SummonTimeEndAfterGCD(uint gcdCount = 0, float offset = 0)
	{
		return SummonTimeEndAfter(GCDTime(gcdCount, offset));
	}

	private static float AttunmentTimeRaw => JobGauge.AttunementTimerRemaining / 1000f;

	/// <summary>
	/// 
	/// </summary>
	public static float AttunmentTime => AttunmentTimeRaw - DataCenter.DefaultGCDRemain;

	/// <summary>
	/// 
	/// </summary>
	/// <param name="time"></param>
	/// <returns></returns>
	protected static bool AttunmentTimeEndAfter(float time)
	{
		return AttunmentTime <= time;
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="gcdCount"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	protected static bool AttunmentTimeEndAfterGCD(uint gcdCount = 0, float offset = 0)
	{
		return AttunmentTimeEndAfter(GCDTime(gcdCount, offset));
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSummon => DataCenter.HasPet() && SummonTimeEndAfterGCD();

	/// <summary>
	/// 
	/// </summary>

	/// <summary>
	/// 
	/// </summary>

	/// <summary>
	/// 
	/// </summary>
	public static bool NoPrimalReady => !IsIfritReady && !IsGarudaReady && !IsTitanReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool AnyPrimalReady => IsIfritReady || IsGarudaReady || IsTitanReady;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAnyFavor => HasGarudaFavor || HasIfritFavor || HasTitanFavor;

	/// <summary>
	/// 
	/// </summary>
	public static bool HasAnyAttunement => EmeraldAttunement || RubyAttunement || TopazAttunement;

	/// <summary>
	/// 
	/// </summary>
	public static bool NoAttunement => !RubyAttunement && !EmeraldAttunement && !TopazAttunement;

	/// <summary>
	/// 
	/// </summary>
	#endregion

	#region Status
	/// <summary>
	/// 
	/// </summary>
	public static bool HasFurtherRuin => StatusHelper.PlayerHasStatus(true, StatusID.FurtherRuin_2701);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasCrimsonStrike => StatusHelper.PlayerHasStatus(true, StatusID.CrimsonStrikeReady_4403);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasRadiantAegis => StatusHelper.PlayerHasStatus(true, StatusID.RadiantAegis);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasGarudaFavor => StatusHelper.PlayerHasStatus(true, StatusID.GarudasFavor);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasIfritFavor => StatusHelper.PlayerHasStatus(true, StatusID.IfritsFavor);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasTitanFavor => StatusHelper.PlayerHasStatus(true, StatusID.TitansFavor);

	/// <summary>
	/// 
	/// </summary>
	public static bool HasSearingLight => StatusHelper.PlayerHasStatus(true, StatusID.SearingLight);

	#endregion


	#region Draw Debug

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("ReturnSummon: " + ReturnSummon.ToString());
		ImGui.Text("SummonTime: " + SummonTime.ToString());
		ImGui.Text("HasSummon: " + HasSummon.ToString());
		ImGui.Text("HasPet: " + DataCenter.HasPet().ToString());
		ImGui.Spacing();
		ImGui.Text("HasAetherflowStacks: " + HasAetherflowStacks.ToString());
		ImGui.Text("AetherflowStacks: " + AetherflowStacks.ToString());
		ImGui.Spacing();
		ImGui.TextColored(RubyAttunement ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "RubyAttunement: " + RubyAttunement.ToString());
		ImGui.TextColored(EmeraldAttunement ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "EmeraldAttunement: " + EmeraldAttunement.ToString());
		ImGui.TextColored(TopazAttunement ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "TopazAttunement: " + TopazAttunement.ToString());
		ImGui.Text("AttunementCount: " + AttunementCount.ToString());
		ImGui.Text("AttunmentTime: " + AttunmentTime.ToString());
		ImGui.Spacing();
		ImGui.TextColored(IfritActive ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "IfritActive: " + IfritActive.ToString());
		ImGui.TextColored(GarudaActive ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "GarudaActive: " + GarudaActive.ToString());
		ImGui.TextColored(TitanActive ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "TitanActive: " + TitanActive.ToString());
		ImGui.Text("AttunementType: " + AttunementType.ToString());
		ImGui.Spacing();
		ImGui.TextColored(IsIfritReady ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "IsIfritReady: " + IsIfritReady.ToString());
		ImGui.TextColored(IsGarudaReady ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "IsGarudaReady: " + IsGarudaReady.ToString());
		ImGui.TextColored(IsTitanReady ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "IsTitanReady: " + IsTitanReady.ToString());
		ImGui.Spacing();
		ImGui.TextColored(IsSolarBahamutReady ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "IsSolarBahamutReady: " + IsSolarBahamutReady.ToString());
		ImGui.TextColored(IsBahamutReady ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "IsBahamutReady: " + IsBahamutReady.ToString());
		ImGui.TextColored(IsPhoenixReady ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "IsPhoenixReady: " + IsPhoenixReady.ToString());
		ImGui.Spacing();
		ImGui.TextColored(InIfrit ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "InIfrit: " + InIfrit.ToString());
		ImGui.TextColored(InTitan ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "InTitan: " + InTitan.ToString());
		ImGui.TextColored(InGaruda ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite, "InGaruda: " + InGaruda.ToString());
		ImGui.Spacing();
		ImGui.Text("Can Heal Single Spell: " + CanHealSingleSpell.ToString());
	}
	#endregion


	#region PvP Actions
	static partial void ModifyRuinIiiPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyRuinIvPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RuinIiiPvP) == ActionID.RuinIvPvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyMountainBusterPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.TargetStatusProvide = [StatusID.Stun_1343];
		setting.StatusProvide = [StatusID.MountainBuster];
	}

	static partial void ModifySlipstreamPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.TargetStatusProvide = [StatusID.Slipping];
	}

	static partial void ModifyCrimsonCyclonePvP(ref ActionSetting setting)
	{
		setting.SpecialType = SpecialActionType.HostileMovingAttack;
		setting.StatusProvide = [StatusID.CrimsonStrikeReady_4403];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyCrimsonStrikePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.CrimsonCyclonePvP) == ActionID.CrimsonStrikePvP;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyRadiantAegisPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.RadiantAegis_3224];
	}

	static partial void ModifyNecrotizePvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.FurtherRuin_4399];
	}

	static partial void ModifyDeathflarePvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.DreadwyrmTrance_3228];
		setting.MPOverride = () => 0;
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyAstralImpulsePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RuinIiiPvP) == ActionID.AstralImpulsePvP;
	}

	static partial void ModifyBrandOfPurgatoryPvP(ref ActionSetting setting)
	{
		setting.StatusNeed = [StatusID.FirebirdTrance];
		setting.MPOverride = () => 0;
		setting.IsFriendly = false;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyFountainOfFirePvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.RuinIiiPvP) == ActionID.FountainOfFirePvP;
	}

	static partial void ModifyMegaflarePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			IsEnabled = false,
		};
	}

	static partial void ModifyEverlastingFlightPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			IsEnabled = false,
		};
	}
	#endregion

}
