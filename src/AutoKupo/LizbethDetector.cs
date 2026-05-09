using Dalamud.Game.ClientState.Objects.Enums;
using System;
using System.Numerics;

namespace AutoKupo;

internal sealed class LizbethDetector
{
    private const string LIZBETH_NAME = "Lizbeth";

    public bool TryFindLizbeth(out float distance, out ulong objectId)
    {
        distance = float.MaxValue;
        objectId = 0;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null || !player.IsValid())
            return false;

        var playerPos = player.Position;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null || !obj.IsValid())
                continue;

            if (obj.ObjectKind != ObjectKind.EventNpc)
                continue;

            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name))
                continue;

            if (!name.Contains(LIZBETH_NAME, StringComparison.OrdinalIgnoreCase))
                continue;

            var dist = Vector3.Distance(playerPos, obj.Position);
            if (dist < distance)
            {
                distance = dist;
                objectId = obj.GameObjectId;
            }
        }

        return objectId != 0;
    }
}
