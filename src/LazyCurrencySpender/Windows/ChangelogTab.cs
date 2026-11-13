using System.IO;

namespace CurrencySpender.Windows;

internal class ChangelogTab
{
    internal static bool rendered = false;
    internal static void Draw()
    {
        if (P != null && P.changelogPath != null && File.Exists(P.changelogPath))
        {
            using (StreamReader reader = new StreamReader(P.changelogPath))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("### ") || line.Contains("## ") || line.Contains("# "))
                    {
                        // Kit type scale (R7): one title size for all markdown headings.
                        LalaUiKit.Widgets.Title(line.Replace("#", "").Trim());
                    }
                    else
                    {
                        ImGui.TextWrapped(line.Trim());
                    }
                }
            }
        }
    }
}
