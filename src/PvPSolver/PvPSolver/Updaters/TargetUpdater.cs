using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;

namespace RotationSolver.Updaters;

internal static partial class TargetUpdater
{
	private static readonly ObjectListDelay<IBattleChara>
		_dispelPartyTargets = new(() => Service.Config.EsunaDelay);


	private static DateTime _lastUpdateTimeToKill = DateTime.MinValue;
	private static readonly TimeSpan TimeToKillUpdateInterval = TimeSpan.FromSeconds(1);

	internal static void UpdateTargets()
	{
		DataCenter.TargetsByRange.Clear();
		DataCenter.AllTargets = GetAllTargets();

		// Early-out: avoid downstream work and stale data when there are no potential targets
		if (DataCenter.AllTargets == null || DataCenter.AllTargets.Count == 0)
		{
			DataCenter.PartyMembers.Clear();
			DataCenter.AllianceMembers.Clear();
			DataCenter.AllHostileTargets.Clear();
			DataCenter.DispelTarget = null;
			DataCenter.ProvokeTarget = null;
			DataCenter.InterruptTarget = null;
			UpdateTimeToKill();
			return;
		}

		UpdateLists();
		DataCenter.DispelTarget = GetDispelTarget();
		DataCenter.ProvokeTarget = (DataCenter.Role == JobRole.Tank || StatusHelper.PlayerHasStatus(true, StatusID.VariantUltimatumSet))
			? GetFirstHostileTarget(ObjectHelper.CanProvoke)
			: null; // Calculating this per frame rather than on-demand is actually a fair amount worse
		DataCenter.InterruptTarget = GetFirstHostileTarget(ObjectHelper.CanInterrupt); // Tanks, Melee, RDM, and various phantom and duty actions can interrupt so just deal with it

		UpdateTimeToKill();
	}

	private static unsafe void UpdateLists()
	{
		var allTargets = DataCenter.AllTargets;
		if (allTargets == null || allTargets.Count == 0)
		{
			// Defensive: ensure lists are empty if no targets
			DataCenter.PartyMembers.Clear();
			DataCenter.AllianceMembers.Clear();
			DataCenter.AllHostileTargets.Clear();
			return;
		}

		// Pre-size lists to reduce reallocations
		List<IBattleChara> partyMembers = new(capacity: allTargets.Count);
		List<IBattleChara> allianceMembers = new(capacity: allTargets.Count);
		List<IBattleChara> hostileTargets = new(capacity: allTargets.Count);

		// Track party membership by id to avoid O(n^2) Contains checks
		HashSet<ulong> partyIds = new(capacity: 32);

		// Compute player eye position once
		Vector3? playerEye = Player.Object?.Position;
		if (playerEye != null)
		{
			playerEye = new Vector3(playerEye.Value.X, playerEye.Value.Y + 2.0f, playerEye.Value.Z);
		}

		foreach (IBattleChara member in allTargets)
		{
			try
			{
				if (member.IsEnemy())
				{
					// Only proceed if playerEye is available
					if (playerEye == null)
					{
						continue;
					}

					if (member.IsTargetable && member.DistanceToPlayer() < 48 && member.CanSeeFrom(playerEye.Value))
					{
						// Valid hostile target
						bool hasInvincible = member.HasInvincibleStatus();
						if (hasInvincible && ((DataCenter.IsPvP && !Service.Config.IgnorePvPInvincibility) || !DataCenter.IsPvP))
						{
							continue; // Invincible enemy doesn't get added to any lists
						}
						hostileTargets.Add(member);
					}
				}
				else if (member.IsPet())
				{
					continue; // We never target these
				}
				else if (member.IsParty())
				{
					FFXIVClientStructs.FFXIV.Client.Game.Character.Character* character = member.Character();
					if (character != null)
					{
						partyMembers.Add(member);
						partyIds.Add(member.GameObjectId);
						continue; // Party members are only added to the party list
					}
				}
			}
			catch (Exception ex)
			{
				PluginLog.Error($"Error in Updating Member Lists: {ex.Message}");
			}
		}

		DataCenter.PartyMembers = partyMembers;
		DataCenter.AllianceMembers = allianceMembers;
		DataCenter.AllHostileTargets = hostileTargets;
	}

	//internal static void OldUpdateTargets()
	//{
	//	//PluginLog.Debug("Updating targets");
	//	DataCenter.TargetsByRange.Clear();
	//	DataCenter.AllTargets = GetAllTargets();

