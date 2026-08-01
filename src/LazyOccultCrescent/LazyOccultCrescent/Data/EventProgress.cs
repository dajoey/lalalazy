using System;
using System.Collections.Generic;
using System.Linq;

namespace LazyOccultCrescent.Data;

public class EventProgress
{
    private const int MaxSamples = 100;

    public List<ProgressSample> samples { get; } = new();

    public int Count
    {
        get => samples.Count;
    }

    public float Latest
    {
        get => samples[^1].Progress;
    }

    public void Add(float progress)
    {
        if (samples.Count >= MaxSamples)
        {
            samples.RemoveAt(0);
        }

        samples.Add(new ProgressSample(progress, DateTimeOffset.UtcNow));
    }

    public TimeSpan? EstimateTimeToCompletion()
    {
        if (samples.Count < 2)
        {
            return null;
        }

        var first = samples.First();
        var last = samples.Last();

        var deltaProgress = last.Progress - first.Progress;
        var deltaSeconds = (last.Timestamp - first.Timestamp).TotalSeconds;

        if (deltaProgress <= 0 || deltaSeconds <= 0)
        {
            return null;
        }

        var remainingProgress = 100f - last.Progress;
        var ratePerSecond = deltaProgress / deltaSeconds;
        var estimatedSecondsRemaining = remainingProgress / ratePerSecond;

        return TimeSpan.FromSeconds(estimatedSecondsRemaining);
    }

    public record ProgressSample(float Progress, DateTimeOffset Timestamp);
}
