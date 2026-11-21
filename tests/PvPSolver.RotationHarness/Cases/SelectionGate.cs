using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     The 0.1.2.34 selection gate. The 0.1.2.33 report was "it's healing, not attacking": the duty-rotation
///     removal (dd9ff1657, shipped v0.1.2.23) deleted the plain GeneralGCD call from the GCD chooser along
///     with the duty-rotation call it sat next to, so no job rotation's attack GCDs were ever invoked - the
///     chooser could only return heals (auto-heal single, HealWhenNothingTodo) or emergency actions. The
///     DefenseArea/DefenseSingle branches were emptied the same way. GeneralAbility on the ability side
///     survived, which is why the failure read as "heals only" rather than "does nothing".
///     T2 (source, replayable via PVPROT_SRC): the chooser calls DefenseAreaGCD, DefenseSingleGCD and
///     GeneralGCD again (GeneralGCD inside the NoCasting branch, after the heal branch); the executor logs
///     every executed-action change (ActionTap, on-change only); every shipped PvP rotation still carries
///     an attack-bearing GeneralGCD override; AST's override still names its attacks.
///     T1-IL (always the current build's assembly): the compiled CustomRotation.GCD calls GeneralGCD
///     exactly once and after HealSingleGCD (upstream order), still calls both defense choosers and the
///     heal chooser; the compiled CustomRotation.Ability still calls GeneralAbility; the string heap
///     carries the ActionTap format. A call the compiler never saw does not exist, whatever the source
///     says - that is the lesson of v0.1.2.23 through v0.1.2.33.
/// </summary>
internal static class SelectionGate
{
    public static void Run()
    {
        Console.WriteLine("-- selection gate: the chooser must be able to choose attacks (T2 source + T1 IL) --");
        SourceTier();
        IlTier();
    }

    private static void SourceTier()
    {
        var gcdPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Basic", "Rotations", "CustomRotation_GCD.cs");
        if (!File.Exists(gcdPath))
        {
            Harness.Case("CustomRotation_GCD.cs found", false, gcdPath);
            return;
        }

        var gcd = File.ReadAllText(gcdPath);
        foreach (var call in new[] { "DefenseAreaGCD(out var action)", "DefenseSingleGCD(out var action)", "GeneralGCD(out var action)" })
        {
            var n = Regex.Matches(gcd, Regex.Escape($"if ({call})")).Count;
            Harness.Case($"chooser calls {call.Split('(')[0]} again (source)", n == 1, $"{n} call sites");
        }

        var noCasting = gcd.IndexOf("!DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting)", StringComparison.Ordinal);
        var general = gcd.IndexOf("GeneralGCD(out var action)", StringComparison.Ordinal);
        var idleHeal = gcd.IndexOf("Service.Config.HealWhenNothingTodo", StringComparison.Ordinal);
        var healSingle = gcd.IndexOf("HealSingleGCD(out IAction? action)", StringComparison.Ordinal);
        Harness.Case("the attack call sits inside the NoCasting branch, after the heal branch",
            noCasting >= 0 && general > noCasting && (idleHeal < 0 || general < idleHeal),
            $"NoCasting@{noCasting}, GeneralGCD@{general}, idle-heal@{idleHeal}, heal-single@{healSingle}");

        var cmdPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Commands", "RSCommands_Actions.cs");
        var cmd = File.Exists(cmdPath) ? File.ReadAllText(cmdPath) : "";
        Harness.Case("executor logs the executed action (ActionTap source shape)",
            cmd.Contains("LogActionTap") && cmd.Contains("ActionTap: ") && cmd.Contains("_lastTappedActionID"),
            "LogActionTap / ActionTap literal / on-change guard missing");
        var useBlock = cmd.IndexOf("if (nextAction.Use())", StringComparison.Ordinal);
        var tapCall = cmd.IndexOf("LogActionTap(nextAction);", StringComparison.Ordinal);
        Harness.Case("the tap fires inside the Use() success block", useBlock >= 0 && tapCall > useBlock,
            $"Use()@{useBlock}, tap@{tapCall}");

        RotationTier();
    }

