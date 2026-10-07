using System.Buffers;
using System.IO.Compression;
using System.Text;

namespace ArmoireAutoFill.Data.Shopping;

// Encoder for Knightshopper's "KS1:" share strings (shopping-list import format).
//
// Format (reverse-engineered from Knightshopper 1.0.1.6, validated against its own
// encode/decode pair):
//   "KS1:" + base64url( Brotli( UTF8( compact JSON ) ) )
//   JSON = [ currencyId(int), listName(string, <=100 chars after trim),
//            [ [itemId, vendorId, shopId, quantity, subCurrency], ... ] ]
// with at most 500 items. Knightshopper's import decodes this, validates every
// (itemId, vendorId, shopId, subCurrency) quadruple against its shop catalog, then
// creates a NEW shopping list and selects it, leaving existing lists untouched.
//
// Pure file: no Dalamud types (also compiled by the offline harness).
public static class KnightshopperShare
{
    public const string Prefix = "KS1:";
    public const int MaxItems = 500;
    public const int MaxNameLength = 100;
    public const int MaxShareLength = 65536;

    // Known-good sanity limits taken from Knightshopper's decoder.
    public static bool IsValidCurrencyId(int currencyId) => currencyId is >= 0 and <= 10;

    // Encodes one per-currency import payload.
    public static string Encode(byte currencyId, string listName, IReadOnlyList<Ks1Item> items)
    {
        ArgumentNullException.ThrowIfNull(listName);
        if (!IsValidCurrencyId(currencyId))
            throw new ArgumentOutOfRangeException(nameof(currencyId), currencyId, "unknown Knightshopper currency id");
        if (listName.Length > MaxNameLength)
            throw new ArgumentException($"list name longer than {MaxNameLength} characters", nameof(listName));
        if (items.Count > MaxItems)
            throw new ArgumentException($"more than {MaxItems} items", nameof(items));

        var json = BuildJson(currencyId, listName, items);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(bytes);
        }

        var encoded = Convert.ToBase64String(compressed.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var share = Prefix + encoded;
        if (share.Length > MaxShareLength)
            throw new InvalidOperationException($"encoded share string exceeds {MaxShareLength} characters");
        return share;
    }

    // Mirrors Knightshopper's decoder for offline round-trip testing. Not used in-game.
    public static bool TryDecode(string share, out byte currencyId, out string listName,
        out IReadOnlyList<Ks1Item> items, out string error)
    {
        currencyId = 0;
        listName = string.Empty;
        items = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(share) || !share.TrimStart().StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "not a KS1 share string";
            return false;
        }

        var trimmed = share.Trim();
        if (trimmed.Length > MaxShareLength)
        {
            error = "share string too large";
            return false;
        }

        var body = trimmed[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        var padded = (body.Length % 4) switch
        {
            0 => body,
            2 => body + "==",
            3 => body + "=",
            _ => null,
        };
        if (padded == null)
        {
            error = "malformed base64";
            return false;
        }

        byte[] compressedBytes;
        try
        {
            compressedBytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            error = "malformed base64";
            return false;
        }

        byte[] json;
        try
        {
            using var decompressed = new MemoryStream();
            using (var input = new MemoryStream(compressedBytes))
            using (var brotli = new BrotliStream(input, CompressionMode.Decompress))
            {
                brotli.CopyTo(decompressed, 4096);
            }
            json = decompressed.ToArray();
        }
        catch (IOException)
        {
            error = "malformed brotli stream";
            return false;
        }

        return TryParseJson(json, ref currencyId, ref listName, ref items, ref error);
    }

