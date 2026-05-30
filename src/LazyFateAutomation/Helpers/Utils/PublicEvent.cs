using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;

namespace LazyFateAutomation.Helpers.Utils;

public enum FateType {
    Normal,
    DynamicEvent, // forays
    MechaEvent, // cosmic exploration
}

/// <summary>
/// Wrapper for all public event types (FATEs, Dynamic Events, Mecha Events)
/// </summary>
public unsafe class PublicEvent(nint address, FateType fateType, uint id) {
    public IntPtr Address { get; } = address;
    public FateType FateType { get; } = fateType;
    public uint Id { get; } = id;

    public static implicit operator PublicEvent(FateContext* fate) {
        ArgumentNullException.ThrowIfNull(fate);
        return new((nint)fate, FateType.Normal, fate->FateId);
    }
    public static implicit operator PublicEvent(Pointer<FateContext> fate) {
        ArgumentNullException.ThrowIfNull(fate.Value);
        return new((nint)fate.Value, FateType.Normal, fate.Value->FateId);
    }
    public static implicit operator PublicEvent(DynamicEvent* dynamicEvent) {
        ArgumentNullException.ThrowIfNull(dynamicEvent);
        return new((nint)dynamicEvent, FateType.DynamicEvent, dynamicEvent->DynamicEventId);
    }
    public static implicit operator PublicEvent(DynamicEvent dynamicEvent) => new((nint)(&dynamicEvent), FateType.DynamicEvent, dynamicEvent.DynamicEventId);
    public static implicit operator PublicEvent(WKSMechaEvent* mechaEvent) {
        ArgumentNullException.ThrowIfNull(mechaEvent);
        return new((nint)mechaEvent, FateType.MechaEvent, mechaEvent->WKSMechaEventDataRowId);
    }
    public static implicit operator PublicEvent(WKSMechaEvent mechaEvent) => new((nint)(&mechaEvent), FateType.MechaEvent, mechaEvent.WKSMechaEventDataRowId);

    public static PublicEvent? CurrentFate => Svc.Objects.LocalPlayer.Territory.Value.TerritoryIntendedUse.Value.StructsEnum switch {
        TerritoryIntendedUse.Overworld => GetCurrentFateOverworld(),
        TerritoryIntendedUse.Bozja or TerritoryIntendedUse.OccultCrescent => GetCurrentForayEvent(),
        TerritoryIntendedUse.CosmicExploration => GetCurrentCosmicEvent(),
        _ => null, // zone doesn't support FATEs (instance, city, etc.)
    };

    public static IEnumerable<PublicEvent> Fates => Svc.Objects.LocalPlayer.Territory.Value.TerritoryIntendedUse.Value.StructsEnum switch {
        TerritoryIntendedUse.Overworld => GetOverworldFates(),
        TerritoryIntendedUse.Bozja or TerritoryIntendedUse.OccultCrescent => GetForayFates(),
        TerritoryIntendedUse.CosmicExploration => GetCosmicFates(),
        _ => [],
    };

    public static PublicEvent? GetFateById(uint id) => Fates.FirstOrDefault(f => f.Id == id);

    private static PublicEvent? GetCurrentFateOverworld() {
        var fate = FateManager.Instance()->CurrentFate;
        return fate != null ? (PublicEvent)fate : null;
    }

    private static PublicEvent? GetCurrentForayEvent() {
        var container = DynamicEventContainer.GetInstance();
        if (container != null) {
            var dynamicEvent = container->GetCurrentEvent();
            if (dynamicEvent != null && dynamicEvent->State != DynamicEventState.Inactive)
                return (PublicEvent)dynamicEvent;
        }
        return GetCurrentFateOverworld();
    }

    private static PublicEvent? GetCurrentCosmicEvent() {
        var mechaEvent = WKSManager.Instance()->MechaEventModule->CurrentEvent;
        if (mechaEvent != null && mechaEvent->Flags.HasFlag(WKSMechaEventFlag.IsEventActive))
            return (PublicEvent)mechaEvent;
        return GetCurrentFateOverworld();
    }

    // on the frame a fate spawns, some of its values are default (at least position), so don't add them until that's loaded
    private static IEnumerable<PublicEvent> GetOverworldFates()
        => FateManager.Instance()->Fates.Select(evt => (PublicEvent)evt).Where(evt => evt.Position != Vector3.Zero);

