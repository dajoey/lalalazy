using System;
using ECommons;
using ECommons.Logging;
using GluttonyCombo.Services;
using LalaUiKit;

namespace GluttonyCombo.Window.Tabs;

// Fork: the approved shell owns headers, search, and job navigation.
internal partial class PvPFeatures
{
    internal static new void Draw()
    {
        var openPvPJob = OpenPvPJob;
        if (openPvPJob is null)
        {
            if (GluttonyCombo.P.IPC.GetAutoRotationState())
                Widgets.Banner(UiState.Blocked, Resources.Localization.UI.Features.FeaturesUI.Info_pvpAutoRotationWarning);
            if (!Service.Configuration.ActionChanging)
                Widgets.Banner(UiState.Blocked, Resources.Localization.UI.Features.FeaturesUI.Info_pvpActionReplacingWarning);
            GluttonyJobGrid.Draw(true);
            return;
        }

        CurrentPreset = 1;
        try
        {
            DrawHeadingContents(openPvPJob.Value);
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error while drawing {openPvPJob} PvP UI:\n{e.ToStringFull()}");
        }
    }
}
