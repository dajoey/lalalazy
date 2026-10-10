// Stand-ins for the Dalamud surface Configuration.cs touches, so the REAL config class compiles
// into this harness and round-trips through the REAL Newtonsoft package - the serializer Dalamud
// saves plugin configs with (the GluttonyCombo.ConfigMigrateHarness precedent). No Dalamud, no
// ECommons, no game.
//
// IPluginConfiguration is a marker interface. VirtualKey mirrors only the members the config
// persists, with their Win32 VK code values. The Plugin stub's SavePluginConfig is what Dalamud's
// own does with the config object: a Newtonsoft serialize.
namespace Dalamud.Configuration
{
  /// <summary>Marker interface every Dalamud plugin configuration carries.</summary>
  public interface IPluginConfiguration
  {
  }
}

namespace Dalamud.Game.ClientState.Keys
{
  /// <summary>The virtual-key members LazyMarketCompanion's configuration persists (Win32 VK codes).</summary>
  public enum VirtualKey
  {
    SHIFT = 0x10,
    Q = 0x51,
  }
}

namespace LazyMarketCompanion
{
  using Newtonsoft.Json;

  /// <summary>
  /// Offline stand-in for the plugin entry point: <see cref="Configuration.Save"/> reaches
  /// Dalamud's SavePluginConfig, which serializes the config object with Newtonsoft - exactly
  /// what this stub does, so the harness round trip exercises the same shape the game writes.
  /// </summary>
  public static class Plugin
  {
    public static readonly StubPluginInterface PluginInterface = new();

    public sealed class StubPluginInterface
    {
      public string? LastConfigJson { get; private set; }

      public void SavePluginConfig(Configuration config)
        => LastConfigJson = JsonConvert.SerializeObject(config);
    }
  }
}
