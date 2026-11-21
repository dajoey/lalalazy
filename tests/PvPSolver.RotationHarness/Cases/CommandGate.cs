using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace PvPSolver.RotationHarness.Cases;

/// <summary>
///     The 0.1.2.33 command-registration gate. The 0.1.2.32 report was "Command /pvpsolver not found":
///     registration only happened near the end of a 10-30 s constructor, so commands typed during
///     start-up were unknown.
///     T2 (source, replayable via PVPROT_SRC): RSCommands_BasicInfo.cs registers and removes exactly the
///     four documented commands, Service.cs defines them with the documented names, and the plugin
///     constructor calls RSCommands.Enable() before MajorUpdater.Enable() and before IPCProvider.
///     T1-IL (always the current build's assembly, by design - see the PVPROT notes in the csproj):
///     counting the actual call sites in the compiled RSCommands.Enable (an AddHandler call the
///     compiler never saw does not exist, whatever the source says), the ctor's call order, and the
///     string-heap presence of every command and the registration log line.
/// </summary>
internal static class CommandGate
{
    private static readonly string[] Commands = ["/pvpsolver", "/pvs", "/pvpsolver Auto", "/pvpsolver Off"];

    public static void Run()
    {
        Console.WriteLine("-- command registration gate (T2 source + T1 IL) --");
        SourceTier();
        IlTier();
    }

    private static void SourceTier()
    {
        var basicPath = Path.Combine(Program.SrcRoot, "PvPSolver", "Commands", "RSCommands_BasicInfo.cs");
        if (!File.Exists(basicPath))
        {
            Harness.Case("RSCommands_BasicInfo.cs found", false, basicPath);
            return;
        }

        var basic = File.ReadAllText(basicPath);
        var addCount = Regex.Matches(basic, @"Svc\.Commands\.AddHandler\(").Count;
        var removeCount = Regex.Matches(basic, @"Svc\.Commands\.RemoveHandler\(").Count;
        Harness.Case("RSCommands registers exactly the four documented commands (source)", addCount == 4,
            $"{addCount} AddHandler calls");
        Harness.Case("RSCommands removes exactly the four commands on disable (source)", removeCount == 4,
            $"{removeCount} RemoveHandler calls");
        Harness.Case("registration results are logged at load", basic.Contains("RSCommands: registered"),
            "the RSCommands: registered log line is missing");

        var servicePath = Path.Combine(Program.SrcRoot, "PvPSolver", "Basic", "Service.cs");
        var service = File.Exists(servicePath) ? File.ReadAllText(servicePath) : "";
        foreach (var (field, cmd) in new[]
                 {
                     ("COMMAND", "/pvpsolver"), ("ALTCOMMAND", "/pvs"), ("AUTOCOMMAND", "/pvpsolver Auto"),
                     ("OFFCOMMAND", "/pvpsolver Off"),
                 })
        {
            var want = $"const string {field} = \"{cmd}\";";
            Harness.Case($"Service.{field} is \"{cmd}\"", service.Contains(want), want + " not found");
        }

        var pluginPath = Path.Combine(Program.SrcRoot, "PvPSolver", "RotationSolverPlugin.cs");
        var plugin = File.Exists(pluginPath) ? File.ReadAllText(pluginPath) : "";
        var early = plugin.IndexOf("RSCommands.Enable();", StringComparison.Ordinal);
        var ipc = plugin.IndexOf("IPCProvider = new();", StringComparison.Ordinal);
        var major = plugin.IndexOf("MajorUpdater.Enable();", StringComparison.Ordinal);
        Harness.Case("the constructor registers commands before the slow init steps and MajorUpdater",
            early >= 0 && early < ipc && ipc >= 0 && early < major,
            $"RSCommands.Enable@{early}, IPCProvider@{ipc}, MajorUpdater.Enable@{major}");
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

        var enableFound = 0;
        var enableAdd = 0;
        var disableAdd = 0;
        var ctorEarly = -1;
        var ctorMajor = -1;
        foreach (var h in md.MethodDefinitions)
        {
            var m = md.GetMethodDefinition(h);
            var name = md.GetString(m.Name);
            var typeName = TypeNameOf(md, m.GetDeclaringType());
            if (typeName == "RSCommands" && name is "Enable" or "Disable" && m.RelativeVirtualAddress != 0)
            {
                var body = pe.GetMethodBody(m.RelativeVirtualAddress);
                var calls = CountCallsTo(md, body, "AddHandler");
                if (name == "Enable")
                {
                    enableFound++;
                    enableAdd += calls;
                }
                else
                {
                    disableAdd += calls;
                }
            }
            else if (typeName == "PvPSolverPlugin" && name == ".ctor" && m.RelativeVirtualAddress != 0)
            {
                var body = pe.GetMethodBody(m.RelativeVirtualAddress);
                ctorEarly = IndexOfCallTo(md, body, "RSCommands", "Enable");
                ctorMajor = IndexOfCallTo(md, body, "MajorUpdater", "Enable");
            }
        }

        Harness.Case("built assembly: RSCommands.Enable has exactly 4 AddHandler call sites",
            enableFound == 1 && enableAdd == 4, $"{enableFound} Enable methods, {enableAdd} AddHandler calls");
        Harness.Case("built assembly: the constructor registers commands before MajorUpdater.Enable",
            ctorEarly >= 0 && ctorMajor >= 0 && ctorEarly < ctorMajor,
            $"ctor RSCommands.Enable@IL{ctorEarly}, MajorUpdater.Enable@IL{ctorMajor}");
        // Canary: the same counter on Disable (which has zero AddHandler calls) must NOT read four -
        // if it did, the counter would be counting nothing and the gate above would be theatre.
        Harness.Canary("the IL counter reads four AddHandler calls in RSCommands.Disable", disableAdd == 4);

        foreach (var needle in Commands.Concat(["RSCommands: registered", "MatchStartAutoOn: player ready"]))
        {
            var found = IndexOfBytes(asmBytes, Encoding.Unicode.GetBytes(needle)) >= 0;
            Harness.Case($"built assembly string heap carries \"{needle}\"", found, "not found in UTF-16 strings");
        }
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

    /// <summary>Calls <c>name</c> through the IL with the operands decoded properly (no naive byte scan).</summary>
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

    /// <summary>The IL offset of the first call to <c>typeName.name</c>, or -1.</summary>
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
                if (table == 0x0A000000) // MemberRef (cross-assembly, e.g. ICommandManager.AddHandler)
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
                else if (table == 0x06000000) // MethodDef (same assembly, e.g. RSCommands.Enable)
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
            var j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
            {
                j++;
            }

            if (j == needle.Length)
            {
                return i;
            }
        }

        return -1;
    }
}