    private static bool TryParseJson(byte[] json, ref byte currencyId, ref string listName,
        ref IReadOnlyList<Ks1Item> items, ref string error)
    {
        // Hand-rolled strict parser for the fixed shape [int, string, [[int x5], ...]].
        var p = new JsonScanner(json);
        if (!p.Expect('[')) { error = "bad json"; return false; }
        if (!p.ReadInt(out var cid)) { error = "bad json"; return false; }
        if (!IsValidCurrencyId(cid)) { error = "unknown currency id"; return false; }
        if (!p.Expect(',')) { error = "bad json"; return false; }
        if (!p.ReadString(out var name)) { error = "bad json"; return false; }
        if (!p.Expect(',')) { error = "bad json"; return false; }
        if (!p.Expect('[')) { error = "bad json"; return false; }

        var list = new List<Ks1Item>();
        var seenKeys = new HashSet<(uint, uint)>();
        while (p.Peek() != ']')
        {
            if (!p.Expect('[')) { error = "bad json"; return false; }
            if (!p.ReadInt(out var itemId) || !p.Expect(',')
                || !p.ReadInt(out var vendorId) || !p.Expect(',')
                || !p.ReadInt(out var shopId) || !p.Expect(',')
                || !p.ReadInt(out var quantity) || !p.Expect(',')
                || !p.ReadInt(out var subCurrency) || !p.Expect(']'))
            {
                error = "bad json";
                return false;
            }

            if (itemId == 0 || quantity == 0 || subCurrency < -1)
            {
                error = "invalid item tuple";
                return false;
            }

            var key = ((uint)itemId, (uint)(subCurrency + 1));
            if (!seenKeys.Add(key))
            {
                error = "duplicate item key";
                return false;
            }

            list.Add(new Ks1Item((uint)itemId, (uint)vendorId, (uint)shopId, quantity, subCurrency));
            if (p.Peek() == ',') { p.Advance(); continue; }
            break;
        }

        if (!p.Expect(']') || !p.Expect(']') || !p.AtEnd()) { error = "bad json"; return false; }
        if (list.Count > MaxItems) { error = "too many items"; return false; }
        if (name.Trim().Length > MaxNameLength) { error = "list name too long"; return false; }

        currencyId = (byte)cid;
        listName = name;
        items = list;
        return true;
    }

    // Compact JSON identical in shape to Newtonsoft's JArray.ToString(Formatting.None)
    // for arrays of integers and plain strings: no spaces, minimal escaping.
    private static string BuildJson(byte currencyId, string listName, IReadOnlyList<Ks1Item> items)
    {
        var sb = new StringBuilder(64 + items.Count * 48);
        sb.Append('[').Append(currencyId).Append(',').Append(JsonString(listName)).Append(",[");
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (i > 0) sb.Append(',');
            sb.Append('[')
                .Append(it.ItemId).Append(',')
                .Append(it.VendorId).Append(',')
                .Append(it.ShopId).Append(',')
                .Append(it.Quantity).Append(',')
                .Append(it.SubCurrency)
                .Append(']');
        }
        sb.Append("]]");
        return sb.ToString();
    }

    private static string JsonString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private ref struct JsonScanner
    {
        private readonly byte[] _data;
        private int _pos;

        public JsonScanner(byte[] data)
        {
            _data = data;
            _pos = 0;
        }

        public bool AtEnd() => _pos >= _data.Length;

        public char Peek() => _pos < _data.Length ? (char)_data[_pos] : '\0';

        public void Advance() => _pos++;

        public bool Expect(char c)
        {
            SkipWhitespace();
            if (_pos < _data.Length && _data[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        public bool ReadInt(out int value)
        {
            SkipWhitespace();
            value = 0;
            var start = _pos;
            var negative = false;
            if (_pos < _data.Length && _data[_pos] == '-')
            {
                negative = true;
                _pos++;
            }
            while (_pos < _data.Length && _data[_pos] >= '0' && _data[_pos] <= '9')
            {
                value = value * 10 + (_data[_pos] - '0');
                _pos++;
            }
            if (_pos == start) return false;
            if (negative) value = -value;
            return true;
        }

        public bool ReadString(out string value)
        {
            SkipWhitespace();
            value = string.Empty;
            if (_pos >= _data.Length || _data[_pos] != '"') return false;
            _pos++;
            var sb = new StringBuilder();
            while (_pos < _data.Length)
            {
                var c = (char)_data[_pos];
                if (c == '"')
                {
                    _pos++;
                    value = sb.ToString();
                    return true;
                }
                if (c == '\\' && _pos + 1 < _data.Length)
                {
                    _pos++;
                    var esc = (char)_data[_pos];
                    sb.Append(esc switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => esc,
                    });
                    _pos++;
                    continue;
                }
                sb.Append(c);
                _pos++;
            }
            return false;
        }

        private void SkipWhitespace()
        {
            while (_pos < _data.Length && _data[_pos] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r')
                _pos++;
        }
    }
}

public readonly record struct Ks1Item(uint ItemId, uint VendorId, uint ShopId, int Quantity, int SubCurrency);
