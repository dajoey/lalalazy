namespace RotationSolver.Basic.Rotations.Basic;

public partial class SageRotation : CustomRotation
{

	#region Job Gauge
	/// <summary>
	/// Gets a value indicating whether Eukrasia is activated. Eukrasia = 1, none = 0
	/// </summary>
	public static bool HasEukrasia => JobGauge.Eukrasia;

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("HasEukrasia: " + HasEukrasia.ToString());
	}
	#endregion

	#region PvP Actions
	static partial void ModifyDosisIiiPvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyPhlegmaIiiPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyPneumaPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};

	}

	static partial void ModifyEukrasiaPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Eukrasia_3107];
		setting.IsFriendly = true;
	}

	static partial void ModifyIcarusPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
	}

	static partial void ModifyToxikonPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyKardiaPvP(ref ActionSetting setting)
	{
		setting.StatusProvide = [StatusID.Kardia_2871];
		setting.TargetStatusProvide = [StatusID.Kardion_2872];
		setting.TargetType = TargetType.Kardia;
	}

	static partial void ModifyEukrasianDosisIiiPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => Service.GetAdjustedActionId(ActionID.DosisIiiPvP) == ActionID.EukrasianDosisIiiPvP;
	}

	static partial void ModifyPsychePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyToxikonIiPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () => StatusHelper.PlayerHasStatus(true, StatusID.Addersting);
		setting.TargetStatusProvide = [StatusID.Toxikon];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}


	#endregion
}
