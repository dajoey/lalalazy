using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

// Offline extraction of Knightshopper 1.0.1.6's native vendor-discovery data.
// Ports exactly what the decompiled plugin does in mY5F8CrkyL0OCX:
//   load Knightshopper.Native.dll, ABI check (must be 15), init, then
//   discover(sqpackPath, language) and marshal the six result arrays.
// Struct layouts mirror the decompiled structs field-for-field (natural alignment).

unsafe
{
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: NativeDump <native dll> <sqpack dir> <out json> [language=2]");
    return 2;
}

var dllPath = Path.GetFullPath(args[0]);
var sqpackPath = Path.GetFullPath(args[1]);
var outPath = Path.GetFullPath(args[2]);
var language = args.Length > 3 ? int.Parse(args[3]) : 2;

Console.WriteLine($"dll={dllPath}");
Console.WriteLine($"sqpack={sqpackPath}");
Console.WriteLine($"lang={language}");

var h = NativeLibrary.Load(dllPath);
try
{
    uint abiVersion = ((delegate* unmanaged[Cdecl]<uint>)GetExport(h, "e_1289F2B1F61E337B2490EEB43777FA8C"))();
    Console.WriteLine($"native ABI version: {abiVersion} (expected 15)");
    if (abiVersion != 15)
    {
        Console.Error.WriteLine("ABI mismatch");
        return 3;
    }

    int init = ((delegate* unmanaged[Cdecl]<int>)GetExport(h, "e_2A525F47BAABC39D658974BF9A3DE8C0"))();
    Console.WriteLine($"init: {init}");
    if (init == 0)
        Console.Error.WriteLine("init returned 0 (ClientStructs resolution?) - continuing to discover anyway");

    var pathBytes = Encoding.UTF8.GetBytes(sqpackPath + "\0");
    int disc;
    unsafe
    {
        fixed (byte* p = pathBytes)
        {
            disc = ((delegate* unmanaged[Cdecl]<byte*, int, int>)GetExport(h, "e_A1060C7C0A62E1E717793C8BFEF7CCB3"))(p, language);
        }
    }
    Console.WriteLine($"discover: {disc}");
    if (disc <= 0)
    {
        string err = ReadNativeString(h, "e_C3BC5AF8A5C5D67A8B87AEAC6328643C");
        Console.Error.WriteLine($"discover failed: {err}");
        return 5;
    }

    var vendors = DumpArray<Vendor>(h, "e_773F8E5FA33E35B7C080639C5B671B4A");
    var locations = DumpArray<Location>(h, "e_80A6D1C081F4ED739F71F84841286945");
    var shops = DumpArray<Shop>(h, "e_4A4B651C8B48AA65C8D65E77886B8B55");
    var listings = DumpArray<Listing>(h, "e_2B59DE6FA2ED3787F00E63700A35B429");
    var territories = DumpArray<Territory>(h, "e_D18261274FB2BAE9FEDB7F91FCCC9C5B");
    var itemMeta = DumpArray<ItemMeta>(h, "e_B50FC43A34250E1F744840B5829670F4");

    Console.WriteLine($"vendors={vendors.Length} locations={locations.Length} shops={shops.Length} listings={listings.Length} territories={territories.Length} itemMeta={itemMeta.Length}");

    var doc = new Dictionary<string, object?>
    {
        ["abiVersion"] = abiVersion,
        ["language"] = language,
        ["vendors"] = vendors,
        ["locations"] = locations,
        ["shops"] = shops,
        ["listings"] = listings,
        ["territories"] = territories,
        ["itemMeta"] = itemMeta,
    };
    var opts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
    File.WriteAllText(outPath, JsonSerializer.Serialize(doc, opts));
    Console.WriteLine($"wrote {outPath}");
}
finally
{
    NativeLibrary.Free(h);
}
return 0;
}


static nint GetExport(nint h, string name)
{
    if (!NativeLibrary.TryGetExport(h, name, out var addr))
        throw new InvalidOperationException($"export not found: {name}");
    return addr;
}

static string ReadNativeString(nint h, string export)
{
    unsafe
    {
        int n = ((delegate* unmanaged[Cdecl]<byte*, int, int>)GetExport(h, export))(null, 0);
        if (n <= 0) return "";
        var buf = new byte[n + 1];
        fixed (byte* p = buf)
        {
            ((delegate* unmanaged[Cdecl]<byte*, int, int>)GetExport(h, export))(p, buf.Length);
        }
        return Encoding.UTF8.GetString(buf, 0, n);
    }
}

