#region

using System.Reflection;
using WrathCombo.API.Attribute;
using WrathCombo.API.Enum;

#endregion

namespace WrathCombo.API.Extension;

/// <summary>
///     Extension methods for <see cref="AutoRotationConfigOption" />.
/// </summary>
public static class AutoRotationConfigOptionExtensions
{
    extension(AutoRotationConfigOption option)
    {
        /// <summary>
        ///     Gets the expected value type for the given
        ///     <see cref="AutoRotationConfigOption" />.
        /// </summary>
        /// <value>
        ///     The option to get the value type for.
        /// </value>
        /// <return>
        ///     The expected <see cref="Type" />,
        ///     or <c>null</c> if not defined.
        /// </return>
        public Type? ValueType
        {
            get
            {
                var enumValue = typeof(AutoRotationConfigOption)
                    .GetField(option.ToString());
                var attr = enumValue?
                    .GetCustomAttribute<ConfigValueTypeAttribute>();
                return attr?.ValueType;
            }
        }
    }
}