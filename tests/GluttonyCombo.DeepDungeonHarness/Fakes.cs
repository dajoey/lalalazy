// Fakes.cs — the harness-owned stand-ins for everything the REAL DeepDungeon module files
// (DeepDungeon.cs, DeepDungeon_Config.cs, DeepDungeon_Behemoth.cs — compiled unchanged) reach
// outside themselves. The boundary:
//   REAL  : UseSustainingPotion's gate logic, the Config defaults/Draw wiring, and the whole
//           behemoth meteor predicate (NameId match, cast-id match, HP thresholds, hold band).
//   FAKE  : the game + Dalamud boundary those decisions READ through — CustomComboFunctions
//           (target as a FakeBattleChara, player HP, statuses), the Preset enum slice, the
//           UserInt config storage (FakeGame.ConfigInts), ALL.Items (ActionManager item checks),
//           the UserConfig slider drawer + localization strings (compile-only, never called),
//           and the missing DeepDungeon_Helper.cs half (consts/Buffs/Pomanders + stub pomander
//           overloads, values mirrored from DeepDungeon_Helper.cs:22-33/41/90).
// The fake layer mirrors real accessibility and signatures so the module files compile byte-identical.

// ======================================================================================
namespace GluttonyCombo.Combos
{
    /// <summary>
    ///     FAKE slice of the real CustomComboPreset enum: only the DeepDungeon members the compiled
    ///     module files reference. PoTD_SustainingPotion mirrors the production value
    ///     (CustomComboPreset.cs:1248); the others are placeholders (values never matter to
    ///     decision logic — presets are compared by identity / looked up in the enabled set).
    /// </summary>
    public enum Preset
    {
        PoTD_SustainingPotion = 110500,
        HoH_EmpyreanPotion = 110501,
        EO_OrthosPotion = 110502,
        PT_PilgrimsPotion = 110503,
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.ALL
{
    /// <summary>
    ///     FAKE of ALL/Items.cs: the two members the module touches. ItemReady mirrors the real
    ///     ActionManager check shape (Items.cs:51) as a set membership test; UseItem is the
    ///     identity the real proxy returns through for (never asserted on).
    /// </summary>
    internal class Items
    {
        public static bool ItemReady(uint itemId) => GluttonyCombo.DeepDungeonHarness.FakeGame.ReadyItems.Contains(itemId);

        public static uint UseItem(uint item) => item;
    }
}

// ======================================================================================
namespace GluttonyCombo.Combos.PvE.Content.DeepDungeons
{
    /// <summary>
    ///     The missing half of the partial DeepDungeons class: DeepDungeon_Helper.cs is NOT compiled
    ///     into this harness (its pomander paths read EventFramework/ActionManager, un-fakeable
    ///     offline), so this half supplies the consts, Buffs/Debuffs and Pomanders the compiled
    ///     files reference — values mirrored from DeepDungeon_Helper.cs:22-33/41/49 — plus no-op
    ///     pomander overloads (UsePomander(out Pomanders)'s inner call and TryGetDDAction's proxy).
    ///     The pomander decision paths are out of this harness's boundary.
    /// </summary>
    internal static partial class DeepDungeons
    {
        public const uint
            PoTDSustainingPotion = 20309, //Item, not an action, PoTD (mirrored DeepDungeon_Helper.cs:23)
            HoHEmpyreanPotion = 23163, //Item, not an action, HoH
            OrthosPotion = 38944, //Item, not an action, Eureka Orthos
            PilgrimsPotion = 47102; //Item, not an action, Pilgrim's Traverse

        public static class Buffs
        {
            public const uint
                Rehabilitation = 648,
                DamageUp = 687;
        }

        public static class Debuffs
        {
            public const uint
                ItemPenalty = 1094;
        }

        public enum Pomanders
        {
            PomanderOfSafety = 1,
        }

        public static uint UsePomander(Pomanders pomander) => 0;

