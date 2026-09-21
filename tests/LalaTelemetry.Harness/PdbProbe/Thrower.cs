// Compiled twice by tests/LalaTelemetry.Harness: once with <DebugType>embedded</DebugType>
// (PdbProbeEmbedded.dll) and once with <DebugType>none</DebugType> (PdbProbeNone.dll).
// The harness finds the line marked THROW-LINE below and asserts that an exception thrown from the
// embedded build reports exactly that line even when the assembly is loaded from a byte array (no
// file, no .pdb next to it) - and that the no-PDB build reports no line at all (the control).
using System;
using System.Threading.Tasks;

namespace PdbProbe;

public static class Thrower
{
    public static void Throw()
    {
        throw new InvalidOperationException("pdb probe"); // THROW-LINE
    }

    /// <summary>A faulted task whose stack involves only this assembly (the "foreign plugin" case).</summary>
    public static Task Faulted() => Task.Run(static () => throw new InvalidOperationException("foreign task"));
}
