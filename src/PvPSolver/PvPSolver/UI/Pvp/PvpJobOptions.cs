using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using RotationSolver.Decisions;
using RotationSolver.Updaters;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     The job options of the settings window, available for any job in any zone. The options are not read from the
///     loaded rotation (<c>DataCenter.CurrentRotation</c> is null outside a PvP zone) but from the job's own cached
///     rotation instance, asked for by job on every frame (<c>RotationUpdater.GetRotations(job, CombatType.PvP)</c>:
///     it makes the instance on first use and has no zone test). No instance is kept between frames: the cache is
///     emptied on every zone change. Values are read and written through <c>IRotationConfig.Value</c>, which stores
///     them under the rotation's own job (the job-keyed accessor), as text in the same format as the legacy window.
///     When the job is the one being played, the cached instance is the live rotation, so an edit applies at once;
///     for another job it is stored and the live rotation reads it at the next zone entry.
/// </summary>
internal static class PvpJobOptions
{
    private enum Load
    {
        Ready,
        NoCharacter,
        Loading,
        Failed,
    }

    private static readonly Dictionary<Job, DateTime> RetryAt = [];

    /// <summary>The state of the options of a job and, when ready, the rotation to read them from. Valid for this frame only.</summary>
    private static Load Get(Job job, out ICustomRotation? rotation, out string message)
    {
        rotation = null;
        message = string.Empty;
        if (!Player.Available)
        {
            message = "Log in with a character to edit job options. They load once the character is in the world.";
            return Load.NoCharacter;
        }

        DateTime now = DateTime.UtcNow;
        if (RetryAt.TryGetValue(job, out DateTime retry) && now < retry)
        {
            message = "Loading the options of this job...";
            return Load.Loading;
        }

        ICustomRotation[] rotations;
        try
        {
            rotations = RotationUpdater.GetRotations(job, CombatType.PvP);
        }
        catch (Exception ex)
        {
            PvpUi.LogOnce("job options " + job, ex);
            RetryAt[job] = now.AddSeconds(5);
            message = "The options of this job could not be loaded: " + ex.Message;
            return Load.Failed;
        }

        if (rotations.Length == 0)
        {
            RetryAt[job] = now.AddSeconds(1);
            message = "Loading the options of this job... They are ready a moment after the character enters the world.";
            return Load.Loading;
        }

        _ = RetryAt.Remove(job);
        rotation = rotations[0];
        return Load.Ready;
    }

    /// <summary>Draws the options of a job, or the reason they cannot be shown.</summary>
    public static void Draw(Job job)
    {
        Load state = Get(job, out ICustomRotation? rotation, out string message);
        if (state != Load.Ready || rotation == null)
        {
            if (state == Load.Failed)
            {
                PvpUi.WarningText(message);
            }
            else
            {
                PvpUi.Help(message, 0f, 0f);
            }

            return;
        }

        try
        {
            DrawOptions(job, rotation);
        }
        catch (Exception ex)
        {
            PvpUi.LogOnce("job options draw " + job, ex);
            PvpUi.WarningText("The options of this job could not be drawn: " + ex.Message);
        }
    }

    private static void DrawOptions(Job job, ICustomRotation rotation)
    {
        IRotationConfigSet set = rotation.Configs;
        List<IRotationConfig> options = [];
        foreach (IRotationConfig config in set.Configs)
        {
            if (config.Type.HasFlag(CombatType.PvP))
            {
                options.Add(config);
            }
        }

        if (options.Count == 0)
        {
            PvpUi.Help("This job has no extra options.", 0f, 0f);
            return;
        }

        foreach (IRotationConfig config in options)
        {
            if (!IsVisible(config, set))
            {
                continue;
            }

            using var id = ImRaii.PushId(job + "." + config.Name);
            DrawOne(config, config.Parent.Length > 0);
        }
    }

    // The same rule as the legacy window: a child option is shown while its parent allows it.
    private static bool IsVisible(IRotationConfig config, IRotationConfigSet set)
    {
        if (string.IsNullOrEmpty(config.Parent))
        {
            return true;
        }

        IRotationConfig? parent = null;
        foreach (IRotationConfig other in set.Configs)
        {
            if (other.Name == config.Parent)
            {
                parent = other;
                break;
            }
        }

        object? required = (config as RotationConfigBase)?.ParentValue;
        return OptionText.ChildVisible(parent != null, parent is RotationConfigBoolean, parent?.Value, required?.ToString());
    }

