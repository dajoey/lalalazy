using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;

namespace AutoPotion;

internal class ConfigWindow : Window
{
    private const float JobIconSize = 20f;

    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin) : base("AutoPotion##cfg")
    {
        _plugin = plugin;
        Size = new Vector2(420, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var changed = false;

        var master = c.MasterEnable;
        if (ImGui.Checkbox("Enabled", ref master)) { c.MasterEnable = master; changed = true; }
        ImGui.Separator();

        var inCombat = c.OnlyInCombat;
        if (ImGui.Checkbox("Only fire in combat", ref inCombat)) { c.OnlyInCombat = inCombat; changed = true; }
        var inDuty = c.OnlyInDuty;
        if (ImGui.Checkbox("Only fire in a duty", ref inDuty)) { c.OnlyInDuty = inDuty; changed = true; }
        ImGui.Separator();

        // Per-job section. Edits the active job's profile, or the "Default" profile when the
        // player isn't on a job. Lazy-cloned from Default the first time a non-default job
        // gets edited so we don't bloat the config with untouched profiles.
        var local = Plugin.Objects.LocalPlayer;
        var jobId = local?.ClassJob.RowId ?? 0;
        var job = jobId == 0 ? c.DefaultJob : c.GetOrCreateJobSettings(jobId);
        var jobName = ResolveJobName(jobId);
        var jobIcon = ResolveJobIcon(jobId);

        DrawJobHeader(jobName, jobIcon);

        var hpEnable = job.HpPotionEnable;
        if (DrawJobCheckbox(jobIcon, "Auto-use HP potion##hp", ref hpEnable))
        { job.HpPotionEnable = hpEnable; changed = true; }
        var hpThr = job.HpPotionThreshold;
        if (DrawJobSlider(jobIcon, "HP threshold (%)##hpthr", ref hpThr))
        { job.HpPotionThreshold = hpThr; changed = true; }

        ImGui.Spacing();
        var mpEnable = job.MpPotionEnable;
        if (DrawJobCheckbox(jobIcon, "Auto-use MP potion (Ether)##mp", ref mpEnable))
        { job.MpPotionEnable = mpEnable; changed = true; }
        var mpThr = job.MpPotionThreshold;
        if (DrawJobSlider(jobIcon, "MP threshold (%)##mpthr", ref mpThr))
        { job.MpPotionThreshold = mpThr; changed = true; }

        ImGui.Spacing();
        ImGui.TextUnformatted("Deep dungeon regen potion");
        ImGui.TextDisabled("Sustaining (PotD) / Eurekan (HoH) / Orthos (EO) / Pilgrim's. Only fires in a deep dungeon.");
        var rgEnable = job.RegenPotionEnable;
        if (DrawJobCheckbox(jobIcon, "Auto-use regen potion##rg", ref rgEnable))
        { job.RegenPotionEnable = rgEnable; changed = true; }
        var rgThr = job.RegenPotionThreshold;
        if (DrawJobSlider(jobIcon, "HP threshold (%)##rgthr", ref rgThr))
        { job.RegenPotionThreshold = rgThr; changed = true; }

        if (changed) _plugin.SaveConfig();
    }

    private static void DrawJobHeader(string jobName, ImTextureID? icon)
    {
        if (icon.HasValue)
        {
            ImGui.Image(icon.Value, new Vector2(JobIconSize, JobIconSize));
            ImGui.SameLine();
        }
        ImGui.TextUnformatted($"Per-job settings — {jobName}");
        ImGui.TextDisabled("Switch jobs in-game to edit a different profile.");
    }

    private static bool DrawJobCheckbox(ImTextureID? icon, string label, ref bool value)
    {
        if (icon.HasValue)
        {
            ImGui.Image(icon.Value, new Vector2(JobIconSize, JobIconSize));
            ImGui.SameLine();
        }
        return ImGui.Checkbox(label, ref value);
    }

    private static bool DrawJobSlider(ImTextureID? icon, string label, ref float value)
    {
        if (icon.HasValue)
        {
            ImGui.Image(icon.Value, new Vector2(JobIconSize, JobIconSize));
            ImGui.SameLine();
        }
        return ImGui.SliderFloat(label, ref value, 1f, 99f, "%.0f");
    }

    private static string ResolveJobName(uint jobId)
    {
        if (jobId == 0) return "Default";
        var sheet = Plugin.Data.GetExcelSheet<ClassJob>();
        if (sheet != null && sheet.TryGetRow(jobId, out var row))
        {
            var name = row.Name.ExtractText();
            if (!string.IsNullOrEmpty(name)) return char.ToUpper(name[0]) + name[1..];
        }
        return $"Job {jobId}";
    }

    private static ImTextureID? ResolveJobIcon(uint jobId)
    {
        // ClassJob sheet doesn't expose an Icon column in this Lumina build, but the small
        // framed job icons are reliably at 62000 + jobId for both classes and jobs.
        if (jobId == 0) return null;
        var iconId = 62000u + jobId;
        var tex = Plugin.Textures.GetFromGameIcon(new GameIconLookup(iconId));
        return tex.GetWrapOrEmpty().Handle;
    }
}
