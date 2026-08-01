using System;
using ECommons.Automation;

namespace LazyOccultCrescent.Modules.Automator;

public enum AiType
{
    VBM, // Boss Mod
    BMR, // Bossmod Reborn
}

public static class AiProviderExtensions
{
    public static string ToLabel(this AiType provider)
    {
        return provider switch
        {
            AiType.VBM => "Boss Mod by veyn, xan_0",
            AiType.BMR => "BossMod Reborn by The Combat Reborn team",
            _ => provider.ToString(),
        };
    }

    public static void On(this AiType provider)
    {
        switch (provider)
        {
            case AiType.VBM:
                Chat.ExecuteCommand("/vbmai on");
                break;
            case AiType.BMR:
                Chat.ExecuteCommand("/bmrai on");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    public static void Off(this AiType provider)
    {
        switch (provider)
        {
            case AiType.VBM:
                Chat.ExecuteCommand("/vbmai off");
                break;
            case AiType.BMR:
                Chat.ExecuteCommand("/bmrai off");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }
}
