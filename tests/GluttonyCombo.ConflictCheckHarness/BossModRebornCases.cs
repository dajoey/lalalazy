using System.Globalization;
using System.Reflection;
using GluttonyCombo.Data.Conflicts;
using GluttonyCombo.Services.IPC;
using GluttonyCombo.Services.IPC_Subscriber;

namespace GluttonyCombo.ConflictCheckHarness;

/// <summary>
///     bst1013: the BossModReborn conflict probes on STOCK BossMod Reborn. The verdicts come from stock IPC
///     replies (Presets.Get JSON in PresetSamples, Configuration console replies); the pre-fix reflection walk is
///     characterized on stock-shaped types so the mechanism behind the 8 s "Periodic check failed" flood is
///     proven, not inferred.
/// </summary>
internal static class BossModRebornCases
{
    internal static void Run(Action<string, bool, string> check)
    {
        PreFixCharacterization(check);
        PresetVerdicts(check);
        AiAndQueue(check);
        PresetCache(check);
        TransientDistance(check);
        ProbeFailures(check);
        CheckOrder(check);
    }

    // -----------------------------------------------------------------------------------------------------------
    // 1. Pre-fix walk (BossMod.cs reborn branch @575821102, GetPresets + IsAutoTargetingEnabled, ECommons GetFoP
    //    semantics) run against types shaped like stock BMR 7.5.6.35 (Autorotation/RotationModuleManager.cs:16,41,
    //    Preset.cs:21-34, RotationModule.cs). It is the old code's behaviour, kept here as evidence.
    // -----------------------------------------------------------------------------------------------------------
    private static void PreFixCharacterization(Action<string, bool, string> check)
    {
        var mgr = new StockShape.RotationModuleManager();
        var preset = new StockShape.Preset("AutoDuty");
        preset.Modules.Add(new StockShape.ModuleSettings(typeof(StockShape.AutoTarget)));
        mgr.Preset = preset;

        // Before BMR's first Update the private list is still null: the walk never reads Preset.Modules (it asks
        // for "Module"), so a preset WITH automatic targeting reads as no targeting.
        bool? answer = null;
        Exception? thrown = null;
        try { answer = PreFix.IsAutoTargetingEnabled(new StockShape.Plugin(mgr)); }
        catch (Exception e) { thrown = e; }
        check("pre-fix: preset with AutoTarget, modules not yet built -> reads 'no targeting' (wrong)",
            answer == false && thrown is null, $"answer={answer} thrown={thrown?.GetType().Name}");

        // Once BMR has built its active modules (every tick with a preset up), the walk takes ActiveModule.Module,
        // a RotationModule, and calls GetProperty("Type") on it: there is none, so .GetValue throws.
        mgr.BuildActiveModules();
        answer = null;
        thrown = null;
        try { answer = PreFix.IsAutoTargetingEnabled(new StockShape.Plugin(mgr)); }
        catch (Exception e) { thrown = e; }
        check("pre-fix: preset active and modules built -> NullReferenceException (the 8 s failure)",
            thrown is NullReferenceException, $"answer={answer} thrown={thrown?.GetType().Name}");
    }

