namespace RotationSolver.Decisions;

/// <summary>One entry the targeting list can hold: the enum member, a short name and a one-line explanation.</summary>
/// <param name="Value">The <c>TargetingType</c> enum value as an int.</param>
/// <param name="Name">The <c>TargetingType</c> member name.</param>
/// <param name="Label">Plain-English name.</param>
/// <param name="Help">One line saying who gets picked.</param>
public sealed record TargetingOption(int Value, string Name, string Label, string Help);

/// <summary>
///     Fork-only decision core (no Dalamud or game types, compiles standalone): edits of the "who to attack" list in
///     the settings window. Targeting types travel as their <c>TargetingType</c> enum values (ints). Every edit returns a
///     new list that is never empty (an edit that would empty it, or a list that is empty to begin with, gives the
///     default list), holds no entry twice (the first of equal entries stays) and keeps moves inside the list; an edit
///     that is not possible returns the list unchanged (after the same clean-up).
/// </summary>
public static class TargetingList
{
    /// <summary>The list the plugin uses when none is stored: Low HP, High HP, Small, Big.</summary>
    public static IReadOnlyList<int> Default => TargetingChoice.DefaultList;

    /// <summary>The choices the "add" menu offers, in menu order (the values are the real enum values; the tests tie them).</summary>
    public static readonly IReadOnlyList<TargetingOption> Choices =
    [
        new(3, "LowHP", "Lowest HP", "The enemy with the least HP."),
        new(2, "HighHP", "Highest HP", "The enemy with the most HP."),
        new(5, "LowHPPercent", "Lowest HP percentage", "The enemy with the lowest share of its HP left."),
        new(4, "HighHPPercent", "Highest HP percentage", "The enemy with the highest share of its HP left."),
        new(7, "LowMaxHP", "Lowest maximum HP", "The enemy with the smallest HP pool."),
        new(6, "HighMaxHP", "Highest maximum HP", "The enemy with the largest HP pool."),
        new(1, "Small", "Smallest", "The enemy with the smallest hit box."),
        new(0, "Big", "Biggest", "The enemy with the biggest hit box."),
        new(8, "Nearest", "Nearest", "The enemy closest to the character."),
        new(9, "Farthest", "Farthest", "The enemy farthest from the character."),
        new(10, "PvPHealers", "Healers first", "The nearest enemy healer."),
        new(11, "PvPTanks", "Tanks first", "The nearest enemy tank."),
        new(12, "PvPDPS", "Damage dealers first", "The nearest enemy damage dealer."),
        new(13, "PvPHighestPressure", "Your team's pressure target", "The enemy that most of the team already has targeted; ties go to the lowest HP percentage."),
    ];

    /// <summary>The choice with this enum value, or null.</summary>
    public static TargetingOption? ChoiceOf(int value) => Choices.FirstOrDefault(c => c.Value == value);

    /// <summary>Plain-English label for an enum value; the number as text for a value the table does not know.</summary>
    public static string LabelOf(int value) => ChoiceOf(value)?.Label ?? value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The list without repeated entries (the first stays); the default list when nothing is left.</summary>
    public static List<int> Clean(IReadOnlyList<int> list)
    {
        List<int> result = [];
        foreach (int value in list)
        {
            if (!result.Contains(value))
            {
                result.Add(value);
            }
        }

        return result.Count == 0 ? [.. Default] : result;
    }

    /// <summary>Appends an entry that is not in the list yet.</summary>
    public static List<int> Add(IReadOnlyList<int> list, int value)
    {
        List<int> result = Clean(list);
        if (!result.Contains(value))
        {
            result.Add(value);
        }

        return result;
    }

    /// <summary>Removes the entry at an index; the last remaining entry cannot be removed.</summary>
    public static List<int> Remove(IReadOnlyList<int> list, int index)
    {
        List<int> result = Clean(list);
        if (index >= 0 && index < result.Count && result.Count > 1)
        {
            result.RemoveAt(index);
        }

        return result;
    }

    /// <summary>Moves the entry at an index one place up (towards the top); the top entry stays.</summary>
    public static List<int> MoveUp(IReadOnlyList<int> list, int index) => Move(list, index, -1);

    /// <summary>Moves the entry at an index one place down; the bottom entry stays.</summary>
    public static List<int> MoveDown(IReadOnlyList<int> list, int index) => Move(list, index, 1);

    private static List<int> Move(IReadOnlyList<int> list, int index, int step)
    {
        List<int> result = Clean(list);
        int target = index + step;
        if (index >= 0 && index < result.Count && target >= 0 && target < result.Count)
        {
            (result[index], result[target]) = (result[target], result[index]);
        }

        return result;
    }

    /// <summary>Replaces the entry at an index by another one that is not in the list yet.</summary>
    public static List<int> Replace(IReadOnlyList<int> list, int index, int value)
    {
        List<int> result = Clean(list);
        if (index >= 0 && index < result.Count && (result[index] == value || !result.Contains(value)))
        {
            result[index] = value;
        }

        return result;
    }

    /// <summary>The entries the add menu can still offer (those not in the list).</summary>
    public static List<TargetingOption> Addable(IReadOnlyList<int> list) =>
        [.. Choices.Where(c => !list.Contains(c.Value))];

    /// <summary>A fresh copy of the default list.</summary>
    public static List<int> Reset() => [.. Default];
}
