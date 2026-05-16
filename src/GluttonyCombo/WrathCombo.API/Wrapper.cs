#region

using System.Reflection;
using Dalamud.Plugin;

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    [Flags]
    public enum ErrorType
    {
        None         = 0,
        APIBehindIPC = 1 << 0,
        IPCNotReady  = 1 << 1,
        GenericIpc   = 1 << 2,
        Unexpected   = 1 << 3,
        All          = ~0,
    }

    private static IDalamudPluginInterface? Interface
    {
        get
        {
            if (field != null)
                return field;

            // Try to get the interface from ECommons if possible
            try
            {
                const string           assemblyName = "ECommons";
                const StringComparison lower = StringComparison.OrdinalIgnoreCase;
                var eCommonsAsm = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a =>
                        a.GetName().Name?.Equals(assemblyName, lower) == true);
                if (eCommonsAsm is null) return null;

                const string svcFullName = "ECommons.DalamudServices.Svc";
                var svcType = eCommonsAsm
                    .GetType(svcFullName, throwOnError: false, ignoreCase: true);
                if (svcType is null) return null;

                var isInit = svcType.GetField("IsInitialized",
                        BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) as bool?;
                if (isInit != true) return null;

                var raw = svcType.GetProperty("PluginInterface",
                        BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (raw is null) return null;

                return field = raw as IDalamudPluginInterface;
            }
            catch
            {
                return null;
            }
        }
        set;
    }

    private static ErrorType SuppressedErrorTypes { get; set; }

    private static bool Suppressing(ErrorType flags) =>
        SuppressedErrorTypes.HasFlag(flags);

    /// <remarks>
    ///     Not required if ECommons is present and initialized.
    /// </remarks>
    public static void Init(IDalamudPluginInterface pluginInterface,
        ErrorType suppressedErrorTypes = ErrorType.None)
    {
        Interface            = pluginInterface;
        SuppressedErrorTypes = suppressedErrorTypes;
    }

    /// <remarks>
    ///     Would require ECommons to be present and initialized.
    /// </remarks>
    public static void Init(ErrorType suppressedErrorTypes)
    {
        SuppressedErrorTypes = suppressedErrorTypes;
    }
}