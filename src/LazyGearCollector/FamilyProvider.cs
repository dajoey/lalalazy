using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace LazyGearCollector;

/// <summary>
/// Builds a <see cref="GearCollection"/> for any "&lt;Family&gt; &lt;Piece&gt; of &lt;Role&gt; [+N]" equipment
/// family straight out of the Item sheet, then prices every tier by walking the <see cref="ShopGraph"/>.
///
/// Nothing here is specific to one patch's set - adding a future collection is one line in
/// <see cref="CollectionRegistry"/>, because the naming convention and the shop wiring are the same
/// every time Square Enix ships an upgradable field-operation set.
/// </summary>
public sealed class FamilyProvider
{
    // Applied to the name with the family prefix already stripped:
    // "Mask of Fending +2" -> piece "Mask", role "Fending", tier 2.
    // The family is matched by prefix rather than captured, because a lazy leading group would
    // split "Phantom Vision Mask" as family "Phantom" / piece "Vision Mask".
    private static readonly Regex NamePattern =
        new(@"^(?<piece>.+?) of (?<role>[A-Za-z]+)(?: \+(?<tier>[0-9]))?$",
            RegexOptions.Compiled);

    private static readonly Dictionary<uint, string> SlotNames = new()
    {
        [3] = "Head", [4] = "Body", [5] = "Hands", [6] = "Waist",
        [7] = "Legs", [8] = "Feet", [9] = "Ears", [10] = "Neck",
        [11] = "Wrists", [12] = "Ring",
    };

    private static readonly string[] SlotOrder =
        ["Head", "Body", "Hands", "Waist", "Legs", "Feet", "Ears", "Neck", "Wrists", "Ring"];

    private readonly IDataManager _data;
    private readonly ShopGraph _shops;

    public FamilyProvider(IDataManager data, ShopGraph shops)
    {
        _data = data;
        _shops = shops;
    }

    public GearCollection? Build(string id, string familyPrefix, string displayName, string sourceNote)
    {
        var sheet = _data.GetExcelSheet<Item>();
        if (sheet == null) return null;

        // 1. Collect every item in the family and slot it into (role, piece) -> tier -> item.
        var chains = new Dictionary<(string Role, string Piece), PieceChain>();
        var familyIds = new HashSet<uint>();

        foreach (var item in sheet)
        {
            string name;
            try { name = item.Name.ExtractText(); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Require the family prefix followed by a space, so "Bygone Brass" never swallows
            // "Bygone Brass Reproduction" style neighbours and "Augmented X" stays a separate family.
            if (name.Length <= familyPrefix.Length + 1) continue;
            if (!name.StartsWith(familyPrefix, StringComparison.Ordinal)) continue;
            if (name[familyPrefix.Length] != ' ') continue;

            var m = NamePattern.Match(name[(familyPrefix.Length + 1)..]);
            if (!m.Success) continue;

            var slot = item.EquipSlotCategory.RowId;
            if (slot == 0) continue;

            var role = m.Groups["role"].Value;
            var piece = m.Groups["piece"].Value;
            var tier = m.Groups["tier"].Success ? int.Parse(m.Groups["tier"].Value) : 0;

            var key = (role, piece);
            if (!chains.TryGetValue(key, out var chain))
            {
                chains[key] = chain = new PieceChain
                {
                    Role = role,
                    PieceName = piece,
                    EquipSlot = slot,
                    SlotName = SlotNames.TryGetValue(slot, out var sn) ? sn : $"Slot{slot}",
                };
            }

            if (chain.Tiers.Any(t => t.Tier == tier)) continue;
            chain.Tiers.Add(new TierNode { Tier = tier, ItemId = item.RowId, Name = name });
            familyIds.Add(item.RowId);
        }

        if (chains.Count == 0)
        {
            Plugin.PluginLog.Warning($"FamilyProvider: no items matched family '{familyPrefix}'");
            return null;
        }

        // 2. Price each tier from the shop graph and classify what each cost line means.
        var collection = new GearCollection
        {
            Id = id,
            DisplayName = displayName,
            SourceNote = sourceNote,
            Pieces = chains.Values.ToList(),
        };

        foreach (var chain in collection.Pieces)
        {
            chain.Tiers.Sort((a, b) => a.Tier.CompareTo(b.Tier));

            foreach (var tier in chain.Tiers)
            {
                var previous = chain.Tier(tier.Tier - 1);

                foreach (var offer in _shops.OffersFor(tier.ItemId))
                {
                    var path = new AcquisitionPath
                    {
                        ShopId = offer.ShopId,
                        ShopName = offer.ShopName,
                        Costs = offer.Costs.ToList(),
                    };

                    foreach (var cost in path.Costs)
                    {
                        if (previous != null && cost.ItemId == previous.ItemId)
                        {
                            path.UpgradeFromItemId = cost.ItemId;
                        }
                        else if (!familyIds.Contains(cost.ItemId) && IsEquipment(sheet, cost.ItemId))
                        {
                            // An equipment item from another family bought this one:
                            // a straight trade-up (e.g. Arcanaut's -> Phantom Vision).
                            path.ExchangeFromItemId = cost.ItemId;
                        }
                        else if (!familyIds.Contains(cost.ItemId) && !IsEquipment(sheet, cost.ItemId))
                        {
                            if (!collection.Currencies.Contains(cost.ItemId))
                                collection.Currencies.Add(cost.ItemId);
                        }
                    }

                    tier.Paths.Add(path);
                }
            }
        }

        collection.Pieces = collection.Pieces
            .OrderBy(p => p.Role, StringComparer.Ordinal)
            .ThenBy(p => Array.IndexOf(SlotOrder, p.SlotName))
            .ToList();

        Plugin.PluginLog.Info(
            $"FamilyProvider '{familyPrefix}': {collection.Pieces.Count} pieces, " +
            $"{collection.Pieces.Sum(p => p.Tiers.Count)} items, {collection.Roles.Count()} roles, " +
            $"{collection.Currencies.Count} currencies");

        return collection;
    }

    private static bool IsEquipment(Lumina.Excel.ExcelSheet<Item> sheet, uint itemId) =>
        sheet.TryGetRow(itemId, out var row) && row.EquipSlotCategory.RowId != 0;
}