static T[] DumpArray<T>(nint h, string export) where T : struct
{
    unsafe
    {
        var fn = (delegate* unmanaged[Cdecl]<T*, int, int>)GetExport(h, export);
        int count = fn(null, 0);
        if (count <= 0) return Array.Empty<T>();
        var arr = new T[count];
        fixed (T* p = arr)
        {
            fn(p, arr.Length);
        }
        return arr;
    }
}

// --- native record structs, field order exactly as decompiled ---

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Vendor // k16l1JBFW0Mq23
{
    public uint fJbfef7FNX9mDJ1ASAX1m0p4fL;
    public uint tdiijft8SkBVkPEJo12pNNx;
    public int gXIrdiDYEKLpBM5mwvP18;
    public fixed uint gtP6DQroQ5ktci0PBTjnlR9FVid[6];
}

[StructLayout(LayoutKind.Sequential)]
internal struct Location // Ye5RrTsitMjVxFDytGy9LjgO
{
    public uint n01hH1ZLjdZ4H3aTyAjZJm;
    public uint HSnOAL0LlPBb8rPJ;
    public uint Nd7oMYKQVkR8CvXZTVXskdO5QPtu;
    public uint T5AWN4VZjMV5JdsW3eLPycb;
    public ushort l8hOzlKWUag2cEBs2E866Kw5q;
    public ushort iLroR7jHMn1rb1fLr8;
    public byte aXrJBbVdtzx9PNc97HSbc2c8Ol8T;
    public int Wd76SocNXoJVSD9oT4Yqh23UuRV3;
    public float swUeisibVdUIlc;
    public float hJc0I3Fs7DFWEyMJpWo5S;
    public float IIF1reqk7sahEbUysIHWUzNtTII;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Shop // tYhn3kI1jIRICwugChys
{
    public uint MBGK4hAr20Qr1we;
    public uint UmrlLzamgJ5zkpgKi0eMnQcl;
    public uint A8MAkcj8Tm9uyYftM;
    public uint vIsDSjAi80RGmFyx04;
    public uint HdTmKbaeRfafcAGgCiP7p;
    public uint QrDgKfN6ImIGTvyGVi3os;
    public uint EvEOv4gTyg8SDQxJcuZNyaWcZGma;
    public uint vVVrn45tRlhjq1eQYvxoe0xGoQi;
    public int nkdfHCusCgcntvVuppF;
    public int UOrHbC4KGZDCqXp6KuAHjxYf;
    public uint f6dKdn2fhesIRT9ga;
    public uint Ydd9fCmufYEWvBo4fgo;
    public ushort sztMX4RkQ3zt3x;
    public ushort SYmFwgWZtLK3AhjlwMejKhG8erWL;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Listing // wY92WutFJhkmXemhp
{
    public uint Plxb2PxvOmKcyRug;
    public uint Pfhp8TZAixc2rct3NENApCSOoB;
    public uint w5IOcihMgg4N7A2ZjBGiVBNi2r;
    public uint WsaAM0wo5Bkn6LOwRM6ppgB;
    public uint AVd6e4H1UHFb75UNhT;
    public uint oheh7u6JmwQtXQqeK0yxeqt;
    public uint HzDZp6su0gY3YeiHVm3QQnpjUhEN;
    public uint EaJVs6Dc23p0rf5pCdUAPX80lfe;
    public uint YLGSTI3ueawttHylRbWHPo6xD4;
    public int QFsbmeeAczg2c1fx4rTBQmuXiu4;
    public int hbfPGUdRiDDquMybx;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Territory // AM4smaUsF7kIiVK3Jsfc
{
    public uint OViUXwIwaEvDT8bu1zLwo;
    public uint XCLrUffIL59qZ4cJv;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ItemMeta // iUEmMMStDR9d8Mkntjcc6
{
    public uint mXbtVaMgPXYfW6AFX0k;
    public uint bjgZiXfYoJR46;
    public uint sl5lDpkaIqyeFUG9TRBWzniD11x;
    public byte D3LcV3f9u1OxqRiUdwq;
}
