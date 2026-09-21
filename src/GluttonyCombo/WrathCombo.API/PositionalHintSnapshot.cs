using GluttonyCombo.API.Enum;

namespace GluttonyCombo.API;

/// <summary>
///     Decoded upcoming positional hint from <see cref="WrathIPCWrapper.GetUpcomingPositionalHint"/>.
/// </summary>
public readonly struct PositionalHintSnapshot
{
    public const int FieldCount = 7;

    public const int IndexDirection = 0;
    public const int IndexActionId = 1;
    public const int IndexGcdsUntil = 2;
    public const int IndexTargetObjectId = 3;
    public const int IndexExpiresInMs = 4;
    public const int IndexCurrentAngle = 5;
    public const int IndexIsSatisfied = 6;

    public PositionalDirection Direction { get; init; }
    public uint ActionId { get; init; }
    public int GcdsUntil { get; init; }
    public uint TargetObjectId { get; init; }
    public int ExpiresInMs { get; init; }
    public byte CurrentAngle { get; init; }
    public bool IsSatisfied { get; init; }

    public bool IsActive =>
        Direction is not PositionalDirection.None &&
        ActionId is not 0 &&
        GcdsUntil > 0 &&
        ExpiresInMs > 0;

    public static bool TryFromWire(uint[]? wire, out PositionalHintSnapshot snapshot)
    {
        snapshot = default;

        if (wire is null || wire.Length < FieldCount)
            return false;

        snapshot = new PositionalHintSnapshot
        {
            Direction = (PositionalDirection)wire[IndexDirection],
            ActionId = wire[IndexActionId],
            GcdsUntil = (int)wire[IndexGcdsUntil],
            TargetObjectId = wire[IndexTargetObjectId],
            ExpiresInMs = (int)wire[IndexExpiresInMs],
            CurrentAngle = (byte)wire[IndexCurrentAngle],
            IsSatisfied = wire[IndexIsSatisfied] is not 0,
        };

        return snapshot.IsActive;
    }

    public uint[] ToWire() =>
    [
        (uint)Direction,
        ActionId,
        (uint)GcdsUntil,
        TargetObjectId,
        (uint)ExpiresInMs,
        CurrentAngle,
        IsSatisfied ? 1u : 0u,
    ];
}
