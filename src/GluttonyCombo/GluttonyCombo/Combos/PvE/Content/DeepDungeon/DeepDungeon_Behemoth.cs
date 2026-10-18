using Dalamud.Game.ClientState.Objects.Types;
using static GluttonyCombo.CustomComboNS.Functions.CustomComboFunctions;

namespace GluttonyCombo.Combos.PvE.Content.DeepDungeons;

// PotD behemoth meteor-state detection for the two Ecliptic Meteor bosses
// (notebook Projects/PotD Behemoth 2026-10/Fight Baseline):
//   Floor 80 Gudanna (NameId 5333): Ecliptic Meteor (7099), 20.0s cast, at <=10.0% HP
//   (13,602 of 136,025), once per fight.
//   Floor 180 Dendainsonne (NameId 5461): Ecliptic Meteor (7166), ~7s cast, loops
//   ~every 10.2s while <=15.0% HP (8,405 of 56,031); Attack (6497) + autos between.
// Everything here is NameId-matched, so every branch is inert outside those two fights.
internal static partial class DeepDungeons
{
    public const uint
        GudannaNameId = 5333,
        DendainsonneNameId = 5461,
        GudannaEclipticMeteor = 7099,
        DendainsonneEclipticMeteor = 7166;

    /// <summary> HP percent at which each behemoth's meteor window opens. </summary>
    public const float
        GudannaMeteorThreshold = 10f,
        DendainsonneMeteorThreshold = 15f;

    /// <summary>
    ///     HP percent above a behemoth's meteor threshold that counts as "the window is imminent":
    ///     survival tools whose own triggers would spend them on chip damage here should bank
    ///     instead. Roughly one meteor cycle of warning at Floor 180 kill pace.
    /// </summary>
    public const float MeteorHoldBand = 7f;

    /// <summary>
    ///     The current target is a PotD behemoth inside its Ecliptic Meteor window: the meteor cast
    ///     is on the bar, or (Floor 180) the boss has dropped to the HP threshold that loops the
    ///     cast back-to-back. False for every other target.
    /// </summary>
    public static bool BehemothMeteorWindow()
    {
        if (CurrentTarget is not IBattleChara behemoth)
            return false;

        return behemoth.NameId switch
        {
            GudannaNameId => behemoth.CastActionId == GudannaEclipticMeteor ||
                             GetTargetHPPercent() <= GudannaMeteorThreshold,
            DendainsonneNameId => behemoth.CastActionId == DendainsonneEclipticMeteor ||
                                  GetTargetHPPercent() <= DendainsonneMeteorThreshold,
            _ => false,
        };
    }

    /// <summary>
    ///     The current target is a PotD behemoth just above its meteor threshold: the window is
    ///     imminent but not open yet. Bank survival tools here; press them once
    ///     <see cref="BehemothMeteorWindow"/> turns true.
    /// </summary>
    public static bool BehemothMeteorHold()
    {
        if (BehemothMeteorWindow())
            return false;

        return CurrentTarget is IBattleChara behemoth && behemoth.NameId switch
        {
            GudannaNameId => GetTargetHPPercent() <= GudannaMeteorThreshold + MeteorHoldBand,
            DendainsonneNameId => GetTargetHPPercent() <= DendainsonneMeteorThreshold + MeteorHoldBand,
            _ => false,
        };
    }
}
