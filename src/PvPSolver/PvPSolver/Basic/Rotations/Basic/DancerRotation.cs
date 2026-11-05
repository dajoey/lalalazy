using Dalamud.Interface.Colors;

namespace RotationSolver.Basic.Rotations.Basic;

public partial class DancerRotation : CustomRotation
{
	/// <summary>
	/// 
	/// </summary>
	public override MedicineType MedicineType => MedicineType.Dexterity;

	#region Job Gauge
	/// <summary>
	/// 
	/// </summary>
	public static bool IsDancing => JobGauge.IsDancing;

	/// <summary>
	/// 
	/// </summary>
	public static byte Esprit => JobGauge.Esprit;

	/// <summary>
	/// 
	/// </summary>
	public static byte Feathers => JobGauge.Feathers;

	/// <summary>
	/// 
	/// </summary>
	public static byte CompletedSteps => JobGauge.CompletedSteps;

	/// <summary>
	/// 
	/// </summary>
	public static IBattleChara? CurrentDancePartner
	{
		get
		{
			if (StatusHelper.PlayerHasStatus(true, StatusID.ClosedPosition))
			{
				foreach (var member in PartyMembers)
				{
					if (member.HasStatus(true, StatusID.DancePartner))
						return member;
				}
			}
			return null;
		}
	}
	#endregion


	#region Debug Status

	/// <inheritdoc/>
	public override void DisplayBaseStatus()
	{
		ImGui.Text("IsDancing: " + IsDancing.ToString());
		ImGui.Text("Esprit: " + Esprit.ToString());
		ImGui.Text("Feathers: " + Feathers.ToString());
		ImGui.Text("CompletedSteps: " + CompletedSteps.ToString());
		ImGui.TextColored(ImGuiColors.DalamudOrange, "Status Tracking");
	}
	#endregion


	#region PvP

	/// <summary>
	/// 
	/// </summary>

	static partial void ModifyCascadePvP(ref ActionSetting setting)
	{

	}

	static partial void ModifyFountainPvP(ref ActionSetting setting)
	{
		setting.ComboIds = [ActionID.CascadePvP, ActionID.ReverseCascadePvP];
	}

	static partial void ModifyClosedPositionPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
		setting.StatusProvide = [StatusID.ClosedPosition];
		setting.TargetStatusProvide = [StatusID.DancePartner];
		setting.TargetType = TargetType.DancePartner;
	}

	static partial void ModifyEnAvantPvP(ref ActionSetting setting)
	{
		//setting.SpecialType = SpecialActionType.MovingForward;
		setting.IsFriendly = true;
		setting.StatusProvide = [StatusID.EnAvant];
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyStarfallDancePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.StatusProvide = [StatusID.StarfallDance];
	}

	static partial void ModifyFanDancePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.StatusProvide = [StatusID.FanDance];
	}

	static partial void ModifyCuringWaltzPvP(ref ActionSetting setting)
	{
		setting.IsFriendly = true;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
	}

	static partial void ModifyReverseCascadePvP(ref ActionSetting setting)
	{
		setting.MPOverride = () => 0;
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.StatusNeed = [StatusID.EnAvant];
		setting.StatusProvide = [StatusID.Bladecatcher];
	}

	static partial void ModifyFountainfallPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.MPOverride = () => 0;
		setting.StatusNeed = [StatusID.EnAvant];
		setting.StatusProvide = [StatusID.Bladecatcher];
	}

	static partial void ModifySaberDancePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.MPOverride = () => 0;
		setting.StatusNeed = [StatusID.FlourishingSaberDance];
		setting.StatusProvide = [StatusID.SaberDance];
	}

	static partial void ModifyDanceOfTheDawnPvP(ref ActionSetting setting)
	{
		setting.ActionCheck = () =>
		{
			return StatusHelper.PlayerHasStatus(true, StatusID.FlourishingSaberDance) && StatusHelper.PlayerHasStatus(true, StatusID.SoloStep);
		};
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.MPOverride = () => 0;
		setting.StatusNeed = [StatusID.FlourishingSaberDance, StatusID.SoloStep];
	}

	static partial void ModifyHoningDancePvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.StatusProvide = [StatusID.HoningDance];

	}

	static partial void ModifyHoningOvationPvP(ref ActionSetting setting)
	{
		setting.CreateConfig = () => new ActionConfig()
		{
			AoeCount = 1,
		};
		setting.StatusProvide = [StatusID.HoningOvation];
	}

	#endregion

	// PvP helper — pulled from upstream DancerRotation.cs:139 to satisfy DNC_DefaultPvP after rotation sync.
	public static bool HasHoningDance => StatusHelper.PlayerHasStatus(true, StatusID.HoningDance);
}
