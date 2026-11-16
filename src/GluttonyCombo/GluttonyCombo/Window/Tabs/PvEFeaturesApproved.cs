using System;
using System.Collections.Generic;
using System.Linq;
using ECommons;
using ECommons.ExcelServices;
using ECommons.Logging;
using GluttonyCombo.Core;
using GluttonyCombo.Resources.Localization.UI.Features;
using GluttonyCombo.Resources.Localization.UI.Misc;
using GluttonyCombo.Window.Functions;
using LalaUiKit;

namespace GluttonyCombo.Window.Tabs;

// Fork: the approved shell owns headers, search, and content navigation.
internal partial class PvEFeatures
{
    internal static new void Draw()
    {
        if (ActionReplacer.ClassLocked())
        {
            Widgets.Banner(UiState.NeedsSetup, FeaturesUI.Warning_EquipJobStone);
            return;
        }

        var openJob = OpenJob;
        if (openJob is null)
        {
            GluttonyJobGrid.Draw(false);
            return;
        }

        var contents = ApprovedContents(openJob.Value);
        if (contents.Count > 0 && contents.All(content => ContentTab(content.Id) != CurrentTab))
            SetCurrentTab(FeatureTab.Normal);

        CurrentPreset = 1;
        try
        {
            switch (CurrentTab)
            {
                case FeatureTab.BlueTank:
                case FeatureTab.BlueDPS:
                case FeatureTab.BlueHealer:
                    DrawBlueContents(CurrentTab);
                    break;
                case FeatureTab.Variant:
                    DrawVariantContents(openJob.Value);
                    break;
                case FeatureTab.Bozja:
                    DrawBozjaContents(openJob.Value);
                    break;
                case FeatureTab.OccultCrescent:
                    DrawOccultContents(openJob.Value);
                    break;
                case FeatureTab.DeepDungeon:
                    DrawDeepDungeonContents(openJob.Value);
                    break;
                default:
                    DrawHeadingContents(openJob.Value);
                    break;
            }
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error while drawing Job's UI:\n{e.ToStringFull()}");
        }
    }

    internal static IReadOnlyList<GluttonyContent> ApprovedContents(Job job)
    {
        var presets = groupedPresets[job];
        var contents = new List<GluttonyContent>();
        if (job == Job.BLU)
        {
            if (presets.Any(preset => preset.IsBlueTank))
                contents.Add(new(ContentId(FeatureTab.BlueTank), MiscUI.Tank));
            if (presets.Any(preset => preset.IsBlueDPS))
                contents.Add(new(ContentId(FeatureTab.BlueDPS), MiscUI.DPS));
            if (presets.Any(preset => preset.IsBlueHealer))
                contents.Add(new(ContentId(FeatureTab.BlueHealer), MiscUI.Healer));
            contents.Add(new(ContentId(FeatureTab.Normal), MiscUI.Miscellaneous));
            return contents;
        }

        contents.Add(new(ContentId(FeatureTab.Normal), job == Job.ADV ? MiscUI.Job_Roles : MiscUI.Normal));
        if (job == Job.ADV)
        {
            if (presets.Any(preset => preset.IsVariant))
                contents.Add(new(ContentId(FeatureTab.Variant), MiscUI.Variant_Dungeons));
            if (presets.Any(preset => preset.IsBozja))
                contents.Add(new(ContentId(FeatureTab.Bozja), MiscUI.Bozja));
            if (presets.Any(preset => preset.IsOccultCrescent))
                contents.Add(new(ContentId(FeatureTab.OccultCrescent), MiscUI.Occult_Crescent));
        }
        if (presets.Any(preset => preset.IsDeepDungeon))
            contents.Add(new(ContentId(FeatureTab.DeepDungeon), MiscUI.Deep_Dungeon));
        return contents;
    }

    internal static string ContentId(FeatureTab tab) => tab switch
    {
        FeatureTab.Variant => "variant",
        FeatureTab.Bozja => "bozja",
        FeatureTab.OccultCrescent => "occult",
        FeatureTab.BlueDPS => "blue-dps",
        FeatureTab.BlueTank => "blue-tank",
        FeatureTab.BlueHealer => "blue-healer",
        FeatureTab.DeepDungeon => "deep-dungeon",
        _ => "normal",
    };

    internal static FeatureTab ContentTab(string id) => id switch
    {
        "variant" => FeatureTab.Variant,
        "bozja" => FeatureTab.Bozja,
        "occult" => FeatureTab.OccultCrescent,
        "blue-dps" => FeatureTab.BlueDPS,
        "blue-tank" => FeatureTab.BlueTank,
        "blue-healer" => FeatureTab.BlueHealer,
        "deep-dungeon" => FeatureTab.DeepDungeon,
        _ => FeatureTab.Normal,
    };
}