        public static uint UsePomander(uint pomander) => 0;
    }
}

// ======================================================================================
namespace GluttonyCombo.CustomComboNS.Functions
{
    /// <summary>Fake player carrier: HasStatus reads FakeGame.Statuses.</summary>
    public class FakePlayer
    {
        public bool HasStatus(uint id, bool anyOwner = false) => GluttonyCombo.DeepDungeonHarness.FakeGame.Statuses.Contains(id);
    }

    /// <summary>
    ///     A fake enemy battle character over harness knobs: implements the REAL Dalamud
    ///     IBattleChara surface (member list reflected from the RotationHarness.SGE fake) so the
    ///     real predicate can pattern-match and read NameId / CastActionId / HP directly.
    /// </summary>
    internal sealed class FakeBattleChara : Dalamud.Game.ClientState.Objects.Types.IBattleChara
    {
        internal static FakeBattleChara Instance { get; } = new();

        // IGameObject
        public Dalamud.Game.Text.SeStringHandling.SeString Name => "FakeBehemoth";
        public ulong GameObjectId => 2;
        public uint EntityId => 2;
        public uint DataId => 0x1001;
        public uint BaseId => 0x1001;
        public uint OwnerId => 0;
        public ushort ObjectIndex => 2;
        public Dalamud.Game.ClientState.Objects.Enums.ObjectKind ObjectKind =>
            Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc;
        public byte SubKind => 5;
        public byte YalmDistanceX => 3;
        public byte YalmDistanceZ => 0;
        public byte CurrentDistance => 3;
        public byte NextDistance => 3;
        public bool IsDead => false;
        public bool IsTargetable => true;
        public System.Numerics.Vector3 Position => new(0f, 0f, -3f);
        public float Rotation => 0f;
        public float HitboxRadius => 2f;
        public ulong TargetObjectId => 0;
        public Dalamud.Game.ClientState.Objects.Types.IGameObject? TargetObject => null;
        public nint Address => (nint)0x2000_0000;
        public bool IsValid() => true;
        public bool Equals(Dalamud.Game.ClientState.Objects.Types.IGameObject? other) => other == this;

        // ICharacter: HP over harness knobs (the predicate reads the percent through
        // CustomComboFunctions.GetTargetHPPercent, which computes from these same knobs).
        public uint CurrentHp => GluttonyCombo.DeepDungeonHarness.FakeGame.TargetCurrentHp;
        public uint MaxHp => GluttonyCombo.DeepDungeonHarness.FakeGame.TargetMaxHp;
        public uint CurrentMp => 10_000;
        public uint MaxMp => 10_000;
        public uint CurrentGp => 0;
        public uint MaxGp => 0;
        public uint CurrentCp => 0;
        public uint MaxCp => 0;
        public byte ShieldPercentage => 0;
        public Lumina.Excel.RowRef<Lumina.Excel.Sheets.ClassJob> ClassJob => default;
        public byte Level => 80;
        public Span<byte> Customize => Span<byte>.Empty;
        public Dalamud.Game.ClientState.Customize.ICustomizeData? CustomizeData => null;
        public Dalamud.Game.Text.SeStringHandling.SeString CompanyTag => "";
        public uint NameId => GluttonyCombo.DeepDungeonHarness.FakeGame.TargetNameId;
        public Lumina.Excel.RowRef<Lumina.Excel.Sheets.OnlineStatus> OnlineStatus => default;
        public Dalamud.Game.ClientState.Objects.Enums.StatusFlags StatusFlags => default;
        public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Mount>? CurrentMount => null;
        public Lumina.Excel.RowRef<Lumina.Excel.Sheets.Companion>? CurrentMinion => null;

        // IBattleChara: cast state over harness knobs.
        public Dalamud.Game.ClientState.Statuses.StatusList? StatusList =>
            throw new NotSupportedException("harness boundary: statuses go through the FakeGame knobs");
        public bool IsCasting => GluttonyCombo.DeepDungeonHarness.FakeGame.TargetCastActionId != 0;
        public bool IsCastInterruptible => false;
        public byte CastActionType => 0;
        public uint CastActionId => GluttonyCombo.DeepDungeonHarness.FakeGame.TargetCastActionId;
        public ulong CastTargetObjectId => 0;
        public float CurrentCastTime => IsCasting ? 3f : 0f;
        public float BaseCastTime => IsCasting ? 7f : 0f;
        public float TotalCastTime => BaseCastTime;
    }

