using Lumina.Excel.Sheets;

namespace LazyFoodBuff;

/// <summary>
/// Scores food items against a job's optimal stat priorities and recommends
/// the best food the player currently has in their inventory.
/// </summary>
internal static class FoodRecommender
{
    // BaseParam RowIds (verified against ECommons BaseParamEnum).
    public const uint Strength = 1;
    public const uint Dexterity = 2;
    public const uint Vitality = 3;
    public const uint Intelligence = 4;
    public const uint Mind = 5;
    public const uint Piety = 6;
    public const uint DirectHitRate = 22;
    public const uint Tenacity = 19;
    public const uint CriticalHit = 27;
    public const uint Determination = 44;
    public const uint SkillSpeed = 45;
    public const uint SpellSpeed = 46;

    /// <summary>
    /// Stat priorities for each job role. Higher weight = more important.
    /// Negative weights penalize stats that hurt the job (e.g., excess SkS for melee).
    /// </summary>
    private static Dictionary<uint, float> GetStatWeights(uint jobId)
    {
        // ClassJob RowId → role mapping.
        // Tanks: PLD(19), WAR(21), DRK(32), GNB(37)
        // Healers: WHM(24), SCH(28), AST(33), SGE(40)
        // Melee: MNK(20), DRG(22), NIN(30), SAM(34), RPR(39)
        // Physical Ranged: BRD(23), MCH(31), DNC(38)
        // Casters: BLM(25), SMN(27), RDM(35), PCT(42), BLU(36)
        return jobId switch
        {
            // Tanks — Tenacity is unique and critical.
            19 or 21 or 32 or 37 => new()
            {
                [Tenacity] = 3.0f,
                [CriticalHit] = 2.0f,
                [Determination] = 1.5f,
                [DirectHitRate] = 1.2f,
                [Vitality] = 0.5f,
            },
            // Healers — Piety matters for WHM/SCH; SGE/AST less so but doesn't hurt.
            24 or 28 or 33 or 40 => new()
            {
                [Piety] = 1.5f,
                [CriticalHit] = 2.0f,
                [Determination] = 1.8f,
                [SpellSpeed] = 1.0f,
                [DirectHitRate] = 1.0f,
                [Mind] = 0.5f,
            },
            // Melee DPS (includes VPR=41) — Crit/Det primary, SkS is situational, penalize excess.
            20 or 22 or 30 or 34 or 39 or 41 => new()
            {
                [CriticalHit] = 2.5f,
                [Determination] = 2.0f,
                [DirectHitRate] = 1.5f,
                [Strength] = 0.5f,
                // Light penalty: SkS food is situational, not universally wanted.
                [SkillSpeed] = -0.3f,
            },
            // Physical Ranged — Crit/Det primary.
            23 or 31 or 38 => new()
            {
                [CriticalHit] = 2.5f,
                [Determination] = 2.0f,
                [DirectHitRate] = 1.5f,
                [Dexterity] = 0.5f,
                [SkillSpeed] = 0.5f,
            },
            // Casters — Crit/Det primary, SpS matters for BLM.
            25 or 27 or 35 or 42 or 36 => new()
            {
                [CriticalHit] = 2.5f,
                [Determination] = 2.0f,
                [DirectHitRate] = 1.5f,
                [SpellSpeed] = 1.0f,
                [Intelligence] = 0.5f,
            },
            // Default fallback — generic Crit/Det weighting.
            _ => new()
            {
                [CriticalHit] = 2.0f,
                [Determination] = 1.5f,
                [DirectHitRate] = 1.0f,
                [Vitality] = 0.3f,
            },
        };
    }

    /// <summary>
    /// Score a food item for a given job. Higher = better.
    /// Uses the percentage bonus (flat stat boost relative to gear stat level).
    /// HQ food scores higher due to increased percentages.
    /// </summary>
    public static float Score(Food food, uint jobId)
    {
        if (food.Stats.Count == 0) return 0f;

        var weights = GetStatWeights(jobId);
        float score = 0f;

        // Check if the player has HQ or NQ. Prefer HQ scoring if available.
        bool preferHq = food.InventoryCount(true) > 0;

        foreach (var (paramId, stat) in food.Stats)
        {
            if (!weights.TryGetValue(paramId, out var weight)) continue;

            var percent = preferHq
                ? stat.NqPercent + stat.HqPercentBonus
                : stat.NqPercent;

            // Score = weight × percentage bonus.
            // A food with 10% Crit and weight 2.5 → 25 points for that stat.
            score += weight * percent;
        }

        return score;
    }

    /// <summary>
    /// Find the best food in the player's inventory for the given job.
    /// Scores ALL food items and returns the one with the highest score that
    /// the player actually has in inventory (NQ or HQ).
    /// </summary>
    public static Food? RecommendBest(IEnumerable<Food> allFoods, uint jobId)
    {
        Food? best = null;
        float bestScore = 0f;

        foreach (var food in allFoods)
        {
            // Must have at least one in inventory (NQ or HQ).
            if (food.InventoryCount(true) == 0 && food.InventoryCount(false) == 0) continue;

            var score = Score(food, jobId);
            if (score > bestScore)
            {
                best = food;
                bestScore = score;
            }
        }

        return best;
    }
}
