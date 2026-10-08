using Dalamud.Interface.Utility.Raii;
using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>Tab 3, Targeting: who to attack (an ordered list), staying on a target, area attacks, burst.</summary>
internal static class PvpTargetingTab
{
    public static void Draw()
    {
        DrawTargetList();

        foreach (string section in PvpSettingsCatalog.SectionsOf(PvpSettingsCatalog.Targeting))
        {
            // The per-job setting is drawn here, for the job picked in the My Job tab, and keeps its place in the order.
            PvpUi.DrawSection(PvpSettingsCatalog.Targeting, section, spec =>
            {
                if (!spec.PerJob)
                {
                    return false;
                }

                DrawPerJob(spec);
                return true;
            });
        }
    }

    private static void DrawPerJob(SettingSpec spec)
    {
        using var id = ImRaii.PushId(spec.Property);
        var job = PvpJobTab.PickedJob();
        bool on = Service.Config.IgnorePvPInvincibilityFor(job);
        if (PvpUi.Check($"{spec.Label} (job: {job})", ref on))
        {
            Service.Config.SetIgnorePvPInvincibilityFor(job, on);
            PvpUi.Save();
        }

        PvpUi.Help(spec.Help);
    }

    private static void DrawTargetList()
    {
        PvpUi.Heading("Who to attack");
        PvpUi.Help("The top entry is used first. /pvpsolver Auto Big (or another type from the list) switches to that entry. " +
                   "A list is never left empty and holds each type once.", 0f);

        List<int> list = [.. Service.Config.TargetingTypes.ConvertAll(t => (int)t)];
        int active = list.Count == 0 ? -1 : (((Service.Config.TargetingIndex % list.Count) + list.Count) % list.Count);
        if (list.Count == 0)
        {
            PvpUi.Help("The list is empty, so the default order is used: " + string.Join(", ", TargetingList.Default.Select(TargetingList.LabelOf)) + ".", 0f);
        }

        List<int>? edited = null;
        for (int i = 0; i < list.Count; i++)
        {
            using var row = ImRaii.PushId("target" + i);
            int value = list[i];
            ImGui.TextUnformatted((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            ImGui.SameLine();
            ImGui.TextUnformatted(TargetingList.LabelOf(value));
            if (i == active)
            {
                ImGui.SameLine();
                using var muted = ImRaii.PushColor(ImGuiCol.Text, PvpUi.Muted);
                ImGui.TextUnformatted("(in use now)");
            }

            ImGui.SameLine();
            if (PvpUi.SmallButton("Up##up", disabled: i == 0))
            {
                edited = TargetingList.MoveUp(list, i);
            }

            ImGui.SameLine();
            if (PvpUi.SmallButton("Down##down", disabled: i == list.Count - 1))
            {
                edited = TargetingList.MoveDown(list, i);
            }

            ImGui.SameLine();
            if (PvpUi.SmallButton("Remove##remove", disabled: list.Count <= 1))
            {
                edited = TargetingList.Remove(list, i);
            }

            if (list.Count <= 1)
            {
                PvpUi.Tip("The list always keeps at least one entry.");
            }

            TargetingOption? choice = TargetingList.ChoiceOf(value);
            if (choice != null)
            {
                PvpUi.Help(choice.Help);
            }
        }

        List<TargetingOption> addable = TargetingList.Addable(list);
        ImGui.Spacing();
        {
            using var off = ImRaii.Disabled(addable.Count == 0);
            using var combo = ImRaii.Combo("##addtarget", "Add another type...");
            if (combo)
            {
                foreach (TargetingOption option in addable)
                {
                    if (ImGui.Selectable(option.Label + "##add" + option.Value))
                    {
                        edited = TargetingList.Add(list, option.Value);
                    }
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset to the default order"))
        {
            edited = TargetingList.Reset();
        }

        if (edited != null)
        {
            Service.Config.TargetingTypes = [.. edited.ConvertAll(v => (TargetingType)v)];
            PvpUi.Save();
        }
    }
}