    // -----------------------------------------------------------------------------------------------------------
    // 2. Verdicts from the stock Presets.Get JSON.
    // -----------------------------------------------------------------------------------------------------------
    private static void PresetVerdicts(Action<string, bool, string> check)
    {
        var passive = BossModRebornState.EvaluatePreset("AutoDuty Passive", false, PresetSamples.AutoDutyPassive);
        check("AutoDuty Passive: 5 modules read", passive.ModuleCount == 5, passive.ToString());
        check("AutoDuty Passive: no automatic targeting", !passive.AutoTarget, passive.ToString());
        check("AutoDuty Passive: no job rotation (all MiscAI)", !passive.Autorotation, passive.ToString());

        var duty = BossModRebornState.EvaluatePreset("AutoDuty", false, PresetSamples.AutoDuty);
        check("AutoDuty: 34 modules read", duty.ModuleCount == 34, duty.ToString());
        check("AutoDuty: automatic targeting", duty.AutoTarget, duty.ToString());
        check("AutoDuty: job rotations", duty.JobRotation && duty.Autorotation, duty.ToString());
        check("AutoDuty: names its BST rotation module", duty.JobActionModule == "BossMod.Autorotation.xan.BST",
            duty.JobActionModule ?? "null");

        var fate = BossModRebornState.EvaluatePreset("Gluttony", false, PresetSamples.LazyFateGluttony);
        check("Gluttony (FATE) preset: targeting + rotations", fate.AutoTarget && fate.Autorotation, fate.ToString());
        check("Gluttony (FATE) preset: no BST/Crucible module to name", fate.JobActionModule is null,
            fate.JobActionModule ?? "null");

        var movement = BossModRebornState.EvaluatePreset("BOCCHI AI FATE", false, PresetSamples.FateMovement);
        check("movement-only preset: targeting yes, rotation no", movement.AutoTarget && !movement.Autorotation,
            movement.ToString());

        var crucible = BossModRebornState.EvaluatePreset("Crucible", false, PresetSamples.CrucibleAi);
        check("Crucible AI module (under MiscAI) is named and counts as autorotation",
            crucible.JobActionModule == "BossMod.Autorotation.MiscAI.CrucibleAI" && crucible.Autorotation
            && !crucible.JobRotation, crucible.ToString());
        check("short module name for the warning", BossModRebornState.ShortModuleName("BossMod.Autorotation.xan.BST") == "BST",
            BossModRebornState.ShortModuleName("BossMod.Autorotation.xan.BST"));
        check("a BST utility-like name without '.BST' is not a BST rotation",
            !BossModRebornState.IsJobActionModule("BossMod.Autorotation.Utility.ClassBSTUtility"), "matched");

        var multibox = BossModRebornState.EvaluatePreset("VBM Multibox", false, PresetSamples.Multibox);
        check("VBM Multibox: targeting skipped as before", !multibox.AutoTarget, multibox.ToString());

        check("no active preset: nothing", BossModRebornState.EvaluatePreset(null, false, null) == default, "");
        check("force-disabled (empty preset \"\"): nothing",
            BossModRebornState.EvaluatePreset("", true, null) == default, "");
        check("force-disabled wins even with a name",
            BossModRebornState.EvaluatePreset("AutoDuty", true, PresetSamples.AutoDuty) == default, "");
        var vanished = BossModRebornState.EvaluatePreset("Deleted", false, null);
        check("active name with no preset behind it: named, no flags",
            vanished.PresetName == "Deleted" && !vanished.AutoTarget && !vanished.Autorotation, vanished.ToString());
        var broken = BossModRebornState.EvaluatePreset("AutoDuty", false, "{not json");
        check("malformed reply: no flags, parse error carried", broken.ParseError is not null && !broken.Autorotation,
            broken.ToString());
        var noModules = BossModRebornState.EvaluatePreset("X", false, """{"Name":"X"}""");
        check("reply without Modules: parse error carried", noModules.ParseError is not null, noModules.ToString());
    }

    // -----------------------------------------------------------------------------------------------------------
    // 3. AI targeting and manual queue from AI.GetPreset + Configuration([node, field], false) replies.
    // -----------------------------------------------------------------------------------------------------------
    private static void AiAndQueue(Action<string, bool, string> check)
    {
        List<string> t = ["True"], f = ["False"], err = ["Field not found ManualTarget, Valid fields:", "- X"];
        check("console bool: True", BossModRebornState.ConsoleBool(t) == true, "");
        check("console bool: False", BossModRebornState.ConsoleBool(f) == false, "");
        check("console bool: error lines read as unknown", BossModRebornState.ConsoleBool(err) is null, "");
        check("console bool: null reply reads as unknown", BossModRebornState.ConsoleBool(null) is null, "");

        check("AI on + preset + ManualTarget off + ForbidActions off -> retargets",
            BossModRebornState.AiTargetsOnItsOwn(true, "AI", f, f), "");
        check("AI on + no AI preset (omasky: AIAutorotPresetName null) -> no retarget",
            !BossModRebornState.AiTargetsOnItsOwn(true, "", f, f), "");
        check("AI on + ManualTarget on (omasky) -> keeps the player's target",
            !BossModRebornState.AiTargetsOnItsOwn(true, "AI", t, f), "");
        check("AI on + ForbidActions on -> no retarget",
            !BossModRebornState.AiTargetsOnItsOwn(true, "AI", f, t), "");
        check("AI off -> no retarget", !BossModRebornState.AiTargetsOnItsOwn(false, "AI", f, f), "");
        check("unreadable ManualTarget never raises a conflict",
            !BossModRebornState.AiTargetsOnItsOwn(true, "AI", err, f), "");
    }