    private static IEnumerable<PublicEvent> GetForayFates() {
        var overworldFates = GetOverworldFates();

        var container = DynamicEventContainer.GetInstance();
        if (container == null)
            return overworldFates;

        var dynamicEvents = container->Events.ToArray().Where(evt => evt.State != DynamicEventState.Inactive).Select(evt => (PublicEvent)evt);
        return overworldFates.Concat(dynamicEvents);
    }

    private static IEnumerable<PublicEvent> GetCosmicFates() {
        var overworldFates = GetOverworldFates();
        var mechaEvents = WKSManager.Instance()->MechaEventModule->Events.ToArray().Where(evt => evt.Flags.HasFlag(WKSMechaEventFlag.IsEventActive)).Select(evt => (PublicEvent)evt);
        return overworldFates.Concat(mechaEvents);
    }

    private FateContext* GetFate() {
        var fate = (FateContext*)Address;
        if (fate != null && fate->FateId == Id)
            return fate;
        return FateManager.Instance()->GetFateById((ushort)Id);
    }

    private DynamicEvent GetDynamicEvent() {
        var dynamicEvent = (DynamicEvent*)Address;
        if (dynamicEvent != null && dynamicEvent->DynamicEventId == Id)
            return *dynamicEvent;

        var container = DynamicEventContainer.GetInstance();
        if (container == null)
            throw new InvalidOperationException("DynamicEventContainer instance is null");

        foreach (var evt in container->Events)
            if (evt.DynamicEventId == Id)
                return evt;
        throw new InvalidOperationException($"DynamicEvent with ID {Id} not found");
    }

    private WKSMechaEvent GetMechaEvent() {
        var mechaEvent = (WKSMechaEvent*)Address;
        if (mechaEvent != null && mechaEvent->WKSMechaEventDataRowId == Id)
            return *mechaEvent;

        foreach (var evt in WKSManager.Instance()->MechaEventModule->Events)
            if (evt.WKSMechaEventDataRowId == Id)
                return evt;
        throw new InvalidOperationException($"WKSMechaEvent with ID {Id} not found");
    }

    private T GetValue<T>(Func<nint, T> getFate, Func<DynamicEvent, T> getDynamicEvent, Func<WKSMechaEvent, T> getMechaEvent, T defaultValue = default!) => FateType switch {
        FateType.Normal => getFate((nint)GetFate()),
        FateType.DynamicEvent => getDynamicEvent(GetDynamicEvent()),
        FateType.MechaEvent => getMechaEvent(GetMechaEvent()),
        _ => defaultValue,
    };

    public Vector3 Position => GetValue(
        fate => fate.As<FateContext>()->Location,
        dynamicEvent => dynamicEvent.MapMarker.Position,
        mechaEvent => mechaEvent.MapMarkers[0].MapMarkerData.Position,
        Vector3.Zero
    );

    public float Radius => GetValue(
        fate => fate.As<FateContext>()->Radius,
        dynamicEvent => dynamicEvent.MapMarker.Radius,
        mechaEvent => mechaEvent.MapMarkers[0].MapMarkerData.Radius,
        0f
    );

    public int Progress => GetValue(
        fate => fate.As<FateContext>()->Progress,
        dynamicEvent => dynamicEvent.Progress,
        mechaEvent => mechaEvent.EventProgress,
        0
    );

    public int Duration => GetValue(
        fate => fate.As<FateContext>()->Duration,
        dynamicEvent => (int)dynamicEvent.SecondsDuration,
        mechaEvent => mechaEvent.EventEndTimestamp - mechaEvent.EventStartTimestamp,
        0
    );

    public float TimeRemaining => GetValue(
        fate => fate.As<FateContext>()->StartTimeEpoch + fate.As<FateContext>()->Duration - DateTimeOffset.Now.ToUnixTimeSeconds(),
        dynamicEvent => dynamicEvent.SecondsLeft,
        mechaEvent => mechaEvent.EventStartTimestamp + (mechaEvent.EventEndTimestamp - mechaEvent.EventStartTimestamp) - DateTimeOffset.Now.ToUnixTimeSeconds(),
        -1f
    );

    public int StartTimeEpoch => GetValue(
        fate => fate.As<FateContext>()->StartTimeEpoch,
        dynamicEvent => dynamicEvent.StartTimestamp,
        mechaEvent => mechaEvent.EventStartTimestamp,
        0
    );

