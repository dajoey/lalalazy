// Harness-only stand-ins for the Dalamud surface that Configuration.cs touches. Save() is never
// executed; the stubs exist so the real Configuration.cs compiles offline (no Dalamud SDK here).
namespace Dalamud.Configuration {
    public interface IPluginConfiguration {
        int Version { get; set; }
    }
}

namespace Dalamud.Plugin {
    public interface IDalamudPluginInterface {
        void SavePluginConfig(object config);
    }
}

namespace LazyFateAutomation {
    public static class Svc {
        public static Dalamud.Plugin.IDalamudPluginInterface PluginInterface = null!;
    }
}
