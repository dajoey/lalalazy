// Shared, fully self-contained fake party member for the harnesses' cleanse cases (the
// 2026-10-24 FATE report): the heal-combo Esuna branches only ever touch the candidate through
// the per-harness faked extension members (GameObjectId, doom, dispellable status ids) and the
// faked GetTargetHPPercent, so everything this interface body declares is inert by design.
// Linked into every rotation harness csproj; namespace is the parent of the per-harness ones so
// every harness sees it without a using.

using Dalamud.Game.ClientState.Objects.Types;

namespace GluttonyCombo.RotationHarness;

internal sealed class FakeCleanseAlly : IBattleChara
{
    internal static FakeCleanseAlly Instance { get; } = new();

    // IGameObject
    public Dalamud.Game.Text.SeStringHandling.SeString Name => "CleanseAlly";
    public ulong GameObjectId => 0xC1EA5E;
    public uint EntityId => 0xC1EA5E;
    public uint DataId => 0;
    public uint BaseId => 0;
    public uint OwnerId => 0;
    public ushort ObjectIndex => 3;
    public Dalamud.Game.ClientState.Objects.Enums.ObjectKind ObjectKind =>
        Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc; // unused by the code under test
    public byte SubKind => 0;
    public byte YalmDistanceX => 3;
    public byte YalmDistanceZ => 0;
    public byte CurrentDistance => 3;
    public byte NextDistance => 3;
    public bool IsDead => false;
    public bool IsTargetable => true;
    public System.Numerics.Vector3 Position => new(0f, 0f, -3f);
    public float Rotation => 0f;
    public float HitboxRadius => 1f;
    public ulong TargetObjectId => 0;
    public IGameObject? TargetObject => null;
    public nint Address => (nint)0x2000_0000;
    public bool IsValid() => true;
    public bool Equals(IGameObject? other) => other == this;

    // ICharacter
    public uint CurrentHp => 10_000;
    public uint MaxHp => 10_000;
    public uint CurrentMp => 10_000;
    public uint MaxMp => 10_000;
    public uint CurrentGp => 0;
    public uint MaxGp => 0;
    public uint CurrentCp => 0;
    public uint MaxCp => 0;
    public byte ShieldPercentage => 0;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.ClassJob> ClassJob => default;
    public byte Level => 100;
    public Span<byte> Customize => Span<byte>.Empty;
    public Dalamud.Game.ClientState.Customize.ICustomizeData? CustomizeData => null;
    public Dalamud.Game.Text.SeStringHandling.SeString CompanyTag => "";
    public uint NameId => 0;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.OnlineStatus> OnlineStatus => default;
    public Dalamud.Game.ClientState.Objects.Enums.StatusFlags StatusFlags => default;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Mount>? CurrentMount => null;
    public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Companion>? CurrentMinion => null;

    // IBattleChara: statuses go through the per-harness FakeGame knobs
    public Dalamud.Game.ClientState.Statuses.StatusList? StatusList => null;
    public bool IsCasting => false;
    public bool IsCastInterruptible => false;
    public byte CastActionType => 0;
    public uint CastActionId => 0;
    public ulong CastTargetObjectId => 0;
    public float CurrentCastTime => 0f;
    public float BaseCastTime => 0f;
    public float TotalCastTime => 0f;
}
