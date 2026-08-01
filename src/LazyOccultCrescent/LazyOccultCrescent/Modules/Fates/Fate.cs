using System;
using System.Numerics;
using LazyOccultCrescent.Data;
using LazyOccultCrescent.Enums;
using Dalamud.Game.ClientState.Fates;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Fates;

public class Fate(IFate fate)
{
    public readonly EventData Data = EventData.Fates[fate.FateId];

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
