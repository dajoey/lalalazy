using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LazyOccultCrescent.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using XIVTreasure = Lumina.Excel.Sheets.Treasure;
using TreasureFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure.TreasureFlags;

namespace LazyOccultCrescent.Modules.Treasure;

public class Treasure(IGameObject obj)
{
    // The Treasure sheet row id. Stays BaseId because the precomputed routing
    // data in Data/<Zone>/precomputed_treasure_hunt_data.json is keyed on it.
    public uint Id
    {
        get => obj.BaseId;
    }

    // Per-instance identity. BaseId identifies the chest TYPE, so it cannot tell
    // two coffers apart; anything tracking individual chests needs this.
    public ulong ObjectId
    {
        get => obj.GameObjectId;
    }

    private TreasureFlags LastFlags = TreasureFlags.None;

    public unsafe bool CheckOpened()
    {
        var gameObject = (GameObject*)(void*)obj.Address;
        if (gameObject == null)
        {
            return false;
        }

        var instance = (FFXIVClientStructs.FFXIV.Client.Game.Object.Treasure*)gameObject;
        var currentFlags = instance->Flags;

        if (currentFlags != LastFlags)
        {
            var wasNotOpened = !LastFlags.HasFlag(TreasureFlags.Opened);
            var isNowOpened = currentFlags.HasFlag(TreasureFlags.Opened);

            LastFlags = currentFlags;

            if (wasNotOpened && isNowOpened)
            {
                return true;
            }
        }

        return false;
    }


    // Was: GetExcelSheet<XIVTreasure>().ToList().FirstOrDefault(t => t.RowId == obj.BaseId)
    // - allocating a List of every row in the sheet and linear-scanning it to find
    // one row by RowId, which is what the sheet's own O(1) indexer does for free.
    // Radar calls GetTreasureType()/GetColor() four times per chest per frame and
    // Panel a fifth, so ~10 visible chests meant 30-50 full sheet materialisations
    // every frame.
    //
    // BaseId cannot change for a given Treasure, so resolve once and cache.
    private static uint? ResolveModelId(uint baseId)
    {
        var row = Svc.Data.GetExcelSheet<XIVTreasure>().GetRowOrDefault(baseId);
        return row?.SGB.RowId;
    }

    public bool IsValid()
    {
        return obj.IsValid() && obj is { IsDead: false, IsTargetable: true };
    }

    public Vector3 GetPosition()
    {
        return obj.Position;
    }

    // Keyed on BaseId rather than cached per instance. A Treasure wrapper is held
    // across frames, and the object-table slot behind it can be recycled to a
    // different chest - an instance cache would then describe the previous
    // occupant. Type is a pure function of BaseId, so a shared map is both correct
    // and keeps the lookup off the hot path entirely.
    private readonly static Dictionary<uint, TreasureType> TypeByBaseId = [];

    public TreasureType GetTreasureType()
    {
        var baseId = obj.BaseId;

        if (!TypeByBaseId.TryGetValue(baseId, out var type))
        {
            type = (ResolveModelId(baseId) ?? 0) switch
            {
                1597 => TreasureType.Silver,
                1596 => TreasureType.Bronze,
                _ => TreasureType.Unknown,
            };

            TypeByBaseId[baseId] = type;
        }

        return type;
    }

    public Vector4 GetColor()
    {
        return GetTreasureType() switch
        {
            TreasureType.Bronze => TreasureModule.Bronze,
            TreasureType.Silver => TreasureModule.Silver,
            _ => TreasureModule.Unknown,
        };
    }

    public string GetName()
    {
        return GetTreasureType() switch
        {
            TreasureType.Bronze => "Bronze Treasure Coffer",
            TreasureType.Silver => "Silver Treasure Coffer",
            _ => "Unknown Treasure Coffer",
        };
    }
}
