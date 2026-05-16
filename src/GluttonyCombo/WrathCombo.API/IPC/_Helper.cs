#region

using Dalamud.Plugin.Ipc.Exceptions;
using WrathCombo.API.Enum;
using WrathCombo.API.Error;
using Exception = System.Exception;

#endregion

namespace WrathCombo.API;

public static partial class WrathIPCWrapper
{
    private static T? SafeInvokeRawMethod<T>(Func<T> ipcCall)
    {
        if (Interface == null)
            throw new UninitializedException(
                "WrathCombo IPC Wrapper not initialized.");

        try
        {
            return ipcCall();
        }
        catch (IpcError ex) when (ex is IpcTypeMismatchError or
                                      IpcValueNullError or
                                      IpcLengthMismatchError)
        {
            if (!Suppressing(ErrorType.APIBehindIPC))
                throw new APIBehindException(
                    "WrathCombo.API needs updated; " +
                    "it is behind WrathCombo.IPC: invalid signatures.",
                    ex);
        }
        catch (IpcError ex) when (ex is IpcNotReadyError)
        {
            if (!Suppressing(ErrorType.IPCNotReady))
                throw new UninitializedException(
                    "WrathCombo.IPC isn't ready.", ex);
        }
        catch (IpcError ex)
        {
            if (!Suppressing(ErrorType.GenericIpc))
                throw new IPCException(
                    "WrathCombo.API encountered an IPC error " +
                    "in using WrathCombo.IPC.",
                    ex);
        }
        catch (Exception ex)
        {
            if (!Suppressing(ErrorType.Unexpected))
                throw new UnexpectedException(
                    "WrathCombo.API encountered an unexpected " +
                    "error in using WrathCombo.IPC.",
                    ex);
        }

        if (typeof(T) == typeof(SetResult))
            return (T)(object)SetResult.IGNORED;

        return default;
    }

    private static void SafeInvokeRawMethod(Action ipcCall)
    {
        SafeInvokeRawMethod(() =>
        {
            ipcCall();
            return 0L;
        });
    }
}