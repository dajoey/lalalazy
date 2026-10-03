using Lalalazy.Hub;

namespace LazyHub.Core;

/// <summary>One line of a plugin's settings page: a group header or a control.</summary>
public sealed class DetailRow
{
    public bool IsHeader;
    public string Header = "";
    public ParsedControl? Control;
}

/// <summary>Turns a plugin's descriptor into the lines of its settings page. No Dalamud types.</summary>
public static class DetailRows
{
    public const string OtherGroup = "Other";

    /// <summary>
    /// Headers and controls, grouped by first appearance of the group, declaration order inside a group.
    /// <paramref name="excludedGroup"/> is left out (the hotbar buttons live on the Quick tab). The result holds at
    /// most <paramref name="maxRows"/> rows, never ending on a lone header; <paramref name="hidden"/> counts the
    /// controls that did not fit.
    /// </summary>
    public static List<DetailRow> Build(ParsedDescriptor descriptor, int maxRows, string excludedGroup, out int hidden)
    {
        var groups = new List<string>();
        var byGroup = new Dictionary<string, List<ParsedControl>>(StringComparer.Ordinal);
        var total = 0;

        foreach (var c in descriptor.Controls)
        {
            var g = string.IsNullOrWhiteSpace(c.Group) ? OtherGroup : c.Group;
            if (!string.IsNullOrEmpty(excludedGroup) && string.Equals(g, excludedGroup, StringComparison.Ordinal)) continue;
            if (!byGroup.TryGetValue(g, out var list))
            {
                list = new List<ParsedControl>();
                byGroup[g] = list;
                groups.Add(g);
            }
            list.Add(c);
            total++;
        }

        var rows = new List<DetailRow>();
        var shown = 0;
        foreach (var g in groups)
        {
            foreach (var c in byGroup[g])
            {
                // A header is only added together with the first control that fits under it.
                var needsHeader = rows.Count == 0 || rows[^1].IsHeader == false && !SameGroupAsLast(rows, g, byGroup);
                if (needsHeader && rows.Count + 2 > maxRows) goto done;
                if (!needsHeader && rows.Count + 1 > maxRows) goto done;
                if (needsHeader) rows.Add(new DetailRow { IsHeader = true, Header = g });
                rows.Add(new DetailRow { Control = c });
                shown++;
            }
        }

    done:
        hidden = total - shown;
        return rows;
    }

    private static bool SameGroupAsLast(List<DetailRow> rows, string group, Dictionary<string, List<ParsedControl>> byGroup)
    {
        var last = rows[^1].Control;
        return last != null && byGroup[group].Contains(last);
    }
}
