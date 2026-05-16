namespace WrathCombo.API.Error;

public class IPCException(string? msg, System.Exception? ex)
    : Exception(msg, ex);