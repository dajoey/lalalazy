using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Lumina.Excel.Sheets;
using RotationSolver.Basic.Configuration;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RotationSolver.Basic.Helpers;

/// <summary>
/// Get the information from object.
/// </summary>
public static class ObjectHelper
{
	private static readonly EventHandlerContent[] _eventType =
	[
		EventHandlerContent.TreasureHuntDirector,
		EventHandlerContent.BattleLeveDirector,
		EventHandlerContent.CompanyLeveDirector,
		EventHandlerContent.Quest,
	];

	private static readonly ConcurrentDictionary<string, Regex> _regexCache = [];

	private static Regex GetCachedRegex(string pattern)
	{
		return _regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled));
	}

	/// <summary>
	/// Returns true if any current hostile target has the specified BNpc NameId.
	/// </summary>
	private static bool AnyHostileHasNameId(uint nameId)
	{
		var hostiles = DataCenter.AllHostileTargets;
		if (hostiles == null || hostiles.Count == 0)
		{
			return false;
		}

		for (int i = 0, n = hostiles.Count; i < n; i++)
		{
			var h = hostiles[i];
			if (h != null && h.NameId == nameId)
			{
				return true;
			}
		}

		return false;
	}

	internal static bool IsOthersPlayersMob(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		// If the mob has a nameplate icon it is tagged as belonging to the current player's quest/leve/treasure.
		if (battleChara.GetNamePlateIcon() != 0)
		{
			return false;
		}

		// If the mob is actively targeting the player or the player's pet it cannot be treated as
		// another player's mob regardless of event type — it is a direct threat to us.
		if (battleChara.TargetObject == Player.Object)
		{
			return false;
		}

		if (Player.Object != null && battleChara.TargetObject?.OwnerId == Player.Object.GameObjectId)
		{
			return false;
		}

		// SpecialType but no NamePlateIcon — check whether the mob's event type matches one of the
		// player-owned content directors that can produce mobs belonging to OTHER players.
		var ev = battleChara.GetEventType();
		for (int i = 0; i < _eventType.Length; i++)
		{
			if (_eventType[i] == ev)
			{
				return true;
			}
		}

		return false;
	}

	internal static bool IsAttackable(this IBattleChara battleChara)
	{
		if (Player.Object == null)
		{
			return false;
		}

		if (!battleChara.IsEnemy())
		{
			return false;
		}

		if (battleChara.GetEventType() == EventHandlerContent.DpsChallengeDirector && Player.Object.GetEventType() != EventHandlerContent.DpsChallengeDirector)
		{
			return false;
		}

		// Dead.
		if (Service.Config.FilterOneHpInvincible && battleChara.CurrentHp <= 1)
		{
			return false;
		}

		if ((!DataCenter.IsPvP || !Service.Config.IgnorePvPInvincibility) && battleChara.HasInvincibleStatus())
		{
			return false;
		}

		// In No Hostiles Names
		if (OtherConfiguration.NoHostileNames != null &&
			OtherConfiguration.NoHostileNames.TryGetValue(Svc.ClientState.TerritoryType, out string[]? ns1))
		{
			foreach (string n in ns1)
			{
				if (!string.IsNullOrEmpty(n) && GetCachedRegex(n).IsMatch(battleChara.Name.TextValue))
				{
					return false;
				}
			}
		}

		// Top-priority named hostile
		if (battleChara.IsTopPriorityNamedHostile())
		{
			return true;
		}

		if (battleChara.IsTopPriorityHostile())
		{
			return true;
		}

		if (Service.CountDownTime > 0 || DataCenter.IsPvP)
		{
			return true;
		}

		//Special cases for Black Star and Mythic Idol, which do not have valid target objects but are still attackable.
		if (battleChara.NameId == 13726 || battleChara.NameId == 13636)
		{
			return true;
		}

		// Tar on me
		return (battleChara.TargetObject == Player.Object)
			|| (Player.Object != null && battleChara.TargetObject?.OwnerId == Player.Object.GameObjectId)
			|| DataCenter.CurrentTargetToHostileType switch
			{
				TargetHostileType.AllTargetsCanAttack => true,
				TargetHostileType.TargetsHaveTarget => battleChara.TargetObject is not null,
				TargetHostileType.AllTargetsWhenSolo => DataCenter.PartyMembers.Count == 1 || battleChara.TargetObject is not null,
				TargetHostileType.AllTargetsWhenSoloInDuty => (DataCenter.PartyMembers.Count == 1 && (Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.BoundByDuty56]))
									|| battleChara.TargetObject is not null,
				_ => true,
			};
	}
	internal static unsafe bool IsEnemy(this IGameObject obj)
	{
		if (obj == null)
		{
			return false;
		}

		if (!obj.IsTargetable)
		{
			return false;
		}

		if (ActionManager.CanUseActionOnTarget((uint)ActionID.BlizzardPvE, obj.Struct()))
		{
			return true;
		}

		return false;
	}

	internal static uint TargetCharaCondition(this IBattleChara obj)
	{
		uint statusId = obj.OnlineStatus.RowId;
		if (statusId != 0)
		{
			return statusId;
		}

		return 0;
	}

	internal static bool IsConditionCannotTarget(this IBattleChara obj)
	{
		uint statusId = obj.OnlineStatus.RowId;
		if (statusId == 15 || statusId == 5)
		{
			return true;
		}

		return false;
	}

	internal static unsafe bool IsPlayer(this IBattleChara battleChara)
	{
		return battleChara == Player.Object;
	}

	/// <summary>
	///
	/// </summary>
	public static bool IsPlayerInParty()
	{
		if (Player.Object == null)
		{
			return false;
		}

		if (Player.Object.GameObjectId == Player.Object.GameObjectId)
		{
			return true;
		}

		if (!Player.Object.IsTargetable)
		{
			return false;
		}

		foreach (Dalamud.Game.ClientState.Party.IPartyMember p in Svc.Party)
		{
			if (p.GameObject?.GameObjectId == Player.Object.GameObjectId)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///
	/// </summary>
	public static bool IsParty(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		if (Player.Object == null)
		{
			return false;
		}

		if (battleChara.GameObjectId == Player.Object.GameObjectId)
		{
			return true;
		}

		if (!battleChara.IsTargetable)
		{
			return false;
		}

		if (battleChara.IsPet())
		{
			return false;
		}

		foreach (Dalamud.Game.ClientState.Party.IPartyMember p in Svc.Party)
		{
			if (p.GameObject?.GameObjectId == battleChara.GameObjectId)
			{
				return true;
			}
		}

		return false;
	}

	internal static bool IsPet(this IBattleChara battleChara)
	{
		if (battleChara == null || Svc.Buddies.PetBuddy == null)
		{
			return false;
		}

		return battleChara.GameObjectId == Svc.Buddies.PetBuddy.GameObject?.GameObjectId;
	}

	internal static bool IsFocusTarget(this IBattleChara battleChara)
	{
		return Svc.Targets.FocusTarget != null && Svc.Targets.FocusTarget.GameObjectId == battleChara.GameObjectId;
	}

	internal static bool PlayerIsTargetOnSelf()
	{
		if (Player.Object == null)
			return false;
		return Player.Object.TargetObject?.TargetObject == Player.Object;
	}

	internal static bool IsTargetOnSelf(this IBattleChara battleChara)
	{
		return battleChara.TargetObject?.TargetObject == battleChara;
	}

	internal static bool PlayerIsAlive()
	{
		if (Player.Object == null)
			return false;
		if (Player.Object.IsDead)
			return false;
		if (!Player.Object.IsTargetable)
			return false;
		if (Player.Object.CurrentHp == 0)
			return false;

		return true;
	}

	internal static bool IsAlive(this IBattleChara battleChara)
	{
		if (battleChara == null)
			return false;
		if (battleChara.IsDead)
			return false;
		if (!battleChara.IsTargetable)
			return false;
		if (battleChara.CurrentHp == 0)
			return false;

		return true;
	}

	/// <summary>
	/// Get the object kind.
	/// </summary>
	/// <param name="obj"></param>
	/// <returns></returns>
	public static unsafe ObjectKind GetObjectKind(this IGameObject obj)
	{
		FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* s = obj.Struct();
		return s == null ? default : (ObjectKind)s->ObjectKind;
	}

	/// <summary>
	/// Determines whether the specified game object is a top priority hostile target based on its name being listed.
	/// </summary>
	/// <param name="battleChara">The battleChara to check.</param>
	/// <returns>
	/// <c>true</c> if the game object is a top priority named hostile, target; otherwise, <c>false</c>.
	/// </returns>
	internal static bool IsTopPriorityNamedHostile(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		foreach (uint id in DataCenter.PrioritizedNameIds)
		{
			if (battleChara.NameId == id)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Determines whether the specified game object is a top priority hostile target.
	/// </summary>
	/// <param name="battleChara">The game object to check.</param>
	/// <returns>
	/// <c>true</c> if the game object is a top priority hostile target; otherwise, <c>false</c>.
	/// </returns>
	private const long TopPriorityTtlMs = 15;
	private static readonly Dictionary<ulong, (long Tick, bool IsPriority)> _topPriorityCache = [];

	internal static bool IsTopPriorityHostile(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		ulong id = battleChara.GameObjectId;
		long now = Environment.TickCount64;
		if (_topPriorityCache.TryGetValue(id, out (long Tick, bool IsPriority) cached) && now - cached.Tick < TopPriorityTtlMs)
		{
			return cached.IsPriority;
		}

		if (_topPriorityCache.Count > 512)
		{
			_topPriorityCache.Clear();
		}

		bool isPriority = IsTopPriorityHostileCore(battleChara);
		_topPriorityCache[id] = (now, isPriority);
		return isPriority;
	}

	private static bool IsTopPriorityHostileCore(IBattleChara battleChara)
	{
		if (battleChara.IsParty())
		{
			return false;
		}

		// Check IBattleChara bespoke IsSpecialInclusionPriority method
		if (battleChara.IsSpecialInclusionPriority())
		{
			return true;
		}

		// MCH prio targeting for Wildfire
		if (Player.Job == Job.MCH && (battleChara.HasStatus(true, StatusID.Wildfire) || battleChara.HasStatus(true, StatusID.Wildfire_1323)))
		{
			return true;
		}

		if (Service.Config.PrioAtomelith && DataCenter.IsPvP)
		{
			var IceBoundTomeLithA1 = battleChara.NameId == 4822;
			var IceBoundTomeLithA2 = battleChara.NameId == 4823;
			var IceBoundTomeLithA3 = battleChara.NameId == 4824;
			var IceBoundTomeLithA4 = battleChara.NameId == 4825;
			if (IceBoundTomeLithA1 || IceBoundTomeLithA2 || IceBoundTomeLithA3 || IceBoundTomeLithA4)
			{
				return true;
			}
		}

		if (Service.Config.PrioBtomelith && DataCenter.IsPvP)
		{
			var IceBoundTomeLithB1 = battleChara.NameId == 4826;
			var IceBoundTomeLithB2 = battleChara.NameId == 4827;
			var IceBoundTomeLithB3 = battleChara.NameId == 4828;
			var IceBoundTomeLithB4 = battleChara.NameId == 4829;
			var IceBoundTomeLithB5 = battleChara.NameId == 4830;
			var IceBoundTomeLithB6 = battleChara.NameId == 4831;
			var IceBoundTomeLithB7 = battleChara.NameId == 4832;
			var IceBoundTomeLithB8 = battleChara.NameId == 4833;
			var IceBoundTomeLithB9 = battleChara.NameId == 4834;
			var IceBoundTomeLithB10 = battleChara.NameId == 4835;
			var IceBoundTomeLithB11 = battleChara.NameId == 4836;
			var IceBoundTomeLithB12 = battleChara.NameId == 4837;
			var IceBoundTomeLithB13 = battleChara.NameId == 4840;
			var IceBoundTomeLithB14 = battleChara.NameId == 4841;
			var IceBoundTomeLithB15 = battleChara.NameId == 4842;
			var IceBoundTomeLithB16 = battleChara.NameId == 4843;
			var IceBoundTomeLithB17 = battleChara.NameId == 4844;
			var IceBoundTomeLithB18 = battleChara.NameId == 4845;

			if (IceBoundTomeLithB1 || IceBoundTomeLithB2 || IceBoundTomeLithB3 || IceBoundTomeLithB4 ||
				IceBoundTomeLithB5 || IceBoundTomeLithB6 || IceBoundTomeLithB7 || IceBoundTomeLithB8 ||
				IceBoundTomeLithB9 || IceBoundTomeLithB10 || IceBoundTomeLithB11 || IceBoundTomeLithB12 ||
				IceBoundTomeLithB13 || IceBoundTomeLithB14 || IceBoundTomeLithB15 || IceBoundTomeLithB16 ||
				IceBoundTomeLithB17 || IceBoundTomeLithB18)
			{
				return true;
			}
		}

		if (battleChara.HasPriorityStatus())
		{
			return true;
		}

		if (Service.Config.ChooseAttackMark)
		{
			long[] targets = MarkingHelper.GetAttackSignTargets();
			if (targets != null)
			{
				foreach (long id in targets)
				{
					if (id != 0 && id == (long)battleChara.GameObjectId && battleChara.IsEnemy())
					{
						return true;
					}
				}
			}
		}

		uint icon = battleChara.GetNamePlateIcon();


		// Quest
		//71204 Main Quest
		//71144 Major Quest
		//71224 Other Quest
		//71344 Major Quest

		// Check if the object is a BattleNpcPart
		if (Service.Config.PrioEnemyParts && battleChara.GetBattleNPCSubKind() == (BattleNpcSubKind)8)
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// True if a Deadly Doornail (NameId 14303) is currently in AllHostileTargets.
	/// </summary>
	public static bool HasDeadlyDoornail => AnyHostileHasNameId(14303);

	/// <summary>
	/// True if a Fatal Flail (NameId 14302) is currently in AllHostileTargets.
	/// </summary>
	public static bool HasFatalFlail => AnyHostileHasNameId(14302);
	/// <summary>
	/// 
	/// </summary>
	public static bool IsBroPriority(this IBattleChara battleChara)
	{
		if (Player.Object == null)
		{
			return false;
		}

		return false;
	}

	internal static bool IsSpecialInclusionPriority(this IBattleChara battleChara)
	{
		if (battleChara.NameId == 8145
			|| battleChara.NameId == 10259
			|| battleChara.NameId == 12704
			|| battleChara.NameId == 14052)
		{
			return true;
		}
		//8145 Root in Dohn Meg boss 2
		//10259 Cinduruva in The Tower of Zot
		//12704 Crystalline Debris

		//14052 hellmaker
		if (DataCenter.TerritoryID == 1292)
		{
			if (battleChara.NameId == 14052)
			{
				StatusID CellBlockCPrisoner = (StatusID)4544;
				StatusID CellBlockDPrisoner = (StatusID)4545;

				if (StatusHelper.PlayerHasStatus(false, CellBlockCPrisoner) || StatusHelper.PlayerHasStatus(false, CellBlockDPrisoner))
				{
					return true;
				}
			}
		}

		// striking shrublet - Floor 10 boss ads
		if (DataCenter.TerritoryID == 1281)
		{
			if (battleChara.NameId == 13980)
			{
				return true;
			}
		}

		// forgiven adulation - Floor 30 boss ads
		if (DataCenter.TerritoryID == 1284)
		{
			if (battleChara.NameId == 13978)
			{
				return true;
			}
		}

		return false;
	}
	internal static unsafe uint GetNamePlateIcon(this IBattleChara battleChara)
	{
		return battleChara.Struct()->NamePlateIconId;
	}

	internal static unsafe EventHandlerContent GetEventType(this IBattleChara battleChara)
	{
		return battleChara.Struct()->EventId.ContentId;
	}

	internal static unsafe BattleNpcSubKind GetBattleNPCSubKind(this IBattleChara battleChara)
	{
		return (BattleNpcSubKind)battleChara.Struct()->SubKind;
	}

	/// <summary>
	/// Attempts to retrieve all tethers currently present using the ECommons TetherInfo API via reflection.
	/// </summary>
	public static IReadOnlyList<TetherInfo> GetAllTethers()
	{
		try
		{
			var tType = typeof(TetherInfo);
			// Look for a public static method that returns an array or IEnumerable of TetherInfo
			var methods = tType.GetMethods(BindingFlags.Public | BindingFlags.Static);
			foreach (var m in methods)
			{
				var ret = m.ReturnType;
				if (ret == typeof(TetherInfo[]) || typeof(System.Collections.IEnumerable).IsAssignableFrom(ret))
				{
					var res = m.Invoke(null, null);
					if (res == null) continue;

					if (res is TetherInfo[] arr)
						return arr;

					if (res is System.Collections.IEnumerable ie)
					{
						var list = new List<TetherInfo>();
						foreach (var o in ie)
						{
							if (o is TetherInfo ti) list.Add(ti);
						}
						return list;
					}
				}
			}
		}
		catch
		{
			// ignore and fallthrough to empty
		}

		return [];
	}

	/// <summary>
	/// Returns tethers where the object is either source or target.
	/// </summary>
	public static IReadOnlyList<TetherInfo> GetTethersFor(this IGameObject obj)
	{
		if (obj == null) return [];
		var all = GetAllTethers();
		ulong id = obj.GameObjectId;
		var result = new List<TetherInfo>();
		foreach (var t in all)
		{
			if (t == null) continue;
			if (ExtractTetherId(t, out _, out var src, out var tgt) && (src == id || tgt == id))
				result.Add(t);
		}
		return result;
	}

	/// <summary>
	/// Returns outgoing tethers (where object is source).
	/// </summary>
	public static IReadOnlyList<TetherInfo> GetOutgoingTethers(this IGameObject obj)
	{
		if (obj == null) return [];
		var all = GetAllTethers();
		ulong id = obj.GameObjectId;
		var result = new List<TetherInfo>();
		foreach (var t in all)
		{
			if (t == null) continue;
			if (ExtractTetherId(t, out _, out var src, out _) && src == id)
				result.Add(t);
		}
		return result;
	}

	/// <summary>
	/// Returns incoming tethers (where object is target).
	/// </summary>
	public static IReadOnlyList<TetherInfo> GetIncomingTethers(this IGameObject obj)
	{
		if (obj == null) return [];
		var all = GetAllTethers();
		ulong id = obj.GameObjectId;
		var result = new List<TetherInfo>();
		foreach (var t in all)
		{
			if (t == null) continue;
			if (ExtractTetherId(t, out _, out _, out var tgt) && tgt == id)
				result.Add(t);
		}
		return result;
	}

	/// <summary>
	/// Try to extract common tether fields (tether id, source object id, target object id) using reflection.
	/// Returns true if at least source/target were obtained.
	/// </summary>
	private static bool ExtractTetherId(TetherInfo t, out uint tetherId, out ulong sourceObjectId, out ulong targetObjectId)
	{
		tetherId = 0;
		sourceObjectId = 0;
		targetObjectId = 0;
		try
		{
			var tType = typeof(TetherInfo);

			// possible names for fields/properties
			string[] tetherNames = ["TetherId", "Id", "Tether"];
			string[] sourceNames = ["SourceObjectId", "SourceId", "Source", "SourceActorId"];
			string[] targetNames = ["TargetObjectId", "TargetId", "Target", "TargetActorId"];

			object? val;

			val = TryGetMemberValue(tType, t, tetherNames);
			if (val != null)
			{
				if (val is uint ui) tetherId = ui;
				else if (val is int i) tetherId = (uint)i;
				else if (uint.TryParse(val.ToString(), out var parsed)) tetherId = parsed;
			}

			val = TryGetMemberValue(tType, t, sourceNames);
			if (val != null)
			{
				if (val is ulong ul) sourceObjectId = ul;
				else if (val is uint u) sourceObjectId = u;
				else if (ulong.TryParse(val.ToString(), out var pul)) sourceObjectId = pul;
			}

			val = TryGetMemberValue(tType, t, targetNames);
			if (val != null)
			{
				if (val is ulong ul2) targetObjectId = ul2;
				else if (val is uint u2) targetObjectId = u2;
				else if (ulong.TryParse(val.ToString(), out var pul2)) targetObjectId = pul2;
			}

			return sourceObjectId != 0 || targetObjectId != 0 || tetherId != 0;
		}
		catch
		{
			return false;
		}
	}

	private static object? TryGetMemberValue(Type tType, object instance, string[] names)
	{
		foreach (var n in names)
		{
			var f = tType.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
			if (f != null)
			{
				var v = f.GetValue(instance);
				if (v != null) return v;
			}
			var p = tType.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
			if (p != null)
			{
				var v = p.GetValue(instance);
				if (v != null) return v;
			}
		}
		return null;
	}

	/// <summary>
	/// Returns true if the specified battleChara is tethered to the player.
	/// </summary>
	public static bool IsTetheredToPlayer(this IBattleChara battleChara)
	{
		if (battleChara == null || Player.Object == null) return false;

		ulong playerId = Player.Object.GameObjectId;
		var tethers = GetTethersFor(battleChara);
		foreach (var t in tethers)
		{
			if (t == null) continue;
			if (ExtractTetherId(t, out _, out var src, out var tgt) && (src == playerId || tgt == playerId))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Returns true if the specified checkedtarget is tethered to checkedtarget.
	/// </summary>
	public static bool IsTetheredToSpecificTarget(this IBattleChara battleChara, IBattleChara checkedtarget)
	{
		if (battleChara == null || checkedtarget == null) return false;

		ulong memberId = checkedtarget.GameObjectId;
		var tethers = GetTethersFor(battleChara);
		foreach (var t in tethers)
		{
			if (t == null) continue;
			if (ExtractTetherId(t, out _, out var src, out var tgt) && (src == memberId || tgt == memberId))
				return true;
		}
		return false;
	}

	private static readonly ConcurrentDictionary<uint, bool> _effectRangeCheck = [];

	/// <summary>
	/// Determines whether the specified game object can be interrupted.
	/// </summary>
	/// <param name="battleChara">The game object to check.</param>
	/// <returns>
	/// <c>true</c> if the game object can be interrupted; otherwise, <c>false</c>.
	/// </returns>
	internal static bool CanInterrupt(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		// Ensure the IBattleChara object is valid before accessing its properties
		unsafe
		{
			if (battleChara.Struct() == null)
			{
				return false;
			}
		}

		bool baseCheck = battleChara.IsCasting && battleChara.IsCastInterruptible && battleChara.TotalCastTime >= 2;
		if (!baseCheck)
		{
			return false;
		}

		if (!Service.Config.InterruptibleMoreCheck)
		{
			return false;
		}

		if (_effectRangeCheck.TryGetValue(battleChara.CastActionId, out bool check))
		{
			return check;
		}

		Lumina.Excel.Sheets.Action act = Service.GetSheet<Lumina.Excel.Sheets.Action>().GetRow(battleChara.CastActionId);
		return act.RowId == 0
			? (_effectRangeCheck[battleChara.CastActionId] = false)
			: (CastType)act.CastType == CastType.Cone || (CastType)act.CastType == CastType.Donut || (act.EffectRange > 0 && act.EffectRange < 8)
			? (_effectRangeCheck[battleChara.CastActionId] = false)
			: (_effectRangeCheck[battleChara.CastActionId] = true);
	}

	internal static bool IsDummy(this IBattleChara battleChara)
	{
		return battleChara?.NameId == 541;
	}

	public static bool IsBossFromTTK(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		if (Service.Config.DummyBoss && battleChara.IsDummy())
		{
			return true;
		}

		// Boss by time-to-kill
		return battleChara.GetTTK(true) >= Service.Config.BossTimeToKill;
	}

	/// <summary>
	/// Returns object's calculated shield value.
	/// </summary>
	/// <param name="battleChara"></param>
	/// <returns></returns>
	public static uint GetObjectShield(this IBattleChara battleChara)
	{
		return battleChara is ICharacter character && character.MaxHp > 0 && character.ShieldPercentage > 0
			? (uint)((ulong)character.MaxHp * character.ShieldPercentage / 100)
			: 0;
	}

	/// <summary>
	/// Returns object's calculated effective HP.
	/// </summary>
	/// <param name="battleChara"></param>
	/// <returns></returns>
	public static uint GetEffectiveHp(this IBattleChara battleChara)
	{
		return battleChara is ICharacter
			? battleChara.CurrentHp + GetObjectShield(battleChara)
			: 0;
	}

	/// <summary>
	/// Returns object's calculated effective HP as a percentage of Max HP, rounded down to the nearest whole number.
	/// </summary>
	/// <param name="battleChara"></param>
	/// <returns>Effective HP percentage (0-100). Returns 0 if MaxHp is 0 or not an ICharacter.</returns>
	public static int GetEffectiveHpPercent(this IBattleChara battleChara)
	{
		if (battleChara is not ICharacter character || character.MaxHp == 0)
			return 0;

		uint effectiveHp = character.CurrentHp + ObjectHelper.GetObjectShield(battleChara);
		return (int)Math.Floor((float)effectiveHp / character.MaxHp * 100f);
	}

	/// <summary>
	/// Is object dying.
	/// </summary>
	/// <param name="battleChara"></param>
	/// <returns></returns>
	public static bool IsDying(this IBattleChara battleChara)
	{
		return battleChara != null && !battleChara.IsDummy() && (battleChara.GetTTK() <= Service.Config.DyingTimeToKill || battleChara.GetHealthRatio() < Service.Config.IsDyingConfig);
	}

	/// <summary>
	/// Determines whether the specified battle character is currently in combat.
	/// </summary>
	/// <param name="battleChara">The battle character to check.</param>
	/// <returns>
	/// <c>true</c> if the battle character is in combat; otherwise, <c>false</c>.
	/// </returns>
	internal static unsafe bool InCombat(this IBattleChara battleChara)
	{
		return battleChara != null && battleChara.Struct() != null && battleChara.Struct()->Character.InCombat;
	}

	private static readonly Dictionary<ulong, Vector3> LastPositions = [];
	internal static bool IsTargetMoving(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		if (Svc.Condition[ConditionFlag.BetweenAreas] || LastPositions.Count > 4096)
		{
			LastPositions.Clear();
		}

		ulong id = battleChara.GameObjectId;
		Vector3 currentPos = battleChara.Position;
		if (LastPositions.TryGetValue(id, out Vector3 lastPos))
		{
			// You can adjust the threshold as needed
			bool isMoving = Vector3.Distance(currentPos, lastPos) > 0.01f;
			LastPositions[id] = currentPos;
			return isMoving;
		}
		else
		{
			LastPositions[id] = currentPos;
			return false; // First check, assume not moving
		}
	}

	private static readonly TimeSpan CheckSpan = TimeSpan.FromSeconds(2.5);

	/// <summary>
	/// Calculates the estimated time to kill the specified battle character. Only applicable after the first 2.5 seconds, and uses a moving average of the last 15 seconds of health ratios
	/// </summary>
	/// <param name="battleChara">The battle character to calculate the time to kill for.</param>
	/// <param name="wholeTime">If set to <c>true</c>, calculates the total time to kill; otherwise, calculates the remaining time to kill.</param>
	/// <returns>
	/// The estimated time to kill the battle character in seconds, or <see cref="float.NaN"/> if the calculation cannot be performed.
	/// </returns>
	internal static float GetTTK(this IBattleChara battleChara, bool wholeTime = false)
	{
		if (battleChara == null)
		{
			return float.NaN;
		}

		if (battleChara.IsDummy())
		{
			return 999.99f;
		}

		const int movingAverageWindow = 5;
		ulong objId = battleChara.GameObjectId;

		DateTime startTime = DateTime.MinValue;
		float initialHpRatio = 0f;

		// Small fixed-size window for last ratios without copying the whole queue
		float[] window = new float[movingAverageWindow];
		int wCount = 0;

		foreach ((DateTime time, Dictionary<ulong, float> hpRatiosDict) in DataCenter.RecordedHP)
		{
			if (hpRatiosDict != null && hpRatiosDict.TryGetValue(objId, out float ratio) && ratio != 1f)
			{
				if (startTime == DateTime.MinValue)
				{
					startTime = time;
					initialHpRatio = ratio;
				}

				if (wCount < movingAverageWindow)
				{
					window[wCount++] = ratio;
				}
				else
				{
					// shift left by one; window is very small so this is cheap
					Array.Copy(window, 1, window, 0, movingAverageWindow - 1);
					window[movingAverageWindow - 1] = ratio;
				}
			}
		}

		if (startTime == DateTime.MinValue || (DateTime.Now - startTime) < CheckSpan)
		{
			return float.NaN;
		}

		float currentHealthRatio = battleChara.GetHealthRatio();
		if (float.IsNaN(currentHealthRatio))
		{
			return float.NaN;
		}

		float sum = 0f;
		for (int i = 0; i < wCount; i++) sum += window[i];
		float avg = wCount > 0 ? sum / wCount : 0f;

		float hpRatioDifference = initialHpRatio - avg;
		if (hpRatioDifference <= 0)
		{
			return float.NaN;
		}

		float elapsedTime = (float)(DateTime.Now - startTime).TotalSeconds;
		return elapsedTime / hpRatioDifference * (wholeTime ? 1 : currentHealthRatio);
	}

	private static readonly ConcurrentDictionary<ulong, DateTime> _aliveStartTimes = [];

	/// <summary>
	/// Gets how long the Player has been alive in seconds since their last death.
	/// </summary>
	/// <returns>
	/// The time in seconds since the character's last death or first appearance, or float.NaN if unable to determine.
	/// </returns>
	internal static float PlayerTimeAlive()
	{
		if (Player.Object == null)
		{
			return float.NaN;
		}

		// If the character is dead, reset their alive time
		if (Player.Object.IsDead || Svc.Condition[ConditionFlag.BetweenAreas])
		{
			_ = _aliveStartTimes.TryRemove(Player.Object.GameObjectId, out _);
			return 0;
		}

		// If we haven't tracked this character yet, start tracking them
		if (!_aliveStartTimes.ContainsKey(Player.Object.GameObjectId))
		{
			_aliveStartTimes[Player.Object.GameObjectId] = DateTime.Now;
		}

		return (float)(DateTime.Now - _aliveStartTimes[Player.Object.GameObjectId]).TotalSeconds > 30 ? 30 : (float)(DateTime.Now - _aliveStartTimes[Player.Object.GameObjectId]).TotalSeconds;
	}

	/// <summary>
	/// Gets how long the battleChara has been alive in seconds since their last death.
	/// </summary>
	/// <param name="battleChara">The battle character to check.</param>
	/// <returns>
	/// The time in seconds since the character's last death or first appearance, or float.NaN if unable to determine.
	/// </returns>
	internal static float TimeAlive(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return float.NaN;
		}

		// If the character is dead, reset their alive time
		if (battleChara.IsDead || Svc.Condition[ConditionFlag.BetweenAreas])
		{
			_ = _aliveStartTimes.TryRemove(battleChara.GameObjectId, out _);
			return 0;
		}

		// If we haven't tracked this character yet, start tracking them
		if (!_aliveStartTimes.ContainsKey(battleChara.GameObjectId))
		{
			_aliveStartTimes[battleChara.GameObjectId] = DateTime.Now;
		}

		return (float)(DateTime.Now - _aliveStartTimes[battleChara.GameObjectId]).TotalSeconds > 30 ? 30 : (float)(DateTime.Now - _aliveStartTimes[battleChara.GameObjectId]).TotalSeconds;
	}

	private static readonly ConcurrentDictionary<ulong, DateTime> _deadStartTimes = [];

	/// <summary>
	/// Gets how long the character has been dead in seconds since their last death.
	/// </summary>
	/// <param name="battleChara">The battle character to check.</param>
	/// <returns>
	/// The time in seconds since the character's death, or float.NaN if unable to determine or character is alive.
	/// </returns>
	internal static float TimeDead(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return float.NaN;
		}

		// If the character is alive, reset their dead time
		if (!battleChara.IsDead)
		{
			_ = _deadStartTimes.TryRemove(battleChara.GameObjectId, out _);
			return 0;
		}

		// If we haven't tracked this character's death yet, start tracking them
		if (!_deadStartTimes.ContainsKey(battleChara.GameObjectId))
		{
			_deadStartTimes[battleChara.GameObjectId] = DateTime.Now;
		}

		return (float)(DateTime.Now - _deadStartTimes[battleChara.GameObjectId]).TotalSeconds > 30 ? 30 : (float)(DateTime.Now - _deadStartTimes[battleChara.GameObjectId]).TotalSeconds;
	}

	/// <summary>
	/// Determines if the specified battle character has been attacked within the last second.
	/// </summary>
	/// <param name="battleChara">The battle character to check.</param>
	/// <returns>
	/// <c>true</c> if the battle character has been attacked within the last second; otherwise, <c>false</c>.
	/// </returns>
	internal static bool IsAttacked(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return false;
		}

		DateTime now = DateTime.Now;
		foreach ((ulong id, DateTime time) in DataCenter.AttackedTargets)
		{
			if (id == battleChara.GameObjectId)
			{
				return now - time >= TimeSpan.FromSeconds(1);
			}
		}
		return false;
	}

	private struct LosCacheEntry
	{
		public Vector3 PlayerPos;
		public Vector3 TargetPos;
		public long ExpiresAt;
		public bool Visible;
	}
	private static readonly ConcurrentDictionary<ulong, LosCacheEntry> _losCache = [];
	private const float LosPosEpsilonSq = 0.04f;   // ~20 cm tolerance
	private const long LosTtlMs = 33;      // one 30–60 FPS frame

	// Optional: clear cache when swapping areas or if it grows too large
	private static void MaybeResetLosCache()
	{
		if (Svc.Condition[ConditionFlag.BetweenAreas] || _losCache.Count > 4096)
			_losCache.Clear();
	}

	// New overload to allow caller to supply eye position once per loop
	internal static unsafe bool CanSeeFrom(this IBattleChara battleChara, Vector3 playerEyePos, float targetYOffset = 2.0f)
	{
		if (battleChara == null || Player.Object == null) return false;
		var targetStruct = battleChara.Struct();
		if (targetStruct == null) return false;

		MaybeResetLosCache();

		Vector3 targetPos = battleChara.Position; targetPos.Y += targetYOffset;
		ulong id = battleChara.GameObjectId;
		long now = Environment.TickCount64;

		if (_losCache.TryGetValue(id, out var entry))
		{
			if (now <= entry.ExpiresAt &&
				Vector3.DistanceSquared(playerEyePos, entry.PlayerPos) <= LosPosEpsilonSq &&
				Vector3.DistanceSquared(targetPos, entry.TargetPos) <= LosPosEpsilonSq)
			{
				return entry.Visible;
			}
		}

		Vector3 offset = targetPos - playerEyePos;
		float maxDist = offset.Length();
		if (maxDist < 0.01f) return true;

		Vector3 direction = offset / maxDist;

		RaycastHit hit;
		int* materialFilter = stackalloc int[] { 0x4000, 0, 0x4000, 0 };
		var module = Framework.Instance()->BGCollisionModule;
		bool blocked = module->RaycastMaterialFilter(&hit, &playerEyePos, &direction, maxDist, 1, materialFilter);

		bool visible = !blocked;
		_losCache[id] = new LosCacheEntry
		{
			PlayerPos = playerEyePos,
			TargetPos = targetPos,
			ExpiresAt = now + LosTtlMs,
			Visible = visible
		};
		return visible;
	}

	// Backward-compatible existing API calls the overload
	internal static unsafe bool CanSee(this IBattleChara battleChara, float playerYOffset = 2.0f, float targetYOffset = 2.0f)
	{
		if (battleChara == null || Player.Object == null) return false;
		Vector3 playerPos = Player.Object.Position; playerPos.Y += playerYOffset;
		return CanSeeFrom(battleChara, playerPos, targetYOffset);
	}

	/// <summary>
	/// Get the Player's current MP percentage.
	/// </summary>
	/// <returns></returns>
	public static float GetPlayerMPRatio()
	{
		if (Player.Object == null)
		{
			return 0;
		}

		if (Player.Object.MaxHp == 0)
		{
			return 0; // Avoid division by zero
		}

		return (float)Player.Object.CurrentMp / Player.Object.MaxMp;
	}

	/// <summary>
	/// Get the Player's current HP percentage.
	/// </summary>
	/// <returns></returns>
	public static float GetPlayerHealthRatio()
	{
		if (Player.Object == null)
		{
			return 0; // This may need to be changed to 100
		}

		if (DataCenter.RefinedHP.TryGetValue(Player.Object.GameObjectId, out float hp))
		{
			return hp;
		}

		if (Player.Object.MaxHp == 0)
		{
			return 0; // Avoid division by zero
		}

		return (float)Player.Object.CurrentHp / Player.Object.MaxHp;
	}

	/// <summary>
	/// Get the <paramref name="battleChara"/>'s current HP percentage.
	/// </summary>
	/// <param name="battleChara"></param>
	/// <returns></returns>
	public static float GetHealthRatio(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return 0; // This may need to be changed to 100
		}

		if (DataCenter.RefinedHP.TryGetValue(battleChara.GameObjectId, out float hp))
		{
			return hp;
		}

		if (battleChara.MaxHp == 0)
		{
			return 0; // Avoid division by zero
		}

		return (float)battleChara.CurrentHp / battleChara.MaxHp;
	}

	/// <summary>
	/// Gets the facing direction vector of the game object.
	/// </summary>
	/// <param name="battleChara">The game object.</param>
	/// <returns>
	/// A <see cref="Vector3"/> representing the facing direction of the game object.
	/// </returns>
	internal static Vector3 GetFaceVector(this IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return Vector3.Zero;
		}

		float rotation = battleChara.Rotation;
		return new Vector3((float)Math.Sin(rotation), 0, (float)Math.Cos(rotation));
	}

	/// <summary>
	/// Calculates the angle between two vectors.
	/// </summary>
	/// <param name="vec1">The first vector.</param>
	/// <param name="vec2">The second vector.</param>
	/// <returns>
	/// The angle in radians between the two vectors.
	/// </returns>
	internal static double AngleTo(this Vector3 vec1, Vector3 vec2)
	{
		double lengthProduct = vec1.Length() * vec2.Length();
		if (lengthProduct == 0)
		{
			return 0;
		}

		double dotProduct = Vector3.Dot(vec1, vec2);
		return Math.Acos(dotProduct / lengthProduct);
	}

	/// <summary>
	/// The distance from <paramref name="battleChara"/> to the player
	/// </summary>
	/// <param name="battleChara"></param>
	/// <returns></returns>
	public static float DistanceToPlayer(this IBattleChara battleChara)
	{
		if (battleChara == null || Player.Object == null)
		{
			return float.MaxValue;
		}

		float distance = Vector3.Distance(Player.Object.Position, battleChara.Position) - (Player.Object.HitboxRadius + battleChara.HitboxRadius);
		return distance;
	}

	/// <summary>
	/// The distance from <paramref name="battleChara"/> to the player's pet
	/// </summary>
	/// <param name="battleChara"></param>
	/// <returns></returns>
	public static float DistanceToPet(IBattleChara battleChara)
	{
		if (battleChara == null)
		{
			return float.MaxValue;
		}
		var pet = DataCenter.GetPet();
		if (pet == null)
		{
			return float.MaxValue;
		}

		return Vector3.Distance(pet.Position, battleChara.Position) - (battleChara.HitboxRadius);
	}

}