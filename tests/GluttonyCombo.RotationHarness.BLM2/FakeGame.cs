// FakeGame.cs — the mutable game-state knobs for the BLM2 (UseManaward) harness. Defaults make a
// clean solo PotD-style state: no behemoth state, player at full HP, Manaward ready, no shield up.

namespace GluttonyCombo.RotationHarness.BLM2;

internal static class FakeGame
{
    // ---- player ----
    public static float HealthPercent = 100f;
    public static bool HasShield = false;

    // ---- party / content ----
    public static bool InParty = false;
    public static float PartyTimeSinceFirestarter = 0f;

    // ---- incoming damage / PotD behemoth state (the fake of DeepDungeon_Behemoth.cs) ----
    public static bool GroupDamageIncoming = false;
    public static bool BehemothMeteorWindow = false;
    public static bool BehemothMeteorHold = false;

    // ---- presets / cooldowns ----
    public static HashSet<GluttonyCombo.Combos.Preset> EnabledPresets = new();
    public static HashSet<uint> OnCooldown = new();

    public static void Reset()
    {
        HealthPercent = 100f;
        HasShield = false;
        InParty = false;
        PartyTimeSinceFirestarter = 0f;
        GroupDamageIncoming = false;
        BehemothMeteorWindow = false;
        BehemothMeteorHold = false;
        EnabledPresets = new();
        OnCooldown = new();
    }
}
