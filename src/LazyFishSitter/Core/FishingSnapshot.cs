namespace LazyFishSitter.Core;

/// <summary>
/// Mirror of FFXIVClientStructs' <c>FFXIV.Client.Game.Event.FishingState</c> (verified against
/// raw.githubusercontent.com/aers/FFXIVClientStructs/main/.../Event/FishingEventHandler.cs on
/// 2026-09-05). Duplicated here so the policy stays Dalamud-free and replay-testable offline;
/// <see cref="FishSitService"/> casts the real enum straight across.
/// </summary>
public enum FishState
{
    None = 0,
    CastingOut = 1,
    /// <summary>Fish slipped, no bite, briefly after reeling in, or Rest.</summary>
    PullingPoleIn = 2,
    Quitting = 3,
    /// <summary>The standby "gathering" condition: rod out, nothing in the water.</summary>
    PoleReady = 4,
    Bite = 5,
    /// <summary>Includes the subsequent reeling in.</summary>
    Hooking = 6,
    ReleasingCatch = 7,
    ConfirmingCollectable = 8,
    AmbitiousLure = 9,
    ModestLure = 10,
    Unk11 = 11,
    /// <summary>Actually fishing - line (or air, or sand) is out.</summary>
    LineInWater = 12,
}

/// <summary>
/// One read of everything the decision looks at. Equality means "nothing changed", which is what
/// drives the transition log. All game reads are primitives so this type carries no Dalamud types.
/// </summary>
public readonly record struct FishingSnapshot(
    bool Fishing,
    bool HandlerAvailable,
    FishState State,
    bool ChangingPosition,
    bool CanFish,
    string ModeName,
    byte ModeValue,
    byte ModeParam,
    ushort EmoteId,
    string GamePosture,
    bool ModeSeated,
    bool GameSeated)
{
    /// <summary>Either independent signal counts. /sit on a seated character STANDS him, so this is deliberately permissive.</summary>
    public bool Seated => ModeSeated || GameSeated;

    public string PostureText =>
        $"mode={ModeName}({ModeValue}) param={ModeParam} emote={EmoteId} posture={GamePosture} seated={Seated}";

    public override string ToString() =>
        $"fishing={Fishing} fstate={(HandlerAvailable ? $"{State}({(int)State})" : "n/a")} " +
        $"chg={ChangingPosition} canFish={CanFish} {PostureText}";

    /// <summary>An empty read for when there is no local player.</summary>
    public static FishingSnapshot Absent(bool fishing, bool handlerAvailable, FishState state, bool changing, bool canFish) =>
        new(fishing, handlerAvailable, state, changing, canFish, "None", 0, 0, 0, "n/a", false, false);
}

/// <summary>What the host tells the policy about things outside the character read.</summary>
/// <param name="Enabled">The plugin's enabled toggle.</param>
/// <param name="SitCommand">The command to send (validated by the host; must start with a slash).</param>
/// <param name="BlockReason">
/// Non-null when a game condition (cutscene, combat, jumping, ...) makes injecting a command unsafe.
/// The policy still tracks state while blocked; it just never sends.
/// </param>
public readonly record struct PolicyContext(bool Enabled, string SitCommand, string? BlockReason);

/// <summary>Outcome of one <see cref="SitPolicy.Step"/>.</summary>
/// <param name="Logs">Lines the host should write at Information. Reused buffer - drain before the next Step.</param>
/// <param name="SendCommand">Non-null means: send this slash command now.</param>
/// <param name="SkipReason">Why nothing was sent (or what was sent). Only refreshed on a deciding step.</param>
public readonly record struct PolicyStep(IReadOnlyList<string> Logs, string? SendCommand, string SkipReason);
