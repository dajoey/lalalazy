using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using LalaUiKit;

namespace GluttonyCombo.Window;

// Fork: drawing and wording of the "New changes" window in the approved style. It carries no plugin or game
// dependencies, so the render harness draws the same code and the same text.
internal sealed record MajorLink(string Id, string Label);

internal sealed record MajorSection(string Title, string[] Paragraphs, string? Alert = null, MajorLink[]? Links = null, string? LinkNote = null);

internal sealed class MajorResult
{
    public bool Close;
    public bool Dismiss;
    public string? Link;
}

internal static class GluttonyMajorChanges
{
    internal const string DismissLabel = "Close and do not show again";

    internal static MajorSection[] Sections(bool hadOldTankOptions) =>
    [
        new("Tank in-combo mitigation has been overhauled and moved",
        [
            "Tanks have had their in-combo mitigation completely overhauled. Mitigation settings are no longer in each combo, and mitigation is no longer triggered entirely by HP%.",
            "Mitigation now has its own section under the Advanced Combos, and those options apply to the Advanced Combos if they have 'Include Mitigations' checked. Simple Modes use the recommended values for these settings, if 'Include Mitigations' is checked there.",
            "The moved settings are under PvE Features, then the tank of choice, then Advanced Mitigation Options.",
            "Make sure Mitigation is enabled in the combos for these to work: PvE Features, then the tank of choice, then the Advanced or Simple combo, then Include Mitigations.",
        ],
        hadOldTankOptions ? "One of these tanks had old mitigation options configured. Please read." : null,
        [
            new("DRK", "Open Dark Knight's config"),
            new("GNB", "Open Gun Breaker's config"),
            new("PLD", "Open Paladin's config"),
            new("WAR", "Open Warrior's config"),
        ],
        "After opening a config, search for 'Mitigation'."),
        new("Raidwide detection improved, tankbuster detection added",
        [
            "Raidwide detection now also checks for nearby stack marker visual effects, in addition to the existing raidwide cast detection. No change is needed for this; it is just expanded.",
            "Tankbuster detection has been added by checking for visual effects. Options to mitigate as a tank for these are in the new mitigation options above.",
            "The new tankbuster mitigation options are under PvE Features, then the tank of choice, then Advanced Mitigation Options, then Boss Encounter. Searching for 'Tankbuster' in a tank's configuration finds them too.",
        ]),
    ];

    internal static ShellModel Header(int sections) => new()
    {
        Title = "Mitigation changes",
        Description = "Tank mitigation has moved, and raidwide and tankbuster detection improved.",
        Meta = [sections == 1 ? "1 change" : $"{sections} changes"],
    };

    /// <summary>Draws the whole window: header band, page header, one card per change, and the footer with the dismiss action.</summary>
    internal static MajorResult Draw(Vector2 winPos, Vector2 winSize, MajorSection[] sections, string bodyId)
    {
        var result = new MajorResult();
        var draw = ImGui.GetWindowDrawList();
        var gutter = GluttonyChrome.Gutter(winSize.X);
        ImGui.SetCursorPos(Vector2.Zero);
        result.Close = GluttonyChrome.DrawTitleBand(draw, winPos, winSize.X, gutter, "New changes");
        var y = GluttonyShellView.DrawPageHeader(Header(sections.Length), winPos, winSize.X, gutter, GluttonyChrome.TitleBandHeight);
        var footer = GluttonyChrome.FooterBandHeight;
        ImGui.SetCursorPos(new Vector2(0, y));
        GluttonyChrome.BeginBody(new Vector2(winSize.X, MathF.Max(40, winSize.Y - y - footer)), gutter, bodyId);
        try
        {
            foreach (var section in sections)
                DrawSection(section, result);
        }
        finally
        {
            GluttonyChrome.EndBody();
        }

        GluttonyChrome.DrawFooterBand(draw, winPos, winSize, footer);
        var top = winPos.Y + winSize.Y - footer;
        var block = GluttonyChrome.LineHeight() + 4 + GluttonyChrome.LineHeight(Theme.SecondaryScale);
        var textTop = top + ((footer - block) / 2f);
        var buttonW = GluttonyChrome.ActionWidth(96, DismissLabel);
        draw.AddCircleFilled(new Vector2(winPos.X + gutter + 4, textTop + (GluttonyChrome.LineHeight() / 2f)),
            Theme.DotRadius + 1, Theme.Pack(Theme.StateGreen));
        var room = winSize.X - (2 * gutter) - 16 - buttonW - 16;
        GluttonyChrome.DrawText(draw, new Vector2(winPos.X + gutter + 16, textTop),
            GluttonyChrome.FitText("New changes in this version", room), Theme.TextPrimary);
        GluttonyChrome.DrawText(draw, new Vector2(winPos.X + gutter + 16, textTop + GluttonyChrome.LineHeight() + 4),
            GluttonyChrome.FitText("The close button hides these notes for this version.", room, Theme.SecondaryScale),
            Theme.TextSecondary, Theme.SecondaryScale);
        var buttonPos = new Vector2(winPos.X + winSize.X - gutter - buttonW, top + ((footer - GluttonyChrome.Control) / 2f));
        if (GluttonyChrome.ActionButton(draw, "dismiss", DismissLabel, buttonPos, new Vector2(buttonW, GluttonyChrome.Control)))
            result.Dismiss = true;
        return result;
    }

    private static void DrawSection(MajorSection section, MajorResult result)
    {
        using var card = GluttonyChrome.BeginCard();
        Widgets.Title(section.Title);
        if (section.Alert is { } alert)
            GluttonyChrome.Notice(UiState.Blocked, alert);
        foreach (var paragraph in section.Paragraphs)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            GluttonyChrome.WrappedText(paragraph);
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(1, 0));
        }
        if (section.Links is { Length: > 0 } links)
        {
            var first = true;
            foreach (var link in links)
            {
                var width = GluttonyChrome.TextWidth(link.Label) + 32;
                // The next button joins the previous line when it still fits inside the card.
                if (!first && ImGui.GetItemRectMax().X + GluttonyChrome.Gap + width <= GluttonyChrome.RightEdge())
                    ImGui.SameLine(0, GluttonyChrome.Gap);
                first = false;
                if (GluttonyChrome.StyledButton(link.Label + "##major_" + link.Id))
                    result.Link = link.Id;
            }
            if (section.LinkNote is { } note)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
                ImGui.SetWindowFontScale(Theme.SecondaryScale);
                ImGui.TextUnformatted(note);
                ImGui.SetWindowFontScale(1f);
                ImGui.PopStyleColor();
            }
        }
    }
}
