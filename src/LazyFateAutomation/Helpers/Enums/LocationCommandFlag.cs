namespace LazyFateAutomation.Helpers.Enums;

/// <summary>
/// Command ids for <see cref="FFXIVClientStructs.FFXIV.Client.Game.GameMain.ExecuteLocationCommand"/>
/// </summary>
/// https://github.com/AtmoOmen/OmenTools/blob/main/Infos/ExecuteCommandComplexFlag.cs
public enum LocationCommandFlag {
    /// <summary>
    /// Dismount from low altitude flight (location)
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Target Location</para>
    /// <para><c>param1</c>: Player Rotation Angle</para>
    /// <para><c>param2</c>: Unk, always 1</para>
    /// <para><c>param2</c>: Unk, always 0</para>
    /// </remarks>
    Dismount = 101,

    /// <summary>
    /// Unknown
    /// </summary>
    Unk208 = 208,

    /// <summary>
    /// Dive through (location)
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Target location</para>
    /// <para><c>param1</c>: Player rotation angle</para>
    /// </remarks>
    DiveThrough = 209,

    /// <summary>
    /// Unknown
    /// </summary>
    Unk212 = 212,

    /// <summary>
    /// Place target marker
    /// </summary>
    /// <remarks>
    /// <para><c>target</c>: Target Entity ID</para>
    /// <para><c>param1</c>: Target marker index (starts from 0)</para>
    /// </remarks>
    PlaceMarker = 301,

    /// <summary>
    /// Use emote
    /// </summary>
    /// <remarks>
    /// <para><c>target</c>: Target Entity ID</para>
    /// <para><c>param1</c>: Emote ID</para>
    /// <para><c>param3</c>: Whether to send emote message (1 - Do not send, 0 - Send)</para>
    /// </remarks>
    Emote = 500,

    /// <summary>
    /// Use emote (location)
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Target location</para>
    /// <para><c>param1</c>: Emote ID</para>
    /// <para><c>param2</c>: Angle</para>
    /// <para><c>param4</c>: Player rotation angle</para>
    /// </remarks>
    EmoteLocation = 501,

    /// <summary>
    /// Interrupt current emote (location)
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Target location</para>
    /// <para><c>param2</c>: Rotation Packet</para>
    /// </remarks>
    EmoteInterruptLocation = 504,

    /// <summary>
    /// Unknown (location)
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Location</para>
    /// <para><c>param1</c>: Rotation Packet</para>
    /// <para><c>param2</c>: Unknown</para>
    /// <para><c>param3</c>: Unknown</para>
    /// </remarks>
    Unk603 = 603,

    /// <summary>
    /// Dive end (location)
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Target location</para>
    /// <para><c>param1</c>: Rotation Packet</para>
    /// <para><c>param2</c>: Whether player is on mount (1 - Yes, 0 - No)</para>
    /// </remarks>
    DiveEnd = 607,

    /// <summary>
    /// Invalid dive → Return to current map spawn point
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Current location (appears to have no effect)</para>
    /// <para><c>param1</c>: Unknown (appears to have no effect)</para>
    /// </remarks>
    DiveInvalid = 610,

    /// <summary>
    /// Pet action
    /// </summary>
    /// <remarks>
    /// <para><c>target/location</c>: 0xE0000000 / Destination location (movement only)</para>
    /// <para><c>param1</c>: Pet Action ID</para>
    /// </remarks>
    PetAction = 1800,

    /// <summary>
    /// Squadron action
    /// </summary>
    /// <remarks>
    /// <para><c>target</c>: Target Entity ID</para>
    /// <para><c>param1</c>: BgcArmyAction ID</para>
    /// </remarks>
    BgcArmyAction = 1810,

    /// <summary>
    /// Unknown (location)
    /// </summary>
    /// <remarks>
    /// <para><c>location</c>: Location</para>
    /// <para><c>param1</c>: Entity ID</para>
    /// </remarks>
    Unk2000 = 2000,
}
