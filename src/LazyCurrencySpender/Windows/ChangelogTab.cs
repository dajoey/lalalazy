using System.IO;
using LalaUiKit;

namespace CurrencySpender.Windows;

internal class ChangelogTab
{
    internal static bool rendered = false;

    internal static void Draw()
    {
        CurrencyChrome.Card("release_history", () =>
        {
            if (P != null && P.changelogPath != null && File.Exists(P.changelogPath))
            {
                using var reader = new StreamReader(P.changelogPath);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("### ") || line.Contains("## ") || line.Contains("# "))
                        Widgets.Title(line.Replace("#", "").Trim());
                    else
                        Widgets.SecondaryWrapped(line.Trim());
                }
            }
            else
            {
                Widgets.StateText("● Empty", UiState.Empty);
                Widgets.SecondaryWrapped("The release history is not available in this installation.");
            }
        });
    }
}