    // -----------------------------------------------------------------------------------------------------------
    // 4. The 8 s check reads the preset once per (active name, Configuration.LastModified), at most a minute old.
    // -----------------------------------------------------------------------------------------------------------
    private static void PresetCache(Action<string, bool, string> check)
    {
        var t0 = new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);
        var lm = new DateTime(2026, 10, 6, 10, 58, 0);
        check("cache: first check reads", BossModRebornState.PresetNeedsRead(false, null, default, default, "AutoDuty Passive", lm, t0), "");

        // Replay one AutoDuty hour at the 8 s cadence with the preset unchanged: 60 reads at most, not 450.
        var reads = 0;
        var hasCache = false;
        string? cachedName = null;
        DateTime cachedLm = default, cachedAt = default;
        for (var s = 0; s < 3600; s += 8)
        {
            var now = t0.AddSeconds(s);
            if (!BossModRebornState.PresetNeedsRead(hasCache, cachedName, cachedLm, cachedAt, "AutoDuty Passive", lm, now))
                continue;
            reads++;
            hasCache = true;
            cachedName = "AutoDuty Passive";
            cachedLm = lm;
            cachedAt = now;
        }
        check("cache: an unchanged preset is read at most once a minute (<= 61 of 450 checks)", reads <= 61, reads.ToString());

