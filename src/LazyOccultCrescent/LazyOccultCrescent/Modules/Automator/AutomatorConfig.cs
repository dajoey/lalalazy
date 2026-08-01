using System.Collections.Generic;
using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace LazyOccultCrescent.Modules.Automator;

public class AutomatorConfig : ModuleConfig
{
    [Checkbox]
    [Illegal]
    [RequiredPlugin("Lifestream", "vnavmesh")]
    [Label("generic.label.enabled")]
    [Tooltip("enabled")]
    public bool Enabled { get; set; } = false;

    [Enum(typeof(AiType), nameof(AiTypeProvider))]
    public AiType AiProvider { get; set; } = AiType.VBM;

    [Checkbox] public bool ToggleAiProvider { get; set; } = true;

    public bool ShouldToggleAiProvider
    {
        get => IsPropertyEnabled(nameof(ToggleAiProvider));
    }

    [Checkbox] public bool ForceTarget { get; set; } = true;

    public bool ShouldForceTarget
    {
        get => IsPropertyEnabled(nameof(ForceTarget));
    }

    [Checkbox]
    [DependsOn(nameof(ForceTarget))]

    public bool ForceTargetCentralEnemy { get; set; } = true;

    public bool ShouldForceTargetCentralEnemy
    {
        get => IsPropertyEnabled(nameof(ForceTargetCentralEnemy));
    }

    [FloatRange(5f, 30f)] public float EngagementRange { get; set; } = 5f;

    // Critical Encounters
    [Checkbox] public bool DoCriticalEncounters { get; set; } = true;

    public bool ShouldDoCriticalEncounters
    {
        get => IsPropertyEnabled(nameof(DoCriticalEncounters));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]

    public bool DelayCriticalEncounters { get; set; } = false;

