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
        var job = PvpJobTab.PickedJob();
        bool on = Service.Config.IgnorePvPInvincibilityFor(job);
        if (PvpApprovedChrome.Toggle(spec.Property, $"{spec.Label} (job: {job})", spec.Help, ref on))
        {
            Service.Config.SetIgnorePvPInvincibilityFor(job, on);
            PvpUi.Save();
        }
    }

    private static void DrawTargetList() => PvpApprovedChrome.Card("target_priority", DrawTargetListContent);

    private static void DrawTargetListContent()
    {
        PvpUi.Heading("Who to attack");
        PvpUi.Help("The top entry is used first. /pvpsolver Auto Big (or another type from the list) switches to that entry. " +
                   "A list is never left empty and holds each type once.");

        List<int> list = [.. Service.Config.TargetingTypes.ConvertAll(t => (int)t)];
        int active = list.Count == 0 ? -1 : (((Service.Config.TargetingIndex % list.Count) + list.Count) % list.Count);
        if (list.Count == 0)
        {
            PvpUi.Help("The list is empty, so the default order is used: " + string.Join(", ", TargetingList.Default.Select(TargetingList.LabelOf)) + ".");
        }

        List<int>? edited = null;
        const float buttonWidth = 72f;
        float actionsWidth = (3 * buttonWidth) + 16;
        for (int i = 0; i < list.Count; i++)
        {
            int index = i;
            int value = list[i];
            TargetingOption? choice = TargetingList.ChoiceOf(value);
            string label = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + TargetingList.LabelOf(value);
            PvpApprovedChrome.Row("target" + i, label, choice?.Help, actionsWidth, PvpApprovedChrome.BodyControl, pos =>
            {
                Vector2 size = new(buttonWidth, PvpApprovedChrome.BodyControl);
                if (PvpApprovedChrome.Button("up", "Up", pos, size, disabled: index == 0))
                {
                    edited = TargetingList.MoveUp(list, index);
                }

                if (PvpApprovedChrome.Button("down", "Down", pos + new Vector2(buttonWidth + 8, 0), size, disabled: index == list.Count - 1))
                {
                    edited = TargetingList.MoveDown(list, index);
                }

                if (PvpApprovedChrome.Button("remove", "Remove", pos + new Vector2(2 * (buttonWidth + 8), 0), size, disabled: list.Count <= 1))
                {
                    edited = TargetingList.Remove(list, index);
                }

                if (list.Count <= 1)
                {
                    PvpUi.Tip("The list always keeps at least one entry.");
                }
            }, tag: i == active ? "In use now" : null);
        }

        List<TargetingOption> addable = TargetingList.Addable(list);
        PvpApprovedChrome.ControlLine(() =>
        {
            const string resetLabel = "Reset to the default order";
            float resetWidth = PvpApprovedChrome.ButtonWidth(resetLabel);
            float comboWidth = MathF.Max(120, MathF.Min(320, PvpApprovedChrome.Avail() - resetWidth - 8));
            using (var off = ImRaii.Disabled(addable.Count == 0))
            {
                PvpApprovedChrome.Combo("##addtarget", "Add another type...", comboWidth, () =>
                {
                    foreach (TargetingOption option in addable)
                    {
                        if (ImGui.Selectable(option.Label + "##add" + option.Value))
                        {
                            edited = TargetingList.Add(list, option.Value);
                        }
                    }
                });
            }

            ImGui.SameLine(0, 8);
            if (PvpApprovedChrome.InlineButton("reset_order", resetLabel))
            {
                edited = TargetingList.Reset();
            }
        });

        if (edited != null)
        {
            Service.Config.TargetingTypes = [.. edited.ConvertAll(v => (TargetingType)v)];
            PvpUi.Save();
        }
    }
}
