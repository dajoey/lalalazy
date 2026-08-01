using System;
using LazyOccultCrescent.ItemHelpers;
using Dalamud.Plugin.Services;

namespace LazyOccultCrescent.Modules.Currency;

public class CurrencyTracker
{
    private float lastGold = 0f;

    private float gainedGold = 0f;

    private DateTime goldStartTime = DateTime.UtcNow;

    private float lastSilver = 0f;

    private float gainedSilver = 0f;

    private DateTime silverStartTime = DateTime.UtcNow;

    public CurrencyTracker()
    {
        Reset();
    }

    public void Tick(IFramework _)
    {
        var currentGold = Items.Gold.Count();
        var currentSilver = Items.Silver.Count();

        var goldDelta = currentGold - lastGold;
        var silverDelta = currentSilver - lastSilver;

        if (goldDelta > 0)
        {
            gainedGold += goldDelta;
        }

        if (silverDelta > 0)
        {
            gainedSilver += silverDelta;
        }

        lastGold = currentGold;
        lastSilver = currentSilver;
    }

    public void ResetSilver()
    {
        lastSilver = Items.Silver.Count();
        gainedSilver = 0;
        silverStartTime = DateTime.UtcNow;
    }

    public void ResetGold()
    {
        lastGold = Items.Gold.Count();
        gainedGold = 0;
        goldStartTime = DateTime.UtcNow;
    }

    public void Reset()
    {
        ResetSilver();
        ResetGold();
    }

    public float GetGoldPerHour()
    {
        var elapsed = (float)(DateTime.UtcNow - goldStartTime).TotalHours;
        if (elapsed <= 0)
        {
            return 0;
        }

        return gainedGold / elapsed;
    }

    public float GetSilverPerHour()
    {
        var elapsed = (float)(DateTime.UtcNow - silverStartTime).TotalHours;
        if (elapsed <= 0)
        {
            return 0;
        }

        return gainedSilver / elapsed;
    }
}