    public int EndTimeEpoch => GetValue(
        fate => fate.As<FateContext>()->StartTimeEpoch + fate.As<FateContext>()->Duration,
        dynamicEvent => (int)(dynamicEvent.StartTimestamp + dynamicEvent.SecondsDuration),
        mechaEvent => mechaEvent.EventEndTimestamp,
        0
    );

    public bool HasBonus => GetValue(
        fate => fate.As<FateContext>()->IsBonus,
        _ => false,
        _ => false,
        false
    );

    public byte Level => GetValue(
        fate => fate.As<FateContext>()->Level,
        dynamicEvent => (byte)dynamicEvent.MapMarker.RecommendedLevel,
        mechaEvent => (byte)mechaEvent.MapMarkers[0].MapMarkerData.RecommendedLevel,
        (byte)0
    );

    public string Name => FateType switch {
        FateType.Normal => Svc.Data.GetRef<Sheets.Fate>(Id).Value.Name.ToString() ?? $"FATE {Id}",
        FateType.DynamicEvent => Svc.Data.GetRef<Sheets.DynamicEvent>(Id).Value.Name.ToString() ?? $"DynamicEvent {Id}",
        FateType.MechaEvent => Svc.Data.GetRef<Sheets.WKSMechaEventData>(Id).Value.Name.ToString() ?? $"MechaEvent {Id}",
        _ => $"Unknown Type: {Id}",
    };

    public uint MotivationNpcId => GetValue(
        fate => fate.As<FateContext>()->MotivationNpc,
        _ => 0u,
        _ => 0u,
        0u
    );

    public IGameObject? MotivationNpc => GetValue(
        // sometimes when they're initially loaded, the gameobject is garbage with an entity id of 200000001 and object kind MountType
        fate => Svc.Objects.FirstOrDefault(o => o.EntityId == fate.As<FateContext>()->MotivationNpc && (o.ObjectKind == ObjectKind.BattleNpc || o.ObjectKind == ObjectKind.EventNpc)),
        _ => null,
        _ => null,
        null
    );

    public IGameObject? ObjectiveNpc => GetValue(
        fate => Svc.Objects.FirstOrDefault(o => o.EntityId == fate.As<FateContext>()->ObjectiveNpc),
        _ => null,
        _ => null,
        null
    );

    public ItemHandle? EventItem => GetValue(
        fate => (ItemHandle)fate.As<FateContext>()->TurnInEventItem,
        _ => null,
        _ => null,
        null
    );

    public FateState State => GetValue(
        fate => fate.As<FateContext>()->State,
        dynamicEvent => ToFateState(dynamicEvent.State),
        mechaEvent => ToFateState(mechaEvent.Flags),
        (FateState)0
    );

    /// <summary>When a fate hasn't appeared on the map yet</summary>
    public bool IsPending => State == FateState.Running && TimeRemaining <= 0 || State == FateState.Preparing && !IsOnMap;

    public bool IsOnMap {
        get {
            // markers go stale when the map isn't open, so force an update
            var agent = AgentMap.Instance();
            agent->UpdateEventMapMarkers(&agent->EventMarkersPtrs);
            return agent->EventMarkers.Any(m => m is { IconId: not 0, DataId: not 0 and var id } && id == Id);
        }
    }

    public FateRule Rule => GetValue(
        fate => (FateRule)fate.As<FateContext>()->Rule,
        _ => FateRule.Normal,
        _ => FateRule.Normal,
        FateRule.None
    );

    private FateState ToFateState(DynamicEventState state) => state switch {
        DynamicEventState.Register => FateState.Preparing,
        DynamicEventState.Warmup => FateState.Preparing,
        DynamicEventState.Battle => FateState.Running,
        _ => FateState.Ended,
    };

    private FateState ToFateState(WKSMechaEventFlag flag) => flag switch {
        WKSMechaEventFlag.IsEventActive => FateState.Running,
        _ => FateState.Ended,
    };

    public enum FateRule : byte {
        None = 0,
        Normal = 1, // trash fates or boss fates
        Collect = 2, // pick up EventObjects or get them from killing mobs
        Escort = 3, // guide some npc to the finish line
        Defend = 4, // defend objectives like crates from being destroyed
        EventFate = 5, // used for seasonal event fates, like Little Ladies Day, Hatching Tide
        Chase = 6, // that one special fate in The Peaks
        ConcertedWorks = 7, // rebuilding the firmament fates
        Fete = 8, // firmament fates
    }
}

