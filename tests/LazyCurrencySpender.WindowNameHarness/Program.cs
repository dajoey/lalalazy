using System.Text.RegularExpressions;

namespace LazyCurrencySpender.WindowNameHarness;

/// <summary>
///     Offline assertions on the WindowSystem registration invariant. Dalamud's
///     WindowSystem.AddWindow keys its dictionary on Window.WindowName AT CONSTRUCTION TIME;
///     the PreDraw rename only changes the drawn title, never the dictionary key. Every Window
///     subclass in src/LazyCurrencySpender/Windows must therefore pass base(...) a name whose
///     identity is provable offline (a literal, or a literal with a ###Id suffix), non-empty,
///     pairwise unique across windows, and - when PreDraw later renames the window with a
///     ###Id - the same identity it was registered under. Replays the 2026-11-14 incident
///     (fingerprint fdb1567f5ff6): MainTabWindow and DebugTabWindow both constructed with
///     base($""), the second AddWindow threw "A window with this name/ID already exists." once
///     per session, and the deferred init lambda died before wiring the rest of the plugin.
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private sealed record Identity(string Id, string Problem);

    /// <summary>Every window class the plugin ships, as of the incident fix. Guards against the
    /// class-declaration regex silently matching nothing.</summary>
    private static readonly string[] ExpectedWindows =
    [
        "SpendingWindow", "DebugTabWindow", "MainTabWindow",
        "ConfigTabWindow", "ConfigWizardWindow", "CurrencyOverlay",
    ];

    private static int Main(string[] args)
    {
        var root = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "LazyCurrencySpender", "Windows"));
        var files = Directory.GetFiles(root, "*.cs").Order().ToList();

        Check("the window source directory resolves and parses", files.Count > 0, $"{files.Count} files under {root}");

        var windows = new List<(string Name, string BaseArg, string? PredrawArg)>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var classMatch = Regex.Match(text, @"class\s+(\w+)[^:\r\n]*:\s*Window\b");
            if (!classMatch.Success)
                continue; // tab/partial classes (MainTab, ChangelogTab, ...) are not WindowSystem windows

            var name = classMatch.Groups[1].Value;
            var baseArg = ExtractCallArgument(text, "base", classMatch.Index);
            var predrawArg = FindPredrawName(text, classMatch.Index);
            windows.Add((name, baseArg ?? "", predrawArg));
        }

        var found = windows.Select(w => w.Name).Order().ToList();
        Check("the harness finds every shipped window class (negative control against a silent regex)",
            ExpectedWindows.All(e => found.Contains(e)),
            found.Count == 0 ? "none found" : string.Join(", ", found));

        var identities = new Dictionary<string, string>();
        foreach (var (name, baseArg, predrawArg) in windows)
        {
            var id = Classify(baseArg);
            Check($"window {name} registers a provable, non-empty identity at AddWindow time",
                id.Problem == null, id.Problem ?? $"identity '{id.Id}'");
            if (id.Problem == null)
                identities[name] = id.Id;

            if (predrawArg == null)
                continue;
            var predraw = Classify(predrawArg);
            if (predraw.Problem == null)
                Check($"window {name} keeps its registration identity after AddWindow (ctor '{id.Id}' == PreDraw '{predraw.Id}')",
                    id.Id == predraw.Id, id.Id == predraw.Id ? "" : $"ctor registers '{id.Id}', PreDraw renames to '{predraw.Id}'");
        }

        var duplicates = identities.GroupBy(kv => kv.Value).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(kv => kv.Key))}").ToList();
        Check("window identities are pairwise unique", duplicates.Count == 0,
            duplicates.Count == 0 ? $"{identities.Count} unique identities" : string.Join(" | ", duplicates));

        NegativeControls();

        Console.WriteLine($"{_pass} pass, {_fail} fail");
        if (_fail == 0) Console.WriteLine("OK");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>Resolves the window-name identity a base(...) argument registers under, or a
    /// Problem string when that identity cannot be proven offline.</summary>
    private static Identity Classify(string arg)
    {
        var literal = Regex.Match(arg, "\"([^\"]*)\"");
        if (!literal.Success)
            return new Identity("", "no string literal in the base() argument - identity unprovable");
        var content = literal.Groups[1].Value;
        if (content.Length == 0)
            return new Identity("", "constructs with an EMPTY WindowName - registers under the \"\" key");
        var hashIdx = content.LastIndexOf("###", StringComparison.Ordinal);
        if (hashIdx < 0)
        {
            if (content.Contains('{'))
                return new Identity("", $"name '{content}' is interpolated without a ###Id suffix - identity unprovable");
            return new Identity(content, null);
        }

        var id = content[(hashIdx + 3)..];
        if (id.Length == 0 || id.Contains('{'))
            return new Identity("", $"###Id suffix in '{content}' is empty or interpolated - identity unprovable");
        return new Identity(id, null);
    }

    /// <summary>Returns the full argument list of the first UNCOMMENTED call to
    /// <paramref name="callee"/> at or after <paramref name="from"/>, with balanced parentheses,
    /// or null. Comment lines are skipped so a comment may discuss code without blinding the
    /// parser.</summary>
    private static string? ExtractCallArgument(string text, string callee, int from)
    {
        var searchFrom = from;
        while (true)
        {
            var open = text.IndexOf($": {callee}(", searchFrom, StringComparison.Ordinal);
            if (open < 0)
                return null;
            var lineStart = text.LastIndexOf('\n', open) + 1;
            if (!text[lineStart..open].TrimStart().StartsWith("//"))
            {
                open += callee.Length + 3; // ": base(" is colon + space + callee + open paren
                var depth = 1;
                var end = open;
                while (end < text.Length && depth > 0)
                {
                    if (text[end] == '(') depth++;
                    else if (text[end] == ')') depth--;
                    end++;
                }

                return depth == 0 ? text[open..(end - 1)] : null;
            }

            searchFrom = open + 1;
        }
    }

    /// <summary>The WindowName assignment in the class's PreDraw (uncommented, single line),
    /// or null when the class never renames itself after registration.</summary>
    private static string? FindPredrawName(string text, int from)
    {
        foreach (var line in text[from..].Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//"))
                continue;
            var idx = trimmed.IndexOf("WindowName =", StringComparison.Ordinal);
            if (idx < 0)
                continue;
            return trimmed[(idx + "WindowName =".Length)..].TrimEnd(';');
        }

        return null;
    }

    /// <summary>Proves the classifier can fail (and can pass) on synthetic shapes, so a silent
    /// regex regression can never report a green bill of health.</summary>
    private static void NegativeControls()
    {
        var empty = Classify("$\"\"");
        Check("negative control: the incident shape base($\"\") is flagged as an empty identity",
            empty.Problem != null, empty.Problem ?? $"classified as '{empty.Id}'");

        var unprovable = Classify("$\"{Something}\"");
        Check("negative control: an interpolated name without a ###Id suffix is flagged unprovable",
            unprovable.Problem != null, unprovable.Problem ?? $"classified as '{unprovable.Id}'");

        var stable = Classify("$\"{P.Name} {P.Version}###MainTabWindow\"");
        Check("negative control: a ###Id name extracts its identity",
            stable.Problem == null && stable.Id == "MainTabWindow",
            stable.Problem ?? stable.Id);

        var plain = Classify("\"Currency overlay\"");
        Check("negative control: a plain literal name is its own identity",
            plain.Problem == null && plain.Id == "Currency overlay",
            plain.Problem ?? plain.Id);

        var multiline = ExtractCallArgument("public X() : base(\"Named\",\n    true)\n{", "base", 0);
        Check("negative control: a multi-line base() call extracts with balanced parentheses",
            multiline == "\"Named\",\n    true",
            multiline ?? "null");

        var skipped = FindPredrawName("//WindowName = \"Ghost\";\nWindowName = $\"Real {P.Version}###Real\";", 0);
        Check("negative control: a commented-out WindowName assignment is skipped",
            skipped != null && skipped.Contains("###Real"),
            skipped ?? "null");
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) _pass++;
        else _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 && !ok ? $"  [{detail}]" : "")}");
    }
}