    private static void DrawOne(IRotationConfig config, bool isChild)
    {
        string label = config.DisplayName;
        string value = config.Value;
        string defaultText = config.DefaultValue;
        int indent = isChild ? 1 : 0;
        string id = config.Name;

        switch (config)
        {
            case RotationConfigBoolean:
            {
                bool on = OptionText.TryRead(value, out bool parsed) && parsed;
                if (PvpApprovedChrome.Toggle(id, label, DefaultLine(OptionKind.Bool, defaultText), ref on, indent: indent, joined: isChild,
                        onReset: () => ResetTo(config, defaultText), resetVisible: !OptionText.SameValue(OptionKind.Bool, value, defaultText)))
                {
                    config.Value = OptionText.Write(on);
                    PvpUi.Save();
                }

                break;
            }

            case RotationConfigFloat { UnitType: ConfigUnitType.Percent } percent:
            {
                if (!OptionText.TryRead(value, out float ratio))
                {
                    BadValue(config, label, value, indent);
                    break;
                }

                int min = OptionText.ToPoints(percent.Min);
                int max = OptionText.ToPoints(percent.Max);
                int points = Math.Clamp(OptionText.ToPoints(ratio), min, max);
                if (PvpUi.PercentSlider(id, label, DefaultLine(OptionKind.Percent, defaultText), ref points, min, max, indent: indent, joined: isChild,
                        onReset: () => ResetTo(config, defaultText), resetVisible: !OptionText.SameValue(OptionKind.Percent, value, defaultText)))
                {
                    config.Value = OptionText.Write(OptionText.FromPoints(points, percent.Min, percent.Max));
                    PvpUi.MarkDirty();
                }

                break;
            }

            case RotationConfigFloat number:
            {
                if (!OptionText.TryRead(value, out float amount))
                {
                    BadValue(config, label, value, indent);
                    break;
                }

                float edited = amount;
                bool changed = false;
                PvpApprovedChrome.Row(id, label, DefaultLine(OptionKind.Float, defaultText), 120f, PvpApprovedChrome.BodyControl, pos =>
                {
                    ImGui.SetCursorScreenPos(pos);
                    changed = PvpApprovedChrome.Field(() =>
                    {
                        ImGui.SetNextItemWidth(120f);
                        return ImGui.DragFloat("##value", ref edited, number.Speed, number.Min, number.Max, "%.2f" + number.UnitType.ToSymbol());
                    });
                    PvpUi.SaveOnRelease();
                }, indent, joined: isChild, onReset: () => ResetTo(config, defaultText),
                    resetVisible: !OptionText.SameValue(OptionKind.Float, value, defaultText));
                if (changed)
                {
                    config.Value = OptionText.Write(OptionText.ClampFloat(edited, number.Min, number.Max, number.Min));
                    PvpUi.MarkDirty();
                }

                break;
            }

            case RotationConfigInt whole:
            {
                if (!OptionText.TryRead(value, out int amount))
                {
                    BadValue(config, label, value, indent);
                    break;
                }

                if (PvpUi.NumberDrag(id, label, DefaultLine(OptionKind.Int, defaultText), ref amount, whole.Min, whole.Max, string.Empty, indent: indent, joined: isChild,
                        onReset: () => ResetTo(config, defaultText), resetVisible: !OptionText.SameValue(OptionKind.Int, value, defaultText)))
                {
                    config.Value = OptionText.Write(OptionText.ClampInt(amount, whole.Min, whole.Max));
                    PvpUi.MarkDirty();
                }

                break;
            }

            case RotationConfigCombo combo:
            {
                string[] names = combo.DisplayValues;
                int index = Array.FindIndex(names, n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    index = 0;
                }

                int picked = -1;
                const float width = 240f;
                PvpApprovedChrome.Row(id, label, DefaultLine(OptionKind.Text, defaultText), width, PvpApprovedChrome.BodyControl, pos =>
                {
                    ImGui.SetCursorScreenPos(pos);
                    PvpApprovedChrome.Combo("##value", names.Length > 0 ? names[index] : string.Empty, width, () =>
                    {
                        for (int i = 0; i < names.Length; i++)
                        {
                            if (ImGui.Selectable(names[i], i == index))
                            {
                                picked = i;
                            }
                        }
                    });
                }, indent, joined: isChild, onReset: () => ResetTo(config, defaultText),
                    resetVisible: !OptionText.SameValue(OptionKind.Text, value, defaultText));
                if (picked >= 0)
                {
                    index = picked;
                    combo.Value = names[index];
                    PvpUi.Save();
                }

                break;
            }

            case RotationConfigString:
            {
                string text = value;
                bool changed = false;
                PvpApprovedChrome.Row(id, label, DefaultLine(OptionKind.Text, defaultText), 240f, PvpApprovedChrome.BodyControl, pos =>
                {
                    ImGui.SetCursorScreenPos(pos);
                    changed = PvpApprovedChrome.Field(() =>
                    {
                        ImGui.SetNextItemWidth(240f);
                        return ImGui.InputTextWithHint("##value", string.Empty, ref text, 128);
                    });
                    PvpUi.SaveOnRelease();
                }, indent, joined: isChild, onReset: () => ResetTo(config, defaultText),
                    resetVisible: !OptionText.SameValue(OptionKind.Text, value, defaultText));
                if (changed)
                {
                    config.Value = text;
                    PvpUi.MarkDirty();
                }

                break;
            }
        }
    }

    // "(default 75%)" is the help line of the row; the Reset button sits left of the control while the value differs.
    private static string DefaultLine(OptionKind kind, string defaultText) => OptionText.DefaultLabel(kind, defaultText);

    private static void ResetTo(IRotationConfig config, string defaultText)
    {
        config.Value = defaultText;
        PvpUi.Save();
    }

    private static void BadValue(IRotationConfig config, string label, string value, int indent)
        => PvpApprovedChrome.Row(config.Name, label, $"The stored value \"{value}\" is not a valid number.", 0, 0, null, indent,
            onReset: () =>
            {
                config.Value = config.DefaultValue;
                PvpUi.Save();
            }, resetVisible: true);
}