	//	// Early-out: avoid downstream work when there are no potential targets
	//	if (DataCenter.AllTargets == null || DataCenter.AllTargets.Count == 0)
	//	{
	//		DataCenter.PartyMembers.Clear();
	//		DataCenter.AllianceMembers.Clear();
	//		DataCenter.AllHostileTargets.Clear();
	//		DataCenter.DeathTarget = null;
	//		DataCenter.DispelTarget = null;
	//		DataCenter.ProvokeTarget = null;
	//		DataCenter.InterruptTarget = null;
	//		UpdateTimeToKill();
	//		return;
	//	}

	//	DataCenter.PartyMembers = GetPartyMembers();
	//	DataCenter.AllianceMembers = GetAllianceMembers();
	//	DataCenter.AllHostileTargets = GetAllHostileTargets();
	//	DataCenter.DeathTarget = GetDeathTarget();
	//	DataCenter.DispelTarget = GetDispelTarget();
	//	DataCenter.ProvokeTarget = (DataCenter.Role == JobRole.Tank || StatusHelper.PlayerHasStatus(true, StatusID.VariantUltimatumSet)) ? GetFirstHostileTarget(ObjectHelper.CanProvoke) : null; // Calculating this per frame rather than on-demand is actually a fair amount worse
	//	DataCenter.InterruptTarget = GetFirstHostileTarget(ObjectHelper.CanInterrupt); // Tanks, Melee, RDM, and various phantom and duty actions can interrupt so just deal with it

	//	UpdateTimeToKill();
	//}

	private static List<IBattleChara> GetAllTargets()
	{
		List<IBattleChara> allTargets = [];
		bool skipDummyCheck = !Service.Config.DisableTargetDummys;

		var objects = Svc.Objects;
		if (objects != null)
		{
			int count = objects.Length;
			for (int i = 0; i < count; i++)
			{
				var obj = objects[i];
				if (obj is IBattleChara battleChara)
				{
					if ((skipDummyCheck || !battleChara.IsDummy())
						&& battleChara.IsTargetable
						&& !battleChara.IsPet())
					{
						allTargets.Add(battleChara);
					}
				}
			}
		}

		return allTargets;
	}

	private static unsafe List<IBattleChara> GetPartyMembers()
	{
		return GetMembers(DataCenter.AllTargets, isParty: true);
	}

	private static unsafe List<IBattleChara> GetMembers(List<IBattleChara> source, bool isParty)
	{
		List<IBattleChara> members = [];
		if (source == null) return members;

		foreach (IBattleChara member in source)
		{
			try
			{
				if (member.IsPet()) continue;
				if (isParty && !member.IsParty()) continue;

				FFXIVClientStructs.FFXIV.Client.Game.Character.Character* character = member.Character();
				if (character == null) continue;

				members.Add(member);
			}
			catch (Exception ex)
			{
				PluginLog.Error($"Error in GetMembers: {ex.Message}");
			}
		}
		return members;
	}

	private static List<IBattleChara> GetAllHostileTargets()
	{
		List<IBattleChara> hostileTargets = [];
		var allTargets = DataCenter.AllTargets;
		if (allTargets == null || allTargets.Count == 0) return hostileTargets;

		// Reserve capacity to minimize internal resizes
		if (hostileTargets.Capacity < allTargets.Count)
			hostileTargets.Capacity = allTargets.Count;

		// Fix: Check if Player.Object is not null before using its Position
		Vector3? playerEye = Player.Object?.Position;
		if (playerEye != null)
		{
			playerEye = new Vector3(playerEye.Value.X, playerEye.Value.Y + 2.0f, playerEye.Value.Z);
		}

		foreach (IBattleChara target in allTargets)
		{
			// Only proceed if playerEye is available
			if (playerEye == null)
				continue;

			if (!target.IsEnemy() || !target.IsTargetable || !target.CanSeeFrom(playerEye.Value) || target.DistanceToPlayer() >= 48)
				continue;

			bool hasInvincible = target.HasInvincibleStatus();
			if (hasInvincible &&
				((DataCenter.IsPvP && !Service.Config.IgnorePvPInvincibility) || !DataCenter.IsPvP))
			{
				continue;
			}

			hostileTargets.Add(target);
		}

		return hostileTargets;
	}

	private static IBattleChara? GetFirstHostileTarget(Func<IBattleChara, bool> predicate)
	{
		var hostileTargets = DataCenter.AllHostileTargets;
		if (hostileTargets == null) return null;

		foreach (IBattleChara target in hostileTargets)
		{
			try
			{
				if (predicate(target))
					return target;
			}
			catch (Exception ex)
			{
				PluginLog.Error($"Error in GetFirstHostileTarget: {ex.Message}");
			}
		}
		return null;
	}

