namespace WrathCombo.API.Attribute;

/// <summary>
///     Attribute to define the type of value that should be set for a given
///     <see cref="WrathCombo.API.Enum.AutoRotationConfigOption" />.
/// </summary>
/// <param name="valueType">The type necessary.</param>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ConfigValueTypeAttribute(Type valueType) : System.Attribute
{
    public Type ValueType { get; } = valueType;
}