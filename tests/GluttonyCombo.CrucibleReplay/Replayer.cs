using System.Reflection;
using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.CrucibleReplay;

/// <summary>
///     A decision. Two outcomes are the same decision when their rule (Reason) is the same: the action id of a combo step
///     (gcdchain) follows the last combo action, which the lines do not carry, and a heal potion's id follows the stock held.
/// </summary>
internal readonly record struct Outcome(uint Id, string Reason)
{
    public bool Equals(Outcome o) => Reason == o.Reason;
    public override int GetHashCode() => Reason.GetHashCode();
    public override string ToString() => $"{Id}:{Reason}";
}

internal enum TickClass
{
    /// <summary> Replayed decision equals the recorded one and no sampled completion of the gap fields changes it. </summary>
    Reproduced,
    /// <summary> Equals the recorded one under the baseline completion, but another completion of the gap fields changes it. </summary>
    ReproducedGap,
    /// <summary> Differs; the recorded decision IS reachable by some completion of the gap fields: a rebuilt-state gap. </summary>
    GapDiffers,
    /// <summary> Differs and no sampled completion reaches the recorded decision: a joint setting of several gap fields the sample missed, an error in a rebuilt field, or a code change since the recorded build. </summary>
    Unreached,
}

internal sealed class TickResult
{
    public RebuiltTick Tick = null!;
    public Outcome Recorded, Replayed;
    public TickClass Class;
    public List<string> Sensitive = new();
    public List<Outcome> Alternatives = new();
    public bool StrictlyRebuilt;
    public bool Matches => Replayed == Recorded;
    public bool IdDiffers => Matches && Replayed.Id != Recorded.Id;

    // the collector's own pure rule, replayed for the Abaddon target when mo=/ou= are logged (2026-10-15)
    public bool RuleApplicable;
    public bool RuleRecordedForbidden;
    public bool RuleReplayedForbidden;
    /// <summary> Gap fields whose single change alone reaches the recorded decision. </summary>
    public List<string> ReachedVia = new();
    public string Cause = "";
    public bool Stable => Alternatives.Count == 0;
}

/// <summary>
///     Runs one rebuilt tick through the real <see cref="BST_RotationLogic.Decide"/>. Gap fields get their baseline (first candidate),
///     then every other candidate is tried one field at a time and <see cref="Samples"/> random completions are drawn; any decision that
///     differs from the baseline is an alternative. No alternative = the decision does not depend on what the lines could not give.
/// </summary>
internal static class Replayer
{
    public const int Samples = 64;
    private static readonly FieldInfo[] Fields = FieldTable.StateFields();
    private static readonly Dictionary<string, FieldInfo> FieldByName = Fields.ToDictionary(f => f.Name);

    public static Outcome Decide(in BstState s, in BstSettings cfg)
    {
        var d = BST_RotationLogic.Decide(s, cfg);
        return new Outcome(d.ActionId, d.Reason);
    }

    /// <summary> The rebuilt state with every gap field set to its baseline candidate. </summary>
    public static BstState Baseline(RebuiltTick t)
    {
        object box = t.State;
        foreach (var row in FieldTable.Rows)
            if (t.Gaps.Contains(row.Name))
                FieldByName[row.Name].SetValue(box, row.Candidates((BstState)box)[0]);
        return (BstState)box;
    }

    public static TickResult Run(RebuiltTick t)
    {
        var r = new TickResult
        {
            Tick = t,
            Recorded = new Outcome(t.Cr.DecId, t.Cr.DecReason),
            StrictlyRebuilt = !t.Gaps.Any(g => FieldTable.ByName[g].Critical),
        };
        var baseline = Baseline(t);
        r.Replayed = Decide(baseline, t.Cfg);

        var gapRows = FieldTable.Rows.Where(row => t.Gaps.Contains(row.Name) && row.Candidates(baseline).Length > 1).ToList();
        var seen = new HashSet<Outcome>();

        // one gap field at a time
        foreach (var row in gapRows)
        {
            var changed = false;
            foreach (var cand in row.Candidates(baseline).Skip(1))
            {
                object box = baseline;
                FieldByName[row.Name].SetValue(box, cand);
                var o = Decide((BstState)box, t.Cfg);
                if (o != r.Replayed)
                {
                    changed = true;
                    if (seen.Add(o)) r.Alternatives.Add(o);
                    if (o == r.Recorded && !r.ReachedVia.Contains(row.Name)) r.ReachedVia.Add(row.Name);
                }
            }
            if (changed)
                r.Sensitive.Add(row.Name);
        }

        // random completions of all gap fields at once (interactions)
        var rng = new Random(unchecked((int)t.Cr.Ms));
        for (var i = 0; i < Samples; i++)
        {
            object box = baseline;
            foreach (var row in gapRows)
            {
                var cands = row.Candidates((BstState)box);
                FieldByName[row.Name].SetValue(box, cands[rng.Next(cands.Length)]);
            }
            var o = Decide((BstState)box, t.Cfg);
            if (o != r.Replayed && seen.Add(o))
                r.Alternatives.Add(o);
        }

        r.Cause = r.Matches ? (r.Stable ? "" : "recorded decision holds only under some gap settings")
            : r.ReachedVia.Count != 0 ? "needs " + string.Join("/", r.ReachedVia.Take(3))
            : r.Alternatives.Contains(r.Recorded) ? "needs a joint setting of several gap fields"
            : r.Recorded.Reason.StartsWith("ooc:", StringComparison.Ordinal) || r.Recorded.Reason.StartsWith("prepull:", StringComparison.Ordinal)
                ? "InCombat is only a heuristic (not logged)"
                : "unreached: joint gap setting missed, rebuilt-field error, or code change since the recorded build";
        r.Class = r.Matches
            ? r.Stable ? TickClass.Reproduced : TickClass.ReproducedGap
            : r.Alternatives.Contains(r.Recorded) ? TickClass.GapDiffers : TickClass.Unreached;

        // The collector's own pure rule (BST_CrucibleLogic.ConditionallyForbidden), replayed from its logged inputs
        // for the Abaddon target: the recorded f=X was stamped by the live collector from this same rule, so a window
        // recorded on a build carrying the fix should agree, and a disagreement names a rebuilt-input or rule drift.
        // Called by parameter name: the signature grew optional parameters across revisions (hpPercent, othersUp).
        if (t.Cr.NewFields && t.Cr.TargetNameId == BST_CrucibleData.AbaddonNameId)
        {
            r.RuleApplicable = true;
            r.RuleRecordedForbidden = t.Cr.Has('X');
            r.RuleReplayedForbidden = BST_CrucibleLogic.ConditionallyForbidden(
                nameId: (uint)t.Cr.TargetNameId, baseId: 0, morphoAlive: t.Cr.MorphoAlive, clubElement: 0,
                gelsSinceClub: false, hpPercent: t.Cr.TargetHp, othersUp: t.Cr.OtherEnemiesUp);
        }
        return r;
    }
}