	private static IBattleChara? GetDispelTarget()
	{
		if (Player.Job is Job.WHM or Job.SCH or Job.AST or Job.SGE or Job.BRD or Job.CNJ)
		{
			List<IBattleChara> weakenPeople = [];
			AddDispelTargets(DataCenter.PartyMembers, weakenPeople);

			// Apply dispel delay to the candidate list
			_dispelPartyTargets.Delay(weakenPeople);

			var canDispelNonDangerous = !DataCenter.MergedStatus.HasFlag(AutoStatus.HealAreaAbility)
										&& !DataCenter.MergedStatus.HasFlag(AutoStatus.HealAreaSpell)
										&& !DataCenter.MergedStatus.HasFlag(AutoStatus.HealSingleAbility)
										&& !DataCenter.MergedStatus.HasFlag(AutoStatus.HealSingleSpell)
										&& !DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseArea)
										&& !DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseSingle);

			// Single-pass selection over the delayed set to avoid extra list allocations
			IBattleChara? closestDangerous = null;
			float closestDangerousDist = float.MaxValue;
			IBattleChara? closestNonDangerous = null;
			float closestNonDangerousDist = float.MaxValue;

			float minRemaining = 1 + DataCenter.DefaultGCDRemain;

			foreach (IBattleChara person in _dispelPartyTargets)
			{
				bool hasDangerous = false;
				foreach (ref readonly var status in person.GetStatuses())
				{
					if (StatusHelper.IsDangerous(status, minRemaining))
					{
						hasDangerous = true;
						break;
					}
				}

				float dist = ObjectHelper.DistanceToPlayer(person);
				if (hasDangerous)
				{
					if (dist < closestDangerousDist)
					{
						closestDangerousDist = dist;
						closestDangerous = person;
					}
				}
				else if (dist < closestNonDangerousDist)
				{
					closestNonDangerousDist = dist;
					closestNonDangerous = person;
				}
			}

			bool allowNonDangerous = canDispelNonDangerous
									 || !DataCenter.HasHostilesInRange
									 || Service.Config.DispelAll
									 || DataCenter.IsPvP;

			if (!allowNonDangerous)
			{
				return closestDangerous;
			}

			return closestDangerous ?? closestNonDangerous;
		}
		return null;
	}

	private static void AddDispelTargets(List<IBattleChara>? members, List<IBattleChara> targetList)
	{
		if (members == null)
		{
			return;
		}

		float minRemaining = 1 + DataCenter.DefaultGCDRemain;
		foreach (IBattleChara member in members)
		{
			foreach (ref readonly var status in member.GetStatuses())
			{
				if (StatusHelper.CanDispel(status, minRemaining))
				{
					targetList.Add(member);
					break; // Add only once per member if any status can be dispelled
				}
			}
		}
	}

	private static IBattleChara? GetClosestTarget(List<IBattleChara> targets)
	{
		IBattleChara? closestTarget = null;
		float closestDistance = float.MaxValue;

		foreach (IBattleChara target in targets)
		{
			float distance = ObjectHelper.DistanceToPlayer(target);
			if (distance < closestDistance)
			{
				closestDistance = distance;
				closestTarget = target;
			}
		}

		return closestTarget;
	}

	// Recording new entries at 1/second and dequeuing old values to keep only the last DataCenter.HP_RECORD_TIME worth of combat time
	// Has performance implications for keeping too much data for too many targets as they're also all evaluated multiple times a frame for expected TTK
	private static void UpdateTimeToKill()
	{
		DateTime now = DateTime.Now;
		if (now - _lastUpdateTimeToKill < TimeToKillUpdateInterval)
		{
			return;
		}

		_lastUpdateTimeToKill = now;

		var hostiles = DataCenter.AllHostileTargets;
		if (hostiles == null || hostiles.Count == 0)
		{
			return;
		}

		if (DataCenter.RecordedHP.Count >= DataCenter.HP_RECORD_TIME)
		{
			_ = DataCenter.RecordedHP.Dequeue();
		}

		Dictionary<ulong, float> currentHPs = new(hostiles.Count);
		for (int i = 0; i < hostiles.Count; i++)
		{
			var target = hostiles[i];
			if (target != null && target.CurrentHp != 0)
			{
				currentHPs[target.GameObjectId] = target.GetHealthRatio();
			}
		}

		DataCenter.RecordedHP.Enqueue((now, currentHPs));
	}
}