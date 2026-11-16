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
                var firstRelease = true;
                while ((line = reader.ReadLine()) != null)
                {
                    // Fork: readable release layout: version headings with a date and channel badge, group labels and bullets.
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || (trimmed.StartsWith("# ", StringComparison.Ordinal)))
                        continue;
                    trimmed = trimmed.Replace("**", "").Replace("`", "");
                    if (trimmed.StartsWith("### ", StringComparison.Ordinal))
                        CurrencyChrome.Subheading(trimmed[4..].Trim());
                    else if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                    {
                        CurrencyChrome.ReleaseHeading(trimmed[3..].Trim(), firstRelease);
                        firstRelease = false;
                    }
                    else if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        CurrencyChrome.Bullet(trimmed[2..].Trim());
                    else
                        CurrencyChrome.Muted(trimmed);
                }
            }
            else
            {
                CurrencyChrome.EmptyBlock("Empty", "The release history is not available in this installation."); // Fork: centred empty block
            }
        });
    }
}