    /// <summary>
    ///     FAKE of the CustomComboFunctions surface the compiled module files read. Every member
    ///     routes into <see cref="GluttonyCombo.DeepDungeonHarness.FakeGame"/>.
    /// </summary>
    internal abstract partial class CustomComboFunctions
    {
        // ---- enabled presets / config ----
        public static bool IsEnabled(Combos.Preset preset) => GluttonyCombo.DeepDungeonHarness.FakeGame.EnabledPresets.Contains(preset);

        public static bool IsNotEnabled(Combos.Preset preset) => !IsEnabled(preset);

        // ---- player / status / target ----
        public static FakePlayer LocalPlayer { get; } = new();

        public static Dalamud.Game.ClientState.Objects.Types.IBattleChara? CurrentTarget =>
            GluttonyCombo.DeepDungeonHarness.FakeGame.TargetPresent
                ? FakeBattleChara.Instance
                : null;

        public static bool HasStatusEffect(uint id, bool anyOwner = false) => LocalPlayer.HasStatus(id, anyOwner);

        public static float PlayerHealthPercentageHp() => GluttonyCombo.DeepDungeonHarness.FakeGame.PlayerHPPercent;

        public static float GetTargetHPPercent(Dalamud.Game.ClientState.Objects.Types.IGameObject? optionalTarget = null, bool includeShield = false, bool forceUsePending = false)
        {
            // mirrors Target.cs:286-291 on the fake chara (no shield, no pending-state override)
            if ((optionalTarget ?? CurrentTarget) is not Dalamud.Game.ClientState.Objects.Types.IBattleChara chara)
                return 0f;
            return chara.CurrentHp * 100f / chara.MaxHp;
        }

        public static uint GetTargetMaxHP(Dalamud.Game.ClientState.Objects.Types.IGameObject? optionalTarget = null) =>
            (optionalTarget ?? CurrentTarget) is Dalamud.Game.ClientState.Objects.Types.IBattleChara chara ? chara.MaxHp : 0;

        public static bool InCombat() => true;
    }

    /// <summary>FAKE of UserData/UserInt (Config.cs): storage in FakeGame.ConfigInts; implicit string kept.</summary>
    internal abstract class UserData(string configName)
    {
        public string ConfigName = configName;

        public static implicit operator string(UserData o) => o.ConfigName;

        public abstract void ResetToDefault();
    }

    internal class UserInt(string configName, int defaults = 0) : UserData(configName)
    {
        public int Default = defaults;

        public static implicit operator int(UserInt o) => GluttonyCombo.DeepDungeonHarness.FakeGame.GetInt(o.ConfigName, o.Default);

        public override void ResetToDefault() { }
    }
}

// ======================================================================================
namespace GluttonyCombo.Extensions
{
    // DeepDungeon.cs carries `using GluttonyCombo.Extensions;`; the namespace must exist for the
    // compile even though the compiled files use none of its members.
}

// ======================================================================================
namespace GluttonyCombo.Window.Functions
{
    /// <summary>FAKE of Window/Functions/UserConfig.cs: the slider drawer the Config file compiles against. Never called.</summary>
    internal static class UserConfig
    {
        public static bool DrawSliderInt(int minValue, int maxValue, string config, string sliderDescription,
            float itemWidth = 150, uint sliderIncrement = 1, bool hasAdditionalChoice = false,
            string additonalChoiceCondition = "", int occurrence = 0) => true;
    }
}

// ======================================================================================
namespace GluttonyCombo.Resources.Localization.JobConfigs
{
    /// <summary>FAKE of Generics.Designer.cs: the one localization string the Config file references.</summary>
    internal class Generics
    {
        internal static string StopFriendlyHpPercent100 => "Set a HP% threshold for when the potion will be used.";
    }
}
