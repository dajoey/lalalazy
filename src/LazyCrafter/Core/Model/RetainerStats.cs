namespace LazyCrafter.Core.Model;

/// <summary>A retainer's venture-relevant stats (Phase 3 reads these from ARControl.json / AutoRetainer; the harness fakes them).</summary>
public sealed record RetainerStats(string Name, int Level, uint JobId, int ItemLevel, int Gathering, int Perception);

/// <summary>A venture one specific retainer can run for an item, with the reward tier its stats unlock.</summary>
public sealed record VentureMatch(LazyCrafter.Core.VentureRow Venture, RetainerStats Retainer, int RewardTier, int Quantity);
