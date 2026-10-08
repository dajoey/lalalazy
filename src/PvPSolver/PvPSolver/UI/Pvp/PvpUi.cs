using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.Logging;
using RotationSolver.Decisions;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     Small drawing helpers of the PvP settings window. Rules kept here: every ImGui scope is an ImRaii object held
///     by a using statement (an exception between begin and end cannot unbalance ImGui's stacks), nothing in
///     this window reads the rotation or the zone, and <c>Service.Config.Save()</c> (a synchronous write of the whole
///     file) runs on edit completion only: when a checkbox or a choice changes, and when a slider or drag is
///     released. A change that has not been saved yet is flushed when the window closes.
/// </summary>
internal static class PvpUi
{
    private static readonly OncePerDistinct Logged = new();
    private static bool _dirty;

    internal static float Scale => ImGuiHelpers.GlobalScale;

    internal static Vector4 Muted => ImGuiColors.DalamudGrey;

    internal static Vector4 Warning => ImGuiColors.DalamudOrange;

    internal static Vector4 Accent => ImGuiColors.DalamudYellow;

    // ---- saving --------------------------------------------------------------------------------------------------

    /// <summary>Writes the configuration file now. Never throws.</summary>
    internal static void Save()
    {
        try
        {
            Service.Config.Save();
            _dirty = false;
        }
        catch (Exception ex)
        {
            LogOnce("save", ex);
        }
    }

    /// <summary>Notes a change that a slider or drag is still editing; saved on release or when the window closes.</summary>
    internal static void MarkDirty() => _dirty = true;

    /// <summary>Saves a change that was never saved (called when the window closes).</summary>
    internal static void Flush()
    {
        if (_dirty)
        {
            Save();
        }
    }

