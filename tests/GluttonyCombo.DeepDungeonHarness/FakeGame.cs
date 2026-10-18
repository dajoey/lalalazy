// FakeGame.cs — the mutable game-state knobs for the DeepDungeon harness. Every member of the
// fake CustomComboFunctions surface routes here; defaults make a clean, on-target state with the
// target NOT a behemoth (NameId 0), nothing casting, the player at full HP, the PoTD potion
// enabled and ready, and no statuses.

using GluttonyCombo.Combos;

namespace GluttonyCombo.DeepDungeonHarness;

internal static class FakeGame
{
    // ---- target (the FakeBattleChara in Fakes.cs reads these) ----
    public static bool TargetPresent = true;
    public static uint TargetNameId;
    public static uint TargetCastActionId;
    public static uint TargetCurrentHp = 100_000;
    public static uint TargetMaxHp = 100_000;

    // ---- player ----
    public static float PlayerHPPercent = 100f;
    public static HashSet<uint> Statuses = new();

    // ---- presets / config ----
    public static HashSet<Preset> EnabledPresets = new();
    public static Dictionary<string, int> ConfigInts = new();

    // ---- items (fake ALL.Items.ItemReady checks this set) ----
    public static HashSet<uint> ReadyItems = new();

    public static int GetInt(string configName, int fallback) =>
        ConfigInts.TryGetValue(configName, out var v) ? v : fallback;

    // ---- state helpers ----
    public static void Reset()
    {
        TargetPresent = true;
        TargetNameId = 0;
        TargetCastActionId = 0;
        TargetCurrentHp = 100_000;
        TargetMaxHp = 100_000;
        PlayerHPPercent = 100f;
        Statuses = new();
        EnabledPresets = new();
        ConfigInts = new();
        ReadyItems = new();
    }

    /// <summary>Point the target at a fight state; hpPercent drives the target HP% directly.</summary>
    public static void SetBehemoth(uint nameId, uint castActionId, float hpPercent)
    {
        TargetPresent = true;
        TargetNameId = nameId;
        TargetCastActionId = castActionId;
        TargetMaxHp = 100_000;
        TargetCurrentHp = (uint)Math.Round(TargetMaxHp * hpPercent / 100f);
    }
}
