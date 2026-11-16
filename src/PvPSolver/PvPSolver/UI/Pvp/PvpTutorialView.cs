using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using LalaUiKit;

namespace RotationSolver.UI.Pvp;

/// <summary>One step of the first-start tutorial: a title, a one-line description, bullets, optional macros and notes.</summary>
/// <param name="Title">The step title (page header).</param>
/// <param name="Description">One line under the title.</param>
/// <param name="Tab">The settings window tab the step's button opens, or null for a step without a button.</param>
/// <param name="Bullets">The points of the step.</param>
/// <param name="Macros">Macros the player can copy; a macro may span several lines.</param>
/// <param name="Note">A closing line under the bullets, or null.</param>
internal sealed record TutorialPage(
    string Title,
    string Description,
    string? Tab = null,
    string[]? Bullets = null,
    string[]? Macros = null,
    string? Note = null);

/// <summary>What the player did in the tutorial this frame.</summary>
internal enum TutorialAction
{
    None,
    Close,
    Back,
    Next,
    Finish,
}

/// <summary>
///     Fork-only drawing of the first-start tutorial in the suite's dialog shell: header band with the brand chip and a
///     drawn close button, a page header with the step title and "step N of M", the step on a card, and a footer with
///     Back, Next and Finish. The window class owns the steps and what the actions do.
/// </summary>
internal static class PvpTutorialView
{
    internal static TutorialAction Draw(TutorialPage page, int index, int count, Action<string> copyMacro, Action<string> openTab)
    {
        PvpApprovedChrome.BeginFrame();
        Theme.PushFrame();
        try
        {
            Vector2 winPos = ImGui.GetWindowPos();
            Vector2 winSize = ImGui.GetWindowSize();
            PvpApprovedChrome.WindowPos = winPos;
            PvpApprovedChrome.WindowSize = winSize;
            float gutter = PopupChrome.Gutter(winSize.X);
            ImDrawListPtr draw = ImGui.GetWindowDrawList();
            TutorialAction action = TutorialAction.None;

            float y = PopupChrome.HeaderBand(draw, winPos, winSize.X, gutter, PvpApprovedChrome.BrandName, "##tutorialClose", out bool close);
            if (close)
            {
                action = TutorialAction.Close;
            }

            y += PopupChrome.PageHeader(draw, winPos, winSize.X, gutter, y, page.Title, page.Description, $"Step {index + 1} of {count}");

            float footerTop = winSize.Y - PopupChrome.FooterHeight;
            DrawBody(page, winSize.X, gutter, y, MathF.Max(80f, footerTop - y), copyMacro, openTab);
            TutorialAction footer = DrawFooter(draw, winPos, winSize, gutter, index, count);
            return action != TutorialAction.None ? action : footer;
        }
        finally
        {
            Theme.PopFrame();
        }
    }