    /// <summary>Saves when the control just drawn was released after an edit.</summary>
    internal static void SaveOnRelease()
    {
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            Save();
        }
    }

    /// <summary>Logs a failure once per distinct failure, never every frame.</summary>
    internal static void LogOnce(string where, Exception ex)
    {
        if (Logged.First(where + "|" + OncePerDistinct.Signature(ex)))
        {
            PluginLog.Error($"[PvP settings window] {where}: {ex}");
        }
    }

    /// <summary>Lets a failure that was fixed be reported again (called when the window opens).</summary>
    internal static void ResetLog() => Logged.Reset();

    // ---- text ----------------------------------------------------------------------------------------------------

    /// <summary>A section heading with a rule under it.</summary>
    internal static void Heading(string text)
    {
        ImGui.Spacing();
        ImGui.TextColored(Accent, text);
        ImGui.Separator();
    }

    /// <summary>Muted, wrapped help text printed under a control, indented by <paramref name="indent"/> scaled pixels.</summary>
    internal static void Help(string text, float indent = 24f)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using var color = ImRaii.PushColor(ImGuiCol.Text, Muted);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (indent * Scale));
        ImGui.TextWrapped(text);
    }

    /// <summary>Wrapped text in the warning colour.</summary>
    internal static void WarningText(string text)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, Warning);
        ImGui.TextWrapped(text);
    }

    /// <summary>Wrapped plain text.</summary>
    internal static void Paragraph(string text) => ImGui.TextWrapped(text);

    /// <summary>
    ///     A tooltip for the control just drawn, wrapped to a readable width. Honours the "Show tooltips" setting; the
    ///     same information is always printed as help text, so nothing depends on a tooltip.
    /// </summary>
    internal static void Tip(string? text)
    {
        if (string.IsNullOrEmpty(text) || !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) || !Service.Config.ShowTooltips.Value)
        {
            return;
        }

        using var tooltip = ImRaii.Tooltip();
        using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 28f);
        ImGui.TextUnformatted(text);
    }

    // ---- controls ------------------------------------------------------------------------------------------------

    /// <summary>A checkbox. Returns true when it was clicked; the caller stores the value and saves.</summary>
    internal static bool Check(string label, ref bool value, bool disabled = false)
    {
        using var off = ImRaii.Disabled(disabled);
        return ImGui.Checkbox(label, ref value);
    }

    /// <summary>
    ///     A whole-percent slider (<paramref name="points"/> 0 to 100). Returns true while the value changes. The
    ///     file is saved when the slider is released.
    /// </summary>
    internal static bool PercentSlider(string id, ref int points, int min, int max, float width = 200f, bool disabled = false)
    {
        using var off = ImRaii.Disabled(disabled);
        ImGui.SetNextItemWidth(width * Scale);
        bool changed = ImGui.SliderInt(id, ref points, min, max, "%d%%");
        SaveOnRelease();
        return changed;
    }

    /// <summary>A whole-number drag with a unit suffix. Returns true while the value changes; saved on release.</summary>
    internal static bool NumberDrag(string id, ref int value, int min, int max, string unit, float width = 120f, bool disabled = false)
    {
        using var off = ImRaii.Disabled(disabled);
        ImGui.SetNextItemWidth(width * Scale);
        bool changed = ImGui.DragInt(id, ref value, 1f, min, max, "%d" + unit);
        SaveOnRelease();
        return changed;
    }

    /// <summary>A small button that is greyed out and does nothing while <paramref name="disabled"/>.</summary>
    internal static bool SmallButton(string label, bool disabled = false)
    {
        bool clicked;
        using (var off = ImRaii.Disabled(disabled))
        {
            clicked = ImGui.SmallButton(label);
        }

        return clicked && !disabled;
    }

    /// <summary>A small "Reset" button. Returns true when clicked.</summary>
    internal static bool ResetButton(string id) => ImGui.SmallButton("Reset##" + id);

    /// <summary>A button that is greyed out with a reason in its tooltip while <paramref name="disabled"/>.</summary>
    internal static bool Button(string label, bool disabled = false, string? whyDisabled = null)
    {
        bool clicked;
        using (var off = ImRaii.Disabled(disabled))
        {
            clicked = ImGui.Button(label);
        }

        if (disabled)
        {
            Tip(whyDisabled);
        }

        return clicked && !disabled;
    }

    // ---- the catalog -----------------------------------------------------------------------------------------------

    /// <summary>
    ///     Draws one section of a tab from the catalog: its heading, then each setting with its help line under it.
    ///     <paramref name="custom"/> may draw a setting itself and return true; the setting keeps its place in the order.
    /// </summary>
    internal static void DrawSection(string tab, string section, Func<SettingSpec, bool>? custom = null)
    {
        Heading(section);
        foreach (SettingSpec spec in PvpSettingsCatalog.ForSection(tab, section))
        {
            if (custom != null && custom(spec))
            {
                continue;
            }

            DrawSetting(spec);
        }
    }

    /// <summary>Draws every section of a tab from the catalog.</summary>
    internal static void DrawTab(string tab)
    {
        foreach (string section in PvpSettingsCatalog.SectionsOf(tab))
        {
            DrawSection(tab, section);
        }
    }

    /// <summary>Draws one catalog setting: control, label, help line and (for numbers and choices) a Reset when it differs from the default.</summary>
    internal static void DrawSetting(SettingSpec spec)
    {
        using var id = ImRaii.PushId(spec.Property);
        if (spec.PerJob)
        {
            // Stored per job: reading it here would ask the generated property for the played job. The tab that owns it draws it.
            WarningText($"{spec.Label}: this setting is stored per job and is drawn by its own tab.");
            return;
        }

        if (!PvpBinding.Exists(spec.Property))
        {
            WarningText($"{spec.Label}: this setting was not found in the configuration ({spec.Property}).");
            return;
        }

        bool parentOn = true;
        if (spec.Parent.Length > 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (24f * Scale));
            parentOn = PvpBinding.TryGetBool(spec.Parent, out bool parentValue) && parentValue;
        }

        float helpIndent = spec.Parent.Length > 0 ? 48f : 24f;
        switch (spec.Control)
        {
            case SettingControl.Toggle:
                DrawToggle(spec, !parentOn);
                Help(spec.Help, helpIndent);
                break;
            case SettingControl.Percent:
                DrawPercent(spec, !parentOn);
                Help(spec.Help + " (default " + OptionText.ToPoints(spec.DefaultNumber).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%)", helpIndent);
                break;
            case SettingControl.Seconds:
                DrawSeconds(spec, !parentOn);
                Help(spec.Help + " (default " + ((int)spec.DefaultNumber).ToString(System.Globalization.CultureInfo.InvariantCulture) + " s)", helpIndent);
                break;
            case SettingControl.Choice:
                DrawChoice(spec, !parentOn);
                Help(spec.Help, helpIndent);
                break;
        }
    }

    private static void DrawToggle(SettingSpec spec, bool disabled)
    {
        bool value = PvpBinding.TryGetBool(spec.Property, out bool current) && current;
        if (Check(spec.Label, ref value, disabled))
        {
            _ = PvpBinding.TrySetBool(spec.Property, value);
            Save();
        }
    }

    private static void DrawPercent(SettingSpec spec, bool disabled)
    {
        _ = PvpBinding.TryGetFloat(spec.Property, out float ratio);
        int min = OptionText.ToPoints(spec.Min);
        int max = OptionText.ToPoints(spec.Max);
        int points = Math.Clamp(OptionText.ToPoints(ratio), min, max);
        if (PercentSlider("##value", ref points, min, max, disabled: disabled))
        {
            _ = PvpBinding.TrySetFloat(spec.Property, OptionText.FromPoints(points, spec.Min, spec.Max));
            MarkDirty();
        }

        ImGui.SameLine();
        ImGui.TextWrapped(spec.Label);
        if (!disabled && OptionText.ToPoints(ratio) != OptionText.ToPoints(spec.DefaultNumber))
        {
            ImGui.SameLine();
            if (ResetButton("reset"))
            {
                _ = PvpBinding.TrySetFloat(spec.Property, spec.DefaultNumber);
                Save();
            }
        }
    }

    private static void DrawSeconds(SettingSpec spec, bool disabled)
    {
        _ = PvpBinding.TryGetFloat(spec.Property, out float seconds);
        int whole = OptionText.ClampInt((int)MathF.Round(seconds), (int)spec.Min, (int)spec.Max);
        if (NumberDrag("##value", ref whole, (int)spec.Min, (int)spec.Max, " s", disabled: disabled))
        {
            _ = PvpBinding.TrySetFloat(spec.Property, OptionText.ClampInt(whole, (int)spec.Min, (int)spec.Max));
            MarkDirty();
        }

        ImGui.SameLine();
        ImGui.TextWrapped(spec.Label);
        if (!disabled && (int)MathF.Round(seconds) != (int)spec.DefaultNumber)
        {
            ImGui.SameLine();
            if (ResetButton("reset"))
            {
                _ = PvpBinding.TrySetFloat(spec.Property, spec.DefaultNumber);
                Save();
            }
        }
    }

    private static void DrawChoice(SettingSpec spec, bool disabled)
    {
        _ = PvpBinding.TryGetChoice(spec.Property, out string current);
        ImGui.TextUnformatted(spec.Label);
        using var off = ImRaii.Disabled(disabled);
        if (spec.Choices.Count <= 3)
        {
            foreach (SettingChoice choice in spec.Choices)
            {
                ImGui.SameLine();
                if (ImGui.RadioButton(choice.Label + "##" + choice.Name, choice.Name == current))
                {
                    _ = PvpBinding.TrySetChoice(spec.Property, choice.Name);
                    Save();
                }
            }

            return;
        }

        string preview = spec.Choices.FirstOrDefault(c => c.Name == current)?.Label ?? current;
        ImGui.SetNextItemWidth(320f * Scale);
        using var combo = ImRaii.Combo("##choice", preview);
        if (!combo)
        {
            return;
        }

        foreach (SettingChoice choice in spec.Choices)
        {
            if (ImGui.Selectable(choice.Label, choice.Name == current))
            {
                _ = PvpBinding.TrySetChoice(spec.Property, choice.Name);
                Save();
            }
        }
    }
}
