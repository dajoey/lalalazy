using System.Globalization;

namespace GluttonyCombo.CrucibleReplay;

/// <summary> One recorded window: `#key=value` header lines, then `ISO-8601 UTC ts TAB message` rows of CR| / BT| / DS| lines. </summary>
internal sealed class LogWindow
{
    public string Name = "";
    public Dictionary<string, string> Header = new();
    public List<CrLine> Cr = new();
    public List<BtLine> Bt = new();
    public List<DsLine> Ds = new();
    public int Skipped;

    public string Build => Header.GetValueOrDefault("build", "unknown");

    public static LogWindow Load(string path)
    {
        var w = new LogWindow { Name = Path.GetFileNameWithoutExtension(path) };
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            if (line[0] == '#')
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    w.Header[line[1..eq]] = line[(eq + 1)..];
                continue;
            }
            var tab = line.IndexOf('\t');
            if (tab < 0)
                continue;
            var msg = line[(tab + 1)..];
            if (msg.StartsWith("CR|", StringComparison.Ordinal)) w.Cr.Add(CrLine.Parse(msg));
            else if (msg.StartsWith("BT|", StringComparison.Ordinal)) w.Bt.Add(BtLine.Parse(msg));
            else if (msg.StartsWith("DS|", StringComparison.Ordinal)) w.Ds.Add(DsLine.Parse(msg));
            else w.Skipped++;
        }
        w.Cr.Sort((a, b) => a.Ms.CompareTo(b.Ms));
        w.Bt.Sort((a, b) => a.Ms.CompareTo(b.Ms));
        w.Ds.Sort((a, b) => a.Ms.CompareTo(b.Ms));
        return w;
    }

    /// <summary> key=value fields of a `|`-separated line (positional fields are skipped). </summary>
    internal static Dictionary<string, string> Fields(string msg)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in msg.Split('|'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
                d[part[..eq]] = part[(eq + 1)..];
        }
        return d;
    }

    internal static float F(string? s, float or) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : or;
    internal static int I(string? s, int or) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : or;

    /// <summary> rd= readiness letters, mirroring the formatter's private CrucibleTelemetryFormat.ReadyLetters:
    ///     bit i of ReadyBits = horns 1-3, Trick, Tempered, Borrow, Parting Blow, axe, Rally, Cheer,
    ///     Beast Mode, Shield Charge. </summary>
    internal const string RdLetters = "123tmbparcBs";
}

/// <summary> CR| line, format in src/GluttonyCombo/GluttonyCombo/Data/CrucibleTelemetryFormat.cs. </summary>
internal sealed class CrLine
{
    public long Ms;
    public string Raw = "";
    public int Board, Battle, Needs, Enemies, HighestEnemyHp, TargetNameId, TargetHp, PlayerHp, PetHp, Ttd, Intake, Vul, Degree = -1;
    public uint CastId;
    public float CastRemaining, Distance = -1f, MoveSpeed;
    public string Flags = "";
    public float[] SlotPetHp = new float[3];
    public bool HasSlotPetHp;
    public uint DecId;
    public string DecReason = "";
    public string Shadow = "";
    public bool Verified;
    public bool BmrSeen;
    public float BmrHitIn = float.MaxValue, BmrMaxCast = float.MaxValue;
    public int BmrType;
    public bool BmrOnPlayer, BmrOnFamiliar;

    // replay-input tail (2026-10-15): f=I, rd=, hpot=, hpa=, gcd=, weave=, ic=, cast=, mo=, ou=. NewFields is false
    // on lines from builds before the tail, and the whole set then stays unknown (the rebuilder keeps its gaps).
    public bool NewFields;
    public uint ReadyBits;
    public uint HealPotion;
    public int PlayerHpCur, PlayerHpMax;
    public bool GcdReady, CanWeave, InCombat, PlayerIsCasting, MorphoAlive, OtherEnemiesUp;

    public bool Has(char flag) => Flags.Contains(flag);