    private static void DrawBody(TutorialPage page, float width, float gutter, float top, float height, Action<string> copyMacro, Action<string> openTab)
    {
        ImGui.SetCursorPos(new Vector2(0, top));
        using var vars = ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 1f)
            .Push(ImGuiStyleVar.ChildRounding, 0f)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(gutter, 16f))
            .Push(ImGuiStyleVar.ScrollbarSize, 10f)
            .Push(ImGuiStyleVar.ScrollbarRounding, 8f);
        using var colors = ImRaii.PushColor(ImGuiCol.Border, 0u)
            .Push(ImGuiCol.ChildBg, 0u)
            .Push(ImGuiCol.ScrollbarBg, 0u)
            .Push(ImGuiCol.ScrollbarGrab, Theme.SurfaceBorder);
        using var child = ImRaii.Child("##tutorialBody", new Vector2(width, height), true);
        if (!child)
        {
            return;
        }

        PvpApprovedChrome.PushRight(ImGui.GetCursorScreenPos().X + width - (2 * gutter));
        try
        {
            PvpApprovedChrome.Card("tutorial_step", () =>
            {
                if (page.Bullets is { Length: > 0 })
                {
                    foreach (string bullet in page.Bullets)
                    {
                        DrawBullet(bullet);
                    }
                }

                if (page.Macros is { Length: > 0 })
                {
                    PvpApprovedChrome.Para("Recommended macros", 1f, Theme.TextPrimary, 8);
                    for (int i = 0; i < page.Macros.Length; i++)
                    {
                        DrawMacro(i, page.Macros[i], copyMacro);
                    }
                }

                if (page.Tab is { Length: > 0 })
                {
                    if (PvpApprovedChrome.InlineButton("open_tab", "Open the " + page.Tab + " tab", PvpApprovedChrome.ButtonStyle.Accent))
                    {
                        openTab(page.Tab);
                    }
                }

                if (page.Note is { Length: > 0 })
                {
                    PvpApprovedChrome.Para(page.Note, Theme.SecondaryScale, Theme.TextSecondary, 0, maxWidth: float.MaxValue);
                }
            });
        }
        finally
        {
            PvpApprovedChrome.PopRight();
        }
    }

    // A dot in the Accent colour and the point beside it, wrapped at the card edge.
    private static void DrawBullet(string text)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 pos = ImGui.GetCursorScreenPos();
        draw.AddCircleFilled(pos + new Vector2(4f, PvpApprovedChrome.LineHeight() / 2f), 2.5f, Theme.Pack(Theme.Accent));
        PvpApprovedChrome.Para(text, 1f, Theme.TextPrimary, 8, indent: 16);
    }

    // A macro in an inset box: its lines on the left, Copy on the right.
    private static void DrawMacro(int index, string macro, Action<string> copyMacro)
    {
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = PvpApprovedChrome.Avail();
        string[] lines = macro.Replace("\r", string.Empty).Split('\n');
        float lineHeight = PvpApprovedChrome.LineHeight();
        float copyWidth = PvpApprovedChrome.ButtonWidth("Copy");
        float textHeight = lines.Length * lineHeight;
        float height = MathF.Max(PvpApprovedChrome.BodyControl, textHeight) + 16f;
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.Pack(Theme.ShellBar), 8f);
        draw.AddRect(origin, origin + new Vector2(width, height), Theme.Pack(Theme.SurfaceBorder), 8f);
        float textTop = origin.Y + ((height - textHeight) / 2f);
        for (int i = 0; i < lines.Length; i++)
        {
            PvpApprovedChrome.Text(draw, new Vector2(origin.X + 16f, textTop + (i * lineHeight)), lines[i], Theme.TextPrimary);
        }

        Vector2 copyPos = new(origin.X + width - 8f - copyWidth, origin.Y + ((height - PvpApprovedChrome.BodyControl) / 2f));
        if (PvpApprovedChrome.Button("copy_" + index, "Copy", copyPos, new Vector2(copyWidth, PvpApprovedChrome.BodyControl)))
        {
            copyMacro(macro);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height + 8f));
    }

    private static TutorialAction DrawFooter(ImDrawListPtr draw, Vector2 winPos, Vector2 winSize, float gutter, int index, int count)
    {
        float top = winSize.Y - PopupChrome.FooterHeight;
        PopupChrome.FooterBand(draw, winPos, winSize, top);
        const float control = PopupChrome.Control;
        float buttonY = top + ((PopupChrome.FooterHeight - control) / 2f);

        // Progress: one dot per step, the current one in the Accent colour.
        float dotsX = gutter + 16f;
        for (int i = 0; i < count; i++)
        {
            Vector4 color = i == index ? Theme.Accent : Theme.SurfaceBorder;
            draw.AddCircleFilled(winPos + new Vector2(dotsX + (i * 14f) + 3f, top + (PopupChrome.FooterHeight / 2f)), Theme.DotRadius, Theme.Pack(color));
        }

        bool last = index == count - 1;
        string forward = last ? "Finish" : "Next";
        float forwardWidth = PvpApprovedChrome.ButtonWidth(forward, 96f);
        float backWidth = PvpApprovedChrome.ButtonWidth("Back", 96f);
        float forwardX = winSize.X - gutter - forwardWidth;
        TutorialAction action = TutorialAction.None;
        if (PvpApprovedChrome.Button("tutorial_back", "Back", winPos + new Vector2(forwardX - 8f - backWidth, buttonY), new Vector2(backWidth, control),
                PvpApprovedChrome.ButtonStyle.Normal, index == 0, Theme.Surface))
        {
            action = TutorialAction.Back;
        }

        if (PvpApprovedChrome.Button("tutorial_forward", forward, winPos + new Vector2(forwardX, buttonY), new Vector2(forwardWidth, control),
                PvpApprovedChrome.ButtonStyle.Accent))
        {
            action = last ? TutorialAction.Finish : TutorialAction.Next;
        }

        return action;
    }
}
