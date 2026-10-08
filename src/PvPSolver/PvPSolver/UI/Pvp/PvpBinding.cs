using RotationSolver.Basic.Configuration;

namespace RotationSolver.UI.Pvp;

/// <summary>
///     Reads and writes the <c>Service.Config</c> properties named in <c>PvpSettingsCatalog</c>. The property is
///     looked up once by name and cached; the value is read from <c>Service.Config</c> on every call (a reset or a
///     restore replaces that object, so it is never kept). A <c>ConditionBoolean</c> is written through
///     <c>.Value</c> (the object is never replaced), a plain bool, float or enum through the property. Every method
///     returns false instead of throwing when the property is missing or has another type.
/// </summary>
internal static class PvpBinding
{
    private static readonly Dictionary<string, PropertyInfo?> Cache = [];

    private static PropertyInfo? Property(string name)
    {
        if (!Cache.TryGetValue(name, out PropertyInfo? property))
        {
            property = typeof(Configs).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Cache[name] = property;
        }

        return property;
    }

    /// <summary>Whether a property of that name exists on <c>Configs</c>.</summary>
    public static bool Exists(string name) => Property(name) != null;

    /// <summary>Reads a bool setting (a <c>ConditionBoolean</c> or a plain bool).</summary>
    public static bool TryGetBool(string name, out bool value)
    {
        value = false;
        switch (Property(name)?.GetValue(Service.Config))
        {
            case ConditionBoolean condition:
                value = condition.Value;
                return true;
            case bool plain:
                value = plain;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Writes a bool setting.</summary>
    public static bool TrySetBool(string name, bool value)
    {
        PropertyInfo? property = Property(name);
        switch (property?.GetValue(Service.Config))
        {
            case ConditionBoolean condition:
                condition.Value = value;
                return true;
            case bool when property!.CanWrite:
                property.SetValue(Service.Config, value);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Reads a float setting.</summary>
    public static bool TryGetFloat(string name, out float value)
    {
        value = 0f;
        if (Property(name)?.GetValue(Service.Config) is float number)
        {
            value = number;
            return true;
        }

        return false;
    }

    /// <summary>Writes a float setting.</summary>
    public static bool TrySetFloat(string name, float value)
    {
        PropertyInfo? property = Property(name);
        if (property is { CanWrite: true } && property.PropertyType == typeof(float))
        {
            property.SetValue(Service.Config, value);
            return true;
        }

        return false;
    }

    /// <summary>Reads an enum setting as its member name.</summary>
    public static bool TryGetChoice(string name, out string member)
    {
        member = string.Empty;
        if (Property(name)?.GetValue(Service.Config) is Enum value)
        {
            member = value.ToString();
            return true;
        }

        return false;
    }

    /// <summary>Writes an enum setting from a member name.</summary>
    public static bool TrySetChoice(string name, string member)
    {
        PropertyInfo? property = Property(name);
        if (property is { CanWrite: true } && property.PropertyType.IsEnum && Enum.TryParse(property.PropertyType, member, false, out object? value))
        {
            property.SetValue(Service.Config, value);
            return true;
        }

        return false;
    }
}