    public static CrLine Parse(string msg)
    {
        var f = LogWindow.Fields(msg);
        var c = new CrLine { Raw = msg };
        var parts = msg.Split('|');
        c.Ms = long.Parse(parts[1], CultureInfo.InvariantCulture);
        c.Board = LogWindow.I(f.GetValueOrDefault("b"), 0);
        c.Battle = LogWindow.I(f.GetValueOrDefault("bt"), -1);
        var nd = f.GetValueOrDefault("nd") ?? "";
        c.Needs = (nd.Contains('I') ? 1 : 0) | (nd.Contains('D') ? 2 : 0) | (nd.Contains('C') ? 4 : 0);
        c.Enemies = LogWindow.I(f.GetValueOrDefault("ne"), 0);
        c.HighestEnemyHp = LogWindow.I(f.GetValueOrDefault("hi"), 0);
        var t = (f.GetValueOrDefault("t") ?? "0:0").Split(':');
        c.TargetNameId = LogWindow.I(t[0], 0);
        c.TargetHp = t.Length > 1 ? LogWindow.I(t[1], 0) : 0;
        var cs = (f.GetValueOrDefault("c") ?? "0:0.0").Split(':');
        c.CastId = (uint)LogWindow.I(cs[0], 0);
        c.CastRemaining = cs.Length > 1 ? LogWindow.F(cs[1], 0f) : 0f;
        c.Flags = f.GetValueOrDefault("f") ?? "";
        c.PlayerHp = LogWindow.I(f.GetValueOrDefault("hp"), 0);
        c.PetHp = LogWindow.I(f.GetValueOrDefault("pet"), 0);
        var sl = (f.GetValueOrDefault("sl") ?? "").Split('.');
        if (sl.Length == 3)
        {
            c.HasSlotPetHp = true;
            for (var i = 0; i < 3; i++)
                c.SlotPetHp[i] = LogWindow.F(sl[i], 0f);
        }
        var dec = f.GetValueOrDefault("dec") ?? "0:";
        var colon = dec.IndexOf(':');
        c.DecId = (uint)LogWindow.I(colon < 0 ? dec : dec[..colon], 0);
        c.DecReason = colon < 0 ? "" : dec[(colon + 1)..];
        c.Shadow = f.GetValueOrDefault("sh") ?? "";
        c.Ttd = LogWindow.I(f.GetValueOrDefault("ttd"), 0);
        c.Intake = LogWindow.I(f.GetValueOrDefault("in"), 0);
        c.Vul = LogWindow.I(f.GetValueOrDefault("vul"), 0);
        c.Verified = f.GetValueOrDefault("xp") == "1";
        var d = f.GetValueOrDefault("d");
        c.Distance = string.IsNullOrEmpty(d) ? -1f : LogWindow.F(d, -1f);
        c.MoveSpeed = LogWindow.F(f.GetValueOrDefault("mv"), 0f);
        var dg = f.GetValueOrDefault("dg");
        c.Degree = dg is { Length: 1 } && char.IsDigit(dg[0]) ? dg[0] - '0' : -1;

        // replay-input tail (2026-10-15): any of the new keys marks a new-format line. rd= maps its readiness
        // letters by bit; RdLetters mirrors CrucibleTelemetryFormat.ReadyLetters (private there) - keep in step.
        c.NewFields = f.ContainsKey("rd") || f.ContainsKey("hpot") || f.ContainsKey("hpa") || f.ContainsKey("gcd")
            || f.ContainsKey("weave") || f.ContainsKey("ic") || f.ContainsKey("cast") || f.ContainsKey("mo") || f.ContainsKey("ou");
        if (c.NewFields)
        {
            var rd = f.GetValueOrDefault("rd") ?? "";
            for (var i = 0; i < LogWindow.RdLetters.Length; i++)
                if (rd.Contains(LogWindow.RdLetters[i]))
                    c.ReadyBits |= 1u << i;
            c.HealPotion = (uint)LogWindow.I(f.GetValueOrDefault("hpot"), 0);
            var hpa = (f.GetValueOrDefault("hpa") ?? "0/0").Split('/');
            c.PlayerHpCur = LogWindow.I(hpa[0], 0);
            c.PlayerHpMax = hpa.Length > 1 ? LogWindow.I(hpa[1], 0) : 0;
            c.GcdReady = f.GetValueOrDefault("gcd") == "1";
            c.CanWeave = f.GetValueOrDefault("weave") == "1";
            c.InCombat = f.GetValueOrDefault("ic") == "1";
            c.PlayerIsCasting = f.GetValueOrDefault("cast") == "1";
            c.MorphoAlive = f.GetValueOrDefault("mo") == "1";
            c.OtherEnemiesUp = f.GetValueOrDefault("ou") == "1";
        }
        if (f.ContainsKey("bh"))
        {
            c.BmrSeen = true;
            var bh = f["bh"];
            c.BmrHitIn = bh == "-" ? float.MaxValue : LogWindow.F(bh, float.MaxValue);
            c.BmrType = LogWindow.I(f.GetValueOrDefault("bk"), 0);
            c.BmrOnPlayer = f.GetValueOrDefault("bp") == "1";
            c.BmrOnFamiliar = f.GetValueOrDefault("bf") == "1";
            var mct = f.GetValueOrDefault("mct") ?? "-";
            c.BmrMaxCast = mct == "-" ? float.MaxValue : LogWindow.F(mct, float.MaxValue);
        }
        return c;
    }
}

