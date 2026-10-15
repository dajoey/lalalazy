using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.CrucibleReplay;

/// <summary>
///     Does the recorded degree reach <c>Decide</c>, and does it change a decision? For every tick whose line carries a degree letter (dg=1..3),
///     the same rebuilt state is decided twice: once with the degree from its dg= letter (what the plugin saw) and once with the degree forced to
///     Standard. The guard and the cover rule read the character's and the familiar's ABSOLUTE HP, which the lines do not carry (PlayerHp and
///     PetHp are gap fields), so the comparison completes them two ways:
///     the realistic case (the character's max HP about 4,060 and a 3,492 HP Diremite, scaled by the logged percentages, every other gap field at
///     its baseline), and the sampled completions (a grid of HP candidates plus <see cref="Replayer.Samples"/> random completions of every gap
///     field, the same draws for both degrees). A tick where the two degrees press a different action on the realistic completion is a claim about the
///     code; one that differs only on a sampled completion shows the scale can matter there, not that it did.
/// </summary>
internal static class DegreeCompare
{
    public const float CharacterMaxHp = 4060f; // BST_CrucibleHeavyCasts.cs: "max HP on these boards is about 4,060"
    public const float FamiliarMaxHp = 3492f;  // BST_CrucibleLogic.cs: a 3,492 HP Diremite familiar

    internal sealed record Diff(long Ms, uint CastId, string Recorded, string AtDegree, string AtStandard, float PlayerHp, float PetHp);

    internal sealed class Result
    {
        public int Ticks, Known, Realistic, Sampled, DegreeMatchesRecorded, StandardMatchesRecorded, SecondVsFirst, ThirdVsSecond;
        /// <summary> Ticks where the decisions differ on the realistic completion WITH a heal potion held (the stock is not logged, so the baseline holds none and the raidwide guard can only decline). </summary>
        public int WithPotion, WithPotionReasonOnly;
        public List<Diff> WithPotionDiffering = new();
        /// <summary> What the scale change does to these recorded states: "decision at First -> decision at Second" (and Second -> Third), by count. </summary>
        public Dictionary<string, int> WhatIf = new();
        public List<Diff> Differing = new();
        /// <summary> Target cast id of the ticks that differ only on a sampled completion, with the number of ticks: the mechanics the scale could matter for. </summary>
        public Dictionary<uint, int> SampledCasts = new();
    }

    /// <summary> The two decisions press different actions. The same action under another rule name (panic heal against raidwide guard) is not a behavioural difference. </summary>
    private static bool Presses(Outcome a, Outcome b) => a.Id != b.Id;

    private static BstState Complete(BstState s, int degree) =>
        s with { CrucibleDegreeKnown = true, CrucibleDegreeLevel = degree };

    public static Result Run(IReadOnlyList<TickResult> ticks)
    {
        var res = new Result { Ticks = ticks.Count };
        foreach (var t in ticks)
        {
            var dg = t.Tick.Cr.Degree;
            if (dg < CrucibleDegree.First)
                continue;
            res.Known++;
            var cfg = t.Tick.Cfg;
            var baseline = Replayer.Baseline(t.Tick);

            var real = baseline with { PlayerHp = baseline.PlayerHpPercent / 100f * CharacterMaxHp, PetHp = baseline.PetHpPercent / 100f * FamiliarMaxHp };
            var atDegree = Replayer.Decide(Complete(real, dg), cfg);
            var atStandard = Replayer.Decide(Complete(real, CrucibleDegree.Standard), cfg);
            var realisticDiffers = Presses(atDegree, atStandard);
            if (realisticDiffers)
            {
                res.Realistic++;
                var recorded = new Outcome(t.Tick.Cr.DecId, t.Tick.Cr.DecReason);
                if (atDegree == recorded) res.DegreeMatchesRecorded++;
                if (atStandard == recorded) res.StandardMatchesRecorded++;
                res.Differing.Add(new Diff(t.Tick.Cr.Ms, t.Tick.Cr.CastId, recorded.ToString(), atDegree.ToString(), atStandard.ToString(), real.PlayerHp, real.PetHp));
            }

            var potion = real with { ReadyHealPotion = BST_CrucibleData.HealPotionActions[0] };
            var potDegree = Replayer.Decide(Complete(potion, dg), cfg);
            var potStandard = Replayer.Decide(Complete(potion, CrucibleDegree.Standard), cfg);
            if (!Presses(potDegree, potStandard) && potDegree != potStandard)
                res.WithPotionReasonOnly++;
            if (Presses(potDegree, potStandard))
            {
                res.WithPotion++;
                res.WithPotionDiffering.Add(new Diff(t.Tick.Cr.Ms, t.Tick.Cr.CastId, new Outcome(t.Tick.Cr.DecId, t.Tick.Cr.DecReason).ToString(), potDegree.ToString(), potStandard.ToString(), potion.PlayerHp, potion.PetHp));
            }

            // what the Second / Third scales would do on this recorded state (the same completion, the degree forced)
            var d1 = Replayer.Decide(Complete(real, CrucibleDegree.First), cfg);
            var d2 = Replayer.Decide(Complete(real, CrucibleDegree.Second), cfg);
            var d3 = Replayer.Decide(Complete(real, CrucibleDegree.Third), cfg);
            if (Presses(d2, d1)) { res.SecondVsFirst++; res.WhatIf[$"First {d1.Reason} -> Second {d2.Reason}"] = res.WhatIf.GetValueOrDefault($"First {d1.Reason} -> Second {d2.Reason}") + 1; }
            if (Presses(d3, d2)) { res.ThirdVsSecond++; res.WhatIf[$"Second {d2.Reason} -> Third {d3.Reason}"] = res.WhatIf.GetValueOrDefault($"Second {d2.Reason} -> Third {d3.Reason}") + 1; }

            var sampledDiffers = realisticDiffers;
            if (!sampledDiffers)
                foreach (var p in new[] { 20f, 40f, 80f, CharacterMaxHp / 100f })
                    foreach (var f in new[] { 10f, 20f, 40f, FamiliarMaxHp / 100f })
                    {
                        var s = baseline with { PlayerHp = baseline.PlayerHpPercent * p, PetHp = baseline.PetHpPercent * f };
                        if (Presses(Replayer.Decide(Complete(s, dg), cfg), Replayer.Decide(Complete(s, CrucibleDegree.Standard), cfg)))
                            sampledDiffers = true;
                    }
            if (!sampledDiffers)
            {
                var rng = new Random(unchecked((int)t.Tick.Cr.Ms));
                var gapRows = FieldTable.Rows.Where(row => t.Tick.Gaps.Contains(row.Name) && row.Candidates(baseline).Length > 1).ToList();
                for (var i = 0; i < Replayer.Samples && !sampledDiffers; i++)
                {
                    object box = baseline;
                    foreach (var row in gapRows)
                    {
                        var cands = row.Candidates((BstState)box);
                        FieldTable.StateFields().First(x => x.Name == row.Name).SetValue(box, cands[rng.Next(cands.Length)]);
                    }
                    var s = (BstState)box;
                    if (Presses(Replayer.Decide(Complete(s, dg), cfg), Replayer.Decide(Complete(s, CrucibleDegree.Standard), cfg)))
                        sampledDiffers = true;
                }
            }
            if (sampledDiffers)
            {
                res.Sampled++;
                if (!realisticDiffers)
                    res.SampledCasts[t.Tick.Cr.CastId] = res.SampledCasts.GetValueOrDefault(t.Tick.Cr.CastId) + 1;
            }
        }
        return res;
    }
}
