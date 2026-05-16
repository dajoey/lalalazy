using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PunishLib
{
    public class PunishConfig
    {
        public string APIKey { get; set; }

        public string FFXIVGameVersion { get; set; }

        public bool UnsupportedConfiguration { get;set; }
    }

    public static class PunishConfigMethods
    {
        public static string SavedPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PunishConfig", "PunishConfig.json"); } }
        public static void Save(this PunishConfig config)
        {
            string output = JsonConvert.SerializeObject(config, new JsonSerializerSettings {  NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented });

            FileInfo fileInfo = new FileInfo(SavedPath);
            fileInfo.Directory.Create();
            File.WriteAllText(SavedPath, output);
        }

        public static PunishConfig Load()
        {
            if (File.Exists(SavedPath))
            {
                return JsonConvert.DeserializeObject<PunishConfig>(File.ReadAllText(SavedPath));
            }

            return new();
        }
    }
}
