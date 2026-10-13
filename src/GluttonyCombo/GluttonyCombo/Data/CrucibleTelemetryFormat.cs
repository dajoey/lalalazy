using System;
using System.Globalization;
using System.Text;

namespace GluttonyCombo.Data;

/// <summary>
///     The pure half of the Beastmaster Crucible of the Unbroken collector (<c>CR|</c>): emit gate and line
///     format, no Dalamud/game types, asserted by <c>tests/GluttonyCombo.TelemetryHarness</c>. Written next to
///     <c>BT|</c> (same switch, same 4 lines/s floor) only while standing on a Crucible board, so the Crucible
///     rules can be graded from <c>plugin_log_lines</c>: what the plugin saw, what it did, and which
///     Snarl / Challenge it would have pressed in shadow mode.
/// </summary>
internal static class CrucibleTelemetryFormat
{
    /// <summary> Fixed, greppable line prefix: <c>message LIKE 'CR|%'</c>. </summary>
    public const string Prefix = "CR|";

    /// <summary> Hard budget for one emitted line (room for the longest reason and shadow plus the trend fields). </summary>
    public const int MaxLineLength = 360;

    /// <inheritdoc cref="BeastmasterTelemetryFormat.MinIntervalMs"/>
    public const int MinIntervalMs = BeastmasterTelemetryFormat.MinIntervalMs;

    /// <summary> HP values are compared in 5% buckets so a slow bleed does not defeat the change gate. </summary>
    public const int HpBucket = 5;

    /// <summary> Per-tick observations, rendered as single letters in <c>f=</c>. </summary>
    [Flags]
    internal enum Flags : ushort
    {
        None = 0,
        /// <summary> D: dispellable buff on the target. </summary>
        TargetDispellable = 1 << 0,
        /// <summary> S: target in a counter / reflect stance. </summary>
        TargetStance = 1 << 1,
        /// <summary> X: target must not be attacked (egg, morpho). </summary>
        TargetDoNotAttack = 1 << 2,
        /// <summary> N: a do-not-attack enemy within AoE reach of the target. </summary>
        ProtectedNear = 1 << 3,
        /// <summary> P: target has Directional Parry. </summary>
        TargetParry = 1 << 4,
        /// <summary> J: Directional Parry just ended. </summary>
        ParryEnded = 1 << 5,
        /// <summary> C: the character has a cleansable debuff. </summary>
        PlayerCleansable = 1 << 6,
        /// <summary> p: the target is attacking the familiar. </summary>
        TargetOnPet = 1 << 7,
        /// <summary> y: the target is attacking the character. </summary>
        TargetOnPlayer = 1 << 8,
        /// <summary> s: Snarl ready. </summary>
        SnarlReady = 1 << 9,
        /// <summary> c: Challenge ready. </summary>
        ChallengeReady = 1 << 10,
        /// <summary> i: the target's cast is interruptible. </summary>
        Interruptible = 1 << 11,
        /// <summary> R: a do-not-attack enemy is inside the active familiar's own release area (crux lane 2026-10-13). </summary>
        ReleaseProtectedNear = 1 << 12,
        /// <summary> B: Blink is up on the character. </summary>
        PlayerBlink = 1 << 13,
        /// <summary> V: the familiar carries a poison it sheds by leaving. </summary>
        PetPoisoned = 1 << 14,
    }

    private const string FlagLetters = "DSXNPJCpysciRBV";

    /// <summary> Panel needs of the matched battle, rendered as letters in <c>nd=</c> (I interrupt, D dispel, C cleanse). </summary>
    internal readonly record struct Snapshot(
        byte Board,
        sbyte Battle,
        byte Needs,
        byte Enemies,
        byte HighestEnemyHp,
        uint TargetNameId,
        byte TargetHp,
        uint CastId,
        float CastRemaining,
        Flags Observed,
        byte PlayerHp,
        byte PetHp,
        string? SlotPetHp,
        uint DecisionActionId,
        string? DecisionReason,
        string? Shadow,
        float TimeToDeath = 0f,
        int IntakePerSecond = 0,
        float VulnerabilityRemaining = 0f,
        bool PartyHpVerified = false,
        float TargetEdgeDistance = -1f,
        float MoveSpeed = 0f,
        int Degree = -1,
        bool BmrSeen = false,
        float BmrHitIn = float.MaxValue,
        int BmrHitType = 0,
        bool BmrHitOnPlayer = false,
        bool BmrHitOnFamiliar = false,
        float BmrMaxCast = float.MaxValue);

