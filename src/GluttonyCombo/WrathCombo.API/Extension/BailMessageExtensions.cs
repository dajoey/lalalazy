#region

using System.ComponentModel;
using System.Reflection;
using WrathCombo.API.Enum;

#endregion

namespace WrathCombo.API.Extension;

/// <summary>
///     Extension methods for <see cref="BailMessage" />.
/// </summary>
public static class BailMessageExtensions
{
    extension(BailMessage value)
    {
        /// <summary>
        ///     Gets the description attribute for the given
        ///     <see cref="BailMessage" />.
        /// </summary>
        /// <value>
        ///     The option to get the description for.
        /// </value>
        /// <return>
        ///     The description attribute contents as a string, or an empty
        ///     string.
        /// </return>
        public string Description
        {
            get
            {
                var enumValue = typeof(BailMessage)
                    .GetField(value.ToString());
                var attr = enumValue?
                    .GetCustomAttribute<DescriptionAttribute>();
                return attr?.Description ?? string.Empty;
            }
        }
    }
}