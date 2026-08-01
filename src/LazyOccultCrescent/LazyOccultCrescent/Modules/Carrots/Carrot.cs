using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;

namespace LazyOccultCrescent.Modules.Carrots;

public class Carrot(IGameObject obj)
{
    public static Vector4 Color { get; } = new(0.2f, 0.8f, 0.2f, 1f);

    public bool IsValid()
    {
        return obj is { IsDead: false } && obj.IsValid();
    }

    public Vector3 GetPosition()
    {
        return obj.Position;
    }
}