    internal static (byte, sbyte, byte, byte, int, uint, int, uint, Flags, int, int, string, uint, string, string, byte) KeyOf(in Snapshot s) =>
        (s.Board, s.Battle, s.Needs, s.Enemies, s.HighestEnemyHp / HpBucket, s.TargetNameId, s.TargetHp / HpBucket, s.CastId, s.Observed,
         s.PlayerHp / HpBucket, s.PetHp / HpBucket, s.SlotPetHp ?? "", s.DecisionActionId, s.DecisionReason ?? "", s.Shadow ?? "", BmrKind(s));

    /// <summary> BMR's prediction as a change key: answering, type and target (the countdown and the cast limit stay out of it). </summary>
    private static byte BmrKind(in Snapshot s) =>
        !s.BmrSeen ? (byte)0 : (byte)(1 | (Math.Clamp(s.BmrHitType, 0, 3) << 1) | (s.BmrHitOnPlayer ? 8 : 0) | (s.BmrHitOnFamiliar ? 16 : 0));

    /// <summary> Same contract as <see cref="BeastmasterTelemetryFormat.ShouldEmit"/>: on change, at most every <see cref="MinIntervalMs"/>. </summary>
    internal static bool ShouldEmit(ref GateState state, long nowMs, in Snapshot snapshot)
    {
        var key = KeyOf(snapshot);

        if (state.HasLast && state.LastKey == key)
            return false;

        if (state.HasEmitted && nowMs - state.LastEmitMs < MinIntervalMs)
            return false;

        state.HasLast = true;
        state.LastKey = key;
        state.HasEmitted = true;
        state.LastEmitMs = nowMs;
        return true;
    }

    internal struct GateState
    {
        public bool HasLast;
        public (byte, sbyte, byte, byte, int, uint, int, uint, Flags, int, int, string, uint, string, string, byte) LastKey;
        public bool HasEmitted;
        public long LastEmitMs;

        public void Reset() => this = default;
    }

    /// <summary>
    ///     <c>CR|unixms|b=board|bt=battle|nd=needs|ne=enemies|hi=highestHp|t=nameId:hp|c=castId:remaining|f=flags|hp=player|pet=familiar|sl=h1.h2.h3|dec=id:reason|sh=shadow|ttd=s|in=hp/s|vul=s|xp=0/1</c>.
    ///     <c>ttd</c> is the target's estimated seconds to death (0 unknown), <c>in</c> the character's HP loss per second
    ///     over 10 s, <c>vul</c> the target's Physical Vulnerability Up left, <c>xp</c> whether the familiar party HP read
    ///     has been verified against a live familiar. <c>d</c> (1.0.4.266) is the hitbox-edge distance to the current
    ///     target (empty without one) and <c>mv</c> the character's own movement speed in yalms/s: together they name
    ///     range and movement on every line, without joining the emit key. <c>dg</c> (1.0.4.288) is the board's difficulty degree
    ///     (0 Standard, 1 First, 2 Second, 3 Third, x while LazyCrucible has not published it), also outside the emit key.
    ///     <c>bt</c> is -1 when no panel enemy is present; <c>c=0:0.0</c> when the target is not casting.
    ///     While stock BossMod Reborn answers its hint IPC (crux lane 2026-10-13) the line ends <c>|bh=s|bk=type|bp=0/1|bf=0/1|mct=s</c>:
    ///     seconds to its earliest predicted hit ('-' none), that hit's type (0 none, 1 tankbuster, 2 raidwide, 3 shared), whether it is
    ///     on the character / the familiar, and the longest cast its movement allows ('-' none). Type and target join the emit key.
    /// </summary>
    internal static string BuildLine(long unixMs, in Snapshot s)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(MaxLineLength + 32);