    private static void RotationTier()
    {
        var rotRoot = Path.Combine(Program.SrcRoot, "PvPSolver", "RebornRotations", "PVPRotations");
        if (!Directory.Exists(rotRoot))
        {
            Harness.Case("PVPRotations folder found", false, rotRoot);
            return;
        }

        var files = Directory.EnumerateFiles(rotRoot, "*Default.PVP.cs", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        Harness.Case("all shipped PvP rotation files found", files.Count >= 20, $"{files.Count} files");

        var head = new Regex(@"\boverride\s+bool\s+(?<name>\w+)\s*\(");
        var missing = new List<string>();
        var thin = new List<string>();
        string? astBody = null;
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            var methods = CsSource.Methods(CsSource.Sanitize(File.ReadAllText(f)), name, head);
            var g = methods.FirstOrDefault(m => m.Name == "GeneralGCD");
            if (g == null)
            {
                missing.Add(name);
                continue;
            }

            if (f.EndsWith("AST_Default.PVP.cs"))
            {
                astBody = g.Body;
            }

            if (Regex.Matches(g.Body, @"\.CanUse\(out").Count < 2)
            {
                thin.Add(name);
            }
        }

        Harness.Case("every PvP rotation still overrides GeneralGCD", missing.Count == 0,
            string.Join(", ", missing));
        Harness.Case("every GeneralGCD override carries attack chains (>=2 CanUse)", thin.Count == 0,
            string.Join(", ", thin));
        Harness.Case("AST's GeneralGCD still names its attacks",
            astBody != null && astBody.Contains("FallMaleficPvP") && astBody.Contains("GravityIiPvP")
                && astBody.Contains("MacrocosmosPvP"),
            astBody == null ? "AST override not found" : "attack names missing from the AST override");
    }

    private static void IlTier()
    {
        var asmPath = typeof(RotationSolver.PvPSolverPlugin).Assembly.Location;
        if (string.IsNullOrEmpty(asmPath) || !File.Exists(asmPath))
        {
            Harness.Case("built PvPSolver assembly found for the IL gate", false, asmPath);
            return;
        }

        var asmBytes = File.ReadAllBytes(asmPath);
        using var pe = new PEReader(new MemoryStream(asmBytes));
        var md = pe.GetMetadataReader();

        var gcdFound = 0;
        var abilityFound = 0;
        var counts = new Dictionary<string, int>();
        var firstOffset = new Dictionary<string, int>();
        foreach (var h in md.MethodDefinitions)
        {
            var m = md.GetMethodDefinition(h);
            var name = md.GetString(m.Name);
            var typeName = TypeNameOf(md, m.GetDeclaringType());
            if (typeName != "CustomRotation" || name is not ("GCD" or "Ability") || m.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var body = pe.GetMethodBody(m.RelativeVirtualAddress);
            if (name == "GCD")
            {
                gcdFound++;
                foreach (var callee in new[] { "GeneralGCD", "DefenseAreaGCD", "DefenseSingleGCD", "HealSingleGCD", "EmergencyGCD", "DispelGCD", "ProvokeGCD" })
                {
                    counts[callee] = CountCallsTo(md, body, callee);
                    firstOffset[callee] = IndexOfCallTo(md, body, "CustomRotation", callee);
                }
            }
            else
            {
                abilityFound++;
                counts["GeneralAbility"] = CountCallsTo(md, body, "GeneralAbility");
            }
        }

        Harness.Case("built assembly: exactly one GCD chooser and one Ability chooser on CustomRotation",
            gcdFound == 1 && abilityFound == 1, $"{gcdFound} GCD, {abilityFound} Ability");
        Harness.Case("built assembly: the GCD chooser calls GeneralGCD exactly once",
            counts.GetValueOrDefault("GeneralGCD") == 1, $"{counts.GetValueOrDefault("GeneralGCD")} call sites");
        Harness.Case("built assembly: the defense choosers are called again",
            counts.GetValueOrDefault("DefenseAreaGCD") >= 1 && counts.GetValueOrDefault("DefenseSingleGCD") >= 1,
            $"area {counts.GetValueOrDefault("DefenseAreaGCD")}, single {counts.GetValueOrDefault("DefenseSingleGCD")}");
        Harness.Case("built assembly: the heal chooser is still called (attacks come after heals, not instead)",
            counts.GetValueOrDefault("HealSingleGCD") >= 1, $"{counts.GetValueOrDefault("HealSingleGCD")} call sites");
        Harness.Case("built assembly: GeneralGCD is evaluated after the heal branch (upstream order)",
            firstOffset.GetValueOrDefault("GeneralGCD") > firstOffset.GetValueOrDefault("HealSingleGCD") 
            && firstOffset.GetValueOrDefault("HealSingleGCD") >= 0,
            $"HealSingleGCD@IL{firstOffset.GetValueOrDefault("HealSingleGCD")}, GeneralGCD@IL{firstOffset.GetValueOrDefault("GeneralGCD")}");
        Harness.Case("built assembly: the ability chooser still calls GeneralAbility",
            counts.GetValueOrDefault("GeneralAbility") >= 1, $"{counts.GetValueOrDefault("GeneralAbility")} call sites");

        // Canary: ProvokeGCD was removed with the PvE machinery and must count zero; if it read one,
        // the walker would be counting nothing and every gate above would be theatre.
        Harness.Canary("the IL walker reads real calls (ProvokeGCD would read 1)",
            counts.GetValueOrDefault("ProvokeGCD") == 1);

        var tapFound = IndexOfBytes(asmBytes, Encoding.Unicode.GetBytes("ActionTap: ")) >= 0;
        Harness.Case("built assembly string heap carries \"ActionTap: \"", tapFound, "not found in UTF-16 strings");
    }

    private static string TypeNameOf(MetadataReader md, EntityHandle type)
    {
        if (type.Kind == HandleKind.TypeDefinition)
        {
            var t = md.GetTypeDefinition((TypeDefinitionHandle)type);
            return md.GetString(t.Name);
        }

        return type.Kind == HandleKind.TypeReference
            ? md.GetString(md.GetTypeReference((TypeReferenceHandle)type).Name)
            : "?";
    }

    // ---- minimal IL walk (opcode sizes derived from System.Reflection.Emit.OpCodes, so no hand table) ----

    private static OpCode[]? _oneByte;
    private static OpCode[]? _twoByte;

    private static void LoadOpcodeTables()
    {
        if (_oneByte != null)
        {
            return;
        }

        var one = new OpCode[256];
        var two = new OpCode[256];
        foreach (var f in typeof(OpCodes).GetFields())
        {
            if (f.GetValue(null) is not OpCode op)
            {
                continue;
            }

            var v = op.Value & 0xFFFF;
            if ((v & 0xFF00) == 0xFE00)
            {
                two[v & 0xFF] = op;
            }
            else
            {
                one[v & 0xFF] = op;
            }
        }

        _oneByte = one;
        _twoByte = two;
    }

    private static int OperandSize(OperandType t) => t switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
            or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
            or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => -1,
    };