        var at = t0.AddSeconds(16);
        check("cache: another active preset reads at once",
            BossModRebornState.PresetNeedsRead(true, "AutoDuty Passive", lm, t0, "AutoDuty", lm, at), "");
        check("cache: BMR config change reads at once",
            BossModRebornState.PresetNeedsRead(true, "AutoDuty Passive", lm, t0, "AutoDuty Passive", lm.AddSeconds(1), at), "");
        check("cache: preset cleared reads at once",
            BossModRebornState.PresetNeedsRead(true, "AutoDuty Passive", lm, t0, null, lm, at), "");
        check("cache: same name + stamp within a minute is served from cache",
            !BossModRebornState.PresetNeedsRead(true, "AutoDuty Passive", lm, t0, "AutoDuty Passive", lm, at), "");
    }

    // -----------------------------------------------------------------------------------------------------------
    // 5. SetTransient arguments: the console's [node, field, value] (BMR Config/ConfigRoot.cs:178 SetTransient ->
    //    ResolveField: args[0] config type name, args[1] field name, args[2] value through float.Parse).
    // -----------------------------------------------------------------------------------------------------------
    private static void TransientDistance(Action<string, bool, string> check)
    {
        var melee = BossModRebornState.MaxDistanceTransientArgs(3f, CultureInfo.InvariantCulture);
        check("SetTransient args: [AIConfig, MaxDistanceToTarget, 3]",
            melee.SequenceEqual(["AIConfig", "MaxDistanceToTarget", "3"]), string.Join(",", melee));

        // BMR parses with the calling thread's culture; the IPC call runs on that same thread, so a comma-decimal
        // culture must produce a comma value that float.Parse there reads back exactly.
        var de = CultureInfo.GetCultureInfo("de-DE");
        var odd = BossModRebornState.MaxDistanceTransientArgs(2.6f, de);
        check("SetTransient args round-trip through float.Parse in the caller's culture",
            float.Parse(odd[2], de) == 2.6f, odd[2]);
    }

    // -----------------------------------------------------------------------------------------------------------
    // 6. A failing probe is named, gated per probe, and never aborts the probes after it.
    // -----------------------------------------------------------------------------------------------------------
    private static void ProbeFailures(Action<string, bool, string> check)
    {
        var line = ConflictProbeFailure.Line("BossModReborn", "AutoTarget", new NullReferenceException("x"));
        check("failure line names check, probe and exception type",
            line == "[ConflictingPlugins] Periodic check failed: BossModReborn.AutoTarget threw NullReferenceException", line);
        check("failure line keeps the searchable prefix", line.StartsWith("[ConflictingPlugins] Periodic check failed", StringComparison.Ordinal), line);
        var noProbe = ConflictProbeFailure.Line("Redirect", null, new InvalidOperationException());
        check("a whole-check failure names the check", noProbe.EndsWith(": Redirect threw InvalidOperationException", StringComparison.Ordinal), noProbe);
        var withMessage = ConflictProbeFailure.Line("BossModReborn", "AutoTarget", new NullReferenceException("varying 0x" + Guid.NewGuid()));
        check("the varying exception message stays out of the gated line", withMessage == line, withMessage);

        var gate = new LogEmitGate(TimeSpan.FromMinutes(15), 1);
        var t = new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);
        var a1 = gate.ShouldEmit(line, t, out _);
        var a2 = gate.ShouldEmit(line, t.AddSeconds(8), out _);
        var b1 = gate.ShouldEmit(ConflictProbeFailure.Line("BossModReborn", "SmartTargeting", new NullReferenceException()), t.AddSeconds(8), out _);
        check("gate: a repeat of the same probe failure is suppressed", a1 && !a2, $"{a1} {a2}");
        check("gate: a different failing probe still gets its own line", b1, b1.ToString());

        var ran = new List<string>();
        var failures = new List<string>();
        void OnFailure(string probe, Exception e) => failures.Add(probe);
        bool hasEntries = false, target = true, queue = true;
        try
        {
            hasEntries = ConflictProbeFailure.Run("HasEntries", () => { ran.Add("HasEntries"); return true; }, OnFailure);
            target = ConflictProbeFailure.Run("AutoTarget", () => { ran.Add("AutoTarget"); throw new NullReferenceException(); }, OnFailure);
            queue = ConflictProbeFailure.Run("ManualQueue", () => { ran.Add("ManualQueue"); return false; }, OnFailure);
        }
        catch (Exception e)
        {
            failures.Add("escaped:" + e.GetType().Name);
        }
        check("runner: the probe after a throwing probe still runs", ran.SequenceEqual(["HasEntries", "AutoTarget", "ManualQueue"]), string.Join(",", ran));
        check("runner: a throwing probe reads false and is reported by name",
            hasEntries && !target && !queue && failures.SequenceEqual(["AutoTarget"]), string.Join(",", failures));
    }

    // -----------------------------------------------------------------------------------------------------------
    // 7. Wiring (source text of the shipped check): HasEntries runs before any settings probe, and every
    //    BossModCheck probe goes through the runner.
    // -----------------------------------------------------------------------------------------------------------
    private static void CheckOrder(Action<string, bool, string> check)
    {
        var src = RepoFile("Data", "Conflicts", "ConflictingPluginsChecks.cs");
        var body = Between(src, "public override void CheckForConflict(bool forceRefresh = false)", "internal sealed class MOActionCheck");
        var hasEntries = body.IndexOf("Probe(\"HasEntries\"", StringComparison.Ordinal);
        var firstSetting = new[] { "Probe(\"AutoTarget\"", "Probe(\"SmartTargeting\"", "Probe(\"ManualQueue\"", "Probe(\"Autorotation\"", "Probe(\"AITargeting\"" }
            .Select(p => body.IndexOf(p, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        check("wiring: HasEntries is probed first", hasEntries >= 0 && firstSetting > hasEntries, $"HasEntries@{hasEntries} first setting@{firstSetting}");
        check("wiring: all five settings probes go through the runner",
            new[] { "AutoTarget", "SmartTargeting", "ManualQueue", "Autorotation", "AITargeting" }
                .All(p => body.Contains($"Probe(\"{p}\"", StringComparison.Ordinal)), "missing Probe(...) call");
    }

    private static string Between(string s, string start, string end)
    {
        var i = s.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return "";
        var j = s.IndexOf(end, i, StringComparison.Ordinal);
        return j < 0 ? s[i..] : s[i..j];
    }

    private static string RepoFile(params string[] rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "check-preset-ids.py")))
            dir = dir.Parent;
        return dir is null
            ? ""
            : File.ReadAllText(Path.Combine([dir.FullName, "src", "GluttonyCombo", "GluttonyCombo", .. rel]));
    }

    // -----------------------------------------------------------------------------------------------------------
    // The pre-fix walk, transcribed from BossMod.cs @575821102 (reborn branch only; logging removed). GetFoP is
    // ECommons ReflectionHelper.GetFoP (fields then properties, every binding flag, up the base types).
    // -----------------------------------------------------------------------------------------------------------
    private static class PreFix
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static object? GetFoP(object? obj, string name)
        {
            if (obj is null) return null;
            for (var type = obj.GetType(); type is not null; type = type.BaseType)
            {
                if (type.GetField(name, All) is { } fi) return fi.GetValue(obj);
                if (type.GetProperty(name, All) is { } pi) return pi.GetValue(obj);
            }
            return null;
        }

        private static IEnumerable<object?> GetPresets(object plugin)
        {
            var rotationManager = GetFoP(plugin, "_rotation");
            if (rotationManager is null) yield break;
            yield return GetFoP(rotationManager, "Preset");
            var presets = GetFoP(rotationManager, "ActiveModules");
            if (presets is null) yield break;
            var count = (int)presets.GetType().GetProperty("Count")!.GetValue(presets)!;
            for (var i = 0; i < count; i++)
                yield return presets.GetType().GetProperty("Item")!.GetValue(presets, [i]);
        }

        internal static bool IsAutoTargetingEnabled(object plugin)
        {
            foreach (var preset in GetPresets(plugin))
            {
                if ((string?)GetFoP(preset, "Name") == "VBM Multibox")
                    continue;
                var modules = GetFoP(preset, "Module");
                if (modules is null)
                    continue;
                var moduleType = modules.GetType().GetProperty("Type")!.GetValue(modules);
                if (moduleType is Type t && t.FullName!.Contains("AutoTarget"))
                    return true;
            }
            return false;
        }
    }
}