        sb.Append(Prefix).Append(unixMs.ToString(inv))
          .Append("|b=").Append(s.Board.ToString(inv))
          .Append("|bt=").Append(s.Battle.ToString(inv))
          .Append("|nd=");
        if ((s.Needs & 1) != 0) sb.Append('I');
        if ((s.Needs & 2) != 0) sb.Append('D');
        if ((s.Needs & 4) != 0) sb.Append('C');

        sb.Append("|ne=").Append(s.Enemies.ToString(inv))
          .Append("|hi=").Append(s.HighestEnemyHp.ToString(inv))
          .Append("|t=").Append(s.TargetNameId.ToString(inv)).Append(':').Append(s.TargetHp.ToString(inv))
          .Append("|c=").Append(s.CastId.ToString(inv)).Append(':').Append(Math.Max(0f, s.CastRemaining).ToString("0.0", inv))
          .Append("|f=");
        for (var i = 0; i < FlagLetters.Length; i++)
            if (((ushort)s.Observed & (1 << i)) != 0)
                sb.Append(FlagLetters[i]);

        sb.Append("|hp=").Append(s.PlayerHp.ToString(inv))
          .Append("|pet=").Append(s.PetHp.ToString(inv))
          .Append("|sl=").Append(Clean(s.SlotPetHp, 14))
          .Append("|dec=").Append(s.DecisionActionId.ToString(inv)).Append(':').Append(Clean(s.DecisionReason, 40))
          .Append("|sh=").Append(Clean(s.Shadow, 30))
          .Append("|ttd=").Append(Math.Min(999f, Math.Max(0f, s.TimeToDeath)).ToString("0", inv))
          .Append("|in=").Append(Math.Max(0, s.IntakePerSecond).ToString(inv))
          .Append("|vul=").Append(Math.Max(0f, s.VulnerabilityRemaining).ToString("0", inv))
          .Append("|xp=").Append(s.PartyHpVerified ? '1' : '0')
          .Append("|d=");
        if (s.TargetEdgeDistance >= 0f)
            sb.Append(Math.Min(99f, s.TargetEdgeDistance).ToString("0.0", inv));
        sb.Append("|mv=").Append(Math.Min(99f, Math.Max(0f, s.MoveSpeed)).ToString("0.0", inv))
          .Append("|dg=").Append(s.Degree is >= 0 and <= 3 ? (char)('0' + s.Degree) : 'x');

        // Stock BMR's incoming-hit clock (crux lane 2026-10-13), only while BMR answers.
        if (s.BmrSeen)
        {
            sb.Append("|bh=");
            if (s.BmrHitIn < 1000f)
                sb.Append(Math.Max(0f, s.BmrHitIn).ToString("0.0", inv));
            else
                sb.Append('-');
            sb.Append("|bk=").Append(Math.Clamp(s.BmrHitType, 0, 9).ToString(inv))
              .Append("|bp=").Append(s.BmrHitOnPlayer ? '1' : '0')
              .Append("|bf=").Append(s.BmrHitOnFamiliar ? '1' : '0')
              .Append("|mct=");
            if (s.BmrMaxCast < 1000f)
                sb.Append(Math.Max(0f, s.BmrMaxCast).ToString("0.0", inv));
            else
                sb.Append('-');
        }

        if (sb.Length > MaxLineLength)
            sb.Length = MaxLineLength;
        return sb.ToString();
    }

    /// <summary> Internal literals only, but never let one fabricate a field or overrun the budget. </summary>
    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        Span<char> buffer = stackalloc char[Math.Min(text.Length, maxLength)];
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = text[i];
            buffer[i] = c is '|' or '\r' or '\n' ? '_' : c;
        }
        return new string(buffer);
    }
}