/// <summary> BT| line, format in src/GluttonyCombo/GluttonyCombo/Data/BeastmasterTelemetryFormat.cs. </summary>
internal sealed class BtLine
{
    public long Ms;
    public byte[] Gauge = new byte[9]; // 0x08..0x10
    public string Pet = "none";
    public int PetBNpcBase;
    public uint BeastModeResolved;
    public uint DecId;
    public string DecReason = "";
    public int[] SlotBeasts = new int[3];
    public bool HasSlotBeasts;
    public int Level;
    public string Trick = "-";
    public List<int> Statuses = new();
    public bool StatusesTruncated;

    public static BtLine Parse(string msg)
    {
        var parts = msg.Split('|');
        var b = new BtLine { Ms = long.Parse(parts[1], CultureInfo.InvariantCulture) };
        var hex = parts[2];
        for (var i = 0; i < 9 && hex.Length >= (i + 1) * 2; i++)
            b.Gauge[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        var f = LogWindow.Fields(msg);
        b.Pet = f.GetValueOrDefault("pet") ?? "none";
        var pp = b.Pet.Split(':');
        if (pp.Length >= 3)
            b.PetBNpcBase = LogWindow.I(pp[^1], 0);
        var bm = (uint)LogWindow.I(f.GetValueOrDefault("bm"), 0);
        b.BeastModeResolved = bm == 44886u ? 0u : bm; // BST.BeastMode (base id): no Kinship held
        var dec = f.GetValueOrDefault("dec") ?? "0:";
        var colon = dec.IndexOf(':');
        b.DecId = (uint)LogWindow.I(colon < 0 ? dec : dec[..colon], 0);
        b.DecReason = colon < 0 ? "" : dec[(colon + 1)..];
        var sl = (f.GetValueOrDefault("sl") ?? "").Split('.');
        if (sl.Length == 3)
        {
            b.HasSlotBeasts = true;
            for (var i = 0; i < 3; i++)
                b.SlotBeasts[i] = LogWindow.I(sl[i], 0);
        }
        b.Level = LogWindow.I(f.GetValueOrDefault("lv"), 0);
        b.Trick = f.GetValueOrDefault("tk") ?? "-";
        var last = parts[^1];
        if (!last.Contains('='))
        {
            b.StatusesTruncated = last.EndsWith('~');
            foreach (var tok in last.TrimEnd('~').Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok, out var id))
                    b.Statuses.Add(id);
        }
        return b;
    }
}

/// <summary> DS| line: the dash-landing verdict `DS|ms|t=target|enemy=0|landing=0|v=safe`. </summary>
internal sealed class DsLine
{
    public long Ms;
    public string Verdict = "";

    public static DsLine Parse(string msg)
    {
        var parts = msg.Split('|');
        return new DsLine { Ms = long.Parse(parts[1], CultureInfo.InvariantCulture), Verdict = LogWindow.Fields(msg).GetValueOrDefault("v") ?? "" };
    }
}