/// <summary> Minimal stand-ins with stock BMR's member names and shapes (origin/main be8ff771c). </summary>
internal static class StockShape
{
    public abstract class RotationModule; // Autorotation/RotationModule.cs: no "Type" member

    public sealed class AutoTarget : RotationModule;

    public sealed record class ModuleSettings(Type Type); // Preset.ModuleSettings(Type Type, Definition, Builder)

    public sealed record class Preset(string Name) // Preset.cs:9-34
    {
        public string Name = Name;
        public List<ModuleSettings> Modules = [];
    }

    public sealed class RotationModuleManager // RotationModuleManager.cs:16,19,41
    {
        private readonly record struct ActiveModule(int DataIndex, object Definition, RotationModule Module);

        public Preset? Preset { get; set; }
        private List<ActiveModule>? ActiveModules;

        /// <summary> What Update does on the first tick with a preset: one ActiveModule per preset module. </summary>
        public void BuildActiveModules()
        {
            ActiveModules = [];
            for (var i = 0; i < (Preset?.Modules.Count ?? 0); i++)
                ActiveModules.Add(new ActiveModule(i, new object(), (RotationModule)Activator.CreateInstance(Preset!.Modules[i].Type)!));
        }
    }

    public sealed class Plugin(RotationModuleManager rotation) // Framework/Plugin.cs:34
    {
        private readonly RotationModuleManager _rotation = rotation;
    }
}
