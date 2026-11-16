using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using Lala.UiRender;

namespace LazySkywardTracker.UiHarness;

/// <summary>Offline checks (no ImGui context needed) and the glyph audit of the strings the window can draw.</summary>
internal static class Checks
{
    private static int _pass;
    private static int _fail;
    private static MethodInfo _derive = null!;
    private static PropertyInfo _state = null!;
    private static PropertyInfo _reason = null!;

    /// <summary>Source root the harness was built against (AssemblyMetadata "LalaSourceRoot"), or null.</summary>
    internal static string? SourceRoot => typeof(Checks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "LalaSourceRoot")?.Value;

    /// <summary>Live-state decisions and row layout maths, as before, plus a drift check of the stubbed achievement table.</summary>
    internal static bool RunOffline()
    {
        var window = typeof(TrackerWindow);
        _derive = window.GetMethod("DeriveLive", BindingFlags.NonPublic | BindingFlags.Static)
                  ?? throw new MissingMemberException("DeriveLive not found");
        var live = _derive.ReturnType;
        _state = live.GetProperty("State") ?? throw new MissingMemberException("Live.State not found");
        _reason = live.GetProperty("Reason") ?? throw new MissingMemberException("Live.Reason not found");

        State("logged out remains Blocked even with cached progress", "Blocked", Derive(false, 11, 4, 2000000, 800));
        State("logged in without a progress report remains Empty", "Empty", Derive(true, 0, 0, 0, 0));
        State("all jobs complete remains Ready", "Ready", Derive(true, 11, 11, 5500000, 0));
        State("partial progress remains Running", "Running", Derive(true, 11, 4, 2000000, 0));
        State("hook down is Blocked", "Blocked", Derive(true, 0, 0, 0, 0, tracking: false));
        State("hook down stays Blocked even with cached progress", "Blocked", Derive(true, 11, 4, 2000000, 0, tracking: false));
        Check("hook down reports why the points may be stale",
            _reason.GetValue(Derive(true, 0, 0, 0, 0, tracking: false)) is string offline && offline == "progress hook did not start; points may be stale");
        State("logged out wins over a dead hook", "Blocked", Derive(false, 0, 0, 0, 0, tracking: false));

        var projected = Derive(true, 11, 4, 2000000, 1200);
        Check("inventory points keep their live footer reason", _reason.GetValue(projected) is string reason && reason.Contains("points waiting", StringComparison.Ordinal));
        Check("no inventory points keep an empty footer reason", _reason.GetValue(Derive(true, 11, 4, 2000000, 0)) is null);

        var columns = window.GetMethod("ColumnsFor", BindingFlags.NonPublic | BindingFlags.Static)
                      ?? throw new MissingMemberException("ColumnsFor not found");
        foreach (var width in new[] { 760f, 900f, 1180f })
        {
            var value = columns.Invoke(null, [width, 110f, 140f, 130f]) ?? throw new InvalidOperationException("ColumnsFor returned null");
            var type = value.GetType();
            var progressX = (float)(type.GetProperty("ProgressX")?.GetValue(value) ?? 0f);
            var progressWidth = (float)(type.GetProperty("ProgressWidth")?.GetValue(value) ?? 0f);
            Check($"wide row columns fit at {width:0}px", progressWidth >= 80 && progressX + progressWidth + 24 + 130 <= width + 0.01f);
        }

        CheckStubTableMatchesPlugin();

        Console.WriteLine(_fail == 0 ? $"OK ({_pass} checks)" : $"FAILED ({_fail} of {_pass + _fail})");
        return _fail == 0;
    }

