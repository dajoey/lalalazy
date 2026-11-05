using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using RotationSolver.Basic.Rotations.Duties;
using RotationSolver.Helpers;


namespace RotationSolver.Updaters;

internal static class RotationUpdater
{
	internal record CustomRotationGroup(Job JobId, Job[] ClassJobIds, Type[] Rotations);

	internal static Dictionary<Job, Dictionary<CombatType, List<ICustomRotation>>> CustomRotationsLookup { get; private set; } = [];

	internal static CustomRotationGroup[] CustomRotations { get; set; } = [];
	internal static SortedList<uint, Type[]> DutyRotations { get; set; } = [];

	public static IAction[] CurrentRotationActions { get; private set; } = [];

	public static IAction[] CurrentDutyRotationActions { get; private set; } = [];

	private static long _actionsRefreshTick = long.MinValue;
	private const long ActionsRefreshMs = 1000;

	private static string _curDutyRotationName = string.Empty;

	private static RotationType _lookupRotationType = RotationType.Reborn;

	public static void LoadBuiltInRotations()
	{
		List<Assembly> assemblies = [typeof(RotationUpdater).Assembly];

		// PvPSolver is a PvP-only mod. Duty rotations (Bozja, Emanation, MonsterHunter,
		// Orbonne, Phantom, Variant) are pure PvE content with zero PvP relevance.
		// Skip loading them entirely to reduce memory and startup time.
		DutyRotations = new SortedList<uint, Type[]>();
		CustomRotations = LoadCustomRotationGroup(assemblies);
		PluginLog.Information($"LoadBuiltInRotations: found {CustomRotations.Length} rotation groups");

		Dictionary<JobRole, List<CustomRotationGroup>> customRotationsGroupedByJobRole = [];
		foreach (CustomRotationGroup customRotationGroup in CustomRotations)
		{
			Job job = customRotationGroup.Rotations[0].GetType().GetCustomAttribute<JobsAttribute>()?.Jobs[0] ?? Job.ADV;
			JobRole jobRole = Svc.Data.GetExcelSheet<ClassJob>()!.GetRow((uint)job)!.GetJobRole();
			if (!customRotationsGroupedByJobRole.TryGetValue(jobRole, out List<CustomRotationGroup>? value))
			{
				value = [];
				customRotationsGroupedByJobRole[jobRole] = value;
			}
			value.Add(customRotationGroup);
		}

		CustomRotationsDict = [];
		foreach (KeyValuePair<JobRole, List<CustomRotationGroup>> kvp in customRotationsGroupedByJobRole)
		{
			List<CustomRotationGroup> customRotationGroups = kvp.Value;
			// Sort by JobId
			for (int i = 0; i < customRotationGroups.Count - 1; i++)
			{
				for (int j = i + 1; j < customRotationGroups.Count; j++)
				{
					if (customRotationGroups[i].JobId > customRotationGroups[j].JobId)
					{
						(customRotationGroups[j], customRotationGroups[i]) = (customRotationGroups[i], customRotationGroups[j]);
					}
				}
			}
			CustomRotationsDict[kvp.Key] = [.. customRotationGroups];
		}
	}

	private static SortedList<uint, Type[]> LoadDutyRotationGroup(List<Assembly> assemblies)
	{
		List<Type> rotationList = [];
		foreach (var assembly in assemblies)
		{
			foreach (var type in TryGetTypes(assembly))
			{
				if (type.IsAssignableTo(typeof(DutyRotation))
					&& !type.IsAbstract && type.GetConstructor([]) != null)
				{
					rotationList.Add(type);
				}
			}
		}

		Dictionary<uint, List<Type>> result = [];
		foreach (var type in rotationList)
		{
			var territories = type.GetCustomAttribute<DutyTerritoryAttribute>()?.TerritoryIds ?? [];

			foreach (var id in territories)
			{
				if (result.TryGetValue(id, out var list))
				{
					list.Add(type);
				}
				else
				{
					result[id] = [type];
				}
			}
		}

		SortedList<uint, Type[]> sorted = [];
		foreach (var pair in result)
		{
			sorted.Add(pair.Key, [.. pair.Value]);
		}
		return sorted;
	}

