using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PunishLib.Configuration
{
    internal class SharedConfig
    {
        internal static string FileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PunishConfig");
        internal static string FilePath => Path.Combine(FileDir, "PunishSharedConfig.json");
        internal SharedConfigStorage Storage;
        internal SharedConfig() 
        {
            Directory.CreateDirectory(FileDir);
            Storage = EzConfig.LoadConfiguration<SharedConfigStorage>(FilePath, false);
            foreach (var x in Storage.Bools) PunishLibMain.PluginInterface.GetOrCreateData<List<bool>>($"PunishLib.{(int)x.Key}", () => new() { x.Value });
            foreach (var x in Storage.Strings) PunishLibMain.PluginInterface.GetOrCreateData<List<string>>($"PunishLib.{(int)x.Key}", () => new() { x.Value });
        }

        internal string APIKey
        {
            get => Get(SharedConfigKey.APIKey, "");
            set => Set(SharedConfigKey.APIKey, value);
        }

        internal string FFXIVGameVersion
        {
            get => Get(SharedConfigKey.FFXIVGameVersion, "");
            set => Set(SharedConfigKey.FFXIVGameVersion, value);
        }

        internal bool UnsupportedConfiguration
        {
            get => Get(SharedConfigKey.UnsupportedConfiguration, false);
            set => Set(SharedConfigKey.UnsupportedConfiguration, value);
        }

        internal string InstalledPlugins
        {
            get => Get(SharedConfigKey.InstalledPlugins, "");
            set => Set(SharedConfigKey.InstalledPlugins, value);
        }
        internal string ClientLanguage
        {
            get => Get(SharedConfigKey.ClientLanguage, "");
            set => Set(SharedConfigKey.ClientLanguage, value);
        }

        T Get<T>(SharedConfigKey key, T defaultValue = default)
        {
            var data = PunishLibMain.PluginInterface.GetOrCreateData<List<T>>($"PunishLib.{(int)key}", () => new() { defaultValue });
            return data[0];
        }

        void Set<T>(SharedConfigKey key, T value)
        {
            var data = PunishLibMain.PluginInterface.GetOrCreateData<List<T>>($"PunishLib.{(int)key}", () => new() { value });
            data[0] = value;
            if (value is bool b) Storage.Bools[(int)key] = b;
            if (value is string s) Storage.Strings[(int)key] = s;
            EzConfig.SaveConfiguration(Storage, FilePath, true, false);
        }
    }
}
