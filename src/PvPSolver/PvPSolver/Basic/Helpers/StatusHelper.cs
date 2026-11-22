using Dalamud.Game.ClientState.Statuses;
using ECommons.Automation;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;
using Lumina.Excel;
using RotationSolver.Basic.Configuration;
using GameStatus = FFXIVClientStructs.FFXIV.Client.Game.Status;
using StatusRow = Lumina.Excel.Sheets.Status;

namespace RotationSolver.Basic.Helpers;

/// <summary>
/// The helper for the status.
/// </summary>
public static class StatusHelper
{
	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] RangePhysicalDefense { get; } =
	[
		StatusID.Troubadour,
		StatusID.Tactician_1951,
		StatusID.Tactician_2177,
		StatusID.ShieldSamba,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] PhysicalResistance { get; } =
	[
		StatusID.IceSpikes_1720,
		StatusID.IceSpikes_1307
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] PhysicalRangedResistance { get; } =
	[
		StatusID.RangedResistance,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] MagicResistance { get; } =
	[
		StatusID.MagicResistance,
		StatusID.MagicResistance_3621,
		StatusID.RepellingSpray_556,
		StatusID.MagitekField_2166,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] AreaHots { get; } =
	[
		StatusID.AspectedHelios,
		StatusID.HeliosConjunction,
		StatusID.MedicaIi,
		StatusID.TrueMedicaIi,
		StatusID.PhysisIi,
		StatusID.Physis,
		StatusID.SacredSoil_1944,
		StatusID.WhisperingDawn,
		StatusID.AngelsWhisper,
		StatusID.Seraphism_3885,
		StatusID.Asylum_1911,
		StatusID.DivineAura,
		StatusID.MedicaIii_3986,
		StatusID.MedicaIii
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] SingleHots { get; } =
	[
		StatusID.AspectedBenefic,
		StatusID.Regen,
		StatusID.Regen_897,
		StatusID.Regen_1330,
		StatusID.TheEwer_3891,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] TankStanceStatus { get; } =
	[
		StatusID.Grit,
		StatusID.RoyalGuard_1833,
		StatusID.IronWill,
		StatusID.Defiance,
		StatusID.Defiance_3124,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] NoNeedHealingStatus { get; } =
	[
		StatusID.Holmgang_409,
		StatusID.LivingDead,
        //StatusID.WalkingDead,
        StatusID.Superbolide,
		StatusID.Invulnerability,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] SwiftcastStatus { get; } =
	[
		StatusID.Swiftcast,
		StatusID.Triplecast,
		StatusID.Dualcast
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] RampartStatus { get; } =
	[
		StatusID.Superbolide,
		StatusID.HallowedGround,
		StatusID.Rampart,
		StatusID.Bulwark,
		StatusID.Bloodwhetting,
		StatusID.Vengeance,
		StatusID.Sentinel,
		StatusID.ShadowWall,
		StatusID.Nebula,
		StatusID.GreatNebula,
		StatusID.Holmgang_409,
		StatusID.LivingDead,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] DoomHealStatus { get; } =
	[
		StatusID.Doom_1769,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] PurifyPvPStatuses { get; } =
	[
		StatusID.Stun_1343,
		StatusID.Heavy_1344,
		StatusID.Bind_1345,
		StatusID.Silence_1347,
		StatusID.DeepFreeze_3219,
		StatusID.MiracleOfNature,
	];

	/// <summary>
	/// 
	/// </summary>
	public static StatusID[] RotationLockoutStatus { get; } =
	[
		StatusID.Reawakened,
		StatusID.Overheated,
		StatusID.InnerRelease,
		StatusID.Eukrasia,
		StatusID.Mudra,
		StatusID.TenChiJin,
		StatusID.FullMetalMachinist
	];

	/// <summary>
	/// Determines if the specified battle character has reached the maximum number of status effects.
	/// </summary>
	/// <param name="battleChara">The battle character to check.</param>
	/// <returns>
	/// <c>true</c> if the character's status list is at the cap (30 for players, 60 for NPCs); otherwise, <c>false</c>.
	/// </returns>
	public unsafe static bool IsStatusCapped(IBattleChara battleChara)
	{
		if (battleChara == null || battleChara.Struct() == null || !battleChara.IsValid())
		{
			return false;
		}

		foreach (ref readonly GameStatus status in battleChara.GetStatuses())
		{
			if (status.StatusId == 0)
			{
				return false;
			}
		}

		return true;
	}

	internal static unsafe ReadOnlySpan<GameStatus> GetStatuses(this IBattleChara? battleChara)
	{
		if (battleChara == null)
		{
			return default;
		}

		FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara* chara = battleChara.Struct();
		if (chara == null)
		{
			return default;
		}

		var statuses = chara->StatusManager.Status;
		int count = chara->StatusManager.NumValidStatuses;
		return count < statuses.Length ? statuses[..count] : statuses;
	}

	/// <summary>
	/// Check whether the Player needs to be healing.
	/// </summary>
	/// <returns></returns>
	public static bool PlayerNoNeedHealingInvuln()
	{
		if (Player.Object == null)
		{
			return false;
		}

		return PlayerWillStatusEndGCD(2, 0, false, NoNeedHealingStatus);
	}

	/// <summary>
	/// Check whether the target needs to be healing.
	/// </summary>
	/// <param name="Invulnp"></param>
	/// <returns></returns>
	public static bool NoNeedHealingInvuln(this IBattleChara Invulnp)
	{
		return Invulnp.WillStatusEndGCD(2, 0, false, NoNeedHealingStatus);
	}

	/// <summary>
	/// Check if the Player needs to be healed because of Doomed To Heal status.
	/// </summary>
	/// <returns></returns>
	public static bool PlayerDoomNeedHealing()
	{
		if (Player.Object == null)
		{
			return false;
		}

		return PlayerHasStatus(false, DoomHealStatus);
	}

	/// <summary>
	/// Check if the target needs to be healed because of Doomed To Heal status.
	/// </summary>
	/// <param name="Doomp"></param>
	/// <returns></returns>
	public static bool DoomNeedHealing(this IBattleChara Doomp)
	{
		return Doomp.HasStatus(false, DoomHealStatus);
	}

	/// <summary>
	/// Will any of <paramref name="statusIDs"/> end after <paramref name="gcdCount"/> GCDs plus <paramref name="offset"/> seconds?
	/// </summary>
	/// <param name="gcdCount"></param>
	/// <param name="offset"></param>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static bool PlayerWillStatusEndGCD(uint gcdCount = 0, float offset = 0, bool isFromSelf = true, params StatusID[] statusIDs)
	{
		if (Player.Object == null)
		{
			return false;
		}

		return PlayerWillStatusEnd(DataCenter.GCDTime(gcdCount, offset), isFromSelf, statusIDs);
	}

	/// <summary>
	/// Will any of <paramref name="statusIDs"/> end after <paramref name="gcdCount"/> GCDs plus <paramref name="offset"/> seconds?
	/// </summary>
	/// <param name="battleChara"></param>
	/// <param name="gcdCount"></param>
	/// <param name="offset"></param>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static bool WillStatusEndGCD(this IBattleChara battleChara, uint gcdCount = 0, float offset = 0, bool isFromSelf = true, params StatusID[] statusIDs)
	{
		return WillStatusEnd(battleChara, DataCenter.GCDTime(gcdCount, offset), isFromSelf, statusIDs);
	}

	/// <summary>
	/// Will any of <paramref name="statusIDs"/> end after <paramref name="time"/> seconds?
	/// </summary>
	/// <param name="time"></param>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static bool PlayerWillStatusEnd(float time, bool isFromSelf = true, params StatusID[] statusIDs)
	{
		if (PlayerHasApplyStatus(statusIDs))
		{
			return false;
		}

		float statusTime = PlayerStatusTime(isFromSelf, statusIDs);
		return (statusTime >= 0f || !PlayerHasStatus(isFromSelf, statusIDs)) && statusTime <= time;
	}

	/// <summary>
	/// Will any of <paramref name="statusIDs"/> end after <paramref name="time"/> seconds?
	/// </summary>
	/// <param name="battleChara"></param>
	/// <param name="time"></param>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static bool WillStatusEnd(this IBattleChara battleChara, float time, bool isFromSelf = true, params StatusID[] statusIDs)
	{
		if (HasApplyStatus(battleChara, statusIDs))
		{
			return false;
		}

		float statusTime = battleChara.StatusTime(isFromSelf, statusIDs);
		return (statusTime >= 0f || !battleChara.HasStatus(isFromSelf, statusIDs)) && statusTime <= time;
	}

	/// <summary>
	/// Get the remaining time of the status (raw remaining time of the earliest matching status). Returns 0 if none.
	/// NOTE: Previously this subtracted DefaultGCDRemain which caused premature refresh decisions.
	/// </summary>
	public static float PlayerStatusTime(bool isFromSelf, params StatusID[] statusIDs)
	{
		if (Player.Object == null)
		{
			return 0f;
		}

		try
		{
			if (PlayerHasApplyStatus(statusIDs))
			{
				return float.MaxValue;
			}

			IEnumerable<float> times = PlayerStatusTimes(isFromSelf, statusIDs);
			float min = float.MaxValue;
			bool found = false;
			foreach (float t in times)
			{
				if (t < min)
				{
					min = t;
				}
				found = true;
			}
			// Return 0 when not found (legacy behaviour expected by callers), otherwise raw remaining time.
			return !found ? 0f : min;
		}
		catch (Exception ex)
		{
			PluginLog.Error($"Failed to get status time: {ex.Message}");
			return 0f;
		}
	}

	/// <summary>
	/// Get the remaining time of the status (raw remaining time of the earliest matching status). Returns 0 if none.
	/// NOTE: Previously this subtracted DefaultGCDRemain which caused premature refresh decisions.
	/// </summary>
	public static float StatusTime(this IBattleChara battleChara, bool isFromSelf, params StatusID[] statusIDs)
	{
		try
		{
			if (HasApplyStatus(battleChara, statusIDs))
			{
				return float.MaxValue;
			}

			IEnumerable<float> times = battleChara.StatusTimes(isFromSelf, statusIDs);
			float min = float.MaxValue;
			bool found = false;
			foreach (float t in times)
			{
				if (t < min)
				{
					min = t;
				}
				found = true;
			}
			// Return 0 when not found (legacy behaviour expected by callers), otherwise raw remaining time.
			return !found ? 0f : min;
		}
		catch (Exception ex)
		{
			PluginLog.Error($"Failed to get status time: {ex.Message}");
			return 0f;
		}
	}

	internal static IEnumerable<float> PlayerStatusTimes(bool isFromSelf, params StatusID[] statusIDs)
	{
		if (Player.Object == null)
		{
			yield break;
		}

		foreach (IStatus status in Player.Object.GetStatus(isFromSelf, statusIDs))
		{
			yield return status.RemainingTime == 0f ? float.MaxValue : status.RemainingTime;
		}
	}

	internal static IEnumerable<float> StatusTimes(this IBattleChara battleChara, bool isFromSelf, params StatusID[] statusIDs)
	{
		foreach (IStatus status in battleChara.GetStatus(isFromSelf, statusIDs))
		{
			yield return status.RemainingTime == 0f ? float.MaxValue : status.RemainingTime;
		}
	}

	/// <summary>
	/// Get the stack count of the status.
	/// </summary>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static byte PlayerStatusStack(bool isFromSelf, params StatusID[] statusIDs)
	{
		if (Player.Object == null)
		{
			return 0;
		}

		if (PlayerHasApplyStatus(statusIDs))
		{
			return byte.MaxValue;
		}

		IEnumerable<byte> stacks = PlayerStatusStacks(isFromSelf, statusIDs);
		byte min = byte.MaxValue;
		bool found = false;
		foreach (byte s in stacks)
		{
			if (s < min)
			{
				min = s;
			}

			found = true;
		}
		return found ? min : (byte)0;
	}

	/// <summary>
	/// Get the stack count of the status.
	/// </summary>
	/// <param name="battleChara"></param>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static byte StatusStack(this IBattleChara battleChara, bool isFromSelf, params StatusID[] statusIDs)
	{
		if (HasApplyStatus(battleChara, statusIDs))
		{
			return byte.MaxValue;
		}

		IEnumerable<byte> stacks = battleChara.StatusStacks(isFromSelf, statusIDs);
		byte min = byte.MaxValue;
		bool found = false;
		foreach (byte s in stacks)
		{
			if (s < min)
			{
				min = s;
			}

			found = true;
		}
		return found ? min : (byte)0;
	}

	private static IEnumerable<byte> PlayerStatusStacks(bool isFromSelf, params StatusID[] statusIDs)
	{
		if (Player.Object == null)
		{
			yield break;
		}

		foreach (IStatus status in PlayerGetStatus(isFromSelf, statusIDs))
		{
			yield return (byte)(status.Param == 0 ? byte.MaxValue : status.Param);
		}
	}

	private static IEnumerable<byte> StatusStacks(this IBattleChara battleChara, bool isFromSelf, params StatusID[] statusIDs)
	{
		foreach (IStatus status in battleChara.GetStatus(isFromSelf, statusIDs))
		{
			yield return (byte)(status.Param == 0 ? byte.MaxValue : status.Param);
		}
	}

	/// <summary>
	/// Check if the player object has any of the specified statuses.
	/// </summary>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static bool PlayerHasStatus(bool isFromSelf, params StatusID[] statusIDs)
	{
		if (Player.Object == null)
		{
			return false;
		}

		if (Player.Object.StatusList == null)
		{
			return false;
		}

		if (PlayerHasApplyStatus(statusIDs))
		{
			return true;
		}

		foreach (IStatus _ in PlayerGetStatus(isFromSelf, statusIDs))
		{
			return true;
		}
		return false;
	}

	/// <summary>
	/// Check if the object has any of the specified statuses.
	/// </summary>
	/// <param name="battleChara"></param>
	/// <param name="isFromSelf"></param>
	/// <param name="statusIDs"></param>
	/// <returns></returns>
	public static bool HasStatus(this IBattleChara battleChara, bool isFromSelf, params StatusID[] statusIDs)
	{
		if (!DataCenter.PlayerAvailable())
		{
			return false;
		}

		if (Player.Object == null)
		{
			return false;
		}

		if (Player.Object.StatusList == null)
		{
			return false;
		}

		try
		{
			if (battleChara.StatusList == null)
			{
				return false;
			}
		}
		catch
		{
			// StatusList threw, treat as unavailable
			return false;
		}

		if (HasApplyStatus(battleChara, statusIDs))
		{
			return true;
		}

		foreach (IStatus _ in battleChara.GetStatus(isFromSelf, statusIDs))
		{
			return true;
		}
		return false;
	}

	/// <summary>
	/// Checks if the player currently has any of the statuses being applied,
	/// as tracked by <c>DataCenter.ApplyStatus</c> during effect time.
	/// </summary>
	/// <param name="statusIDs">An array of status IDs to check against the applied status.</param>
	/// <returns>
	/// <c>true</c> if any of the specified statuses are currently being applied to the character; otherwise, <c>false</c>.
	/// </returns>
	public static bool PlayerHasApplyStatus(StatusID[] statusIDs)
	{
		if (!DataCenter.InEffectTime)
		{
			return false;
		}

		IBattleChara? player = Player.Object;
		return player != null && IsApplyingStatus(player.GameObjectId, statusIDs);
	}

	/// <summary>
	/// Checks if the specified <paramref name="battleChara"/> currently has any of the statuses being applied,
	/// as tracked by <c>DataCenter.ApplyStatus</c> during effect time.
	/// </summary>
	/// <param name="battleChara">The battle character to check for applied statuses.</param>
	/// <param name="statusIDs">An array of status IDs to check against the applied status.</param>
	/// <returns>
	/// <c>true</c> if any of the specified statuses are currently being applied to the character; otherwise, <c>false</c>.
	/// </returns>
	public static bool HasApplyStatus(this IBattleChara battleChara, StatusID[] statusIDs)
	{
		return battleChara != null && battleChara.Address != nint.Zero && DataCenter.InEffectTime
			&& IsApplyingStatus(battleChara.GameObjectId, statusIDs);
	}

	private static bool IsApplyingStatus(ulong objectId, StatusID[] statusIDs)
	{
		return DataCenter.ApplyStatus.TryGetValue(objectId, out uint statusId) && ContainsId(statusId, statusIDs);
	}

	private static bool ContainsId(uint id, StatusID[] ids)
	{
		foreach (StatusID s in ids)
		{
			if ((uint)s == id)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Remove the specified status.
	/// </summary>
	/// <param name="status"></param>
	public static void StatusOff(StatusID status)
	{
		if (!DataCenter.IsActivated())
		{
			return;
		}

		if (!PlayerHasStatus(false, status))
		{
			return;
		}

		try
		{
			Chat.SendMessage($"/statusoff {GetStatusName(status)}");
			PluginLog.Information($"Status {GetStatusName(status)} removed successfully.");
		}
		catch (Exception ex)
		{
			PluginLog.Error($"Failed to remove status {GetStatusName(status)}: {ex.Message}");
		}
	}

	/// <summary>
	/// Get the name of the specified status.
	/// </summary>
	/// <param name="id">The status ID.</param>
	/// <returns>The name of the status.</returns>
	internal static string GetStatusName(StatusID id)
	{
		Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Status> sheet = Service.GetSheet<Lumina.Excel.Sheets.Status>();
		if (sheet == null)
		{
			return string.Empty;
		}

		Lumina.Excel.Sheets.Status statusRow = sheet.GetRow((uint)id);
		return statusRow.RowId == 0 ? string.Empty : statusRow.Name.ToString() ?? string.Empty;
	}

	/// <summary>
	/// Get the statuses of the specified object.
	/// </summary>
	/// <param name="battleChara">The object to get the statuses from.</param>
	/// <param name="isFromSelf">Whether the statuses are from self.</param>
	/// <param name="statusIDs">The status IDs to look for.</param>
	/// <returns>An enumerable of statuses.</returns>
	private static IEnumerable<IStatus> GetStatus(this IBattleChara battleChara, bool isFromSelf, params StatusID[] statusIDs)
	{
		if (battleChara == null)
		{
			yield break;
		}

		StatusList statusList;
		try
		{
			statusList = battleChara.StatusList;
			if (statusList == null)
			{
				yield break;
			}
		}
		catch
		{
			// StatusList threw, treat as unavailable
			yield break;
		}

		// Linear membership check to avoid HashSet allocation (statusIDs is small in practice)
		static bool ContainsId(uint id, StatusID[] ids)
		{
			for (int i = 0; i < ids.Length; i++)
			{
				if ((uint)ids[i] == id) return true;
			}
			return false;
		}

		ulong playerId = Player.Object?.GameObjectId ?? 0;

		for (int i = 0; i < statusList.Length; i++)
		{
			IStatus? status = statusList[i];
			if (status == null || status.StatusId == 0)
			{
				continue;
			}

			if (isFromSelf)
			{
				if (status.SourceId != playerId && status.SourceObject?.OwnerId != playerId)
				{
					continue;
				}
			}

			if (ContainsId(status.StatusId, statusIDs))
			{
				yield return status;
			}
		}
	}

	/// <summary>
	/// Get the statuses of the Player.
	/// </summary>
	/// <param name="isFromSelf">Whether the statuses are from self.</param>
	/// <param name="statusIDs">The status IDs to look for.</param>
	/// <returns>An enumerable of statuses.</returns>
	private static IEnumerable<IStatus> PlayerGetStatus(bool isFromSelf, params StatusID[] statusIDs)
	{
		if (Player.Object == null)
		{
			yield break;
		}

		StatusList statusList;
		try
		{
			statusList = Player.Object.StatusList;
			if (statusList == null)
			{
				yield break;
			}
		}
		catch
		{
			// StatusList threw, treat as unavailable
			yield break;
		}

		// Linear membership check to avoid HashSet allocation (statusIDs is small in practice)
		static bool ContainsId(uint id, StatusID[] ids)
		{
			for (int i = 0; i < ids.Length; i++)
			{
				if ((uint)ids[i] == id) return true;
			}
			return false;
		}

		ulong playerId = Player.Object?.GameObjectId ?? 0;

		for (int i = 0; i < statusList.Length; i++)
		{
			IStatus? status = statusList[i];
			if (status == null || status.StatusId == 0)
			{
				continue;
			}

			if (isFromSelf)
			{
				if (status.SourceId != playerId && status.SourceObject?.OwnerId != playerId)
				{
					continue;
				}
			}

			if (ContainsId(status.StatusId, statusIDs))
			{
				yield return status;
			}
		}
	}


	/// <summary>
	/// Check if the status is invincible.
	/// </summary>
	/// <param name="status">The status to check.</param>
	/// <returns>True if the status is invincible, otherwise false.</returns>
	public static bool IsInvincible(this IStatus status)
	{
		return status != null && IsInvincibleStatus(status.StatusId);
	}

	private static ExcelSheet<StatusRow>? _statusSheet;

	private static bool TryGetStatusRow(uint statusId, out StatusRow row)
	{
		return (_statusSheet ??= Service.GetSheet<StatusRow>()).TryGetRow(statusId, out row);
	}

	internal static bool IsInvincibleStatus(uint statusId)
	{
		return (TryGetStatusRow(statusId, out StatusRow row) && row.Icon == 15024)
			|| OtherConfiguration.InvincibleStatus?.Contains(statusId) == true;
	}

	internal static bool HasInvincibleStatus(this IBattleChara battleChara)
	{
		foreach (ref readonly GameStatus status in battleChara.GetStatuses())
		{
			if (status.StatusId != 0 && IsInvincibleStatus(status.StatusId))
			{
				return true;
			}
		}

		return false;
	}

	internal static bool HasPriorityStatus(this IBattleChara battleChara)
	{
		var priority = OtherConfiguration.PriorityStatus;
		if (priority == null || priority.Count == 0)
		{
			return false;
		}

		foreach (ref readonly GameStatus status in battleChara.GetStatuses())
		{
			if (status.StatusId != 0 && priority.Contains(status.StatusId))
			{
				return true;
			}
		}

		return false;
	}

	internal static bool CanDispel(in GameStatus status, float minRemaining)
	{
		return status.StatusId != 0 && status.RemainingTime > minRemaining
			&& TryGetStatusRow(status.StatusId, out StatusRow row) && row.CanDispel;
	}

	internal static bool IsDangerous(in GameStatus status, float minRemaining)
	{
		if (!CanDispel(status, minRemaining))
		{
			return false;
		}

		return (status.RemainingTime > 3 && TryGetStatusRow(status.StatusId, out StatusRow row) && row.Icon == 215503)
			|| status.Param > 2
			|| status.RemainingTime > 20
			|| OtherConfiguration.DangerousStatus?.Contains(status.StatusId) == true;
	}

	internal static bool LocksActions(in GameStatus status, float minRemaining)
	{
		if (status.StatusId == 0)
		{
			return false;
		}

		var remaining = status.RemainingTime == 0f ? float.MaxValue : status.RemainingTime;
		if (remaining <= minRemaining)
		{
			return false;
		}

		return (StatusID)status.StatusId is StatusID.OutOfTheAction_1113 or StatusID.OutOfTheAction_1284 or StatusID.OutOfTheAction_1462
			|| (TryGetStatusRow(status.StatusId, out StatusRow row) && row.LockActions);
	}

	/// <summary>
	/// Check if the status is a priority.
	/// </summary>
	/// <param name="status">The status to check.</param>
	/// <returns>True if the status is a priority, otherwise false.</returns>
	public static bool IsPriority(this IStatus status)
	{
		return status != null && OtherConfiguration.PriorityStatus?.Contains(status.StatusId) == true;
	}

	/// <summary>
	/// Check if the status needs to be dispelled immediately.
	/// </summary>
	/// <param name="status">The status to check.</param>
	/// <returns>True if the status needs to be dispelled, otherwise false.</returns>
	public static bool IsDangerous(this IStatus status)
	{
		if (status == null)
		{
			return false;
		}

		if (!status.CanDispel())
		{
			return false;
		}

		// Catch all doom statuses that use the Doom icon
		if (status.GameData.Value.Icon == 215503 && status.RemainingTime > 3)
		{
			return true;
		}

		if (status.Param > 2)
		{
			return true;
		}

		if (status.RemainingTime > 20)
		{
			return true;
		}

		return OtherConfiguration.DangerousStatus?.Contains(status.StatusId) == true;
	}

	/// <summary>
	/// Check if the status can be dispelled.
	/// </summary>
	/// <param name="status">The status to check.</param>
	/// <returns>True if the status can be dispelled, otherwise false.</returns>
	public static bool CanDispel(this IStatus status)
	{
		return status != null && status.GameData.Value.CanDispel == true && status.RemainingTime > 1 + DataCenter.DefaultGCDRemain;
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool CanStatusOff(this IStatus status)
	{
		return status != null && status.GameData.Value.CanStatusOff == true && status.RemainingTime > 1 + DataCenter.DefaultGCDRemain;
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool LockActions(this IStatus status)
	{
		return status != null && status.GameData.Value.LockActions == true && status.RemainingTime > 1 + DataCenter.DefaultGCDRemain;
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool LockMovement(this IStatus status)
	{
		return status != null && status.GameData.Value.LockMovement == true && status.RemainingTime > 1 + DataCenter.DefaultGCDRemain;
	}

	/// <summary>
	/// 
	/// </summary>
	public static bool LockControl(this IStatus status)
	{
		return status != null && status.GameData.Value.LockControl == true && status.RemainingTime > 1 + DataCenter.DefaultGCDRemain;
	}

	/// <summary>
	/// Unknown3 is used to determine if the status indicates a tether.
	/// </summary>
	public static bool IsTether(this IStatus status)
	{
		return status != null && status.GameData.Value.Unknown3 == true && status.RemainingTime > 1 + DataCenter.DefaultGCDRemain;
	}
}
