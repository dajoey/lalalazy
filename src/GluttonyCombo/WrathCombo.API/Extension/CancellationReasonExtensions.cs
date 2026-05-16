#region

using System.ComponentModel;
using System.Reflection;
using WrathCombo.API.Enum;

#endregion

namespace WrathCombo.API.Extension;

/// <summary>
///     Extension methods for <see cref="CancellationReason" />.
/// </summary>
public static class CancellationReasonExtensions
{
    extension(CancellationReason value)
    {
        /// <summary>
        ///     Gets the description attribute for the given
        ///     <see cref="CancellationReason" />.
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
                var enumValue = typeof(CancellationReason)
                    .GetField(value.ToString());
                var attr = enumValue?
                    .GetCustomAttribute<DescriptionAttribute>();
                return attr?.Description ?? string.Empty;
            }
        }
    }
}