	private static CustomRotationGroup[] LoadCustomRotationGroup(List<Assembly> assemblies)
	{
		List<Type> rotationList = [];
		int totalTypes = 0;
		int icrCount = 0;
		int ctorCount = 0;

		foreach (var assembly in assemblies)
		{
			foreach (var type in TryGetTypes(assembly))
			{
				totalTypes++;
				bool implementsICustomRotation = false;
				foreach (var iface in type.GetInterfaces())
				{
					if (iface == typeof(ICustomRotation))
					{
						implementsICustomRotation = true;
						break;
					}
				}

				if (!implementsICustomRotation || type.IsAbstract || type.IsInterface)
				{
					continue;
				}

				icrCount++;

				if (type.GetConstructor(Type.EmptyTypes) == null)
				{
					PluginLog.Warning($"LoadCustomRotationGroup: type {type.Name} has no parameterless constructor");
					continue;
				}

				ctorCount++;
				rotationList.Add(type);
			}
		}

		PluginLog.Information($"LoadCustomRotationGroup: scanned {totalTypes} types, {icrCount} implement ICustomRotation, {ctorCount} have parameterless ctor");

		Dictionary<Job, List<Type>> rotationGroups = [];
		foreach (var rotation in rotationList)
		{
			var attr = rotation.GetCustomAttribute<JobsAttribute>(inherit: true);
			if (attr == null)
			{
				PluginLog.Information($"Rotation {rotation.FullName} has no Jobs attribute");
				continue;
			}

			var jobId = attr.Jobs[0];
			if (!rotationGroups.TryGetValue(jobId, out var value))
			{
				value = [];
				rotationGroups.Add(jobId, value);
			}

			value.Add(rotation);
		}

		List<CustomRotationGroup> result = [];
		foreach (var kvp in rotationGroups)
		{
			var jobId = kvp.Key;
			Type[] rotations = [.. kvp.Value];

			result.Add(new CustomRotationGroup(jobId, rotations[0].GetCustomAttribute<JobsAttribute>(inherit: true)!.Jobs,
				rotations));
		}

		CustomRotationsLookup = [];
		return [.. result];
	}

