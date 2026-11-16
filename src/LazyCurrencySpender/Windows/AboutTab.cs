using CurrencySpender.Classes;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class AboutTab
{
    internal static void Draw()
    {
        CurrencyChrome.Card("about_project", () =>
        {
            CurrencyChrome.Section("Lazy Currency Spender", $"Version {P.Version}");
            // Fork: secondary size, one paragraph per credit line with the standard item spacing.
            CurrencyChrome.Muted("Developed with ♥ by Blackcatz1911/catz/Ayaya");
            CurrencyChrome.Muted("Lazy version by dajoey");
        });

        CurrencyChrome.Card("about_thanks", () =>
        {
            CurrencyChrome.Section("Special thanks");
            List<string> thanks = ["The Dalamud Team", "FFXIVClientStructs", "Yuki", "Limiana", "Taurenkey", "MidoriKami", "CriticalImpact", "Haselnussbomber"];
            CurrencyChrome.Muted(string.Join(" · ", thanks)); // Fork: secondary size
        });

        CurrencyChrome.Card("about_links", () =>
        {
            CurrencyChrome.Section("Project links", "Suggestions and feature requests can be raised on the repository.");
            // Fork: the three links share one row at equal widths (they wrap onto the next row when the card is narrow).
            var linkWidth = CurrencyChrome.ActionWidth(96, "Lazy GitHub", "Original GitHub", "♥ Ko-fi");
            var perRow = Math.Max(1, (int)((CurrencyChrome.Avail() + 8) / (linkWidth + 8)));
            if (CurrencyChrome.Button("lazy_github", "Lazy GitHub", false, linkWidth))
                GenericHelpers.ShellStart("https://github.com/dajoey/lalalazy");
            if (perRow > 1)
                ImGui.SameLine(0, 8);
            if (CurrencyChrome.Button("original_github", "Original GitHub", false, linkWidth))
                GenericHelpers.ShellStart("https://github.com/Blackcatz1911/CurrencySpender");
            if (perRow > 2)
                ImGui.SameLine(0, 8);
            if (CurrencyChrome.Button("support_kofi", "♥ Ko-fi", false, linkWidth))
                GenericHelpers.ShellStart(KofiBanner.DonateLink);
        });
    }
}
