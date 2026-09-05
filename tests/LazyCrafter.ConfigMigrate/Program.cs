// Config-migration regression proof for the DagobertAfterCraft -> PriceMatchAfterCraft rename (card t_89a7ebec).
// Compiles the REAL Configuration.cs against the stubs at the bottom of this file and asserts a pre-rename
// saved config survives the round trip. Exit 0 = all cases pass; any failure prints FAIL and exits 1.
using LazyCrafter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

int failures = 0;
void Check(string what, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}{(detail.Length > 0 ? "  [{detail}]" : "")}");
    if (!ok) failures++;
}

// Case 1: a v4 config from a real install - old key present and TRUE, plus unrelated keys untouched.
var oldJson = """
{
  "Version": 4,
  "EnabledSources": {
    "Bags": true, "ArmouryChest": true, "Saddlebag": true, "Retainers": true,
    "AltCharacters": true, "FCChest": false, "GlamourDresser": true
  },
  "PriceCacheMinutes": 10,
  "PriceByWorld": false,
  "RevenueBasis": 0,
  "ShowAboveLevel": false,
  "UndersuppliedMinVelocity": 3.0,
  "UndersuppliedMaxListings": 2,
  "DagobertAfterCraft": true,
  "VnavWalkToVendor": false,
  "RetrieveFromRetainers": true,
  "Cart": [
    { "RecipeId": 3762, "Crafts": 2 }
  ]
}
""";
var cfg = JsonConvert.DeserializeObject<Configuration>(oldJson)!;
Check("v4 config deserializes", cfg is not null);
Check("Version field read", cfg.Version == 4, $"got {cfg.Version}");
// The critical assertion: the old key landed in the legacy shadow, not lost.
Check("old DagobertAfterCraft=true captured by legacy shadow", cfg.DagobertAfterCraftLegacy == true, $"shadow={cfg.DagobertAfterCraftLegacy}");

cfg.MigrateIfNeeded();
Check("migrated to v5", cfg.Version == Configuration.CurrentVersion && cfg.Version == 5);
Check("value survived rename: PriceMatchAfterCraft=true", cfg.PriceMatchAfterCraft, $"got {cfg.PriceMatchAfterCraft}");
Check("cart survived migration", cfg.Cart is { Count: 1 } && cfg.Cart[0].RecipeId == 3762 && cfg.Cart[0].Crafts == 2);
Check("unrelated settings survived migration", cfg.PriceCacheMinutes == 10 && cfg.RetrieveFromRetainers && cfg.Cart.Count == 1);

{
    var c2 = JsonConvert.DeserializeObject<Configuration>(oldJson)!;
    c2.MigrateIfNeeded();
    var before = c2.PriceMatchAfterCraft;
    c2.MigrateIfNeeded();
    Check("migration idempotent (second call is a no-op)", before == c2.PriceMatchAfterCraft && c2.Version == 5);
}

// Round-trip: the save path Dalamud runs (SerializeObject with defaults) must drop the old key (it is null)
// and write the new one; reloading THAT must keep the value - i.e. no save/load drift for existing users.
var saved = JsonConvert.SerializeObject(cfg, Formatting.Indented);
Check("resaved config writes the new key", saved.Contains("\"PriceMatchAfterCraft\": true"));
Check("resaved config drops the old key", !saved.Contains("DagobertAfterCraft"), "legacy shadow null + NullValueHandling.Ignore");
var reloaded = JsonConvert.DeserializeObject<Configuration>(saved)!;
Check("resaved config reloads with value intact", reloaded.PriceMatchAfterCraft);

// Case 2: a v4 config with the old key FALSE also survives (a false value is data, not an absence).
var oldFalse = oldJson.Replace("\"DagobertAfterCraft\": true", "\"DagobertAfterCraft\": false");
var cfgFalse = JsonConvert.DeserializeObject<Configuration>(oldFalse)!;
cfgFalse.MigrateIfNeeded();
Check("old key false survives too", !cfgFalse.PriceMatchAfterCraft, $"got {cfgFalse.PriceMatchAfterCraft}");

// Case 3 (negative control): WITHOUT the legacy shadow the value is lost - proves the shadow is load-bearing.
{
    var nc = JsonConvert.DeserializeObject<Configuration>(oldJson)!;
    nc.GetType().GetProperty("DagobertAfterCraftLegacy")!.SetValue(nc, null); // simulate the no-shadow world
    nc.MigrateIfNeeded();
    Check("negative control: without the shadow the value WOULD be lost (stays default false)", !nc.PriceMatchAfterCraft,
        "this is the regression the shadow prevents");
}

// Case 4: brand-new config (no old key anywhere) - fresh install path.
var fresh = new Configuration();
fresh.MigrateIfNeeded();
Check("fresh config is v5 with default off", fresh.Version == 5 && !fresh.PriceMatchAfterCraft);
Check("fresh config serializes with no old key", !JsonConvert.SerializeObject(fresh, Formatting.Indented).Contains("DagobertAfterCraft"));

// Case 5: v5 config saved by the new build reloaded directly (post-migration steady state).
var v5 = JsonConvert.DeserializeObject<Configuration>(saved)!;
v5.MigrateIfNeeded();
Check("steady-state v5 reload keeps value without remigrating", v5.Version == 5 && v5.PriceMatchAfterCraft);

Console.WriteLine(failures == 0 ? "OK - all config migration cases passed" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

// ---- stubs mirroring what Configuration.cs references outside System + Newtonsoft ----
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace LazyCrafter.Adapters
{
    // Stub of src/LazyCrafter/Adapters/InventorySource.cs - the real bodies are irrelevant to JSON behavior.
    public enum InventorySource { Bags, ArmouryChest, Saddlebag, Retainers, AltCharacters, FCChest, GlamourDresser }
    public static class InventorySources
    {
        public static Dictionary<string, bool> Defaults() => new()
        {
            ["Bags"] = true, ["ArmouryChest"] = true, ["Saddlebag"] = true, ["Retainers"] = true,
            ["AltCharacters"] = true, ["FCChest"] = false, ["GlamourDresser"] = true,
        };
        public static bool DefaultFor(InventorySource s) => s != InventorySource.FCChest;
    }
}

namespace LazyCrafter.Core.Model
{
    public enum RevenueBasis { MinListing, MedianListing, AverageSale }
}