    public bool ShouldDelayCriticalEncounters
    {
        get => IsPropertyEnabled(nameof(DelayCriticalEncounters));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoScourgeOfTheMind { get; set; } = true;

    public bool ShouldDoScourgeOfTheMind
    {
        get => IsPropertyEnabled(nameof(DoScourgeOfTheMind));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoTheBlackRegiment { get; set; } = true;

    public bool ShouldDoTheBlackRegiment
    {
        get => IsPropertyEnabled(nameof(DoTheBlackRegiment));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoTheUnbridled { get; set; } = true;

    public bool ShouldDoTheUnbridled
    {
        get => IsPropertyEnabled(nameof(DoTheUnbridled));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCrawlingDeath { get; set; } = true;

    public bool ShouldDoCrawlingDeath
    {
        get => IsPropertyEnabled(nameof(DoCrawlingDeath));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCalamityBound { get; set; } = true;

    public bool ShouldDoCalamityBound
    {
        get => IsPropertyEnabled(nameof(DoCalamityBound));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoTrialByClaw { get; set; } = true;

    public bool ShouldDoTrialByClaw
    {
        get => IsPropertyEnabled(nameof(DoTrialByClaw));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoFromTimesBygone { get; set; } = true;

    public bool ShouldDoFromTimesBygone
    {
        get => IsPropertyEnabled(nameof(DoFromTimesBygone));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCompanyOfStone { get; set; } = true;

    public bool ShouldDoCompanyOfStone
    {
        get => IsPropertyEnabled(nameof(DoCompanyOfStone));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoSharkAttack { get; set; } = true;

    public bool ShouldDoSharkAttack
    {
        get => IsPropertyEnabled(nameof(DoSharkAttack));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoCriticalEncounters))]

    public bool DoOnTheHunt { get; set; } = true;

    public bool ShouldDoOnTheHunt
    {
        get => IsPropertyEnabled(nameof(DoOnTheHunt));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoWithExtremePrejudice { get; set; } = true;

    public bool ShouldDoWithExtremePrejudice
    {
        get => IsPropertyEnabled(nameof(DoWithExtremePrejudice));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoNoiseComplaint { get; set; } = true;

    public bool ShouldDoNoiseComplaint
    {
        get => IsPropertyEnabled(nameof(DoNoiseComplaint));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCursedConcern { get; set; } = true;

    public bool ShouldDoCursedConcern
    {
        get => IsPropertyEnabled(nameof(DoCursedConcern));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoEternalWatch { get; set; } = true;

    public bool ShouldDoEternalWatch
    {
        get => IsPropertyEnabled(nameof(DoEternalWatch));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoFlameOfDusk { get; set; } = true;

    public bool ShouldDoFlameOfDusk
    {
        get => IsPropertyEnabled(nameof(DoFlameOfDusk));
    }

    // Fates
    [Checkbox] public bool DoFates { get; set; } = true;

    public bool ShouldDoFates
    {
        get => IsPropertyEnabled(nameof(DoFates));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoRoughWaters { get; set; } = true;

    public bool ShouldDoRoughWaters
    {
        get => IsPropertyEnabled(nameof(DoRoughWaters));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoTheGoldenGuardian { get; set; } = true;

    public bool ShouldDoTheGoldenGuardian
    {
        get => IsPropertyEnabled(nameof(DoTheGoldenGuardian));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoKingOfTheCrescent { get; set; } = true;

    public bool ShouldDoKingOfTheCrescent
    {
        get => IsPropertyEnabled(nameof(DoKingOfTheCrescent));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]
    [Experimental]

    public bool DoTheWingedTerror { get; set; } = false;

    public bool ShouldDoTheWingedTerror
    {
        get => IsPropertyEnabled(nameof(DoTheWingedTerror));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoAnUnendingDuty { get; set; } = true;

    public bool ShouldDoAnUnendingDuty
    {
        get => IsPropertyEnabled(nameof(DoAnUnendingDuty));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoBrainDrain { get; set; } = true;

    public bool ShouldDoBrainDrain
    {
        get => IsPropertyEnabled(nameof(DoBrainDrain));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoADelicateBalance { get; set; } = true;

    public bool ShouldDoADelicateBalance
    {
        get => IsPropertyEnabled(nameof(DoADelicateBalance));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoSwornToSoil { get; set; } = true;

    public bool ShouldDoSwornToSoil
    {
        get => IsPropertyEnabled(nameof(DoSwornToSoil));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoAPryingEye { get; set; } = true;

    public bool ShouldDoAPryingEye
    {
        get => IsPropertyEnabled(nameof(DoAPryingEye));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoFatalAllure { get; set; } = true;

    public bool ShouldDoFatalAllure
    {
        get => IsPropertyEnabled(nameof(DoFatalAllure));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoServingDarkness { get; set; } = true;

    public bool ShouldDoServingDarkness
    {
        get => IsPropertyEnabled(nameof(DoServingDarkness));
    }

    [Checkbox]
    [Experimental]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoPersistentPots { get; set; } = false;

    public bool ShouldDoPersistentPots
    {
        get => IsPropertyEnabled(nameof(DoPersistentPots));
    }

    [Checkbox]
    [Experimental]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoPleadingPots { get; set; } = false;

    public bool ShouldDoPleadingPots
    {
        get => IsPropertyEnabled(nameof(DoPleadingPots));
    }

    // ---------------------------------------------------------------------
    // North Horn (territory 1346). The config UI is driven by reflection over
    // these properties, so an event with no property here is invisible in
    // settings and unselectable - which is exactly what happened to every
    // North Horn event before this block existed.
    // ---------------------------------------------------------------------

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoManyMouthsToFeed { get; set; } = true;

    public bool ShouldDoManyMouthsToFeed
    {
        get => IsPropertyEnabled(nameof(DoManyMouthsToFeed));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoDoubledTrouble { get; set; } = true;

    public bool ShouldDoDoubledTrouble
    {
        get => IsPropertyEnabled(nameof(DoDoubledTrouble));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoQuarriedAway { get; set; } = true;

    public bool ShouldDoQuarriedAway
    {
        get => IsPropertyEnabled(nameof(DoQuarriedAway));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoForbiddenFolios { get; set; } = true;

    public bool ShouldDoForbiddenFolios
    {
        get => IsPropertyEnabled(nameof(DoForbiddenFolios));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoCursedResurgence { get; set; } = true;

    public bool ShouldDoCursedResurgence
    {
        get => IsPropertyEnabled(nameof(DoCursedResurgence));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoImbalancedDiet { get; set; } = true;

    public bool ShouldDoImbalancedDiet
    {
        get => IsPropertyEnabled(nameof(DoImbalancedDiet));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoWebOfTerror { get; set; } = true;

    public bool ShouldDoWebOfTerror
    {
        get => IsPropertyEnabled(nameof(DoWebOfTerror));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoABeastUnleashed { get; set; } = true;

    public bool ShouldDoABeastUnleashed
    {
        get => IsPropertyEnabled(nameof(DoABeastUnleashed));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoDarkArtistry { get; set; } = true;

    public bool ShouldDoDarkArtistry
    {
        get => IsPropertyEnabled(nameof(DoDarkArtistry));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoFamiliarTactics { get; set; } = true;

    public bool ShouldDoFamiliarTactics
    {
        get => IsPropertyEnabled(nameof(DoFamiliarTactics));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoAppallingBehavior { get; set; } = true;

    public bool ShouldDoAppallingBehavior
    {
        get => IsPropertyEnabled(nameof(DoAppallingBehavior));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoTinyTerror { get; set; } = true;

    public bool ShouldDoTinyTerror
    {
        get => IsPropertyEnabled(nameof(DoTinyTerror));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoLostOnTheWind { get; set; } = true;

    public bool ShouldDoLostOnTheWind
    {
        get => IsPropertyEnabled(nameof(DoLostOnTheWind));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoAheadOfTheCompetition { get; set; } = true;

    public bool ShouldDoAheadOfTheCompetition
    {
        get => IsPropertyEnabled(nameof(DoAheadOfTheCompetition));
    }

    [Checkbox]
    [DependsOn(nameof(DoCriticalEncounters))]
    [Indent]

    public bool DoAcceptNoImitators { get; set; } = true;

    public bool ShouldDoAcceptNoImitators
    {
        get => IsPropertyEnabled(nameof(DoAcceptNoImitators));
    }

    [Checkbox]
    [Experimental]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoDaylightPottery { get; set; } = false;

    public bool ShouldDoDaylightPottery
    {
        get => IsPropertyEnabled(nameof(DoDaylightPottery));
    }

    [Checkbox]
    [Experimental]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoInAPotOfBother { get; set; } = false;

    public bool ShouldDoInAPotOfBother
    {
        get => IsPropertyEnabled(nameof(DoInAPotOfBother));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoRagingThrall { get; set; } = true;

    public bool ShouldDoRagingThrall
    {
        get => IsPropertyEnabled(nameof(DoRagingThrall));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoEyeToEye { get; set; } = true;

    public bool ShouldDoEyeToEye
    {
        get => IsPropertyEnabled(nameof(DoEyeToEye));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoShorelineShowdown { get; set; } = true;

    public bool ShouldDoShorelineShowdown
    {
        get => IsPropertyEnabled(nameof(DoShorelineShowdown));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoWavedAway { get; set; } = true;

    public bool ShouldDoWavedAway
    {
        get => IsPropertyEnabled(nameof(DoWavedAway));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoAllureOfTheOccult { get; set; } = true;

    public bool ShouldDoAllureOfTheOccult
    {
        get => IsPropertyEnabled(nameof(DoAllureOfTheOccult));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoInconstantGardener { get; set; } = true;

    public bool ShouldDoInconstantGardener
    {
        get => IsPropertyEnabled(nameof(DoInconstantGardener));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoTerritorialDispute { get; set; } = true;

    public bool ShouldDoTerritorialDispute
    {
        get => IsPropertyEnabled(nameof(DoTerritorialDispute));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoARottenAffair { get; set; } = true;

    public bool ShouldDoARottenAffair
    {
        get => IsPropertyEnabled(nameof(DoARottenAffair));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoGaleForceEncounter { get; set; } = true;

    public bool ShouldDoGaleForceEncounter
    {
        get => IsPropertyEnabled(nameof(DoGaleForceEncounter));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoScaleModel { get; set; } = true;

    public bool ShouldDoScaleModel
    {
        get => IsPropertyEnabled(nameof(DoScaleModel));
    }

    [Checkbox]
    [Indent]
    [DependsOn(nameof(DoFates))]

    public bool DoThunderregnum { get; set; } = true;

    public bool ShouldDoThunderregnum
    {
        get => IsPropertyEnabled(nameof(DoThunderregnum));
    }

    public IReadOnlyDictionary<uint, bool> CriticalEncountersMap
    {
        get => new Dictionary<uint, bool>
        {
            { 33, ShouldDoScourgeOfTheMind },
            { 34, ShouldDoTheBlackRegiment },
            { 35, ShouldDoTheUnbridled },
            { 36, ShouldDoCrawlingDeath },
            { 37, ShouldDoCalamityBound },
            { 38, ShouldDoTrialByClaw },
            { 39, ShouldDoFromTimesBygone },
            { 40, ShouldDoCompanyOfStone },
            { 41, ShouldDoSharkAttack },
            { 42, ShouldDoOnTheHunt },
            { 43, ShouldDoWithExtremePrejudice },
            { 44, ShouldDoNoiseComplaint },
            { 45, ShouldDoCursedConcern },
            { 46, ShouldDoEternalWatch },
            { 47, ShouldDoFlameOfDusk },

            // North Horn
            { 49, ShouldDoManyMouthsToFeed },
            { 50, ShouldDoDoubledTrouble },
            { 51, ShouldDoQuarriedAway },
            { 52, ShouldDoForbiddenFolios },
            { 53, ShouldDoCursedResurgence },
            { 54, ShouldDoImbalancedDiet },
            { 55, ShouldDoWebOfTerror },
            { 56, ShouldDoABeastUnleashed },
            { 57, ShouldDoDarkArtistry },
            { 58, ShouldDoFamiliarTactics },
            { 59, ShouldDoAppallingBehavior },
            { 60, ShouldDoTinyTerror },
            { 61, ShouldDoLostOnTheWind },
            { 62, ShouldDoAheadOfTheCompetition },
            { 63, ShouldDoAcceptNoImitators },
        };
    }

    public IReadOnlyDictionary<uint, bool> FatesMap
    {
        get => new Dictionary<uint, bool>
        {
            { 1962, ShouldDoRoughWaters },
            { 1963, ShouldDoTheGoldenGuardian },
            { 1964, ShouldDoKingOfTheCrescent },
            { 1965, ShouldDoTheWingedTerror },
            { 1966, ShouldDoAnUnendingDuty },
            { 1967, ShouldDoBrainDrain },
            { 1968, ShouldDoADelicateBalance },
            { 1969, ShouldDoSwornToSoil },
            { 1970, ShouldDoAPryingEye },
            { 1971, ShouldDoFatalAllure },
            { 1972, ShouldDoServingDarkness },
            { 1976, ShouldDoPersistentPots },
            { 1977, ShouldDoPleadingPots },

            // North Horn
            { 2072, ShouldDoDaylightPottery },
            { 2073, ShouldDoInAPotOfBother },
            { 2074, ShouldDoRagingThrall },
            { 2075, ShouldDoEyeToEye },
            { 2076, ShouldDoShorelineShowdown },
            { 2077, ShouldDoWavedAway },
            { 2078, ShouldDoAllureOfTheOccult },
            { 2079, ShouldDoInconstantGardener },
            { 2080, ShouldDoTerritorialDispute },
            { 2081, ShouldDoARottenAffair },
            { 2082, ShouldDoGaleForceEncounter },
            { 2083, ShouldDoScaleModel },
            { 2084, ShouldDoThunderregnum },
        };
    }
}
