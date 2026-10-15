using System.Reflection;
using GluttonyCombo.Combos.PvE;
using static GluttonyCombo.Combos.PvE.BST_RotationLogic;

namespace GluttonyCombo.BSTRotationHarness;

/// <summary>
///     Reflection adapters for the four r4-shipped fix files: they reach whatever a fix added to a type (a state field, a parameter) by name, so the same
///     file compiles on the build before the fix. On that build a field that is absent is skipped and a parameter that is absent is not passed, which is
///     exactly what the old code ignored; the assert then fails at runtime.
/// </summary>
internal static partial class Program
{
    /// <summary> Sets one <see cref="BstState"/> field by name; a field the build does not have is skipped. </summary>
    private static BstState R4ShippedSet(BstState s, string field, object value)
    {
        object boxed = s;
        var f = typeof(BstState).GetField(field);
        f?.SetValue(boxed, Convert.ChangeType(value, f.FieldType, System.Globalization.CultureInfo.InvariantCulture));
        return (BstState)boxed;
    }

    /// <summary> <see cref="BST_CrucibleLogic.ConditionallyForbidden"/> with the arguments the build's signature takes (the HP and others-up arguments came with the Abaddon fix). </summary>
    private static bool R4ShippedForbidden(uint nameId, bool morphoAlive, float hpPercent, bool othersUp)
    {
        var m = typeof(BST_CrucibleLogic).GetMethod("ConditionallyForbidden", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] all = [nameId, 0u, morphoAlive, 0u, false, hpPercent, othersUp];
        return (bool)m.Invoke(null, all.Take(m.GetParameters().Length).ToArray())!;
    }

    /// <summary> A <see cref="BST_CrucibleLogic.TargetCandidate"/> built by parameter name; a parameter the build does not have (DispellableProven, Forbidden) is dropped. </summary>
    private static BST_CrucibleLogic.TargetCandidate R4ShippedCandidate(uint nameId, float hp = 100f, bool avoid = false, bool dispellable = false, bool proven = false, bool forbidden = false)
    {
        var ctor = typeof(BST_CrucibleLogic.TargetCandidate).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var given = new Dictionary<string, object>
        {
            ["NameId"] = nameId, ["HpPercent"] = hp, ["Avoid"] = avoid, ["Dispellable"] = dispellable, ["DispellableProven"] = proven, ["Forbidden"] = forbidden,
        };
        var args = ctor.GetParameters().Select(p => given.TryGetValue(p.Name!, out var v) ? v : p.HasDefaultValue ? p.DefaultValue : Activator.CreateInstance(p.ParameterType)).ToArray();
        return (BST_CrucibleLogic.TargetCandidate)ctor.Invoke(args);
    }

    /// <summary> The status ids the build's tables call dispellable (the rotation's aim reads exactly this set) and the ones the panel flags as known to come off. </summary>
    private static (bool Carries, bool Proven) R4ShippedCarries(params uint[] statuses) =>
        (statuses.Any(BST_CrucibleData.DispellableBuffs.Contains), statuses.Any(BST_CrucibleData.PanelFlagsDispellable));
}
