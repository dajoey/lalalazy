using LazyOccultCrescent.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Chain;
using Ocelot.Chain.ChainEx;

namespace LazyOccultCrescent.Data;

public class Job
{
    public readonly JobId id;

    public byte ByteId
    {
        get => (byte)id;
    }

    public readonly PlayerStatus status;

    public uint UintStatus
    {
        get => (uint)status;
    }

    public static unsafe Job Current
    {
        get
        {
            var id = (JobId)PublicContentOccultCrescent.GetState()->CurrentSupportJob;
            return id switch
            {
                JobId.Freelancer => Freelancer,
                JobId.Knight => Knight,
                JobId.Berserker => Berserker,
                JobId.Monk => Monk,
                JobId.Ranger => Ranger,
                JobId.Samurai => Samurai,
                JobId.Bard => Bard,
                JobId.Geomancer => Geomancer,
                JobId.TimeMage => TimeMage,
                JobId.Cannoneer => Cannoneer,
                JobId.Chemist => Chemist,
                JobId.Oracle => Oracle,
                JobId.Thief => Thief,
                JobId.MysticKnight => MysticKnight,
                JobId.Gladiator => Gladiator,
                JobId.Dancer => Dancer,
                JobId.Ninja => Ninja,
                JobId.WhiteMage => WhiteMage,
                JobId.BlackMage => BlackMage,
                JobId.Dragoon => Dragoon,
                JobId.Summoner => Summoner,
                JobId.BlueMage => BlueMage,
                JobId.RedMage => RedMage,
                JobId.Necromancer => Necromancer,
                _ => Freelancer,
            };
        }
    }

    public Job(JobId id, PlayerStatus status)
    {
        this.id = id;
        this.status = status;
    }

    public void ChangeTo()
    {
        PublicContentOccultCrescent.ChangeSupportJob(ByteId);
    }

    public Chain ChangeToChain()
    {
        return Chain.Create()
            .RunIf(() => Current.id != id)
            .Then(_ => ChangeTo())
            .WaitUntilStatus(UintStatus);
    }

    public readonly static Job Freelancer = new(JobId.Freelancer, PlayerStatus.PhantomFreelancer);

    public readonly static Job Knight = new(JobId.Knight, PlayerStatus.PhantomKnight);

    public readonly static Job Berserker = new(JobId.Berserker, PlayerStatus.PhantomBerserker);

    public readonly static Job Monk = new(JobId.Monk, PlayerStatus.PhantomMonk);

    public readonly static Job Ranger = new(JobId.Ranger, PlayerStatus.PhantomRanger);

    public readonly static Job Samurai = new(JobId.Samurai, PlayerStatus.PhantomSamurai);

    public readonly static Job Bard = new(JobId.Bard, PlayerStatus.PhantomBard);

    public readonly static Job Geomancer = new(JobId.Geomancer, PlayerStatus.PhantomGeomancer);

    public readonly static Job TimeMage = new(JobId.TimeMage, PlayerStatus.PhantomTimeMage);

    public readonly static Job Cannoneer = new(JobId.Cannoneer, PlayerStatus.PhantomCannoneer);

    public readonly static Job Chemist = new(JobId.Chemist, PlayerStatus.PhantomChemist);

    public readonly static Job Oracle = new(JobId.Oracle, PlayerStatus.PhantomOracle);

    public readonly static Job Thief = new(JobId.Thief, PlayerStatus.PhantomThief);
    
    public readonly static Job MysticKnight = new(JobId.MysticKnight, PlayerStatus.PhantomMysticKnight);
    
    public readonly static Job Gladiator = new(JobId.Gladiator, PlayerStatus.PhantomGladiator);
    
    public readonly static Job Dancer = new(JobId.Dancer, PlayerStatus.PhantomDancer);

    // 7.55 (North Horn)
    public readonly static Job Ninja = new(JobId.Ninja, PlayerStatus.PhantomNinja);

    public readonly static Job WhiteMage = new(JobId.WhiteMage, PlayerStatus.PhantomWhiteMage);

    public readonly static Job BlackMage = new(JobId.BlackMage, PlayerStatus.PhantomBlackMage);

    public readonly static Job Dragoon = new(JobId.Dragoon, PlayerStatus.PhantomDragoon);

    public readonly static Job Summoner = new(JobId.Summoner, PlayerStatus.PhantomSummoner);

    public readonly static Job BlueMage = new(JobId.BlueMage, PlayerStatus.PhantomBlueMage);

    public readonly static Job RedMage = new(JobId.RedMage, PlayerStatus.PhantomRedMage);

    public readonly static Job Necromancer = new(JobId.Necromancer, PlayerStatus.PhantomNecromancer);
}