	public static Type[] TryGetTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"Failed to load the types from {assembly.FullName}: {ex.Message}");
			return [];
		}
	}

	private static IEnumerable<IGrouping<string, IAction>>? _groupedActions;
	private static (ICustomRotation? Rotation, DutyRotation? Duty, bool IsPvP, Job Job) _groupedActionsKey;
	private static long _groupedActionsTick;
	private const long GroupedActionsRefreshMs = 1000;

	public static IEnumerable<IGrouping<string, IAction>>? AllGroupedActions
	{
		get
		{
			var key = (DataCenter.CurrentRotation, DataCenter.CurrentDutyRotation, DataCenter.IsPvP, DataCenter.Job);
			var now = Environment.TickCount64;
			if (_groupedActions == null || key != _groupedActionsKey || now - _groupedActionsTick >= GroupedActionsRefreshMs)
			{
				_groupedActions = GroupActions([
					.. key.CurrentRotation?.AllActions ?? [],
					.. key.CurrentDutyRotation?.AllActions ?? []]);
				_groupedActionsKey = key;
				_groupedActionsTick = now;
			}

			return _groupedActions;
		}
	}

	public static IEnumerable<IGrouping<string, IAction>>? GroupActions(IEnumerable<IAction> actions)
	{
		if (actions == null)
		{
			return null;
		}

		Dictionary<string, List<IAction>> groups = [];
		foreach (var a in actions)
		{
			var key = string.Empty;
			if (a is IBaseAction act)
			{
				if (act.Info.IsSpecialAction)
				{
					continue;
				}
				if (act.Setting.IsFakeAction)
				{
					continue;
				}
				else if (act.Info.IsMountAction)
				{
					continue;
				}
				else if (act.Info.IsSystemAction && act.AdjustedID != 3)
				{
					continue;
				}
				else if (!act.Info.IsOnSlot && act.ID != 14415 && act.ID != 14414)
				{
					key = string.Empty;
				}
				else if (act.Info.IsSystemAction)
				{
					key = "System Action";
				}
				else if (act.Action.IsRoleAction)
				{
					if (DataCenter.Job == Job.BST)
					{
						continue;
					}
					else
					{
						key = "Role Action";
					}
				}
				else if (act.Info.IsPvPLimitBreak && DataCenter.IsPvP)
				{
					key = "PvP Limit Break";
				}
				else if (act.Info.IsLimitBreak && !DataCenter.IsPvP)
				{
					key = "Limit Break";
				}
				else if (act.Info.IsDutyAction)
				{
					key = "Duty Action";
				}
				else
				{
					key = act.Info.IsRealGCD ? "GCD" : "oGCD";
					if (act.Setting.IsFriendly)
					{
						key += "-Friendly";
					}
					else
					{
						key += "-Attack";
					}
				}
			}
			else if (a is IBaseItem && !DataCenter.IsPvP)
			{
				key = "Item";
			}

			if (!groups.TryGetValue(key, out var list))
			{
				list = [];
				groups[key] = list;
			}
			list.Add(a);
		}

		List<string> sortedKeys = [.. groups.Keys];
		sortedKeys.Sort(StringComparer.Ordinal);

		List<IGrouping<string, IAction>> result = [];
		foreach (var key in sortedKeys)
		{
			result.Add(new SimpleGrouping<string, IAction>(key, groups[key]));
		}
		return result;
	}

	public static ICustomRotation[] GetRotations(Job playerJob, CombatType combatType)
	{
		if (Player.Object == null || CustomRotations.Length == 0)
		{
			return [];
		}

		if (!CustomRotationsLookup.TryGetValue(playerJob, out _))
		{
			InitReferenceDict(playerJob);
		}

		if (CustomRotationsLookup.TryGetValue(playerJob, out var validCustomRotations))
		{
			if (validCustomRotations.Count == 0)
			{
				PluginLog.Warning($"No valid rotations found for {playerJob}");
				return [];
			}

			if (validCustomRotations.TryGetValue(combatType, out var validCustomRotationsList))
			{
				return [.. validCustomRotationsList];
			}
		}

		return [];
	}

	private class SimpleGrouping<TKey, TElement>(TKey key, IEnumerable<TElement> elements) : IGrouping<TKey, TElement>
	{
		private readonly IEnumerable<TElement> _elements = elements;

		public TKey Key { get; } = key;
		public IEnumerator<TElement> GetEnumerator()
		{
			return _elements.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return _elements.GetEnumerator();
		}
	}

	public static void UpdateRotation()
	{
		UpdateCustomRotation();
		UpdateDutyRotation();
		RefreshActions();
	}
	private static void RefreshActions()
	{
		var now = Environment.TickCount64;
		if (_actionsRefreshTick != long.MinValue && now - _actionsRefreshTick < ActionsRefreshMs)
		{
			return;
		}

		_actionsRefreshTick = now;
		CurrentRotationActions = DataCenter.CurrentRotation?.AllActions ?? [];
		CurrentDutyRotationActions = DataCenter.CurrentDutyRotation?.AllActions ?? [];
	}

	private static void UpdateDutyRotation()
	{
		if (!DutyRotations.TryGetValue(Svc.ClientState.TerritoryType, out var rotations))
		{
			if (DataCenter.CurrentDutyRotation != null)
			{
				DataCenter.CurrentDutyRotation.Dispose();
				DataCenter.CurrentDutyRotation = null;
				_curDutyRotationName = string.Empty;
				CurrentDutyRotationActions = [];
			}
			return;
		}

		_ = Service.Config.DutyRotationChoice.TryGetValue(Svc.ClientState.TerritoryType, out var value);
		var name = value ?? string.Empty;
		if (name == _curDutyRotationName && DataCenter.CurrentDutyRotation != null)
		{
			return;
		}

		var type = GetChosenType(rotations, name);
		if (type != DataCenter.CurrentDutyRotation?.GetType())
		{
			DataCenter.CurrentDutyRotation?.Dispose();
			DataCenter.CurrentDutyRotation = GetRotation(type);
			_actionsRefreshTick = long.MinValue;
		}

		_curDutyRotationName = name;

		static DutyRotation? GetRotation(Type? t)
		{
			if (t == null)
			{
				return null;
			}

			try
			{
				return (DutyRotation?)Activator.CreateInstance(t);
			}
			catch (Exception ex)
			{
				BasicWarningHelper.AddSystemWarning($"Failed to create the rotation: {t.Name}");
				PluginLog.Error($"Failed to create the rotation: {t.Name}: {ex.Message}");
				return null;
			}
		}
	}

	/// <summary>
	///     Last (job, combat type) pair the "no rotation found" warning was emitted for, so a
	///     persistent miss is reported once instead of once per frame. Reset on a successful load.
	/// </summary>
	private static (Job Job, CombatType Type)? _lastNoRotationLogged;

	private static void UpdateCustomRotation()
	{
		if (Player.Object == null || CustomRotations.Length == 0)
		{
			return;
		}

		// PvPSolver is PvP-only and ships no PvE rotations, so outside PvP the lookup below is
		// guaranteed to miss. The miss path nulls CurrentRotation, which permanently defeats the
		// unchanged-state guard further down - so this ran, and logged, on every framework tick for
		// the whole time the player was in PvE content. Bail before doing any of that. (Issue #3)
		if (!DataCenter.IsPvP)
		{
			if (DataCenter.CurrentRotation != null)
			{
				DataCenter.CurrentRotation = null;
				CurrentRotationActions = [];
			}

			return;
		}

		Job nowJob = Player.Job;
		CombatType curCombatType = CombatType.PvP;

		var playerRotationType = DataCenter.PlayerRotationType;
		if (playerRotationType != _lookupRotationType)
		{
			_lookupRotationType = playerRotationType;
			CustomRotationsLookup = [];
		}
		else if (DataCenter.CurrentRotation?.Job == nowJob && DataCenter.CurrentRotation?.GetAttributes()?.Type == curCombatType)
		{
			return;
		}

		if (!CustomRotationsLookup.TryGetValue(nowJob, out _))
		{
			InitReferenceDict(nowJob);
		}

		if (CustomRotationsLookup.TryGetValue(nowJob, out var validCustomRotations))
		{
			if (validCustomRotations.Count == 0)
			{
				if (_lastNoRotationLogged != (nowJob, curCombatType))
				{
					_lastNoRotationLogged = (nowJob, curCombatType);
					PluginLog.Warning($"No valid rotations found for {nowJob}");
				}

				return;
			}

			if (validCustomRotations.TryGetValue(curCombatType, out List<ICustomRotation>? validCustomRotationsList)
				&& validCustomRotationsList.Count > 0)
			{
				if (validCustomRotationsList.Count == 0)
				{
					if (DataCenter.CurrentRotation != null)
					{
						PluginLog.Information($"No valid {curCombatType} rotations found for {nowJob}");
					}
				}
				else
				{
					var desiredRotationName = DataCenter.IsPvP ? Service.Config.PvPRotationChoice : Service.Config.RotationChoice;

					var rotation = validCustomRotationsList[0];
					foreach (var possibleRotation in validCustomRotationsList)
					{
						if (possibleRotation.GetType().FullName == desiredRotationName)
						{
							rotation = possibleRotation;
							break;
						}
					}

					if (rotation != DataCenter.CurrentRotation)
					{
						ChangeRotation(rotation);
					}

					return;
				}
			}
			else
			{

			}
		}
		else
		{

		}

		DataCenter.CurrentRotation = null;
		CurrentRotationActions = [];

		if (_lastNoRotationLogged != (nowJob, curCombatType))
		{
			_lastNoRotationLogged = (nowJob, curCombatType);
			PluginLog.Warning($"UpdateCustomRotation: no rotation found for job={nowJob} combatType={curCombatType}");
		}
	}

	public static void ChangeRotation(ICustomRotation rotation)
	{
		rotation.OnTerritoryChanged();
		_lastNoRotationLogged = null;
		DataCenter.CurrentRotation = rotation;
		CurrentRotationActions = DataCenter.CurrentRotation?.AllActions ?? [];
	}

	private static ICustomRotation? GetRotation(Type? t)
	{
		if (t == null)
		{
			return null;
		}

		try
		{
			return (ICustomRotation?)Activator.CreateInstance(t);
		}
		catch (Exception ex)
		{
			PluginLog.Error($"Failed to create the rotation: {t.Name}: {ex}");
			return null;
		}
	}

	private static void InitReferenceDict(Job currentJob)
	{
		int addedPvE = 0;
		int addedPvP = 0;
		foreach (CustomRotationGroup customRotationGroup in CustomRotations)
		{
			var classMatch = false;
			var classJobs = customRotationGroup.ClassJobIds;
			for (var i = 0; i < classJobs.Length; i++)
			{
				if (classJobs[i] == currentJob)
				{
					classMatch = true;
					break;
				}
			}
			if (!classMatch)
			{
				continue;
			}

			foreach (var job in customRotationGroup.ClassJobIds)
			{
				if (!CustomRotationsLookup.TryGetValue(job, out var rotationsListByType))
				{
					rotationsListByType = new Dictionary<CombatType, List<ICustomRotation>>
				{
					{ CombatType.PvE, new List<ICustomRotation>() },
					{ CombatType.PvP, new List<ICustomRotation>() }
				};
					CustomRotationsLookup[job] = rotationsListByType;
				}

				foreach (var rotationType in customRotationGroup.Rotations)
				{
					var rotAttr = rotationType.GetCustomAttribute<RotationAttribute>();
					if (rotAttr == null)
					{
						continue;
					}

					if (rotAttr.Disabled)
					{
						continue;
					}

					if (!DataCenter.IsRotationTypeAllowed(job, rotAttr.RotationType))
					{
						continue;
					}

					CombatType? comType = rotAttr.Type;
					if (comType == null)
					{
						continue;
					}

					var possibleRotation = GetRotation(rotationType);
					if (possibleRotation == null)
					{
						continue;
					}

					rotationsListByType[(CombatType)comType].Add(possibleRotation);
					if (comType == CombatType.PvE) addedPvE++;
					else if (comType == CombatType.PvP) addedPvP++;
				}
			}
		}
		PluginLog.Information($"InitReferenceDict for {currentJob}: added {addedPvE} PvE, {addedPvP} PvP rotations");
	}

	private static Type? GetChosenType(Type[] types, string name)
	{
		foreach (var r in types)
		{
			if (r.FullName == name)
			{
				return r;
			}
		}

		return types.Length > 0 ? types[0] : null;
	}
}