    private static int CountCallsTo(MetadataReader md, MethodBodyBlock body, string name)
    {
        var count = 0;
        WalkCalls(md, body, (memberName, _, _) =>
        {
            if (memberName == name)
            {
                count++;
            }
        });
        return count;
    }

    private static int IndexOfCallTo(MetadataReader md, MethodBodyBlock body, string typeName, string name)
    {
        var result = -1;
        WalkCalls(md, body, (memberName, resolvedType, offset) =>
        {
            if (result < 0 && memberName == name && resolvedType == typeName)
            {
                result = offset;
            }
        });
        return result;
    }

    private static void WalkCalls(MetadataReader md, MethodBodyBlock body, Action<string, string, int> visit)
    {
        LoadOpcodeTables();
        var il = body.GetILBytes();
        var i = 0;
        while (i < il.Length)
        {
            var offset = i;
            byte b0 = il[i];
            OpCode op;
            if (b0 == 0xFE)
            {
                if (i + 1 >= il.Length)
                {
                    return;
                }

                op = _twoByte![il[i + 1]];
                i += 2;
            }
            else
            {
                op = _oneByte![b0];
                i += 1;
            }

            if (op.OperandType == OperandType.InlineSwitch)
            {
                if (i + 4 > il.Length)
                {
                    return;
                }

                var n = BitConverter.ToInt32(il, i);
                i += 4 + 4 * n;
                continue;
            }

            var size = OperandSize(op.OperandType);
            if (size < 0)
            {
                return; // unknown shape: stop rather than misread
            }

            var isCall = op.OperandType == OperandType.InlineMethod
                         && (op == OpCodes.Call || op == OpCodes.Callvirt);
            if (isCall && i + 4 <= il.Length)
            {
                var token = BitConverter.ToInt32(il, i);
                var table = token & 0xFF000000;
                if (table == 0x0A000000) // MemberRef (cross-assembly)
                {
                    var mr = md.GetMemberReference(MetadataTokens.MemberReferenceHandle(token));
                    var memberName = md.GetString(mr.Name);
                    var parentType = mr.Parent.Kind switch
                    {
                        HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name),
                        HandleKind.TypeDefinition => md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent).Name),
                        HandleKind.TypeSpecification => "TSpec",
                        _ => "?",
                    };
                    visit(memberName, parentType, offset);
                }
                else if (table == 0x06000000) // MethodDef (same assembly, e.g. CustomRotation.GeneralGCD)
                {
                    var def = md.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(token));
                    var memberName = md.GetString(def.Name);
                    var parentType = TypeNameOf(md, def.GetDeclaringType());
                    visit(memberName, parentType, offset);
                }
            }

            i += size;
        }
    }

    private static int IndexOfBytes(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                return i;
            }
        }

        return -1;
    }
}
