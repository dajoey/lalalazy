using System;
using System.Numerics;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using Dalamud.Game.ClientState.Fates;
using ECommons.DalamudServices;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Fates;

public class Fate(IFate fate)
{
    // This is constructed for every live fate on every frame, and EventData.Fates
    // is a hand-maintained table. A raw indexer meant one unseen FATE id - a
    // content patch, a seasonal event, anything the author had not catalogued -
    // threw KeyNotFoundException every frame and took FatesModule down with it,
    // and TowerTimer and the Automator's fate selection with that.
    //
    // Degrading to a synthesised entry rather than null keeps every consumer
    // working: an uncatalogued fate simply has no curated metadata (no demiatma,
    // no aethernet hint), which is exactly the truth.
    public readonly EventData Data = Resolve(fate);

    private static EventData Resolve(IFate fate)
    {
        uint id;
        try
        {
            id = fate.FateId;
        }
        catch (AccessViolationException)
        {
            return new EventData { Type = EventType.Fate, InternalName = "Unknown Fate" };
        }

        if (EventData.Fates.TryGetValue(id, out var known))
        {
            return known;
        }

        Svc.Log.Debug($"[Fates] no catalogue entry for fate {id}; continuing without metadata");
        return new EventData { Id = id, Type = EventType.Fate, InternalName = "Unknown Fate" };
    }

    public uint Id
    {
        get
        {
            try
            {
                return fate.FateId;
            }
            catch (AccessViolationException)
            {
                return 0;
            }
        }
    }

    public string Name
    {
        get
        {
            try
            {
                return fate.Name.ToString();
            }
            catch (AccessViolationException)
            {
                return "Unknown Fate";
            }
        }
    }

    public float Radius
    {
        get
        {
            try
            {
                return Data.Radius ?? fate.Radius;
            }
            catch (AccessViolationException)
            {
                return 0f;
            }
        }
    }

    public Vector3 StartPosition
    {
        get
        {
            try
            {
                return Data.StartPosition ?? fate.Position;
            }
            catch (AccessViolationException)
            {
                return Vector3.Zero;
            }
        }
    }

    public readonly EventProgress Progress = new();

    public byte CurrentProgress
    {
        get
        {
            try
            {
                return fate.Progress;
            }
            catch (AccessViolationException)
            {
                return 100;
            }
        }
    }

    public void Update(UpdateContext context)
    {
        if (CurrentProgress <= 0)
        {
            return;
        }

        if (Progress.Count == 0 || Progress.Latest != CurrentProgress)
        {
            Progress.Add(CurrentProgress);
        }
    }

    public bool IsPotFate()
    {
        return Data.Note == MonsterNote.PersistentPots;
    }

    public Aethernet GetAethernet()
    {
        return Data.Aethernet ?? ZoneData.GetClosestAethernetShard(StartPosition);
    }
}
