using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.CrucibleReplay;

/// <summary> A rebuilt tick: the state, the settings the replay used, and which fields are gaps (not rebuilt for this tick). </summary>
internal sealed class RebuiltTick
{
    public CrLine Cr = null!;
    public BstState State;
    public BstSettings Cfg;
    public HashSet<string> Gaps = new();
    public BtLine? Bt;
    public double BtAgeSeconds;
}

/// <summary>
///     Builds a <see cref="BstState"/> for each CR| tick from that line, the newest BT| line at or before it (within
///     <see cref="BtMaxAgeMs"/>), and the newest DS| line within <see cref="DsMaxAgeMs"/>. Stateful over the window: SinceSummon
///     needs the BT| series. Every field the lines cannot give is listed in <see cref="RebuiltTick.Gaps"/>; nothing is guessed silently.
/// </summary>
internal sealed class Rebuilder
{
    public const long BtMaxAgeMs = 2000;
    public const long DsMaxAgeMs = 2000;

    private readonly LogWindow _w;
    private int _lastSlot = -1;
    private long _slotSinceMs = -1;
    private bool _slotSinceKnown;
    private int _btIdx = -1, _dsIdx = -1, _btSeen = -1;

    public Rebuilder(LogWindow w) => _w = w;

    /// <summary> Must be called for CR ticks in time order. </summary>
    public RebuiltTick Rebuild(CrLine cr)
    {
        var t = new RebuiltTick { Cr = cr };
        var s = new BstState();
        var gaps = t.Gaps;

        // newest BT| / DS| at or before the tick
        while (_btIdx + 1 < _w.Bt.Count && _w.Bt[_btIdx + 1].Ms <= cr.Ms) _btIdx++;
        while (_dsIdx + 1 < _w.Ds.Count && _w.Ds[_dsIdx + 1].Ms <= cr.Ms) _dsIdx++;
        // keep the slot-arrival clock current over every BT| line up to this tick
        for (var i = _btSeen + 1; i <= _btIdx; i++)
        {
            var slot = _w.Bt[i].Gauge[3];
            if (slot != _lastSlot)
            {
                var first = _lastSlot < 0;
                _lastSlot = slot;
                _slotSinceMs = _w.Bt[i].Ms;
                _slotSinceKnown = !first || slot == 0; // the window opened mid-summon: arrival unknown
            }
        }
        _btSeen = _btIdx;

        var bt = _btIdx >= 0 && cr.Ms - _w.Bt[_btIdx].Ms <= BtMaxAgeMs ? _w.Bt[_btIdx] : null;
        t.Bt = bt;
        t.BtAgeSeconds = bt is null ? -1 : (cr.Ms - bt.Ms) / 1000.0;
        var ds = _dsIdx >= 0 && cr.Ms - _w.Ds[_dsIdx].Ms <= DsMaxAgeMs ? _w.Ds[_dsIdx] : null;

        // ---- CR-sourced
        s.CrucibleBoard = cr.Board;
        s.CrucibleBattle = cr.Battle;
        s.CrucibleNeeds = (CrucibleNeeds)cr.Needs;
        s.EnemyCount = cr.Enemies;
        s.EnemiesWithin6y = cr.Enemies;
        s.HighestEnemyHpPercent = cr.HighestEnemyHp;
        s.TargetNameId = (uint)cr.TargetNameId;
        s.HasHostileTarget = cr.TargetNameId != 0;
        s.TargetHpPercent = s.HasHostileTarget ? cr.TargetHp : 100f;
        s.TargetCastId = cr.CastId;
        s.TargetCastRemaining = cr.CastRemaining;
        s.PlayerHpPercent = cr.PlayerHp;
        s.PetHpPercent = cr.PetHp;
        s.PlayerIntakePerSecond = cr.Intake;
        s.TargetVulnerabilityRemaining = cr.Vul;
        s.TargetTimeToDeath = cr.Ttd;
        s.Slot1PetHp = cr.SlotPetHp[0];
        s.Slot2PetHp = cr.SlotPetHp[1];
        s.Slot3PetHp = cr.SlotPetHp[2];
        s.TargetDistance = s.HasHostileTarget && cr.Distance >= 0f ? cr.Distance : float.MaxValue;
        s.IsMoving = cr.MoveSpeed > 0.5f;
        s.CrucibleDegreeKnown = cr.Degree >= 0;
        s.CrucibleDegreeLevel = Math.Max(0, cr.Degree);

        s.TargetHasDispellableBuff = cr.Has('D');
        s.EnemyHasDispellableBuff = cr.Has('D');
        s.TargetInStance = cr.Has('S');
        s.TargetDoNotAttack = cr.Has('X');
        s.ProtectedNearTarget = cr.Has('N');
        s.TargetHasParry = cr.Has('P');
        s.ParryJustEnded = cr.Has('J');
        s.PlayerHasCleansableDebuff = cr.Has('C');
        s.EnemyTargetsPet = cr.Has('p');
        s.EnemyTargetsPlayer = cr.Has('y');
        s.PlayerTargetedByEnemy = cr.Has('y');
        s.ReadySnarl = cr.Has('s');
        s.ReadyChallenge = cr.Has('c');
        s.TargetInterruptible = cr.Has('i');
        s.ReleaseProtectedNear = cr.Has('R');
        s.PlayerBlink = cr.Has('B');
        s.PetPoisonStatus = cr.Has('V') ? 5140u : 0u;
        if (cr.BmrSeen)
        {
            s.BmrSeen = true;
            s.BmrHitIn = cr.BmrHitIn;
            s.BmrHitType = cr.BmrType;
            s.BmrHitOnPlayer = cr.BmrOnPlayer;
            s.BmrHitOnFamiliar = cr.BmrOnFamiliar;
            s.BmrMaxCast = cr.BmrMaxCast;
        }
        else
        {
            s.BmrHitIn = float.MaxValue;
            s.BmrMaxCast = float.MaxValue;
        }
        s.CrucibleDegreeLevel = Math.Max(0, cr.Degree);

        // InCombat: positive evidence of a fight, else unknown
        var fighting = cr.PlayerHp is > 0 and < 100 || (s.HasHostileTarget && cr.TargetHp < 100) || cr.Intake > 0 || cr.Has('y') || cr.Has('p');
        s.InCombat = fighting;
        if (!fighting)
            gaps.Add(nameof(BstState.InCombat));

        // ---- DS-sourced
        s.DashLanding = ds is null ? DashLanding.Safe : ds.Verdict switch
        {
            "danger" => DashLanding.Danger,
            "unreachable" => DashLanding.Unreachable,
            "unknown" => DashLanding.Unknown,
            _ => DashLanding.Safe,
        };

        // ---- BT-sourced
        if (bt is null)
        {
            foreach (var row in FieldTable.Rows.Where(r => r.NeedsBt))
                gaps.Add(row.Name);
        }
        else
        {
            var g = bt.Gauge;
            s.Level = bt.Level;
            s.PlayerTp = g[0];
            s.FamiliarTp = g[1];
            s.ActiveSlot = g[3];
            s.ComboState = (BeastmasterAffinity)g[4];
            s.LastAffinity = (BeastmasterAffinity)g[5];
            s.KinshipSlot = g[7] & 0x0F;
            s.MasterStacks = (g[8] & 0xC) >> 2;
            s.NaturalStacks = g[8] & 0x3;
            if (bt.HasSlotBeasts)
            {
                s.Slot1Beast = bt.SlotBeasts[0];
                s.Slot2Beast = bt.SlotBeasts[1];
                s.Slot3Beast = bt.SlotBeasts[2];
                s.SlotBeastsKnown = bt.SlotBeasts.Any(x => x != 0);
            }
            s.PetObjectBeast = bt.PetBNpcBase != 0 ? BST_Beasts.RowFromBNpcBase((uint)bt.PetBNpcBase) : 0;
            s.PetObjectPresent = bt.Pet != "none" && s.ActiveSlot != 0;
            s.BeastModeResolved = bt.BeastModeResolved;

            bool Has(params int[] ids) => ids.Any(bt.Statuses.Contains);
            s.OneWithNature = Has(4601);
            s.LingeringVantage = Has(4614);
            s.WaveringHeart = Has(4643);
            s.SunMoon = Has(4599) ? BeastmasterAffinity.Sunstrider : Has(4600) ? BeastmasterAffinity.Moonstalker : BeastmasterAffinity.None;
            s.SunOrMoonActive = s.SunMoon != BeastmasterAffinity.None;
            s.KinshipHeld = Has(4602, 4603, 4604, 4605, 4606, 4607, 4608, 4609, 4644, 4645, 4646, 4647, 4648, 4649, 4650, 4651);
            // the status list is cut at 10 ids: an id that is not in a cut list may simply have been cut
            if (bt.StatusesTruncated)
            {
                if (!s.OneWithNature) gaps.Add(nameof(BstState.OneWithNature));
                if (!s.LingeringVantage) gaps.Add(nameof(BstState.LingeringVantage));
                if (!s.WaveringHeart) gaps.Add(nameof(BstState.WaveringHeart));
                if (!s.SunOrMoonActive) { gaps.Add(nameof(BstState.SunOrMoonActive)); gaps.Add(nameof(BstState.SunMoon)); }
                if (!s.KinshipHeld) gaps.Add(nameof(BstState.KinshipHeld));
            }

            // Trick timers: tk=- means no Trick inside the 7 s window and no Heart since
            if (bt.Trick == "-")
            {
                s.SinceTrick = float.MaxValue;
                s.SincePetHeart = float.MaxValue;
            }
            else
            {
                gaps.Add(nameof(BstState.SinceTrick));
                gaps.Add(nameof(BstState.SincePetHeart));
            }

            // SinceSummon: seconds the current slot has been out, from the BT| series
            if (s.ActiveSlot == 0)
                s.SinceSummon = 0f;
            else if (_slotSinceKnown)
                s.SinceSummon = (cr.Ms - _slotSinceMs) / 1000f;
            else
                gaps.Add(nameof(BstState.SinceSummon));
        }

        // un-rebuilt timers that have a "never" rest value stay at the field default; gap probing replaces them
        t.State = s;
        foreach (var row in FieldTable.Rows.Where(r => r.Kind == Kind.Missing))
            gaps.Add(row.Name);
        // a family absent from the line is a gap even though the table calls it rebuilt
        if (!cr.HasSlotPetHp)
            foreach (var n in new[] { nameof(BstState.Slot1PetHp), nameof(BstState.Slot2PetHp), nameof(BstState.Slot3PetHp) })
                gaps.Add(n);
        if (!s.CrucibleDegreeKnown)
            gaps.Remove(nameof(BstState.CrucibleDegreeLevel)); // unread by design when unknown

        t.Cfg = Settings(_w);
        return t;
    }

    /// <summary> The recorded configuration is not logged: defaults, except Shadow aggro when the window shows a shadow pick (sh=). </summary>
    internal static BstSettings Settings(LogWindow w)
    {
        var cfg = BstSettings.Defaults();
        if (w.Cr.Any(c => c.Shadow.StartsWith("aggro:", StringComparison.Ordinal)))
            cfg = cfg with { CrucibleAggro = CrucibleAggroMode.Shadow };
        return cfg;
    }
}
