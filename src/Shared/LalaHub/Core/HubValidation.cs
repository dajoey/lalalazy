#nullable enable
using System;
using System.Collections.Generic;

namespace Lalalazy.Hub;

/// <summary>
/// Checks a declaration before it is registered. A bad declaration is logged and never registered,
/// so a mistake in an adapter cannot reach the hub or the game.
/// </summary>
internal static class HubValidation
{
    public static List<string> Validate(string plugin, string version, IReadOnlyList<ControlDef> defs)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(plugin) || plugin.Length > HubProtocol.MaxPluginLength)
            errors.Add("plugin name is required and at most " + HubProtocol.MaxPluginLength + " characters");
        if (version == null || version.Length > HubProtocol.MaxVersionLength)
            errors.Add("plugin version must be at most " + HubProtocol.MaxVersionLength + " characters");
        if (defs.Count > HubProtocol.MaxControls)
            errors.Add("at most " + HubProtocol.MaxControls + " controls are allowed, found " + defs.Count);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var masters = 0;

        foreach (var d in defs)
        {
            if (!IsValidId(d.Id))
            {
                errors.Add("control id '" + d.Id + "' must be lowercase letters, digits and underscores, starting with a letter, at most " + HubProtocol.MaxIdLength + " characters");
                continue;
            }

            if (!seen.Add(d.Id))
                errors.Add("duplicate control id '" + d.Id + "'");

            if (string.IsNullOrWhiteSpace(d.Label) || d.Label.Length > HubProtocol.MaxLabelLength)
                errors.Add("control '" + d.Id + "': label must be 1 to " + HubProtocol.MaxLabelLength + " characters");
            if (d.Tip != null && d.Tip.Length > HubProtocol.MaxTipLength)
                errors.Add("control '" + d.Id + "': tip is longer than " + HubProtocol.MaxTipLength + " characters");
            if (d.Confirm != null && d.Confirm.Length > HubProtocol.MaxConfirmLength)
                errors.Add("control '" + d.Id + "': confirm text is longer than " + HubProtocol.MaxConfirmLength + " characters");

            if (d.Master)
            {
                masters++;
                if (d.Kind != ControlKind.Toggle)
                    errors.Add("control '" + d.Id + "': only a toggle can be the master");
            }

            switch (d.Kind)
            {
                case ControlKind.Stepper:
                    if (!(d.Min < d.Max))
                        errors.Add("stepper '" + d.Id + "': min must be below max");
                    if (!(d.Step > 0))
                        errors.Add("stepper '" + d.Id + "': step must be above 0");
                    if (d.Get == null || d.Set == null)
                        errors.Add("stepper '" + d.Id + "': needs a getter and a setter");
                    break;

                case ControlKind.Choice:
                    if (d.Choices.Count < 1 || d.Choices.Count > HubProtocol.MaxChoices)
                        errors.Add("choice '" + d.Id + "' needs 1 to " + HubProtocol.MaxChoices + " options");
                    foreach (var c in d.Choices)
                        if (string.IsNullOrWhiteSpace(c) || c.Length > HubProtocol.MaxChoiceLength)
                            errors.Add("choice '" + d.Id + "': every option must be 1 to " + HubProtocol.MaxChoiceLength + " characters");
                    if (d.Get == null || d.Set == null)
                        errors.Add("choice '" + d.Id + "': needs a getter and a setter");
                    break;

                case ControlKind.Toggle:
                    if (d.Get == null || d.Set == null)
                        errors.Add("toggle '" + d.Id + "': needs a getter and a setter");
                    break;

                case ControlKind.Button:
                    if (d.Run == null)
                        errors.Add("button '" + d.Id + "': needs an action");
                    break;
            }
        }

        if (masters > 1)
            errors.Add("only one master control is allowed, found " + masters);

        return errors;
    }

    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > HubProtocol.MaxIdLength) return false;
        if (id[0] < 'a' || id[0] > 'z') return false;
        foreach (var ch in id)
        {
            var ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_';
            if (!ok) return false;
        }
        return true;
    }
}
