using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.Logging;
using LalaUiKit;
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

    internal static Vector4 Muted => Theme.TextSecondary;

    internal static Vector4 Warning => Theme.StateAmber;

    internal static Vector4 Accent => Theme.Accent;

    internal static Vector4 On => Theme.StateGreen;

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

    /// <summary>A section title of a card; the settings below it are rows separated by hairlines.</summary>
    internal static void Heading(string text, bool blocks = false) => PvpApprovedChrome.CardTitle(text, blocks);

    /// <summary>Muted help text under a block, at the secondary size, indented by <paramref name="indent"/> pixels.</summary>
    internal static void Help(string text, float indent = 0f, float after = 8f)
        => PvpApprovedChrome.Para(text, Theme.SecondaryScale, Muted, after, indent);

    /// <summary>A tinted notice in the warning state.</summary>
    internal static void WarningText(string text) => PvpApprovedChrome.Notice(text, string.Empty, UiState.Blocked);

    /// <summary>Wrapped plain text.</summary>
    internal static void Paragraph(string text) => PvpApprovedChrome.Para(text, 1f, Theme.TextSecondary, 8);

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

        ImguiTooltips.ShowTooltip(() =>
        {
            using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 28f);
            ImGui.TextUnformatted(text);
        });
    }

    // ---- controls ------------------------------------------------------------------------------------------------

    private const float SliderWidth = 180f;
    private const float DragWidth = 112f;

    /// <summary>The width of a percent readout cell beside a slider (fits "100%").</summary>
    private static float ValueCell => MathF.Ceiling(PvpApprovedChrome.TextWidth("100%")) + 4f;

    /// <summary>
    ///     A whole-percent slider row (<paramref name="points"/> 0 to 100). Returns true while the value changes. The
    ///     value is printed to the right of the track, never over the handle, and the file is saved on release.
    /// </summary>
    internal static bool PercentSlider(string id, string label, string? help, ref int points, int min, int max, bool disabled = false,
        int indent = 0, bool joined = false, Action? onReset = null, bool resetVisible = false)
    {
        int value = points;
        bool changed = false;
        float cell = ValueCell;
        PvpApprovedChrome.Row(id, label, help, SliderWidth + 8 + cell, PvpApprovedChrome.BodyControl, pos =>
        {
            ImGui.SetCursorScreenPos(pos);
            changed = PvpApprovedChrome.Field(() =>
            {
                ImGui.SetNextItemWidth(SliderWidth);
                return ImGui.SliderInt("##value", ref value, min, max, string.Empty);
            });
            SaveOnRelease();
            string readout = value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
            PvpApprovedChrome.TextRight(ImGui.GetWindowDrawList(), pos.X + SliderWidth + 8 + cell,
                pos.Y + ((PvpApprovedChrome.BodyControl - PvpApprovedChrome.LineHeight()) / 2f),
                readout, disabled ? Theme.TextSecondary : Theme.TextPrimary);
        }, indent, disabled, joined, onReset, resetVisible);
        points = value;
        return changed;
    }

    /// <summary>A whole-number drag row with a unit suffix. Returns true while the value changes; saved on release.</summary>
    internal static bool NumberDrag(string id, string label, string? help, ref int number, int min, int max, string unit, bool disabled = false,
        int indent = 0, bool joined = false, Action? onReset = null, bool resetVisible = false)
    {
        int value = number;
        bool changed = false;
        PvpApprovedChrome.Row(id, label, help, DragWidth, PvpApprovedChrome.BodyControl, pos =>
        {
            ImGui.SetCursorScreenPos(pos);
            changed = PvpApprovedChrome.Field(() =>
            {
                ImGui.SetNextItemWidth(DragWidth);
                return ImGui.DragInt("##value", ref value, 1f, min, max, "%d" + unit);
            });
            SaveOnRelease();
        }, indent, disabled, joined, onReset, resetVisible);
        number = value;
        return changed;
    }

    /// <summary>A button of a card body, greyed out with a reason in its tooltip while <paramref name="disabled"/>.</summary>
    internal static bool Button(string label, bool disabled = false, string? whyDisabled = null,
        PvpApprovedChrome.ButtonStyle style = PvpApprovedChrome.ButtonStyle.Normal)
    {
        bool clicked = PvpApprovedChrome.InlineButton("b_" + label, label, style, disabled);
        if (disabled)
        {
            Tip(whyDisabled);
        }

        return clicked;
    }

    // ---- the catalog -----------------------------------------------------------------------------------------------

    /// <summary>
    ///     Draws one section of a tab from the catalog as a card: its title, then each setting as a row with its help
    ///     line. <paramref name="custom"/> may draw a setting itself and return true; the setting keeps its place in the
    ///     order. <paramref name="after"/> draws extra rows or notes at the end of the same card.
    /// </summary>
    internal static void DrawSection(string tab, string section, Func<SettingSpec, bool>? custom = null, Action? after = null)
    {
        PvpApprovedChrome.Card("section_" + tab + "_" + section, () =>
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

            after?.Invoke();
        });
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
        int indent = 0;
        if (spec.Parent.Length > 0)
        {
            indent = 1;
            parentOn = PvpBinding.TryGetBool(spec.Parent, out bool parentValue) && parentValue;
        }

        bool joined = indent > 0;
        switch (spec.Control)
        {
            case SettingControl.Toggle:
                DrawToggle(spec, !parentOn, indent, joined);
                break;
            case SettingControl.Percent:
                DrawPercent(spec, !parentOn, indent, joined);
                break;
            case SettingControl.Seconds:
                DrawSeconds(spec, !parentOn, indent, joined);
                break;
            case SettingControl.Choice:
                DrawChoice(spec, !parentOn, indent, joined);
                break;
        }
    }

    private static string WithDefault(string help, string defaultText) => (help + " " + defaultText).Trim();

    private static void DrawToggle(SettingSpec spec, bool disabled, int indent, bool joined)
    {
        bool value = PvpBinding.TryGetBool(spec.Property, out bool current) && current;
        if (PvpApprovedChrome.Toggle(spec.Property, spec.Label, spec.Help, ref value, disabled, indent, joined))
        {
            _ = PvpBinding.TrySetBool(spec.Property, value);
            Save();
        }
    }

    private static void DrawPercent(SettingSpec spec, bool disabled, int indent, bool joined)
    {
        _ = PvpBinding.TryGetFloat(spec.Property, out float ratio);
        int min = OptionText.ToPoints(spec.Min);
        int max = OptionText.ToPoints(spec.Max);
        int points = Math.Clamp(OptionText.ToPoints(ratio), min, max);
        string help = WithDefault(spec.Help, "(default " + OptionText.ToPoints(spec.DefaultNumber).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%)");
        bool differs = OptionText.ToPoints(ratio) != OptionText.ToPoints(spec.DefaultNumber);
        if (PercentSlider(spec.Property, spec.Label, help, ref points, min, max, disabled, indent, joined, () =>
            {
                _ = PvpBinding.TrySetFloat(spec.Property, spec.DefaultNumber);
                Save();
            }, differs && !disabled))
        {
            _ = PvpBinding.TrySetFloat(spec.Property, OptionText.FromPoints(points, spec.Min, spec.Max));
            MarkDirty();
        }
    }

    private static void DrawSeconds(SettingSpec spec, bool disabled, int indent, bool joined)
    {
        _ = PvpBinding.TryGetFloat(spec.Property, out float seconds);
        int whole = OptionText.ClampInt((int)MathF.Round(seconds), (int)spec.Min, (int)spec.Max);
        string help = WithDefault(spec.Help, "(default " + ((int)spec.DefaultNumber).ToString(System.Globalization.CultureInfo.InvariantCulture) + " s)");
        bool differs = (int)MathF.Round(seconds) != (int)spec.DefaultNumber;
        if (NumberDrag(spec.Property, spec.Label, help, ref whole, (int)spec.Min, (int)spec.Max, " s", disabled, indent, joined, () =>
            {
                _ = PvpBinding.TrySetFloat(spec.Property, spec.DefaultNumber);
                Save();
            }, differs && !disabled))
        {
            _ = PvpBinding.TrySetFloat(spec.Property, OptionText.ClampInt(whole, (int)spec.Min, (int)spec.Max));
            MarkDirty();
        }
    }

    private static void DrawChoice(SettingSpec spec, bool disabled, int indent, bool joined)
    {
        _ = PvpBinding.TryGetChoice(spec.Property, out string current);
        string? picked = null;
        if (spec.Choices.Count <= 3)
        {
            (string Name, string Label)[] choices = spec.Choices.Select(c => (c.Name, c.Label)).ToArray();
            float width = PvpApprovedChrome.ChoiceWidth(choices.Select(c => c.Label));
            PvpApprovedChrome.Row(spec.Property, spec.Label, spec.Help, width, PvpApprovedChrome.BodyControl,
                pos => picked = PvpApprovedChrome.ChoiceButtons(spec.Property, choices, current, pos), indent, disabled, joined);
        }
        else
        {
            string preview = spec.Choices.FirstOrDefault(c => c.Name == current)?.Label ?? current;
            const float comboWidth = 240f;
            PvpApprovedChrome.Row(spec.Property, spec.Label, spec.Help, comboWidth, PvpApprovedChrome.BodyControl, pos =>
            {
                ImGui.SetCursorScreenPos(pos);
                PvpApprovedChrome.Combo("##choice", preview, comboWidth, () =>
                {
                    foreach (SettingChoice choice in spec.Choices)
                    {
                        if (ImGui.Selectable(choice.Label, choice.Name == current))
                        {
                            picked = choice.Name;
                        }
                    }
                });
            }, indent, disabled, joined);
        }

        if (picked is not null)
        {
            _ = PvpBinding.TrySetChoice(spec.Property, picked);
            Save();
        }
    }
}
