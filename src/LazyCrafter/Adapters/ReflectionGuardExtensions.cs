using System.Reflection;

namespace LazyCrafter.Adapters;

/// <summary>
/// The one extension ReflectionGuard needed for 0.1.3.0: Artisan has TWO <c>RestockFromRetainers</c> overloads
/// (per item id, and the list-shaped one), but a pin can only carry one member per key, so the second overload
/// could not be pinned. An alias re-verifies an already-pinned type's method under a different parameter list
/// and hands it back under a new key - so <see cref="Adapters.Dispatch.RetainerFetch"/> can pin both overloads
/// and have every member proved by GuardProbe before any game session touches them. Parameter types are named as
/// strings because the overload's parameter type lives in the target plugin and cannot be referenced at compile
/// time.
/// </summary>
public static class ReflectionGuardExtensions
{
    /// <summary>
    /// Alias entry: same target type and method name as a pinned member, the full names of the parameter types of
    /// the wanted overload, plus the key the caller resolves it under. Verified with <see cref="VerifyAlias"/>
    /// right after the pin passes.
    /// </summary>
    public sealed record AliasMember(string Type, string Name, string AsKey, string[] ParameterTypeNames);

    /// <summary>Resolve one alias against the pin's assembly. Returns the failure text, or null when found.</summary>
    public static string? VerifyAlias(ReflectionGuard.Pin pin, Type pluginType, AliasMember alias, out MethodInfo? found)
    {
        found = null;
        var type = string.IsNullOrEmpty(alias.Type)
            ? pluginType
            : pluginType.Assembly.GetType(alias.Type);
        if (type is null)
            return $"type '{alias.Type}' no longer exists in {pin.InternalName} (alias {alias.AsKey}).";
        MethodInfo? method = null;
        foreach (var c in type.GetMethods(ReflectionGuard.Any))
        {
            if (c.Name != alias.Name) continue;
            var ps = c.GetParameters();
            if (ps.Length != alias.ParameterTypeNames.Length) continue;
            var ok = true;
            for (var i = 0; i < ps.Length && ok; i++)
                ok = ps[i].ParameterType.FullName == alias.ParameterTypeNames[i];
            if (ok) { method = c; break; }
        }
        if (method is null)
            return $"method '{alias.Name}({string.Join(", ", alias.ParameterTypeNames)})' is missing from {type.FullName} in {pin.InternalName} (alias {alias.AsKey}).";
        found = method;
        return null;
    }
}