    /// <summary>The stubbed job/achievement table must equal the one in the real Plugin.cs, so scenarios show real names.</summary>
    private static void CheckStubTableMatchesPlugin()
    {
        var root = SourceRoot;
        var path = root is null ? null : Path.Combine(root, "src", "LazySkywardTracker", "Plugin.cs");
        if (path is null || !File.Exists(path))
        {
            Console.WriteLine("SKIP stub table drift check (Plugin.cs not found next to the harness)");
            return;
        }

        var text = File.ReadAllText(path);
        var rows = Regex.Matches(text, "\\{\\s*(\\d+)\\s*,\\s*\\(\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\)\\s*\\}")
            .Select(m => (Id: uint.Parse(m.Groups[1].Value), Job: m.Groups[2].Value, Ach: m.Groups[3].Value))
            .ToList();
        var ids = Regex.Match(text, "AchievementIds\\s*=\\s*\\[([^\\]]+)\\]").Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(uint.Parse).ToArray();
        Check("stubbed achievement ids equal Plugin.cs", ids.SequenceEqual(Plugin.AchievementIds));
        Check("stubbed job and achievement names equal Plugin.cs",
            rows.Count == Plugin.SkywardAchievements.Count &&
            rows.All(r => Plugin.SkywardAchievements.TryGetValue(r.Id, out var s) && s.Job == r.Job && s.AchName == r.Ach));
    }

    private static object Derive(bool loggedIn, int reports, int completed, uint current, uint inventory, bool tracking = true)
        => _derive.Invoke(null, [loggedIn, tracking, reports, completed, current, inventory])
           ?? throw new InvalidOperationException("DeriveLive returned null");

    private static void State(string name, string expected, object live)
        => Check(name, Equals(_state.GetValue(live)?.ToString(), expected));

    internal static void Check(string name, bool passed)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {name}");
        if (passed) _pass++; else _fail++;
    }

    /// <summary>
    /// Every non-ASCII character in the string literals of the compiled window, chrome and kit sources, and in the
    /// scenario data, must have a glyph in the game font; the Font Awesome mountain tile must exist in the icon font.
    /// Returns false when something is missing (printed as a report).
    /// </summary>
    internal static bool GlyphAudit(RenderLook look, IEnumerable<string> scenarioStrings)
    {
        var found = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var scanned = 0;
        void Scan(string source, string text)
        {
            scanned++;
            foreach (var entry in look.MissingGlyphs(text))
            {
                if (!found.TryGetValue(entry, out var sources))
                    found[entry] = sources = [];
                sources.Add(source);
            }
        }

        var literal = new Regex("@?\"(?:[^\"\\\\\r\n]|\\\\.)*\"");
        var root = SourceRoot;
        var files = new List<string>();
        if (root is not null)
        {
            files.Add(Path.Combine(root, "src", "LazySkywardTracker", "TrackerWindow.cs"));
            files.Add(Path.Combine(root, "src", "LazySkywardTracker", "SkywardChrome.cs"));
            var kit = Path.Combine(root, "src", "Shared", "LalaUiKit");
            if (Directory.Exists(kit))
                files.AddRange(Directory.EnumerateFiles(kit, "*.cs", SearchOption.AllDirectories));
        }

        foreach (var file in files.Where(File.Exists))
        {
            var text = new StringBuilder();
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                foreach (Match match in literal.Matches(line))
                {
                    var value = match.Value;
                    try { value = Regex.Unescape(value); } catch (ArgumentException) { }
                    text.Append(value).Append('\n');
                }
            }
            Scan(Path.GetFileName(file), text.ToString());
        }

        foreach (var value in scenarioStrings)
            Scan("scenario data", value);
        Scan("job and achievement names", string.Concat(Plugin.SkywardAchievements.Values.Select(v => v.Job + " " + v.AchName + "\n")));

        var iconMissing = GlyphCoverage.Missing(look.IconFont, FontAwesomeIcon.Mountain.ToIconString());
        Console.WriteLine($"Glyph check scanned {scanned} text sources ({files.Count} source files).");
        var ok = true;
        if (found.Count == 0)
            Console.WriteLine("Glyph check: every drawn non-ASCII character exists in the game font.");
        else
        {
            ok = false;
            Console.WriteLine($"Glyph check: {found.Count} character(s) in drawn text have no glyph in the game font:");
            foreach (var (entry, sources) in found)
                Console.WriteLine($"  {entry} in {string.Join(", ", sources)}");
        }

        if (iconMissing.Count == 0)
            Console.WriteLine("Glyph check: the mountain icon exists in the icon font.");
        else
        {
            ok = false;
            Console.WriteLine($"Glyph check: the mountain icon is missing from the icon font ({string.Join(", ", iconMissing)}).");
        }

        return ok;
    }
}
