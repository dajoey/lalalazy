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
            Widgets.SecondaryWrapped("Developed with ♥ by Blackcatz1911/catz/Ayaya\nLazy version by dajoey");
        });

        CurrencyChrome.Card("about_thanks", () =>
        {
            CurrencyChrome.Section("Special thanks");
            List<string> thanks = ["The Dalamud Team", "FFXIVClientStructs", "Yuki", "Limiana", "Taurenkey", "MidoriKami", "CriticalImpact", "Haselnussbomber"];
            Widgets.SecondaryWrapped(string.Join(" · ", thanks));
        });

        CurrencyChrome.Card("about_links", () =>
        {
            CurrencyChrome.Section("Project links", "Suggestions and feature requests can be raised on the repository.");
            if (CurrencyChrome.Button("lazy_github", "Lazy GitHub"))
                GenericHelpers.ShellStart("https://github.com/dajoey/lalalazy");
            if (CurrencyChrome.Button("original_github", "Original GitHub"))
                GenericHelpers.ShellStart("https://github.com/Blackcatz1911/CurrencySpender");
            if (CurrencyChrome.Button("support_kofi", "♥ Ko-fi"))
                GenericHelpers.ShellStart(KofiBanner.DonateLink);
        });
    }
}
